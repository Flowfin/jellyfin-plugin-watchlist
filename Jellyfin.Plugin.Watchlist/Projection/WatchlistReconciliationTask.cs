using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Watchlist.Configuration;
using MediaBrowser.Model.Tasks;

namespace Jellyfin.Plugin.Watchlist.Projection;

/// <summary>
/// The scheduled entry an administrator sees, and the trigger that runs the pass
/// without anybody asking.
/// </summary>
/// <remarks>
/// <para>
/// EVENTS ARE MISSED, WHICH IS WHAT THIS EXISTS FOR. A server restarts mid-write, a
/// client edits a playlist while the plugin is disabled, an item is removed during a
/// scan. Nothing about those cases is recoverable by the route that missed them, so
/// something has to converge the two sides on its own.
/// </para>
/// <para>
/// It is a shell and holds no rule. Everything a run does is
/// <see cref="WatchlistProjectionPass"/>, which the suite drives with no server; what
/// is here is the name, the description, the key, the category and the trigger, and
/// those are exactly the parts a server has to be running to observe. The split is why
/// the conditions about what a run writes are testable at all.
/// </para>
/// <para>
/// THE INTERVAL IS THE SETTING RATHER THAN A NUMBER WRITTEN HERE, and the reason for
/// the default is at <see cref="PluginConfiguration.DefaultReconciliationIntervalHours"/>
/// where the setting lives. A number in this file would be a second copy of a value an
/// administrator can change, and the two would disagree the first time anybody did.
/// </para>
/// <para>
/// The key is fixed and spelled out. The server stores an administrator's trigger
/// changes against it, so a key derived from a type name would silently discard those
/// the day a class is renamed.
/// </para>
/// </remarks>
public sealed class WatchlistReconciliationTask : IScheduledTask
{
    private readonly WatchlistProjectionPass _pass;
    private readonly Func<PluginConfiguration> _configuration;

    /// <summary>
    /// Initializes a new instance of the <see cref="WatchlistReconciliationTask"/> class.
    /// </summary>
    /// <param name="pass">What a run does.</param>
    /// <param name="configuration">The server's settings, asked for rather than held,
    /// because the server replaces the object whenever the page is saved.</param>
    /// <exception cref="ArgumentNullException">Any argument is null.</exception>
    public WatchlistReconciliationTask(WatchlistProjectionPass pass, Func<PluginConfiguration> configuration)
    {
        ArgumentNullException.ThrowIfNull(pass);
        ArgumentNullException.ThrowIfNull(configuration);

        _pass = pass;
        _configuration = configuration;
    }

    /// <inheritdoc />
    public string Name => "Reconcile watchlist playlists";

    /// <inheritdoc />
    /// <remarks>
    /// It says what a run converges rather than what it is called again. An
    /// administrator meeting this row in the dashboard has to be able to decide whether
    /// running it now would help, and the answer is that it makes every user's playlist
    /// agree with their stored list.
    /// </remarks>
    public string Description =>
        "Makes every user's projected playlist agree with the watchlist stored for them, "
        + "which is what converges a change that an event missed. A run over a server whose "
        + "playlists are already correct writes nothing.";

    /// <inheritdoc />
    public string Category => "Watchlist";

    /// <inheritdoc />
    public string Key => "WatchlistReconciliation";

    /// <inheritdoc />
    /// <remarks>
    /// One trigger, an interval, taken from the setting. A server that has never had the
    /// page saved gets the default the configuration declares, and an administrator who
    /// changes the trigger in the dashboard keeps their change: this is what the server
    /// uses when it has none of its own stored against the key above.
    ///
    /// An interval of zero or less would be a trigger the server cannot honour, so the
    /// default stands in for it. Validation refuses such a value on the way in; this is
    /// the second half of the same rule, for a configuration file somebody edited by
    /// hand.
    /// </remarks>
    public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
    {
        var hours = _configuration().ReconciliationIntervalHours;

        if (hours <= 0)
        {
            hours = PluginConfiguration.DefaultReconciliationIntervalHours;
        }

        return
        [
            new TaskTriggerInfo
            {
                Type = TaskTriggerInfoType.IntervalTrigger,
                IntervalTicks = TimeSpan.FromHours(hours).Ticks,
            },
        ];
    }

    /// <inheritdoc />
    /// <remarks>
    /// The progress and the token are handed straight through. Both are the server's,
    /// and a task that swallowed either would be one the dashboard cannot show and a
    /// shutdown cannot stop.
    /// </remarks>
    public Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken) =>
        _pass.RunAsync(progress, cancellationToken);
}
