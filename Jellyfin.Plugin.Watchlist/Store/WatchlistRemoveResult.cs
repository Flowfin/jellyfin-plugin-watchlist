namespace Jellyfin.Plugin.Watchlist.Store;

/// <summary>
/// What a remove did, or why it did nothing.
/// </summary>
/// <remarks>
/// "It was not on the list" and "the list could not be read" both leave the list
/// alone and are not the same answer, for the same reason the add result separates
/// them. What an endpoint returns for each is #26.
/// </remarks>
/// <param name="Removed">Whether an entry left the list because of this call.</param>
/// <param name="ListWasAvailable">Whether the list could be read at all.</param>
/// <param name="EntryCount">How many entries the list holds now.</param>
public sealed record WatchlistRemoveResult(bool Removed, bool ListWasAvailable, int EntryCount)
{
    /// <summary>
    /// The list could not be read, so nothing was removed from it.
    /// </summary>
    /// <returns>The result.</returns>
    public static WatchlistRemoveResult Unavailable() => new(false, false, 0);
}
