using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Watchlist.Api;
using Jellyfin.Plugin.Watchlist.Configuration;
using Jellyfin.Plugin.Watchlist.Store;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Watchlist.Projection;

/// <summary>
/// One run over every projection this server holds: each user gets a playlist and that
/// playlist gets what their list says it should hold.
/// </summary>
/// <remarks>
/// <para>
/// This is the whole of what the scheduled task does, and it is separate from the task
/// so that every condition the task owes is driven with no server present. What the
/// task adds is a name, a description, a trigger and a progress report; a pass is what
/// the suite can run.
/// </para>
/// <para>
/// THE POPULATION IS THE STORE'S. It walks the users the store holds a document for,
/// not the users the server has, so a server where the plugin is installed and unused
/// does no work at all. A user with no document has nothing to project, and a pass
/// driven from the server's user list would read nothing and move on once per user per
/// run to learn that.
/// </para>
/// <para>
/// ONE USER AT A TIME, and that is a bound rather than a style. A run reads one
/// document, reconciles one playlist and moves on, so the memory a pass costs does not
/// grow with the number of users and a server with a thousand of them is a thousand
/// small pieces of work rather than one large one. Nothing here starts a second task.
/// </para>
/// <para>
/// A CORRECT SERVER COSTS NO WRITE. Both halves below already have that property - the
/// projector creates only where a target has no playlist the server still holds, and
/// the reconciler writes only where the playlist differs from the list - so this adds
/// nothing to make it true and only counts what happened. That is what makes a
/// scheduled pass safe to run four times a day.
/// </para>
/// <para>
/// ONE USER'S FAILURE IS NOT THE RUN'S. A document that cannot be read or a record that
/// cannot be written is counted and stepped over. A pass that stopped there would leave
/// every user after them unreconciled, and the one who noticed would be the last user
/// in the folder.
/// </para>
/// </remarks>
public sealed class WatchlistProjectionPass
{
    private readonly WatchlistDocumentStore _store;
    private readonly WatchlistProjector _projector;
    private readonly WatchlistReconciler _reconciler;
    private readonly IPlaylistGateway _playlists;
    private readonly IWatchlistItemDescriber _describer;
    private readonly ISeriesEpisodes _episodes;
    private readonly TimeProvider _clock;
    private readonly Func<PluginConfiguration> _configuration;
    private readonly ILogger<WatchlistProjectionPass> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="WatchlistProjectionPass"/> class.
    /// </summary>
    /// <param name="store">The store, which is also where the population comes from.</param>
    /// <param name="projector">Which playlist a list belongs in.</param>
    /// <param name="reconciler">What is inside that playlist.</param>
    /// <param name="playlists">The seam the rows are read through, before the list is
    /// reconciled against them.</param>
    /// <param name="describer">What a library item is, for one user.</param>
    /// <param name="episodes">What a series holds, for one user.</param>
    /// <param name="clock">The clock an adopted entry is stamped from.</param>
    /// <param name="configuration">The server's settings, asked for rather than held,
    /// because the server replaces the object whenever the page is saved and a value
    /// captured here would be the one in force when the pass was constructed.</param>
    /// <param name="logger">The logger.</param>
    /// <exception cref="ArgumentNullException">Any argument is null.</exception>
    public WatchlistProjectionPass(
        WatchlistDocumentStore store,
        WatchlistProjector projector,
        WatchlistReconciler reconciler,
        IPlaylistGateway playlists,
        IWatchlistItemDescriber describer,
        ISeriesEpisodes episodes,
        TimeProvider clock,
        Func<PluginConfiguration> configuration,
        ILogger<WatchlistProjectionPass> logger)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(projector);
        ArgumentNullException.ThrowIfNull(reconciler);
        ArgumentNullException.ThrowIfNull(playlists);
        ArgumentNullException.ThrowIfNull(describer);
        ArgumentNullException.ThrowIfNull(episodes);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(logger);

