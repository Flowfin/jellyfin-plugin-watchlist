using System;
using System.Collections.Generic;
using Jellyfin.Plugin.Watchlist.Store;

namespace Jellyfin.Plugin.Watchlist.Watched;

/// <summary>
/// Which entries of one user's list a played item retires. The whole rule, and
/// nothing else.
/// </summary>
/// <remarks>
/// <para>
/// It is a function of the entries, the played item and one answer about the series,
/// so it holds no state, reads no clock and touches no server type. Everything that
/// needs a server is asked through <see cref="ISeriesCompletion"/> before this decides
/// anything.
/// </para>
/// <para>
/// The shared list is not reachable from here and that is deliberate rather than an
/// omission: the watched rule never prunes it. Watched is individual, and taking a
/// title off a list everybody sees because one person finished it would take from one
/// person what another still wants to see.
/// </para>
/// </remarks>
public static class WatchedRemoval
{
    /// <summary>
    /// The entries that leave this user's list because that item was played.
    /// </summary>
    /// <param name="entries">The user's entries.</param>
    /// <param name="played">What was played.</param>
    /// <param name="userId">The user the event named.</param>
    /// <param name="series">The answer about a series being finished.</param>
    /// <returns>The identifiers to remove, in the order the entries are held.</returns>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    public static IReadOnlyList<Guid> EntriesRetiredBy(
        IReadOnlyList<WatchlistEntry> entries,
        WatchedItem played,
        Guid userId,
        ISeriesCompletion series)
    {
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentNullException.ThrowIfNull(played);
        ArgumentNullException.ThrowIfNull(series);

        var retired = new List<Guid>();

        foreach (var entry in entries)
        {
            if (!Names(entry, played))
            {
                continue;
            }

            // A series entry is the one case where the played item is not the whole
            // answer. Finishing one episode names the series and retires nothing of
            // it; finishing the last one names the same series and retires it.
            if (entry.Kind == WatchlistItemKind.Series
                && !series.EveryEpisodeIsPlayed(entry.ItemId, userId))
            {
                continue;
            }

            retired.Add(entry.ItemId);
        }

        return retired;
    }

    /// <summary>
    /// Whether the played item reaches this entry at all.
    /// </summary>
    /// <param name="entry">The entry.</param>
    /// <param name="played">What was played.</param>
    /// <returns>True when the entry is the played item, or the series it belongs to.</returns>
    private static bool Names(WatchlistEntry entry, WatchedItem played) =>
        entry.ItemId == played.ItemId
        || (played.SeriesId is Guid seriesId && entry.ItemId == seriesId);
}
