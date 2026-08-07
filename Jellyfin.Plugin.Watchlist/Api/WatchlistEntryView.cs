using System;
using Jellyfin.Plugin.Watchlist.Store;

namespace Jellyfin.Plugin.Watchlist.Api;

/// <summary>
/// One entry as the endpoint hands it out: what the store recorded about it, and what
/// the library says about the item it points at.
/// </summary>
/// <remarks>
/// The two halves are kept apart on purpose. <see cref="Kind"/> is what the entry was
/// recorded as when it was added, and the description beside it is what the library
/// holds now, so an item that changed type in the library does not silently rewrite
/// what the list says was put on it.
///
/// It carries enough for a caller to draw a row without asking about each item
/// separately, which is the whole reason it is not a list of identifiers. There is no
/// image tag on it: a client fetches an image by item identifier, so the identifier is
/// the only thing it needs and a tag would be a second copy of state the server owns.
/// </remarks>
public sealed record WatchlistEntryView
{
    /// <summary>
    /// Gets the library item this entry points at.
    /// </summary>
    public required Guid ItemId { get; init; }

    /// <summary>
    /// Gets what the entry was recorded as when it was added.
    /// </summary>
    public required WatchlistItemKind Kind { get; init; }

    /// <summary>
    /// Gets the instant the entry was added, in UTC.
    /// </summary>
    public required DateTimeOffset AddedAt { get; init; }

    /// <summary>
    /// Gets the name the library holds for the item.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Gets the year, where the library has one.
    /// </summary>
    public int? ProductionYear { get; init; }

    /// <summary>
    /// Gets the series an episode belongs to, where the item is one.
    /// </summary>
    public string? SeriesName { get; init; }

    /// <summary>
    /// Gets the season number of an episode, where the item is one.
    /// </summary>
    public int? SeasonNumber { get; init; }

    /// <summary>
    /// Gets the episode number of an episode, where the item is one.
    /// </summary>
    public int? EpisodeNumber { get; init; }
}
