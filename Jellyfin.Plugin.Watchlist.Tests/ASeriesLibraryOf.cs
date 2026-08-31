using System;
using System.Collections.Generic;
using System.Linq;
using Jellyfin.Plugin.Watchlist.Projection;

namespace Jellyfin.Plugin.Watchlist.Tests;

/// <summary>
/// The episodes of a series, out of a table, with what it was asked kept.
/// </summary>
/// <remarks>
/// The questions are kept as well as answered because two of the rules this stands
/// under are about a question NOT being asked. A list holding no series has no series
/// to ask about, and a film on a list is not a folder to be looked inside; a fake that
/// only answered could not tell a rule that skipped the question from one that asked it
/// and threw the answer away.
///
/// A series that is not in the table answers with no episodes, which is the interface's
/// own rule rather than a shortcut here: a series the library cannot answer for and one
/// holding nothing this user may see are one answer.
/// </remarks>
internal sealed class ASeriesLibraryOf : ISeriesEpisodes
{
    private readonly Dictionary<(Guid SeriesId, Guid UserId), List<SeriesEpisode>> _table = [];

    private readonly List<(Guid SeriesId, Guid UserId)> _asked = [];

    /// <summary>
    /// Gets what this was asked, in order.
    /// </summary>
    public IReadOnlyList<(Guid SeriesId, Guid UserId)> Asked => _asked;

    /// <summary>
    /// Puts one episode of one series in front of one user.
    /// </summary>
    /// <param name="seriesId">The series it belongs to.</param>
    /// <param name="userId">The user who can see it.</param>
    /// <param name="episode">The episode.</param>
    /// <returns>This, so a series can be described in one expression.</returns>
    public ASeriesLibraryOf Holding(Guid seriesId, Guid userId, SeriesEpisode episode)
    {
        if (!_table.TryGetValue((seriesId, userId), out var episodes))
        {
            episodes = [];
            _table[(seriesId, userId)] = episodes;
        }

        episodes.Add(episode);

        return this;
    }

    /// <inheritdoc />
    public IReadOnlyList<SeriesEpisode> Of(Guid seriesId, Guid userId)
    {
        _asked.Add((seriesId, userId));

        return _table.TryGetValue((seriesId, userId), out var episodes) ? [.. episodes] : [];
    }
}
