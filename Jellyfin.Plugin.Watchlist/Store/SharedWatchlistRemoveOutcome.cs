namespace Jellyfin.Plugin.Watchlist.Store;

/// <summary>
/// What a removal from the shared list did.
/// </summary>
public enum SharedWatchlistRemoveOutcome
{
    /// <summary>
    /// The entry left the list and the document was written.
    /// </summary>
    Removed = 0,

    /// <summary>
    /// The item was not on the list. Nothing was written, and nothing needed to be.
    /// </summary>
    NotOnTheList = 1,

    /// <summary>
    /// The entry belongs to somebody else and this caller may remove only their own.
    /// Nothing was written.
    /// </summary>
    RefusedNotTheirEntry = 2,

    /// <summary>
    /// Nobody has made a shared list on this server.
    /// </summary>
    NoSharedList = 3,

    /// <summary>
    /// The list could not be read, so nothing was written.
    /// </summary>
    RefusedListUnavailable = 4,
}
