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

    /// <summary>
    /// The item was already on the list. Nothing was written, and nothing needed to
    /// be: this is a success for a caller that asked for the item to be on the list.
    /// </summary>
    AlreadyOnTheList = 3,

    /// <summary>
    /// Nobody has made a shared list on this server, so there is nothing to add to.
    /// Only an add to the shared list can produce it: a user's own list exists because
    /// the user does, and a read of a document that is not there is an empty list.
    /// </summary>
    RefusedNoSharedList = 4,
}
