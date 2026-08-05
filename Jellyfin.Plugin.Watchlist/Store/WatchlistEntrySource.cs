namespace Jellyfin.Plugin.Watchlist.Store;

/// <summary>
/// How an entry arrived. Kept because the three routes fail differently and a
/// support question starts with which one put the entry there.
/// </summary>
public enum WatchlistEntrySource
{
    /// <summary>
    /// The source was not recorded. Only reachable by reading a document written by
    /// something that did not set it, which the serialisation refuses.
    /// </summary>
    Unknown = 0,

    /// <summary>
    /// A call to this plugin's own endpoint.
    /// </summary>
    Api = 1,

    /// <summary>
    /// An edit a user made to the projected playlist on a client, adopted back into
    /// the store.
    /// </summary>
    PlaylistEdit = 2,

    /// <summary>
    /// An import of a list exported from somewhere else.
    /// </summary>
    Import = 3,
}
