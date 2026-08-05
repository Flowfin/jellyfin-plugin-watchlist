using System;
using System.Globalization;
using System.IO;

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

    private readonly string _dataFolderPath;

    /// <summary>
    /// Initializes a new instance of the <see cref="WatchlistDocumentStore"/> class.
    /// </summary>
    /// <param name="dataFolderPath">The folder every document lives in.</param>
    public WatchlistDocumentStore(string dataFolderPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataFolderPath);

        _dataFolderPath = Path.GetFullPath(dataFolderPath);
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
    /// The stored document, or an empty one for a user who has never had a list. A new
    /// user needs no provisioning step, so a missing file is a user with nothing on
    /// their list rather than an error.
    /// </returns>
    public WatchlistDocument Read(Guid userId)
    {
        var path = PathFor(userId);

        if (!File.Exists(path))
        {
            return Empty(userId);
        }

        return WatchlistDocumentFormat.Read(File.ReadAllText(path));
    }

    /// <summary>
    /// Writes one user's document, so that a reader sees either the whole of the old
    /// one or the whole of the new one and never part of either.
    /// </summary>
    /// <param name="document">The document to write.</param>
    public void Write(WatchlistDocument document)
    {
        Commit(Stage(document));
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
