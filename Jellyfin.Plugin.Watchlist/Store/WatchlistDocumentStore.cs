using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Jellyfin.Plugin.Watchlist.Store;

/// <summary>
/// Reads and writes one document per user, under a folder handed to it.
/// </summary>
/// <remarks>
/// The folder is the plugin's own data folder in the running server. It is taken as
/// a parameter rather than read from the plugin, so nothing here needs a server to
/// be exercised and every path is built from one place.
///
/// The plugin's configuration is not the place for this. The server reads and
/// rewrites the whole configuration document when an administrator saves the
/// settings page, so a list living inside it is lost to a concurrent save and grows
/// the file that every save rewrites.
/// </remarks>
public sealed class WatchlistDocumentStore
{
    /// <summary>
    /// The suffix a staged write carries until it is committed. A crash leaves one of
    /// these beside the document rather than a truncated document.
    /// </summary>
    internal const string PendingSuffix = ".writing";

    /// <summary>
    /// One gate per document path, shared by every store in the process. Two stores
    /// over one folder are two objects guarding one file, and the file is what the
    /// serialisation is about.
    /// </summary>
    private static readonly ConcurrentDictionary<string, object> Gates = new(StringComparer.Ordinal);

    private readonly string _dataFolderPath;
    private readonly ILogger _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="WatchlistDocumentStore"/> class.
    /// </summary>
    /// <param name="dataFolderPath">The folder every document lives in.</param>
    /// <param name="logger">Where a refused document is reported.</param>
    public WatchlistDocumentStore(string dataFolderPath, ILogger<WatchlistDocumentStore> logger)
        : this(dataFolderPath, (ILogger?)logger)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="WatchlistDocumentStore"/> class
    /// with no logger, for a caller that has none.
    /// </summary>
    /// <param name="dataFolderPath">The folder every document lives in.</param>
    public WatchlistDocumentStore(string dataFolderPath)
        : this(dataFolderPath, (ILogger?)null)
    {
    }

