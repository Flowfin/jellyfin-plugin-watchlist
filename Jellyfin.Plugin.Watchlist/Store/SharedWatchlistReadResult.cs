using System;

namespace Jellyfin.Plugin.Watchlist.Store;

/// <summary>
/// What a read of the shared list produced.
/// </summary>
/// <remarks>
/// Three answers rather than two, and the third is the one a user's document does
/// not have. There is no shared list until somebody makes one, so "nobody has made
/// one" is an ordinary state of a server rather than a failure, and it is not the
/// same answer as "the list is there and holds nothing". A caller that cannot tell
/// them apart creates a list on the first read of a server that deliberately has
/// none.
///
/// The refusal half is the same rule as a user's document: a list this plugin will
/// not read is never handed back as an empty one, because an empty list is what a
/// client shows and then writes over.
/// </remarks>
public sealed record SharedWatchlistReadResult
{
    private SharedWatchlistReadResult(SharedWatchlistDocument? document, bool exists, int? storedSchemaVersion)
    {
        Document = document;
        Exists = exists;
        StoredSchemaVersion = storedSchemaVersion;
    }

    /// <summary>
    /// Gets the document, or null when there is no shared list or it is unavailable.
    /// </summary>
    public SharedWatchlistDocument? Document { get; }

    /// <summary>
    /// Gets a value indicating whether a shared list exists on this server at all.
    /// </summary>
    /// <remarks>
    /// True for a list this plugin refused to read, because the file is there and the
    /// answer to "has somebody made one" is yes. Making a second one over it is the
    /// move this distinction exists to refuse.
    /// </remarks>
    public bool Exists { get; }

    /// <summary>
    /// Gets the version the stored document declared, when that is why it is
    /// unavailable.
    /// </summary>
    public int? StoredSchemaVersion { get; }

    /// <summary>
    /// Gets a value indicating whether the list could be read.
    /// </summary>
    public bool IsAvailable => Document is not null;

    /// <summary>
    /// A shared list this plugin read.
    /// </summary>
    /// <param name="document">The document.</param>
    /// <returns>The result.</returns>
    public static SharedWatchlistReadResult Available(SharedWatchlistDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        return new SharedWatchlistReadResult(document, true, null);
    }

    /// <summary>
    /// A server on which nobody has made a shared list.
    /// </summary>
    /// <returns>The result.</returns>
    public static SharedWatchlistReadResult NoSharedList() => new(null, false, null);

    /// <summary>
    /// A shared list this plugin will not read, because the document was written by a
    /// newer version of it.
    /// </summary>
    /// <param name="storedSchemaVersion">The version the document declared.</param>
    /// <returns>The result.</returns>
    public static SharedWatchlistReadResult UnavailableFromTheFuture(int storedSchemaVersion) =>
        new(null, true, storedSchemaVersion);

    /// <summary>
    /// A shared list this plugin will not read, because it carries no chain of upgrade
    /// steps from the version the document declares to the version it writes.
    /// </summary>
    /// <param name="storedSchemaVersion">The version the document declared.</param>
    /// <returns>The result.</returns>
    /// <remarks>
    /// Told apart from <see cref="UnavailableFromTheFuture"/> by comparing
    /// <see cref="StoredSchemaVersion"/> with
    /// <see cref="SharedWatchlistDocument.CurrentSchemaVersion"/>: a refusal above it
    /// is a document from a newer plugin, and one below it is a version this plugin no
    /// longer knows how to bring forward.
    /// </remarks>
    public static SharedWatchlistReadResult UnavailableFromAnUnreachableVersion(int storedSchemaVersion) =>
        new(null, true, storedSchemaVersion);
}
