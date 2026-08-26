using System;

namespace Jellyfin.Plugin.Watchlist.Watched;

/// <summary>
/// Answers whether one user has played every episode of a series.
/// </summary>
/// <remarks>
/// A single finished episode must not take a whole series off a list, so the rule
/// needs an answer about the rest of the series, and that answer is only in the
/// library. It is one question behind an interface for the same reason
/// <see cref="Api.IWatchlistItemDescriber"/> is: the rule above it is then a function
/// of what it is told, and the suite drives it with no server present.
/// </remarks>
public interface ISeriesCompletion
{
    /// <summary>
    /// Whether this user has played every episode the series holds.
    /// </summary>
    /// <param name="seriesId">The series.</param>
    /// <param name="userId">The user.</param>
    /// <returns>
    /// True when the series holds at least one episode and this user has played all of
    /// them. A series the library cannot answer for is false, because the rule this
    /// serves removes an entry and an unanswerable question must not remove anything.
    /// </returns>
    bool EveryEpisodeIsPlayed(Guid seriesId, Guid userId);
}
