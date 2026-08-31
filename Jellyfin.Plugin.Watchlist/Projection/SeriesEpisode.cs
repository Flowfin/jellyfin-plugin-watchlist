using System;

namespace Jellyfin.Plugin.Watchlist.Projection;

/// <summary>
/// One episode of a series, as much of it as the rule choosing a row needs.
/// </summary>
/// <remarks>
/// <para>
/// Four values and not a library item. The rule that picks which episode a series
/// projects as reads a season number, an episode number and whether this user has
/// played it, and a rule handed a server type could only be driven with a server
/// present. What the suite drives instead is this record, and the one class that
/// turns library items into it is the seam below the rule.
/// </para>
/// <para>
/// The two numbers are nullable because the library's are. An episode outside a
/// numbered season and an episode with no number of its own are both things a library
/// holds, and a rule that assumed otherwise would throw on the first show somebody
/// scanned from a folder of loose files.
/// </para>
/// </remarks>
public sealed record SeriesEpisode
{
    /// <summary>
    /// Gets the library item this episode is.
    /// </summary>
    public required Guid ItemId { get; init; }

    /// <summary>
    /// Gets the season it sits in, or null where the library holds no number for it.
    /// </summary>
    public required int? SeasonNumber { get; init; }

    /// <summary>
    /// Gets its number inside that season, or null where the library holds none.
    /// </summary>
    public required int? EpisodeNumber { get; init; }

    /// <summary>
    /// Gets a value indicating whether this user has played it.
    /// </summary>
    public required bool IsPlayed { get; init; }
}
