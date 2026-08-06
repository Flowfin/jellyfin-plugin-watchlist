namespace Jellyfin.Plugin.Watchlist.Store;

/// <summary>
/// What an add did.
/// </summary>
public enum WatchlistAddOutcome
{
    /// <summary>
    /// The entry is on the list and the document was written.
    /// </summary>
    Added = 0,

    /// <summary>
    /// The list is at its cap. Nothing was written.
    /// </summary>
    RefusedListIsFull = 1,

    /// <summary>
    /// The list could not be read, so nothing was written.
    /// </summary>
    RefusedListUnavailable = 2,
}
