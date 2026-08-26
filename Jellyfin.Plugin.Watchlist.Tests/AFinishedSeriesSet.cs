using System;
using System.Collections.Generic;
using Jellyfin.Plugin.Watchlist.Watched;

namespace Jellyfin.Plugin.Watchlist.Tests;

/// <summary>
/// The completion answer, out of a set, with what it was asked kept.
/// </summary>
/// <remarks>
/// The questions are kept as well as answered, because two of the rules this stands
/// under are about a question NOT being asked: a played movie has no series to ask
/// about, and a list holding no series entry has nothing to ask about either. A fake
/// that only answers cannot tell a rule that skipped the question from one that asked
/// it and ignored the answer.
/// </remarks>
internal sealed class AFinishedSeriesSet : ISeriesCompletion
{
    private readonly HashSet<Guid> _finished;

    private readonly List<(Guid SeriesId, Guid UserId)> _asked = [];

    /// <summary>
    /// Initializes a new instance of the <see cref="AFinishedSeriesSet"/> class.
    /// </summary>
    /// <param name="finished">The series this user has played every episode of.</param>
    public AFinishedSeriesSet(params Guid[] finished)
    {
        _finished = [.. finished];
    }

    /// <summary>
    /// Gets what this was asked, in order.
    /// </summary>
    public IReadOnlyList<(Guid SeriesId, Guid UserId)> Asked => _asked;

    /// <inheritdoc />
    public bool EveryEpisodeIsPlayed(Guid seriesId, Guid userId)
    {
        _asked.Add((seriesId, userId));

        return _finished.Contains(seriesId);
    }
}
