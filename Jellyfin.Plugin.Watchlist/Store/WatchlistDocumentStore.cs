using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
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
    /// The file the one shared list lives in, under the same folder as every user's
    /// document.
    /// </summary>
    /// <remarks>
    /// It cannot collide with a user's document, and that is a property of the two
    /// names rather than a name chosen because a collision looked unlikely. Every
    /// user document is named by an identifier in its hexadecimal form, which is
    /// thirty-two hexadecimal digits and nothing else; this name is not one, so no
    /// identifier a server can mint produces it. The test asserts exactly that rather
    /// than comparing against a handful of identifiers somebody thought of.
    /// </remarks>
    internal const string SharedListFileName = "shared-list.json";

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
    /// Gets where the one shared list lives.
    /// </summary>
    public string SharedListPath => Path.Combine(_dataFolderPath, SharedListFileName);

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

        var stored = ParseDocument(File.ReadAllText(path));
        var storedVersion = ReadSchemaVersion(stored);

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

        if (!WatchlistDocumentUpgrades.CanBringForward(storedVersion))
        {
            // The other direction, and the same refusal to guess. Calling the document
            // current because the number is smaller leaves an old shape carrying a new
            // label, and from then on nothing can tell it from a document that really
            // was brought forward.
            _logger.LogError(
                "Refusing to read {Path}: it declares watchlist schema version {StoredVersion} and this plugin carries no upgrade step from it to version {UnderstoodVersion}. The list is unavailable for this user and the file is left alone.",
                path,
                storedVersion,
                WatchlistDocument.CurrentSchemaVersion);

            return WatchlistReadResult.UnavailableFromAnUnreachableVersion(storedVersion);
        }

        var upgraded = WatchlistDocumentUpgrades.BringForward(stored, storedVersion);

        return WatchlistReadResult.Available(WatchlistDocumentFormat.Read(upgraded.ToJsonString()));
    }

    /// <summary>
    /// Reads the document text as JSON, before anything tries to make sense of its
    /// members.
    /// </summary>
    /// <param name="text">The document text.</param>
    /// <returns>The stored members.</returns>
    /// <exception cref="JsonException">The text is not a JSON object.</exception>
    private static JsonObject ParseDocument(string text) =>
        JsonNode.Parse(text) as JsonObject
        ?? throw new JsonException("The document text is not a JSON object.");

    /// <summary>
    /// Reads the schema version out of a document without deserialising the rest of
    /// it, so a document from a version this plugin cannot read is judged before
    /// anything tries to make sense of its entries.
    /// </summary>
    /// <param name="document">The stored members.</param>
    /// <returns>The declared version.</returns>
    /// <exception cref="JsonException">The document declares no integer schema version.</exception>
    private static int ReadSchemaVersion(JsonObject document)
    {
        if (!document.TryGetPropertyValue(nameof(WatchlistDocument.SchemaVersion), out var version)
            || version is not JsonValue value
            || !value.TryGetValue<int>(out var number))
        {
            throw new JsonException("The document declares no integer " + nameof(WatchlistDocument.SchemaVersion) + ".");
        }

        return number;
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

            // Asked and answered. The check is inside the gate rather than in the
            // caller, because a caller that reads then adds is two changes and two
            // clients retrying one timeout each would both read a list without the
            // item and both write it. A list holding one film twice is a list the
            // user has to repair by hand.
            if (document.Entries.Any(existing => existing.ItemId == entry.ItemId))
            {
                return WatchlistAddResult.AlreadyOnTheList(document.Entries.Count, maxEntriesPerUser);
            }

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
    /// Writes one user's own answers into their document, beside their entries.
    /// </summary>
    /// <param name="userId">The user.</param>
    /// <param name="preferences">
    /// What the user answered, or null where they have withdrawn every answer. A block
    /// holding no answer is stored as no block, so withdrawing the last one leaves the
    /// document in the state it was in before the first.
    /// </param>
    /// <returns>
    /// False when the document could not be read at all, which is a document from a
    /// newer plugin or one no upgrade step reaches. Nothing is written in that case,
    /// for the same reason a read of it returns no list: the file is left alone rather
    /// than replaced by a shape this build understands.
    /// </returns>
    /// <remarks>
    /// The read and the write are one change under the same gate as every other
    /// writer here, so a preference saved while an entry is being added does not lose
    /// either of them, and the bytes reach disk through the same staged write.
    /// </remarks>
    public bool SetPreferences(Guid userId, WatchlistUserPreferences? preferences)
    {
        lock (GateFor(userId))
        {
            var read = ReadInsideTheGate(userId);

            if (!read.IsAvailable)
            {
                return false;
            }

            Commit(Stage(read.Document! with
            {
                Preferences = preferences is null || preferences.IsEmpty ? null : preferences,
            }));

            return true;
        }
    }

    /// <summary>
    /// Reads the shared list.
    /// </summary>
    /// <returns>
    /// The stored list, or the answer that this server has none, or an unavailable
    /// result for a document this plugin will not read. A server with no shared list
    /// is not a server with an empty one: nobody has made one, and a caller that
    /// cannot tell those apart makes a second list out of a read.
    /// </returns>
    /// <remarks>
    /// A read never writes, and the version rule is the one a user's document is read
    /// under. Both refusals leave the file exactly as it was.
    /// </remarks>
    public SharedWatchlistReadResult ReadShared()
    {
        lock (SharedGate())
        {
            return ReadSharedInsideTheGate();
        }
    }

    private SharedWatchlistReadResult ReadSharedInsideTheGate()
    {
        var path = SharedListPath;

        if (!File.Exists(path))
        {
            return SharedWatchlistReadResult.NoSharedList();
        }

        var stored = ParseDocument(File.ReadAllText(path));
        var storedVersion = ReadSchemaVersion(stored);

        if (storedVersion > SharedWatchlistDocument.CurrentSchemaVersion)
        {
            _logger.LogError(
                "Refusing to read {Path}: it declares shared watchlist schema version {StoredVersion} and this plugin understands version {UnderstoodVersion}. The shared list is unavailable and the file is left alone.",
                path,
                storedVersion,
                SharedWatchlistDocument.CurrentSchemaVersion);

            return SharedWatchlistReadResult.UnavailableFromTheFuture(storedVersion);
        }

        if (!WatchlistDocumentUpgrades.CanBringSharedForward(storedVersion))
        {
            _logger.LogError(
                "Refusing to read {Path}: it declares shared watchlist schema version {StoredVersion} and this plugin carries no upgrade step from it to version {UnderstoodVersion}. The shared list is unavailable and the file is left alone.",
                path,
                storedVersion,
                SharedWatchlistDocument.CurrentSchemaVersion);

            return SharedWatchlistReadResult.UnavailableFromAnUnreachableVersion(storedVersion);
        }

        var upgraded = WatchlistDocumentUpgrades.BringSharedForward(stored, storedVersion);

        return SharedWatchlistReadResult.Available(
            WatchlistDocumentFormat.ReadShared(upgraded.ToJsonString()));
    }

    /// <summary>
    /// Writes the shared list, so that a reader sees either the whole of the old one
    /// or the whole of the new one and never part of either.
    /// </summary>
    /// <param name="document">The document to write.</param>
    public void WriteShared(SharedWatchlistDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        lock (SharedGate())
        {
            Commit(Stage(document));
        }
    }

    /// <summary>
    /// Puts one entry on the shared list, unless that would take it past its cap.
    /// </summary>
    /// <param name="entry">The entry to add, carrying who is adding it.</param>
    /// <param name="maxEntriesInSharedList">
    /// The greatest number of entries the shared list may hold, passed in for the same
    /// reason the per-user cap is: the store needs no server to be exercised and the
    /// caller stays the one place that decides which cap applies.
    /// </param>
    /// <returns>What happened, and the numbers behind it.</returns>
    /// <remarks>
    /// A server with no shared list is refused rather than given one. Making the list
    /// is a decision an administrator takes, and an add that made it would take that
    /// decision on the first call from anybody.
    ///
    /// An item already on the list keeps the entry that is there, attribution and all.
    /// A second person asking for a title that is already on the list is asking for
    /// the list to hold it, which it does, and rewriting the entry would take the
    /// first person's name off something they put there.
    /// </remarks>
    public WatchlistAddResult AddShared(WatchlistEntry entry, int maxEntriesInSharedList)
    {
        ArgumentNullException.ThrowIfNull(entry);

        lock (SharedGate())
        {
            var read = ReadSharedInsideTheGate();

            if (!read.Exists)
            {
                return WatchlistAddResult.RefusedNoSharedList();
            }

            if (!read.IsAvailable)
            {
                return WatchlistAddResult.RefusedListUnavailable();
            }

            var document = read.Document!;

            if (document.Entries.Any(existing => existing.ItemId == entry.ItemId))
            {
                return WatchlistAddResult.AlreadyOnTheList(document.Entries.Count, maxEntriesInSharedList);
            }

            if (document.Entries.Count >= maxEntriesInSharedList)
            {
                _logger.LogWarning(
                    "Refusing to add to the shared watchlist: it holds {EntryCount} entries and the maximum is {Cap}. Nothing was added and nothing was removed.",
                    document.Entries.Count,
                    maxEntriesInSharedList);

                return WatchlistAddResult.RefusedListIsFull(document.Entries.Count, maxEntriesInSharedList);
            }

            var entries = new List<WatchlistEntry>(document.Entries) { entry };

            Commit(Stage(document with { Entries = entries }));

            return WatchlistAddResult.Added(entries.Count, maxEntriesInSharedList);
        }
    }

    /// <summary>
    /// Takes one entry off the shared list, if this caller may.
    /// </summary>
    /// <param name="itemId">The item to take off.</param>
    /// <param name="callerId">Who is asking.</param>
    /// <param name="callerMayRemoveAnyEntry">
    /// Whether this caller may remove an entry somebody else put there. Answered by
    /// the caller rather than decided here, because who counts as an administrator is
    /// the server's question and this store never meets a request.
    /// </param>
    /// <returns>What happened.</returns>
    /// <remarks>
    /// The rule is applied inside the gate rather than by whoever asks. A caller that
    /// read the list, checked the attribution and then called a plain removal would be
    /// making two changes out of one, and between them the entry can be removed and a
    /// different one added under the same item by somebody else.
    /// </remarks>
    public SharedWatchlistRemoveResult RemoveShared(Guid itemId, Guid callerId, bool callerMayRemoveAnyEntry)
    {
        lock (SharedGate())
        {
            var read = ReadSharedInsideTheGate();

            if (!read.Exists)
            {
                return SharedWatchlistRemoveResult.NoSharedList();
            }

            if (!read.IsAvailable)
            {
                return SharedWatchlistRemoveResult.Unavailable();
            }

            var document = read.Document!;
            var target = document.Entries.FirstOrDefault(existing => existing.ItemId == itemId);

            if (target is null)
            {
                return SharedWatchlistRemoveResult.NotOnTheList(document.Entries.Count);
            }

            if (!callerMayRemoveAnyEntry && target.AddedBy != callerId)
            {
                return SharedWatchlistRemoveResult.RefusedNotTheirEntry(document.Entries.Count);
            }

            var remaining = document.Entries.Where(existing => existing.ItemId != itemId).ToArray();

            Commit(Stage(document with { Entries = remaining }));

            return SharedWatchlistRemoveResult.Removed(remaining.Length);
        }
    }

    /// <summary>
    /// The object that serialises changes to the shared list.
    /// </summary>
    /// <returns>The gate for the shared document.</returns>
    /// <remarks>
    /// Keyed by the path, in the same dictionary a user's gate is keyed in, so two
    /// stores over one folder share it. It is not any user's gate, so writing the
    /// shared list never waits for a user's list and never blocks one.
    /// </remarks>
    internal object SharedGate() => Gates.GetOrAdd(SharedListPath, _ => new object());

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
    /// A shared list holding nothing, for a server on which one is being made.
    /// </summary>
    /// <param name="listId">The identity of the list, which is not its displayed name.</param>
    /// <param name="ownerUserId">The user the list belongs to.</param>
    /// <returns>An empty shared document.</returns>
    /// <remarks>
    /// Not what a read of a missing file returns, which is the difference between this
    /// record and a user's. A user's list exists because the user does; a shared list
    /// exists because somebody made one, so this is called by whoever makes it rather
    /// than by a read.
    /// </remarks>
    public static SharedWatchlistDocument EmptyShared(Guid listId, Guid ownerUserId) => new()
    {
        SchemaVersion = SharedWatchlistDocument.CurrentSchemaVersion,
        ListId = listId,
        OwnerUserId = ownerUserId,
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
    /// The same for the shared list, through the same two halves.
    /// </summary>
    /// <param name="document">The document to write.</param>
    /// <returns>The staged file and the target it is for.</returns>
    /// <remarks>
    /// The shared list is written by the writer a user's document is written by, so an
    /// interrupted write leaves a staged file beside the list rather than a truncated
    /// list, and that is one behaviour rather than two.
    /// </remarks>
    internal StagedWrite Stage(SharedWatchlistDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        Directory.CreateDirectory(_dataFolderPath);

        var target = SharedListPath;
        var staged = target + PendingSuffix;

        File.WriteAllText(staged, WatchlistDocumentFormat.Write(document));

        return new StagedWrite(staged, target);
    }

    /// <summary>
    /// Puts a staged write in place of the document, in one step.
    /// </summary>
    /// <param name="staged">What either <c>Stage</c> overload returned.</param>
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