    private WatchlistDocumentStore(string dataFolderPath, ILogger? logger)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataFolderPath);

        _dataFolderPath = Path.GetFullPath(dataFolderPath);
        _logger = logger ?? NullLogger.Instance;
    }

    /// <summary>
    /// Gets the folder every document lives in, resolved.
    /// </summary>
    public string DataFolderPath => _dataFolderPath;

    /// <summary>
    /// Where one user's document lives.
    /// </summary>
    /// <param name="userId">The user.</param>
    /// <returns>The full path of that user's document.</returns>
    /// <remarks>
    /// The file name is the identifier in its hexadecimal form and nothing else, so it
    /// carries no separator, no dot and no parent reference, and the result cannot
    /// leave the folder whatever the caller passes.
    /// </remarks>
    public string PathFor(Guid userId) => Path.Combine(
        _dataFolderPath,
        string.Format(CultureInfo.InvariantCulture, "{0}.json", userId.ToString("N", CultureInfo.InvariantCulture)));

    /// <summary>
    /// Reads one user's document.
    /// </summary>
    /// <param name="userId">The user.</param>
    /// <returns>
    /// The stored document, or an empty one for a user who has never had a list, or an
    /// unavailable result for a document this plugin is too old to read. A new user
    /// needs no provisioning step, so a missing file is a user with nothing on their
    /// list rather than an error.
    /// </returns>
    /// <remarks>
    /// A read never writes. A document declaring an older version is brought up to the
    /// current one in memory and the file is left exactly as it was, so opening a list
    /// does not rewrite the tree and a downgrade does not lose what it could not read.
    /// </remarks>
    public WatchlistReadResult Read(Guid userId)
    {
        lock (GateFor(userId))
        {
            return ReadInsideTheGate(userId);
        }
    }

    private WatchlistReadResult ReadInsideTheGate(Guid userId)
    {
        var path = PathFor(userId);

        if (!File.Exists(path))
        {
            return WatchlistReadResult.Available(Empty(userId));
        }

        var text = File.ReadAllText(path);
        var storedVersion = ReadSchemaVersion(text);

        if (storedVersion > WatchlistDocument.CurrentSchemaVersion)
        {
            // Not parsed and not touched. Guessing at a shape a newer plugin wrote and
            // then rewriting the file is how a downgrade drops entries, and a
            // downgrade is one click.
            _logger.LogError(
                "Refusing to read {Path}: it declares watchlist schema version {StoredVersion} and this plugin understands version {UnderstoodVersion}. The list is unavailable for this user and the file is left alone.",
                path,
                storedVersion,
                WatchlistDocument.CurrentSchemaVersion);

            return WatchlistReadResult.UnavailableFromTheFuture(storedVersion);
        }

        var document = WatchlistDocumentFormat.Read(text);

        return WatchlistReadResult.Available(
            document.SchemaVersion == WatchlistDocument.CurrentSchemaVersion
                ? document
                : document with { SchemaVersion = WatchlistDocument.CurrentSchemaVersion });
    }

    /// <summary>
    /// Reads the schema version out of a document without deserialising the rest of
    /// it, so a document from the future is judged before anything tries to make sense
    /// of its entries.
    /// </summary>
    /// <param name="text">The document text.</param>
    /// <returns>The declared version.</returns>
    /// <exception cref="JsonException">The text declares no integer schema version.</exception>
    private static int ReadSchemaVersion(string text)
    {
        using var parsed = JsonDocument.Parse(text);

        if (!parsed.RootElement.TryGetProperty(nameof(WatchlistDocument.SchemaVersion), out var version)
            || !version.TryGetInt32(out var value))
        {
            throw new JsonException("The document declares no integer " + nameof(WatchlistDocument.SchemaVersion) + ".");
        }

        return value;
    }

    /// <summary>
    /// Writes one user's document, so that a reader sees either the whole of the old
    /// one or the whole of the new one and never part of either.
    /// </summary>
    /// <param name="document">The document to write.</param>
    public void Write(WatchlistDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        lock (GateFor(document.UserId))
        {
            Commit(Stage(document));
        }
    }

    /// <summary>
    /// The object that serialises changes to one user's document.
    /// </summary>
    /// <param name="userId">The user.</param>
    /// <returns>The gate for that user's document.</returns>
    /// <remarks>
    /// Keyed by the document path rather than by the user, so it is the file that is
    /// guarded and two stores over the same folder share the gate. One user's gate is
    /// not another's, so two users never wait for each other.
    ///
    /// A monitor rather than a semaphore, and a synchronous store rather than an
    /// asynchronous one, because a monitor is reentrant on the same thread. Add reads
    /// and then writes through the same gate, and with a semaphore that is a deadlock
    /// against itself. There is no await anywhere inside a gate here, so no lock is
    /// held across one.
    /// </remarks>
    internal object GateFor(Guid userId) => Gates.GetOrAdd(PathFor(userId), _ => new object());

    /// <summary>
    /// Takes one entry off a user's list.
    /// </summary>
    /// <param name="userId">The user.</param>
    /// <param name="itemId">The item to take off.</param>
    /// <returns>What happened.</returns>
    public WatchlistRemoveResult Remove(Guid userId, Guid itemId)
    {
        lock (GateFor(userId))
        {
            var read = ReadInsideTheGate(userId);

            if (!read.IsAvailable)
            {
                return WatchlistRemoveResult.Unavailable();
            }

            var document = read.Document!;
            var remaining = document.Entries.Where(entry => entry.ItemId != itemId).ToArray();

            if (remaining.Length == document.Entries.Count)
            {
                return new WatchlistRemoveResult(false, true, remaining.Length);
            }

            Commit(Stage(document with { Entries = remaining }));

            return new WatchlistRemoveResult(true, true, remaining.Length);
        }
    }

    /// <summary>
    /// Puts one entry on a user's list, unless that would take the list past its cap.
    /// </summary>
    /// <param name="userId">The user.</param>
    /// <param name="entry">The entry to add.</param>
    /// <param name="maxEntriesPerUser">
    /// The greatest number of entries the list may hold. The value is passed in rather
    /// than read from the plugin configuration here, so the store needs no server to be
    /// exercised and the caller stays the one place that decides which cap applies.
    /// </param>
    /// <returns>What happened, and the numbers behind it.</returns>
    /// <remarks>
    /// A refusal writes nothing at all. It also removes nothing: a list that silently
    /// drops its oldest entry to make room is a list a user cannot trust, and lowering
    /// the cap under an existing list must not delete what is already on it.
    /// </remarks>
    public WatchlistAddResult Add(Guid userId, WatchlistEntry entry, int maxEntriesPerUser)
    {
        ArgumentNullException.ThrowIfNull(entry);

        // The read and the write are one change, not two. Outside a gate, two adds that
        // both read a list of four and both write a list of five lose one of the two
        // entries and report success for both.
        lock (GateFor(userId))
        {
            var read = ReadInsideTheGate(userId);

            if (!read.IsAvailable)
            {
                return WatchlistAddResult.RefusedListUnavailable();
            }

            var document = read.Document!;

            if (document.Entries.Count >= maxEntriesPerUser)
            {
                _logger.LogWarning(
                    "Refusing to add to the watchlist of user {UserId}: it holds {EntryCount} entries and the maximum is {Cap}. Nothing was added and nothing was removed.",
                    userId,
                    document.Entries.Count,
                    maxEntriesPerUser);

                return WatchlistAddResult.RefusedListIsFull(document.Entries.Count, maxEntriesPerUser);
            }

            var entries = new List<WatchlistEntry>(document.Entries) { entry };

            Commit(Stage(document with { Entries = entries }));

            return WatchlistAddResult.Added(entries.Count, maxEntriesPerUser);
        }
    }

    /// <summary>
    /// An empty document for a user, which is what a read of a file that is not there
    /// returns.
    /// </summary>
    /// <param name="userId">The user.</param>
    /// <returns>An empty document.</returns>
    public static WatchlistDocument Empty(Guid userId) => new()
    {
        SchemaVersion = WatchlistDocument.CurrentSchemaVersion,
        UserId = userId,
        Entries = [],
    };

    /// <summary>
    /// Writes the new content to a file beside the target and returns where it went.
    /// Nothing a reader can see has changed when this returns.
    /// </summary>
    /// <param name="document">The document to write.</param>
    /// <returns>The staged file and the target it is for.</returns>
    /// <remarks>
    /// The two halves are separate members rather than one method with a hook in it,
    /// so a test can stop between them and the path it exercises is the path the
    /// server takes.
    /// </remarks>
    internal StagedWrite Stage(WatchlistDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        Directory.CreateDirectory(_dataFolderPath);

        var target = PathFor(document.UserId);
        var staged = target + PendingSuffix;

        File.WriteAllText(staged, WatchlistDocumentFormat.Write(document));

        return new StagedWrite(staged, target);
    }

    /// <summary>
    /// Puts a staged write in place of the document, in one step.
    /// </summary>
    /// <param name="staged">What <see cref="Stage"/> returned.</param>
    /// <remarks>
    /// The staged file is in the same directory as the target, so this is a rename
    /// within one volume rather than a copy. A reader either opens the old file or the
    /// new one.
    /// </remarks>
    internal static void Commit(StagedWrite staged)
    {
        File.Move(staged.StagedPath, staged.TargetPath, overwrite: true);
    }

    /// <summary>
    /// A write that has been put on disk but not yet put in place.
    /// </summary>
    /// <param name="StagedPath">Where the new content is.</param>
    /// <param name="TargetPath">Where it is going.</param>
    internal sealed record StagedWrite(string StagedPath, string TargetPath);
}
