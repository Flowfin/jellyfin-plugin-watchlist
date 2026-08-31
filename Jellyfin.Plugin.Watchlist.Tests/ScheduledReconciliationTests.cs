using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Watchlist.Configuration;
using Jellyfin.Plugin.Watchlist.Projection;
using Jellyfin.Plugin.Watchlist.Store;
using MediaBrowser.Model.Tasks;
using Xunit;

namespace Jellyfin.Plugin.Watchlist.Tests;

/// <summary>
/// The pass that converges what the events missed: what it costs on a correct server,
/// what it does twice, and what it says afterwards.
/// </summary>
/// <remarks>
/// Every test drives the pass rather than the scheduled shell around it, except the
/// three that are about the shell. Nothing here needs a server, a library or a file
/// outside its own temporary directory, and no test reads the machine clock.
/// </remarks>
public sealed class ScheduledReconciliationTests : IDisposable
{
    private static readonly Guid AUser = Guid.Parse("55555555-5555-5555-5555-555555555555");

    private static readonly Guid AnotherUser = Guid.Parse("66666666-6666-6666-6666-666666666666");

    private static readonly Guid AFilm = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");

    private static readonly Guid AnotherFilm = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000002");

    private readonly TemporaryDirectory _sandbox = new("watchlist-scheduled-pass");

    private string DataFolder => Path.Join(_sandbox.FullPath, "plugin-data");

    /// <inheritdoc />
    public void Dispose()
    {
        _sandbox.Dispose();
    }

    /// <summary>
    /// Every user the store holds a document for is reconciled, and each of them ends up
    /// with their own playlist holding their own list.
    /// </summary>
    [Fact]
    public async Task EveryUserWithADocumentIsReconciled()
    {
        var store = AStore();
        Add(store, AUser, AFilm);
        Add(store, AnotherUser, AnotherFilm);

        var server = new APlaylistServerOf();
        var run = await Pass(store, server).RunAsync(null, CancellationToken.None);

        Assert.Equal(2, run.Users);
        Assert.Equal(2, run.Created);
        Assert.Equal(0, run.Skipped);

        Assert.Equal(new[] { AFilm }, RowsOf(store, server, AUser));
        Assert.Equal(new[] { AnotherFilm }, RowsOf(store, server, AnotherUser));
    }

    /// <summary>
    /// A server where the plugin is installed and unused costs nothing. The population
    /// is the store's rather than the server's, so there is no user to read a document
    /// for and no playlist call at all.
    /// </summary>
    [Fact]
    public async Task AServerNobodyHasUsedCostsNoCall()
    {
        var server = new APlaylistServerOf();

        var run = await Pass(AStore(), server).RunAsync(null, CancellationToken.None);

        Assert.Equal(0, run.Users);
        Assert.Empty(server.Calls);
    }

    /// <summary>
    /// The shared list's document sits in the same folder under a name of its own, and
    /// it is not a user. A pass that read every file in the folder as a user would
    /// project a list for something that is not one, once per run.
    /// </summary>
    [Fact]
    public async Task TheSharedListsDocumentIsNotMistakenForAUser()
    {
        var store = AStore();
        Add(store, AUser, AFilm);
        Assert.True(store.CreateShared(Guid.NewGuid(), AUser));

        var run = await Pass(store, new APlaylistServerOf()).RunAsync(null, CancellationToken.None);

        Assert.Equal(1, run.Users);
    }

    /// <summary>
    /// The second run is the one this whole shape exists for. A pass over a server whose
    /// projections are already correct issues no write, counted on the fake rather than
    /// argued, which is what makes a scheduled run safe four times a day.
    /// </summary>
    [Fact]
    public async Task ASecondRunOverACorrectServerWritesNothing()
    {
        var store = AStore();
        Add(store, AUser, AFilm);
        Add(store, AnotherUser, AnotherFilm);

        var server = new APlaylistServerOf();
        var pass = Pass(store, server);

        var first = await pass.RunAsync(null, CancellationToken.None);
        var writesAfterTheFirst = server.Writes;

        var second = await pass.RunAsync(null, CancellationToken.None);

        Assert.True(first.Writes > 0);
        Assert.Equal(0, second.Writes);
        Assert.Equal(writesAfterTheFirst, server.Writes);
    }

