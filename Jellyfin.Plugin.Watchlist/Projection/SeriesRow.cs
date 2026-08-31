using System;
using System.Collections.Generic;
using System.Linq;

namespace Jellyfin.Plugin.Watchlist.Projection;

/// <summary>
/// Which single episode a series on a list appears as in the projected playlist.
/// </summary>
/// <remarks>
/// <para>
/// A SERIES CANNOT BE A PLAYLIST ROW, WHICH IS WHY THIS RULE EXISTS RATHER THAN A
/// SIMPLER ONE. A server handed a folder puts its non-folder children in, so an entry
/// for a show would become every episode of it, and a playlist of four hundred rows is
/// not what somebody who added one show asked for. Leaving shows out is the other way
/// of avoiding that and is refused: a watchlist without shows is not the feature this
/// plugin is for.
/// </para>
/// <para>
/// SO THE RULE IS ONE ROW, AND THE ROW IS WHERE THE USER WOULD RESUME. The earliest
/// episode they have not played, by season and then by episode number, and the first
/// episode where they have played nothing - which is the same sentence, because the
/// first episode is the earliest unplayed one when nothing is played.
/// </para>
/// <para>
/// A SERIES WHOSE EPISODES ARE ALL PLAYED STILL PROJECTS, AND IT PROJECTS AS THE
/// FIRST. This is the case the rule above says nothing about and it has to be total,
/// because a show sits on a list until something takes it off and the watched rule
/// only takes it off when the setting says so. The alternative - no row at all - makes
/// a show vanish from the playlist while staying on the list, which is the disappearance
/// this whole issue exists against, and it would be invisible rather than wrong. So the
/// list keeps its show and the row is the beginning of it.
/// </para>
/// <para>
/// THE ORDER IS TOTAL AND A MISSING NUMBER SORTS LAST. A library holds episodes with no
/// number and seasons with no number, from a folder somebody scanned or from a show
/// nobody has matched yet, and an order that is merely usually the same makes a
/// reconciliation pass rebuild a playlist for no reason. A number that is absent sorts
/// behind every number that is present, and the item identifier breaks whatever is left,
/// so two runs over one library always choose the same episode.
/// </para>
/// <para>
/// SEASON ZERO IS A SEASON AND SORTS FIRST. Specials are usually numbered zero, so a
/// user with an unplayed special sees that special rather than the next numbered
/// episode. That follows from the rule being by season number and it is a consequence
/// worth having written down rather than discovered: the rule this issue names is the
/// numeric one, and a specials-last exception would be a second rule nobody asked for.
/// </para>
/// <para>
/// The rule is a function of a list rather than a method on a seam, so every case of it
/// is driven by the suite with no library present. What asks a library is
/// <see cref="ISeriesEpisodes"/>, which decides nothing.
/// </para>
/// </remarks>
public static class SeriesRow
{
    /// <summary>
    /// The one episode this series appears as.
    /// </summary>
    /// <param name="episodes">The episodes the series holds for one user.</param>
    /// <returns>The episode to project, or null where the series holds none.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="episodes"/> is null.</exception>
    public static Guid? OneEpisodeOf(IReadOnlyList<SeriesEpisode> episodes)
    {
        ArgumentNullException.ThrowIfNull(episodes);

        var inOrder = episodes
            .OrderBy(episode => episode.SeasonNumber ?? int.MaxValue)
            .ThenBy(episode => episode.EpisodeNumber ?? int.MaxValue)
            .ThenBy(episode => episode.ItemId)
            .ToList();

        if (inOrder.Count == 0)
        {
            return null;
        }

        var unplayed = inOrder.Find(episode => !episode.IsPlayed);

        return unplayed?.ItemId ?? inOrder[0].ItemId;
    }
}
