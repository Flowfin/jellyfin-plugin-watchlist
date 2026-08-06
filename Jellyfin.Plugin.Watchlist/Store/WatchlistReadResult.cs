using System;

namespace Jellyfin.Plugin.Watchlist.Store;

/// <summary>
/// What a read of one user's document produced.
/// </summary>
/// <remarks>
/// "The user has nothing on their list" and "this plugin cannot read this user's
/// list" are different answers and a caller has to be able to tell them apart. An
/// empty document standing for both is how a refusal turns into an empty list on a
/// client and then into an overwrite.
/// </remarks>
public sealed record WatchlistReadResult
{
    private WatchlistReadResult(WatchlistDocument? document, int? storedSchemaVersion)
    {
        Document = document;
        StoredSchemaVersion = storedSchemaVersion;
    }

    /// <summary>
    /// Gets the document, or null when the list is unavailable.
    /// </summary>
    public WatchlistDocument? Document { get; }

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
    /// A list this plugin read.
    /// </summary>
    /// <param name="document">The document.</param>
    /// <returns>The result.</returns>
    public static WatchlistReadResult Available(WatchlistDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        return new WatchlistReadResult(document, null);
    }

    /// <summary>
    /// A list this plugin will not read, because the document was written by a newer
    /// version of it.
    /// </summary>
    /// <param name="storedSchemaVersion">The version the document declared.</param>
    /// <returns>The result.</returns>
    public static WatchlistReadResult UnavailableFromTheFuture(int storedSchemaVersion) =>
        new(null, storedSchemaVersion);

    /// <summary>
    /// A list this plugin will not read, because it carries no chain of upgrade steps
    /// from the version the document declares to the version it writes.
    /// </summary>
    /// <param name="storedSchemaVersion">The version the document declared.</param>
    /// <returns>The result.</returns>
    /// <remarks>
    /// Told apart from <see cref="UnavailableFromTheFuture"/> by comparing
    /// <see cref="StoredSchemaVersion"/> with
    /// <see cref="WatchlistDocument.CurrentSchemaVersion"/>: a refusal above it is a
    /// document from a newer plugin, and one below it is a version this plugin no
    /// longer knows how to bring forward.
    /// </remarks>
    public static WatchlistReadResult UnavailableFromAnUnreachableVersion(int storedSchemaVersion) =>
        new(null, storedSchemaVersion);
}
