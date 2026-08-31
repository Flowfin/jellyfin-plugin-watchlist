using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Watchlist.Projection;

/// <summary>
/// Brings one playlist to what one list says it should be, in the fewest writes that
/// get there.
/// </summary>
/// <remarks>
/// <para>
/// This is the half of the projection that decides WHAT IS IN a playlist. Which
/// playlist a list belongs in is <see cref="WatchlistProjector"/>, and the two are
/// separate because that question is asked once per target per pass and this one is
/// asked of every row.
/// </para>
/// <para>
/// ONE DIFFERENCE CALCULATION AND NO SECOND ONE. Everything a target could differ in -
/// whose entries these are, who may see them, what an entry of a kind a playlist cannot
/// hold becomes, what the list is called - has already been answered by the time
/// <see cref="IProjectionTarget.Wanted"/> is read. What is left is a set difference and
/// an order, and neither has a private-list spelling and a shared-list spelling. So a
/// shared list is reconciled by this class rather than beside it.
/// </para>
/// <para>
/// THE PASS IS BOUNDED. It reads the wanted set the target already holds and the rows
/// of one playlist, and it asks the library nothing. There is no route through this
/// that walks a library, a user set or a second playlist, which is a property of the
/// shape rather than of a flag: nothing here is given anything it could walk.
/// </para>
/// <para>
/// A CORRECT PLAYLIST COSTS NO WRITE. That is the property the whole class is arranged
/// around, because the pass is scheduled and a pass that wrote every time would touch
/// every user's playlist every interval for nothing, and every one of those writes is
/// something a client re-syncs.
/// </para>
/// <para>
/// THE ORDER IS THE PART THAT IS NOT FREE. The current stable server line appends and
/// cannot insert at a position, so on that line an order that differs from the one the
/// playlist is in can only be reached by building the list again. The reconciler asks
/// the gateway which line it is on, through
/// <see cref="IPlaylistGateway.CanInsertAtAPosition"/>, and takes the answer: where a
/// position is honoured the order is reached by inserting, and where it is not the
/// rebuild happens only when the order is ACTUALLY wrong. A playlist already in the
/// wanted order is never rebuilt on either line.
/// </para>
/// <para>
/// Nothing here reaches a server playlist type. Every call goes through
/// <see cref="IPlaylistGateway"/>, and a guard over the plugin's own sources refuses
/// such a type anywhere but the one implementation of it.
/// </para>
/// </remarks>
public sealed class WatchlistReconciler
{
    private readonly IPlaylistGateway _playlists;
    private readonly ILogger<WatchlistReconciler> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="WatchlistReconciler"/> class.
    /// </summary>
    /// <param name="playlists">The seam every playlist operation goes through.</param>
    /// <param name="logger">The logger.</param>
    public WatchlistReconciler(IPlaylistGateway playlists, ILogger<WatchlistReconciler> logger)
    {
        _playlists = playlists;
        _logger = logger;
    }

    /// <summary>
    /// Makes one playlist hold what one target's list says it should, in that order.
    /// </summary>
    /// <param name="target">The list being projected.</param>
    /// <param name="playlistId">The playlist it is projected into, which
    /// <see cref="WatchlistProjector"/> has already settled.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>What the pass did.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="target"/> is null.</exception>
    public async Task<ReconciliationResult> ReconcileAsync(
        IProjectionTarget target,
        Guid playlistId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(target);

        var wanted = target.Wanted;
        var places = Places(wanted);
        var rows = _playlists.EntriesOf(playlistId, target.OwnerUserId);

        var kept = new List<Guid>();
        var stale = new List<string>();
        var held = new HashSet<Guid>();

        foreach (var row in rows)
        {
            // A row whose item is not wanted goes, and so does a SECOND row pointing at
            // an item that is already held: a playlist can carry one film twice, the
            // list cannot, and the copy is what the difference names rather than both.
            if (places.ContainsKey(row.ItemId) && held.Add(row.ItemId))
            {
                kept.Add(row.ItemId);
            }
            else
            {
                stale.Add(row.EntryId);
            }
        }

        var missing = wanted.Where(itemId => !held.Contains(itemId)).ToList();

        if (!OrderIsReachable(kept, places))
        {
            return await RebuildAsync(playlistId, target.OwnerUserId, wanted, rows, cancellationToken)
                .ConfigureAwait(false);
        }

        if (stale.Count > 0)
        {
            await _playlists.RemoveAsync(playlistId, stale, cancellationToken).ConfigureAwait(false);
        }

        if (missing.Count > 0)
        {
            await AddAsync(playlistId, target.OwnerUserId, missing, places, cancellationToken)
                .ConfigureAwait(false);
        }

        return new ReconciliationResult
        {
            Added = missing.Count,
            Removed = stale.Count,
            Rebuilt = false,
        };
    }

    /// <summary>
    /// Where each wanted item belongs, by item.
    /// </summary>
    /// <param name="wanted">The wanted items, in order.</param>
    /// <returns>The position of each.</returns>
    private static Dictionary<Guid, int> Places(IReadOnlyList<Guid> wanted)
    {
        var places = new Dictionary<Guid, int>(wanted.Count);

        for (var at = 0; at < wanted.Count; at++)
        {
            places[wanted[at]] = at;
        }

        return places;
    }