    /// <summary>
    /// Making a playlist is a write, even for a user whose list is empty and whose
    /// playlist therefore gets no rows. A count that left the creation out would report
    /// zero for a run that made a playlist on the server for every user it met, which is
    /// the one number the third condition is judged by.
    /// </summary>
    [Fact]
    public async Task MakingAPlaylistIsAWriteEvenWithNoRowsToPutInIt()
    {
        var store = AStore();
        store.Write(WatchlistDocumentStore.Empty(AUser));

        var server = new APlaylistServerOf();
        var run = await Pass(store, server).RunAsync(null, CancellationToken.None);

        Assert.Equal(1, run.Created);
        Assert.Equal(1, run.Writes);
        Assert.Equal(1, server.Writes);
    }

    /// <summary>
    /// And two runs back to back leave the same state as one, which is the other half of
    /// the same property: nothing accumulates, no playlist gains a second copy of a row.
    /// </summary>
    [Fact]
    public async Task TwoRunsLeaveWhatOneRunLeft()
    {
        var store = AStore();
        Add(store, AUser, AFilm);
        Add(store, AUser, AnotherFilm);

        var server = new APlaylistServerOf();
        var pass = Pass(store, server);

        await pass.RunAsync(null, CancellationToken.None);
        var afterOne = RowsOf(store, server, AUser);

        await pass.RunAsync(null, CancellationToken.None);

        Assert.Equal(afterOne, RowsOf(store, server, AUser));
        Assert.Equal(2, afterOne.Length);
    }

    /// <summary>
    /// A change made while nothing was watching is converged by the next run. This is
    /// what the task exists for, stated as a test rather than as a sentence: an entry
    /// added to the store with no event reaching anything still reaches the playlist.
    /// </summary>
    [Fact]
    public async Task AChangeNothingSawIsConvergedByTheNextRun()
    {
        var store = AStore();
        Add(store, AUser, AFilm);

        var server = new APlaylistServerOf();
        var pass = Pass(store, server);

        await pass.RunAsync(null, CancellationToken.None);
        Assert.Equal(new[] { AFilm }, RowsOf(store, server, AUser));

        Add(store, AUser, AnotherFilm);
        await pass.RunAsync(null, CancellationToken.None);

        Assert.Equal(2, RowsOf(store, server, AUser).Length);
    }

    /// <summary>
    /// Progress is reported per user and reaches a hundred. A dashboard showing a task
    /// that never moves is one an administrator cannot tell from a hung one.
    /// </summary>
    [Fact]
    public async Task ProgressIsReportedForEveryUser()
    {
        var store = AStore();
        Add(store, AUser, AFilm);
        Add(store, AnotherUser, AnotherFilm);

        var reported = new List<double>();

        await Pass(store, new APlaylistServerOf())
            .RunAsync(new WhatWasReported(reported), CancellationToken.None);

        Assert.Equal(2, reported.Count);
        Assert.Equal(100d, reported[^1]);
    }

    /// <summary>
    /// A cancelled run stops, and it stops between users rather than inside one. The
    /// token is checked before each user, so a token that was already cancelled reaches
    /// no user at all and makes no playlist call.
    /// </summary>
    [Fact]
    public async Task ACancelledRunStopsAndTouchesNothing()
    {
        var store = AStore();
        Add(store, AUser, AFilm);

        var server = new APlaylistServerOf();

        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => Pass(store, server).RunAsync(null, cancelled.Token));

