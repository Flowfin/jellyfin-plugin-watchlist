using System;
using System.Collections.Generic;

namespace Jellyfin.Plugin.Watchlist.Projection;

/// <summary>
/// The episodes of one series, as one user sees them.
/// </summary>
/// <remarks>
/// <para>
/// A seam for the same reason <see cref="Api.IWatchlistItemDescriber"/> is one: the
/// rule above it becomes a function of what it is told, and the suite drives every
/// case of that rule with no server present. What is behind it on a running server is
/// a library query, and what is behind it in the suite is a table.
/// </para>
/// <para>
/// FOR THIS USER, and that is not a convenience of the signature. An episode in a
/// library this user was never given, or above a rating they are held below, must not
/// become the row their playlist carries, and whether they have played one is a fact
/// about them rather than about the series. Both answers are per user, so the user is
/// asked about here rather than filtered afterwards.
/// </para>
/// </remarks>
public interface ISeriesEpisodes
{
    /// <summary>
    /// The episodes this series holds for this user.
    /// </summary>
    /// <param name="seriesId">The series.</param>
    /// <param name="userId">The user the question is asked for.</param>
    /// <returns>
    /// The episodes, in any order. Empty where the series holds none this user can
    /// see, and empty where the library cannot answer for it at all: a series that
    /// produces no episode produces no playlist row, which is what this issue's own
    /// condition asks for and is safe in a way an exception is not.
    /// </returns>
    IReadOnlyList<SeriesEpisode> Of(Guid seriesId, Guid userId);
}
