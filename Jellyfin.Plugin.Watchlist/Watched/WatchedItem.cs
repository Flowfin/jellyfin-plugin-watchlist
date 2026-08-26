using System;
using Jellyfin.Plugin.Watchlist.Store;

namespace Jellyfin.Plugin.Watchlist.Watched;

/// <summary>
/// What the server said was played, in this plugin's own vocabulary.
/// </summary>
/// <remarks>
/// The event the server raises carries a library item, and a library item drags the
/// whole server type graph behind it. The rule that decides which entries leave a list
/// takes this instead, so it can be driven from a table and the one place that knows
/// there is a library is the adapter that builds this.
/// </remarks>
public sealed record WatchedItem
{
    /// <summary>
    /// Gets the item that was played.
    /// </summary>
    public required Guid ItemId { get; init; }

    /// <summary>
    /// Gets what the library holds it as.
    /// </summary>
    public required WatchlistItemKind Kind { get; init; }

    /// <summary>
    /// Gets the series the played item belongs to, where it is an episode, and null
    /// otherwise.
    /// </summary>
    public Guid? SeriesId { get; init; }
}