    /// <summary>
    /// Whether the rows that are staying can end up in the wanted order without the
    /// list being built again.
    /// </summary>
    /// <param name="kept">The items of the rows that stay, in the order the playlist
    /// holds them.</param>
    /// <param name="places">Where each wanted item belongs.</param>
    /// <returns>True where adds alone reach the wanted order.</returns>
    /// <remarks>
    /// Two conditions, and the second is the one the server line decides.
    ///
    /// Nothing this gateway offers MOVES a row, so the rows that stay have to be in the
    /// wanted order relative to each other already. Where they are not, no sequence of
    /// adds reaches the order and the list is built again.
    ///
    /// Where a position is honoured that is the whole test, because a missing item can
    /// be put exactly where it belongs. Where it is not, an add lands at the end, so
    /// the rows that stay have to be the FIRST of the wanted items as well as in their
    /// order - anything else would need an item placed before a row that is already
    /// there. An empty playlist passes both, which is why a first pass writes the list
    /// once and rebuilds nothing.
    /// </remarks>
    private bool OrderIsReachable(List<Guid> kept, Dictionary<Guid, int> places)
    {
        var previous = -1;

        for (var at = 0; at < kept.Count; at++)
        {
            var place = places[kept[at]];

            if (place <= previous)
            {
                return false;
            }

            if (!_playlists.CanInsertAtAPosition && place != at)
            {
                return false;
            }

            previous = place;
        }

        return true;
    }

    /// <summary>
    /// Puts the missing items in, in as few calls as the gateway allows.
    /// </summary>
    /// <param name="playlistId">The playlist.</param>
    /// <param name="ownerUserId">The user the change is made as.</param>
    /// <param name="missing">The items with no row, in wanted order.</param>
    /// <param name="places">Where each wanted item belongs.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task.</returns>
    /// <remarks>
    /// Where a position is not honoured the whole set goes in one appending call, which
    /// is correct precisely because the caller has already established that appending
    /// reaches the order.
    ///
    /// Where it is, items that belong next to each other go in together and a call is
    /// spent only where the run breaks. The runs are inserted from the front, and that
    /// is what makes a wanted position and a playlist position the same number: by the
    /// time a run is inserted, every wanted item before it is already in the list and
    /// in order, so the count of rows ahead of the run is its own place.
    /// </remarks>
    private async Task AddAsync(
        Guid playlistId,
        Guid ownerUserId,
        List<Guid> missing,
        Dictionary<Guid, int> places,
        CancellationToken cancellationToken)
    {
        if (!_playlists.CanInsertAtAPosition)
        {
            await _playlists.AddAsync(playlistId, ownerUserId, missing, null, cancellationToken)
                .ConfigureAwait(false);

            return;
        }

        var run = new List<Guid>();
        var from = 0;

        for (var at = 0; at < missing.Count; at++)
        {
            var place = places[missing[at]];

            if (run.Count > 0 && place != from + run.Count)
            {
                await _playlists.AddAsync(playlistId, ownerUserId, run, from, cancellationToken)
                    .ConfigureAwait(false);
                run = [];
            }

            if (run.Count == 0)
            {
                from = place;
            }

            run.Add(missing[at]);
        }

        await _playlists.AddAsync(playlistId, ownerUserId, run, from, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Empties the playlist and writes the wanted list into it in order.
    /// </summary>
    /// <param name="playlistId">The playlist.</param>
    /// <param name="ownerUserId">The user the change is made as.</param>
    /// <param name="wanted">The wanted items, in order.</param>
    /// <param name="rows">Every row the playlist holds now.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>What the pass did.</returns>
    /// <remarks>
    /// This is the expensive outcome and it is logged, because a server that rebuilds
    /// the same playlist every interval has an ordering rule fighting something else
    /// and the only place that would show is here.
    ///
    /// The removal names every row rather than only the ones that are wrong, which is
    /// what makes the write that follows a plain append in the wanted order on a line
    /// that cannot insert.
    /// </remarks>
    private async Task<ReconciliationResult> RebuildAsync(
        Guid playlistId,
        Guid ownerUserId,
        IReadOnlyList<Guid> wanted,
        IReadOnlyList<ProjectedPlaylistEntry> rows,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "Rebuilding playlist {PlaylistId} of user {UserId}: {Rows} rows are not in the wanted order and this line cannot move one",
            playlistId,
            ownerUserId,
            rows.Count);

        await _playlists
            .RemoveAsync(playlistId, rows.Select(row => row.EntryId).ToList(), cancellationToken)
            .ConfigureAwait(false);

        await _playlists
            .AddAsync(playlistId, ownerUserId, wanted, null, cancellationToken)
            .ConfigureAwait(false);

        return new ReconciliationResult
        {
            Added = wanted.Count,
            Removed = rows.Count,
            Rebuilt = true,
        };
    }
}
