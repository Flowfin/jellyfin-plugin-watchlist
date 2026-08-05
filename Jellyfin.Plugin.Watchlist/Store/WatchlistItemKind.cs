namespace Jellyfin.Plugin.Watchlist.Store;

/// <summary>
/// What kind of thing an entry points at. Stored with the entry rather than looked
/// up when the list is read, so an entry whose item has been deleted from the
/// library can still be shown and counted.
/// </summary>
public enum WatchlistItemKind
{
    /// <summary>
    /// The kind was not recorded. Only reachable by reading a document written by
    /// something that did not set it, which the serialisation refuses.
    /// </summary>
    Unknown = 0,

    /// <summary>
    /// A film.
    /// </summary>
    Movie = 1,

    /// <summary>
    /// A whole series. The store can hold one; the projected playlist cannot, which
    /// is the cost recorded in docs/storage-decision.md.
    /// </summary>
    Series = 2,

    /// <summary>
    /// A single episode.
    /// </summary>
    Episode = 3,

    /// <summary>
    /// Anything else the library holds.
    /// </summary>
    Other = 4,
}