        _store = store;
        _projector = projector;
        _reconciler = reconciler;
        _playlists = playlists;
        _describer = describer;
        _episodes = episodes;
        _clock = clock;
        _configuration = configuration;
        _logger = logger;
    }

    /// <summary>
    /// Runs one pass over every user this store holds a document for.
    /// </summary>
    /// <param name="progress">Where the share of the users done so far is reported, or
    /// null where nobody is watching.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>What the pass did, in counts.</returns>
    /// <remarks>
    /// The token is honoured between users rather than inside one. A user half
    /// reconciled is a playlist with some of its rows, and the next pass converges it;
    /// what the token is for is a server shutting down, and stopping between two users
    /// is as fast as that needs to be.
    ///
    /// The settings are read once, at the start of the run, so a save halfway through
    /// does not give two users different list names in one pass. The next run takes the
    /// new value.
    /// </remarks>
    public async Task<WatchlistProjectionRun> RunAsync(
        IProgress<double>? progress,
        CancellationToken cancellationToken)
    {
        var configuration = _configuration();

        if (!configuration.ProjectionEnabled)
        {
            // Off is off, including for the pass. Turning the projection off has to
            // stop the scheduled run as well as the event route, or a disabled
            // projection is one that still writes to every playlist four times a day.
            _logger.LogDebug("Watchlist reconciliation skipped: the projection is turned off.");

            return new WatchlistProjectionRun { Users = 0, Created = 0, Writes = 0, Skipped = 0 };
        }

        var users = _store.UsersWithADocument();
        var created = 0;
        var writes = 0;
        var skipped = 0;
        var done = 0;

        // The shared list first, and it is one target rather than one per user. A server
        // that has none - the setting is off, or nobody has made one - produces no target
        // here, so there is nothing to make a playlist for and no call at all.
        var shared = SharedProjectionTarget.For(_store, configuration, _describer, _episodes, _clock);

        if (shared is not null)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var outcome = await OneTargetAsync(shared, cancellationToken).ConfigureAwait(false);

            created += outcome.Created;
            writes += outcome.Writes;
            skipped += outcome.Skipped;
        }

        foreach (var userId in users)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var outcome = await OneTargetAsync(
                UserProjectionTarget.For(_store, configuration, _describer, _episodes, _clock, userId),
                cancellationToken).ConfigureAwait(false);

            created += outcome.Created;
            writes += outcome.Writes;
            skipped += outcome.Skipped;

            // The divisor cannot be zero here: this line is inside the walk over those
            // users, so there is at least one. A guard against it would be a branch no
            // input can take and no test can reach.
            done++;
            progress?.Report(done * 100d / users.Count);
        }

        var run = new WatchlistProjectionRun
        {
            Users = users.Count,
            Created = created,
            Writes = writes,
            Skipped = skipped,
        };

        _logger.LogInformation(
            "Watchlist reconciliation finished: {Users} users, {Created} playlists created, {Writes} playlist writes, {Skipped} skipped.",
            run.Users,
            run.Created,
            run.Writes,
            run.Skipped);

        return run;
    }

    /// <summary>
    /// One target: its playlist made, opened where it has to be, the edits somebody made
    /// to it taken back into the list, and the list written into it.
    /// </summary>
    /// <param name="target">The list being projected.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>What this target cost the run.</returns>
    /// <remarks>
    /// ONE ROUTE FOR BOTH KINDS OF LIST. A user's own and the one the server shares reach
    /// this by different constructors and differ in nothing it does: the difference
    /// calculation, the ordering, the series rule and the adoption are the target's
    /// answers rather than this method's, and the one thing they disagree about is
    /// whether everybody may see the playlist, which is a value on the target that this
    /// reads rather than a case it decides.
    /// </remarks>
    private async Task<(int Created, int Writes, int Skipped)> OneTargetAsync(
        IProjectionTarget target,
        CancellationToken cancellationToken)
    {
        var projection = await _projector.EnsurePlaylistAsync(target, cancellationToken).ConfigureAwait(false);

        if (projection.Projection is null)
        {
            return (0, 0, 1);
        }

        var playlistId = projection.Projection.PlaylistId;
        var opened = 0;

        if (target.IsOpenToEveryone && !_playlists.IsOpenToEveryone(playlistId, target.OwnerUserId))
        {
            // Once, and never again: the question above is a read, so a playlist that is
            // already open costs this pass nothing. A projection that set it every time
            // would touch the shared playlist on every scheduled run for nothing.
            await _playlists.OpenToEveryoneAsync(playlistId, target.OwnerUserId, cancellationToken)
                .ConfigureAwait(false);

            opened = 1;
        }

        // THE PLAYLIST IS READ INTO THE LIST BEFORE THE LIST IS WRITTEN INTO THE
        // PLAYLIST, and the order is the whole of it. A pass that reconciled first would
        // undo on a television exactly what somebody had just done there, and they would
        // watch it happen.
        target.TakeEdits(_playlists.EntriesOf(playlistId, target.OwnerUserId)
            .Select(row => row.ItemId)
            .ToList());

        // A target made afresh, because a target is a snapshot taken when it is made and
        // the line above has just changed the record it was made from. Reconciling the
        // first one would write the list as it stood before the edits were taken.
        var written = target.Reread();

        var reconciliation = await _reconciler
            .ReconcileAsync(written, playlistId, cancellationToken)
            .ConfigureAwait(false);

        Record(written, projection.Projection);

        var created = projection.Outcome == ProjectionOutcome.Created ? 1 : 0;

        // A creation, a rename, an adoption and an opening are all writes the server saw,
        // and so is every row this pass moved. They are counted into one number because
        // what the third condition asks is whether an already-correct server was touched
        // at all, and a count that left the creation out would answer zero for a pass
        // that made a playlist.
        var writes = (projection.Outcome == ProjectionOutcome.AlreadyProjected ? 0 : 1)
            + opened
            + reconciliation.Added
            + reconciliation.Removed;

        return (created, writes, 0);
    }

    /// <summary>
    /// Writes down what this pass put in the playlist, so the next one can tell an edit
    /// from a projection.
    /// </summary>
    /// <param name="target">The target that was reconciled.</param>
    /// <param name="projection">The playlist it was reconciled into, as the projector
    /// settled it. It is handed in rather than read off the target again because the
    /// projector has already established that it exists, and reading it back would put a
    /// branch here that no input can take.</param>
    /// <remarks>
    /// WITHOUT THIS EVERY PASS READS EVERY ROW AS SOMEBODY'S ADDITION, which is harmless
    /// on its own - the store refuses the duplicate - and makes a removal on a client
    /// unreadable forever, because nothing is ever recorded as having been written.
    ///
    /// What is recorded is what the target ASKED for rather than what the seam confirmed,
    /// and the difference is a bound rather than a detail. The reconciler answers how
    /// many rows moved and not which, so a pass that failed part way through would record
    /// more than it wrote; it does not fail part way through silently, because a failure
    /// on that seam leaves this method unreached and the record unchanged, and the next
    /// pass then reads the rows it did write as additions rather than as removals. That
    /// is the safe direction again.
    ///
    /// A record that cannot be written is not an error here. It is the same unavailable
    /// document the pass already counts, and the next pass meets it at the top.
    /// </remarks>
    private void Record(IProjectionTarget target, WatchlistProjectionState projection) =>
        target.Remember(projection with
        {
            ProjectedItemIds = target.Wanted,
            WrittenAt = _clock.GetUtcNow(),
        });
}
