namespace Jellyfin.Plugin.Watchlist.Api;

/// <summary>
/// What the library says about one item, for one user, at the moment it is asked.
/// </summary>
/// <remarks>
/// None of this is stored. The document holds identifiers and nothing else, on
/// purpose, because a title or a path written into the list is a second copy of
/// something the server owns and it is wrong the moment the media is renamed. So the
/// list is read from the store and described from the library on the way out.
///
/// It is per user rather than per item. Two users asking about one identifier can get
/// different answers, because one of them may not be allowed to see it, and a
/// description that ignored that would be the whole leak this API has to avoid.
/// </remarks>
public sealed record WatchlistItemDescription
{
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
