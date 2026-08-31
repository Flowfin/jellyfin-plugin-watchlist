using System;
using System.Collections.Generic;
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
        IWatchlistItemDescriber describer,
        ISeriesEpisodes episodes,
        TimeProvider clock,
        Func<PluginConfiguration> configuration,
        ILogger<WatchlistProjectionPass> logger)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(projector);
        ArgumentNullException.ThrowIfNull(reconciler);
        ArgumentNullException.ThrowIfNull(describer);
        ArgumentNullException.ThrowIfNull(episodes);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(logger);

        _store = store;
        _projector = projector;
        _reconciler = reconciler;
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

        foreach (var userId in users)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var outcome = await OneUserAsync(userId, configuration, cancellationToken).ConfigureAwait(false);

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
    /// One user's projection, made to agree with their list.
    /// </summary>
    /// <param name="userId">The user.</param>
    /// <param name="configuration">The settings this run took at its start.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>What this user cost the run.</returns>
    private async Task<(int Created, int Writes, int Skipped)> OneUserAsync(
        Guid userId,
        PluginConfiguration configuration,
        CancellationToken cancellationToken)
    {
        var target = UserProjectionTarget.For(_store, configuration, _describer, _episodes, _clock, userId);

        var projection = await _projector.EnsurePlaylistAsync(target, cancellationToken).ConfigureAwait(false);

        if (projection.Projection is null)
        {
            return (0, 0, 1);
        }

        var reconciliation = await _reconciler
            .ReconcileAsync(target, projection.Projection.PlaylistId, cancellationToken)
            .ConfigureAwait(false);

        var created = projection.Outcome == ProjectionOutcome.Created ? 1 : 0;

        // A creation, a rename and an adoption are all writes the server saw, and so is
        // every row this pass moved. They are counted into one number because what the
        // third condition asks is whether an already-correct server was touched at all,
        // and a count that left the creation out would answer zero for a pass that made
        // a playlist.
        var writes = (projection.Outcome == ProjectionOutcome.AlreadyProjected ? 0 : 1)
            + reconciliation.Added
            + reconciliation.Removed;

        return (created, writes, 0);
    }
}