        Assert.Empty(server.Calls);
    }

    /// <summary>
    /// A user whose record cannot be written is counted and stepped over, and the users
    /// after them are still reconciled. A pass that stopped on one bad document would
    /// leave everybody behind it unconverged, and the person who noticed would be
    /// whoever is last in the folder.
    /// </summary>
    [Fact]
    public async Task AUserWhoseRecordCannotBeReadIsSkippedAndTheRestStillRun()
    {
        var store = AStore();
        Add(store, AUser, AFilm);
        Add(store, AnotherUser, AnotherFilm);
        FromTheFuture(store, AUser);

        var server = new APlaylistServerOf();
        var run = await Pass(store, server).RunAsync(null, CancellationToken.None);

        Assert.Equal(2, run.Users);
        Assert.Equal(1, run.Skipped);
        Assert.Equal(1, run.Created);
        Assert.Equal(new[] { AnotherFilm }, RowsOf(store, server, AnotherUser));
    }

    /// <summary>
    /// The projection turned off stops the scheduled pass too. Otherwise a disabled
    /// projection is one that still writes to every playlist on the server four times a
    /// day, and the setting means nothing.
    /// </summary>
    [Fact]
    public async Task TheProjectionTurnedOffStopsTheRun()
    {
        var store = AStore();
        Add(store, AUser, AFilm);

        var server = new APlaylistServerOf();
        var run = await Pass(store, server, new PluginConfiguration { ProjectionEnabled = false })
            .RunAsync(null, CancellationToken.None);

        Assert.Equal(0, run.Users);
        Assert.Empty(server.Calls);
    }

    /// <summary>
    /// One summary line, with counts, and nothing in it that names anything a user put
    /// on a list. The absence is asserted rather than trusted: a scheduled task logs on
    /// a server whose log an administrator reads, and a title is what somebody meant to
    /// watch.
    /// </summary>
    [Fact]
    public async Task ARunLogsOneSummaryLineWithCountsAndNoTitles()
    {
        var store = AStore();
        Add(store, AUser, AFilm);

        var log = new RecordingPassLogger();
        await Pass(store, new APlaylistServerOf(), log: log).RunAsync(null, CancellationToken.None);

        var summary = Assert.Single(log.Lines, line => line.Contains("finished", StringComparison.Ordinal));

        Assert.Contains("1 users", summary, StringComparison.Ordinal);
        Assert.DoesNotContain(AFilm.ToString(), summary, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(AUser.ToString(), summary, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(PluginConfiguration.DefaultProjectedListName, summary, StringComparison.Ordinal);
    }

    /// <summary>
    /// The scheduled entry an administrator meets has a name, a description and a
    /// category, and a key that is a fixed string. The server stores trigger changes
    /// against the key, so a key that moved with a type name would discard them.
    /// </summary>
    [Fact]
    public void TheTaskAppearsWithANameADescriptionAndAStableKey()
    {
        var task = Task();

        Assert.False(string.IsNullOrWhiteSpace(task.Name));
        Assert.False(string.IsNullOrWhiteSpace(task.Description));
        Assert.False(string.IsNullOrWhiteSpace(task.Category));
        Assert.Equal("WatchlistReconciliation", task.Key);
    }

    /// <summary>
    /// The default trigger is an interval, and the interval is the setting rather than a
    /// number written into the task. A second copy of that value in the task would
    /// disagree with the page the first time anybody changed it.
    /// </summary>
    [Fact]
    public void TheDefaultTriggerIsTheConfiguredInterval()
    {
        var trigger = Assert.Single(Task(new PluginConfiguration { ReconciliationIntervalHours = 3 })
            .GetDefaultTriggers());

        Assert.Equal(TaskTriggerInfoType.IntervalTrigger, trigger.Type);
        Assert.Equal(TimeSpan.FromHours(3).Ticks, trigger.IntervalTicks);
    }

    /// <summary>
    /// An interval a configuration file carries that the server could not honour falls
    /// back to the declared default rather than being handed over as it stands. A
    /// trigger of zero ticks is not a schedule.
    /// </summary>
    [Fact]
    public void AnUnusableIntervalFallsBackToTheDeclaredDefault()
    {
        var trigger = Assert.Single(Task(new PluginConfiguration { ReconciliationIntervalHours = 0 })
            .GetDefaultTriggers());

        Assert.Equal(
            TimeSpan.FromHours(PluginConfiguration.DefaultReconciliationIntervalHours).Ticks,
            trigger.IntervalTicks);
    }

    /// <summary>
    /// The shell runs the pass. Asserted through what the server saw rather than through
    /// a returned value, because the interface the dashboard calls returns none.
    /// </summary>
    [Fact]
    public async Task ExecutingTheTaskRunsThePass()
    {
        var store = AStore();
        Add(store, AUser, AFilm);

        var server = new APlaylistServerOf();
        var task = new WatchlistReconciliationTask(Pass(store, server), () => new PluginConfiguration());

        await task.ExecuteAsync(new WhatWasReported([]), CancellationToken.None);

        Assert.Equal(new[] { AFilm }, RowsOf(store, server, AUser));
    }

    /// <summary>
    /// There is no pass and no task without what each of them reads.
    /// </summary>
    [Fact]
    public void ThereIsNoPassWithoutWhatItReads()
    {
        var store = AStore();
        var server = new APlaylistServerOf();

        Assert.Throws<ArgumentNullException>(() => new WatchlistProjectionPass(
            null!,
            new WatchlistProjector(server, new RecordingProjectorLogger()),
            new WatchlistReconciler(server, new RecordingReconcilerLogger()),
            server,
            new ADescriberOf(),
            new ASeriesLibraryOf(),
            AStoppedClock(),
            () => new PluginConfiguration(),
            new RecordingPassLogger()));

        Assert.Throws<ArgumentNullException>(() => new WatchlistProjectionPass(
            store,
            null!,
            new WatchlistReconciler(server, new RecordingReconcilerLogger()),
            server,
            new ADescriberOf(),
            new ASeriesLibraryOf(),
            AStoppedClock(),
            () => new PluginConfiguration(),
            new RecordingPassLogger()));

        Assert.Throws<ArgumentNullException>(() => new WatchlistProjectionPass(
            store,
            new WatchlistProjector(server, new RecordingProjectorLogger()),
            null!,
            server,
            new ADescriberOf(),
            new ASeriesLibraryOf(),
            AStoppedClock(),
            () => new PluginConfiguration(),
            new RecordingPassLogger()));

        Assert.Throws<ArgumentNullException>(() => new WatchlistProjectionPass(
            store,
            new WatchlistProjector(server, new RecordingProjectorLogger()),
            new WatchlistReconciler(server, new RecordingReconcilerLogger()),
            null!,
            new ADescriberOf(),
            new ASeriesLibraryOf(),
            AStoppedClock(),
            () => new PluginConfiguration(),
            new RecordingPassLogger()));

        Assert.Throws<ArgumentNullException>(() => new WatchlistProjectionPass(
            store,
            new WatchlistProjector(server, new RecordingProjectorLogger()),
            new WatchlistReconciler(server, new RecordingReconcilerLogger()),
            server,
            null!,
            new ASeriesLibraryOf(),
            AStoppedClock(),
            () => new PluginConfiguration(),
            new RecordingPassLogger()));

        Assert.Throws<ArgumentNullException>(() => new WatchlistProjectionPass(
            store,
            new WatchlistProjector(server, new RecordingProjectorLogger()),
            new WatchlistReconciler(server, new RecordingReconcilerLogger()),
            server,
            new ADescriberOf(),
            null!,
            AStoppedClock(),
            () => new PluginConfiguration(),
            new RecordingPassLogger()));

        Assert.Throws<ArgumentNullException>(() => new WatchlistProjectionPass(
            store,
            new WatchlistProjector(server, new RecordingProjectorLogger()),
            new WatchlistReconciler(server, new RecordingReconcilerLogger()),
            server,
            new ADescriberOf(),
            new ASeriesLibraryOf(),
            null!,
            () => new PluginConfiguration(),
            new RecordingPassLogger()));

        Assert.Throws<ArgumentNullException>(() => new WatchlistProjectionPass(
            store,
            new WatchlistProjector(server, new RecordingProjectorLogger()),
            new WatchlistReconciler(server, new RecordingReconcilerLogger()),
            server,
            new ADescriberOf(),
            new ASeriesLibraryOf(),
            AStoppedClock(),
            null!,
            new RecordingPassLogger()));

        Assert.Throws<ArgumentNullException>(() => new WatchlistProjectionPass(
            store,
            new WatchlistProjector(server, new RecordingProjectorLogger()),
            new WatchlistReconciler(server, new RecordingReconcilerLogger()),
            server,
            new ADescriberOf(),
            new ASeriesLibraryOf(),
            AStoppedClock(),
            () => new PluginConfiguration(),
            null!));

        Assert.Throws<ArgumentNullException>(() =>
            new WatchlistReconciliationTask(null!, () => new PluginConfiguration()));
        Assert.Throws<ArgumentNullException>(() =>
            new WatchlistReconciliationTask(Pass(store, server), null!));
    }

    private static StoppedClock AStoppedClock() =>
        new(new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero));

    private static void Add(WatchlistDocumentStore store, Guid userId, Guid itemId)
    {
        var result = store.Add(
            userId,
            new WatchlistEntry
            {
                ItemId = itemId,
                Kind = WatchlistItemKind.Movie,
                AddedAt = new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero),
                Source = WatchlistEntrySource.Api,
            },
            PluginConfiguration.DefaultMaxEntriesPerUser);

        Assert.Equal(WatchlistAddOutcome.Added, result.Outcome);
    }

    /// <summary>
    /// Makes one user's document unreadable to this build, by declaring a version from
    /// the future in it. That is the one way a record becomes unavailable without the
    /// file system being interfered with.
    /// </summary>
    /// <param name="store">The store.</param>
    /// <param name="userId">The user whose document is put out of reach.</param>
    private static void FromTheFuture(WatchlistDocumentStore store, Guid userId)
    {
        var path = store.PathFor(userId);
        var text = File.ReadAllText(path);

        // The version this build writes rather than a number typed here, so this stays
        // true on the change that bumps the schema instead of quietly stopping to make
        // the document unreadable.
        File.WriteAllText(
            path,
            text.Replace(
                string.Format(CultureInfo.InvariantCulture, "\"SchemaVersion\": {0}", WatchlistDocument.CurrentSchemaVersion),
                "\"SchemaVersion\": 9999",
                StringComparison.Ordinal));
    }

    private WatchlistDocumentStore AStore() => new(DataFolder, new RecordingLogger());

    private WatchlistProjectionPass Pass(
        WatchlistDocumentStore store,
        APlaylistServerOf server,
        PluginConfiguration? configuration = null,
        RecordingPassLogger? log = null) => new(
            store,
            new WatchlistProjector(server, new RecordingProjectorLogger()),
            new WatchlistReconciler(server, new RecordingReconcilerLogger()),
            server,
            new ADescriberOf(
                (AFilm, AUser, WatchlistItemKind.Movie),
                (AnotherFilm, AUser, WatchlistItemKind.Movie),
                (AFilm, AnotherUser, WatchlistItemKind.Movie),
                (AnotherFilm, AnotherUser, WatchlistItemKind.Movie)),
            new ASeriesLibraryOf(),
            AStoppedClock(),
            () => configuration ?? new PluginConfiguration(),
            log ?? new RecordingPassLogger());

    private WatchlistReconciliationTask Task(PluginConfiguration? configuration = null) =>
        new(Pass(AStore(), new APlaylistServerOf()), () => configuration ?? new PluginConfiguration());

    private static Guid[] RowsOf(WatchlistDocumentStore store, APlaylistServerOf server, Guid userId)
    {
        var playlistId = store.Read(userId).Document!.Projection!.PlaylistId;

        return server.EntriesOf(playlistId, userId).Select(row => row.ItemId).ToArray();
    }

    /// <summary>
    /// A progress sink that keeps what it was told.
    /// </summary>
    /// <remarks>
    /// A shape of its own rather than a lambda so that what is asserted is the sequence
    /// of reports rather than the last one: a pass that reported a hundred once and
    /// nothing in between is a dashboard that jumps.
    /// </remarks>
    private sealed class WhatWasReported : IProgress<double>
    {
        private readonly List<double> _reports;

        public WhatWasReported(List<double> reports)
        {
            _reports = reports;
        }

        public void Report(double value) => _reports.Add(value);
    }
}
