using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Watchlist.Api;
using Jellyfin.Plugin.Watchlist.Configuration;
using Jellyfin.Plugin.Watchlist.Library;
using Jellyfin.Plugin.Watchlist.Projection;
using Jellyfin.Plugin.Watchlist.Store;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Audio;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Library;
using Xunit;

namespace Jellyfin.Plugin.Watchlist.Tests;

/// <summary>
/// What happens to a list and to the playlist showing it when media leaves the library,
/// including the rescan that removes everything and puts it back.
/// </summary>
/// <remarks>
/// <para>
/// The rule this stands on was decided in M2 and is not re-taken here: an entry whose
/// item does not resolve is skipped on read and left in the document. So a removal
/// writes to no document at all, and what these tests hold down is that this stays true
/// under a storm as well as under one event, and that the playlist still stops offering
/// an item nobody can play.
/// </para>
/// <para>
/// Nothing here needs a server, a library or a file outside its own temporary directory,
/// and no test waits on the clock: the handler exposes the pass it started, so a test
/// awaits the work rather than sleeping for it.
/// </para>
/// </remarks>
public sealed class LibraryRemovalTests : IDisposable
{
    private const int EventsInTheStorm = 500;

    private static readonly Guid AUser = Guid.Parse("55555555-5555-5555-5555-555555555555");

    private static readonly Guid AFilm = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");

    private static readonly Guid AnotherFilm = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000002");

    private readonly TemporaryDirectory _sandbox = new("watchlist-library-removal");

    private string DataFolder => Path.Join(_sandbox.FullPath, "plugin-data");

    /// <inheritdoc />
    public void Dispose()
    {
        _sandbox.Dispose();
    }

    /// <summary>
    /// Nothing is attached to the library before the server starts the subscription, and
    /// nothing is left attached after it stops it. A handler that outlived the plugin is
    /// one running against a store nobody is using.
    /// </summary>
    [Fact]
    public async Task NothingIsSubscribedBeforeTheStartAndNothingIsLeftAfterTheStop()
    {
        var library = new ALibraryOf();
        var subscription = new LibraryRemovalSubscription(library, AHandler(AStore(), new APlaylistServerOf()));

        Assert.Equal(0, library.RemovalListeners);

        await subscription.StartAsync(CancellationToken.None);
        Assert.Equal(1, library.RemovalListeners);

        await subscription.StopAsync(CancellationToken.None);
        Assert.Equal(0, library.RemovalListeners);
    }

    /// <summary>
    /// And a stopped subscription hears nothing. The count above says the handler was
    /// detached; this says the detachment is what it looks like, which is the assertion a
    /// wrongly-written unsubscribe would still pass the first half of.
    /// </summary>
    [Fact]
    public async Task AStoppedSubscriptionHearsNothing()
    {
        var store = AStore();
        Add(store, AFilm);

        var server = new APlaylistServerOf();
        var handler = AHandler(store, server);
        var library = new ALibraryOf();
        var subscription = new LibraryRemovalSubscription(library, handler);

        await subscription.StartAsync(CancellationToken.None);
        await subscription.StopAsync(CancellationToken.None);

        library.RaiseRemoval(new Movie { Id = AFilm });
        await handler.InFlight;

        Assert.Empty(server.Calls);
    }

    /// <summary>
    /// The row goes and the entry stays. This is the M2 rule and the reason for this
    /// handler in one assertion: the document is untouched, and the playlist stops
    /// offering an item the user can no longer play.
    /// </summary>
    [Fact]
    public async Task ARemovedItemLeavesThePlaylistAndStaysInTheDocument()
    {
        var store = AStore();
        Add(store, AFilm);
        Add(store, AnotherFilm);

        var server = new APlaylistServerOf();
        var describer = new ADescriberOf(
            (AFilm, AUser, WatchlistItemKind.Movie),
            (AnotherFilm, AUser, WatchlistItemKind.Movie));

        var handler = AHandler(store, server, describer);
        var library = new ALibraryOf();
        var subscription = new LibraryRemovalSubscription(library, handler);
        await subscription.StartAsync(CancellationToken.None);

        library.RaiseRemoval(new Movie { Id = AFilm });
        await handler.InFlight;

        Assert.Equal(2, RowsOf(store, server).Length);

        // The library forgets it, which is what a removal is from everything above the
        // describer: an item that no longer resolves for this user.
        describer.NoLongerSees(AFilm, AUser);

        library.RaiseRemoval(new Movie { Id = AFilm });
        await handler.InFlight;

        Assert.Equal(new[] { AnotherFilm }, RowsOf(store, server));
        Assert.Equal(
            new[] { AFilm, AnotherFilm },
            store.Read(AUser).Document!.Entries.Select(entry => entry.ItemId).OrderBy(id => id).ToArray());
    }

    /// <summary>
    /// A rescan that removes everything and puts it back does not empty a list. The
    /// document holds every entry throughout, which is what the promise is about; what
    /// the user SEES goes empty while nothing resolves and comes back when it does, and
    /// those are different statements.
    /// </summary>
    [Fact]
    public async Task ARescanThatRemovesAndReAddsDoesNotEmptyTheList()
    {
        var store = AStore();
        Add(store, AFilm);
        Add(store, AnotherFilm);

        var server = new APlaylistServerOf();
        var describer = new ADescriberOf(
            (AFilm, AUser, WatchlistItemKind.Movie),
            (AnotherFilm, AUser, WatchlistItemKind.Movie));

        var handler = AHandler(store, server, describer);
        var library = new ALibraryOf();
        var subscription = new LibraryRemovalSubscription(library, handler);
        await subscription.StartAsync(CancellationToken.None);

        library.RaiseRemoval(new Movie { Id = AFilm });
        await handler.InFlight;

        var beforeTheScan = EntriesOf(store);

        // The scan takes both films out.
        describer.NoLongerSees(AFilm, AUser);
        describer.NoLongerSees(AnotherFilm, AUser);
        library.RaiseRemoval(new Movie { Id = AFilm });
        library.RaiseRemoval(new Movie { Id = AnotherFilm });
        await handler.InFlight;

        Assert.Empty(RowsOf(store, server));
        Assert.Equal(beforeTheScan, EntriesOf(store));

        // And puts them back under the same identifiers, which is the case where the
        // list comes back whole.
        describer.SeesAgain(AFilm, AUser, WatchlistItemKind.Movie);
        describer.SeesAgain(AnotherFilm, AUser, WatchlistItemKind.Movie);
        library.RaiseRemoval(new Movie { Id = AFilm });
        await handler.InFlight;

        Assert.Equal(2, RowsOf(store, server).Length);
        Assert.Equal(beforeTheScan, EntriesOf(store));
    }

    /// <summary>
    /// A bulk removal writes no document, per item or otherwise. The instrument is the
    /// bytes at the path the store gives for that user rather than a counter inside the
    /// store, which is the same reading the watched storm takes: a handler that wrote
    /// once per event would pass every behaviour test in this file and still put a write
    /// on the disk for every item a scan touched.
    /// </summary>
    [Fact]
    public async Task ABulkRemovalWritesNoDocumentAtAll()
    {
        var store = AStore();
        Add(store, AFilm);

        var server = new APlaylistServerOf();
        var handler = AHandler(store, server);
        var library = new ALibraryOf();
        var subscription = new LibraryRemovalSubscription(library, handler);
        await subscription.StartAsync(CancellationToken.None);

        // One removal first, so the playlist has been made and remembered. Making it
        // writes the document once, which is the projector recording an identity rather
        // than anything a removal did, and the reading below is taken after that so a
        // storm is measured against a settled tree.
        library.RaiseRemoval(new Movie { Id = AFilm });
        await handler.InFlight;

        var afterTheFirstPass = File.ReadAllText(store.PathFor(AUser));

        for (var i = 0; i < EventsInTheStorm; i++)
        {
            library.RaiseRemoval(new Movie { Id = Guid.Parse(string.Format(CultureInfo.InvariantCulture, "eeeeeeee-0000-0000-0000-{0:D12}", i)) });
        }

        await handler.InFlight;

        Assert.Equal(afterTheFirstPass, File.ReadAllText(store.PathFor(AUser)));
    }

    /// <summary>
    /// And a bulk removal does not put a playlist write on the server per item either. A
    /// scan raises this thousands of times; the playlist is correct throughout, so the
    /// passes it costs write nothing, and a handler that ran one pass per event would be
    /// invisible to the document instrument above and visible here.
    /// </summary>
    [Fact]
    public async Task ABulkRemovalDoesNotChurnThePlaylist()
    {
        var store = AStore();
        Add(store, AFilm);

        var server = new APlaylistServerOf();
        var handler = AHandler(store, server);
        var library = new ALibraryOf();
        var subscription = new LibraryRemovalSubscription(library, handler);
        await subscription.StartAsync(CancellationToken.None);

        library.RaiseRemoval(new Movie { Id = AFilm });
        await handler.InFlight;

        var afterTheFirst = server.Writes;

        for (var i = 0; i < EventsInTheStorm; i++)
        {
            library.RaiseRemoval(new Movie { Id = AFilm });
        }

        await handler.InFlight;

        Assert.Equal(afterTheFirst, server.Writes);
    }

    /// <summary>
    /// A storm costs a handful of passes rather than one per item, and the count is read
    /// off the summary line each finished run writes.
    /// </summary>
    /// <remarks>
    /// THE TWO STORM TESTS ABOVE CANNOT SEE THIS AND THAT IS WHY THIS ONE EXISTS. Neither
    /// a document write nor a playlist write happens on a correct server whatever the
    /// handler does, so a version running one full pass per event passes both of them.
    /// Measured rather than supposed: it did, and this test was written for that.
    ///
    /// The bound asserted is an order of magnitude rather than an exact number, and the
    /// looseness is deliberate. How many passes a storm costs depends on how many events
    /// land while one is running, which is a scheduling fact rather than a rule; what the
    /// shape guarantees is that a run under way absorbs every event that arrives during
    /// it into ONE more run. A number pinned exactly here would be a test about the
    /// thread pool.
    /// </remarks>
    [Fact]
    public async Task AStormOfRemovalsCostsAHandfulOfPassesRatherThanOnePerItem()
    {
        var store = AStore();
        Add(store, AFilm);

        var log = new RecordingPassLogger();
        var handler = AHandler(store, new APlaylistServerOf(), log: log);
        var library = new ALibraryOf();
        var subscription = new LibraryRemovalSubscription(library, handler);
        await subscription.StartAsync(CancellationToken.None);

        for (var i = 0; i < EventsInTheStorm; i++)
        {
            library.RaiseRemoval(new Movie { Id = AFilm });
        }

        await handler.InFlight;

        var passes = log.Lines.Count(line => line.Contains("finished", StringComparison.Ordinal));

        Assert.InRange(passes, 1, EventsInTheStorm / 10);
    }

    /// <summary>
    /// A removal that arrives while a pass is walking buys exactly one more pass. That
    /// arm is what stops an event being lost: the walk may already have gone past the
    /// user it mattered to, and without the second run that user waits for the scheduled
    /// pass instead.
    /// </summary>
    /// <remarks>
    /// Deterministic rather than timed. The describer the pass reads is held inside its
    /// first call until this test lets it go, so "while a pass is running" is a state the
    /// test puts the handler in rather than one it hopes to catch. Nothing here waits on
    /// the clock.
    /// </remarks>
    [Fact]
    public async Task ARemovalDuringAPassBuysExactlyOneMorePass()
    {
        var store = AStore();
        Add(store, AFilm);

        var log = new RecordingPassLogger();
        using var held = new ADescriberHeldOnItsFirstCall(
            new ADescriberOf((AFilm, AUser, WatchlistItemKind.Movie)));

        var handler = AHandler(store, new APlaylistServerOf(), describer: held, log: log);
        var library = new ALibraryOf();
        var subscription = new LibraryRemovalSubscription(library, handler);
        await subscription.StartAsync(CancellationToken.None);

        library.RaiseRemoval(new Movie { Id = AFilm });

        await held.Entered;
        library.RaiseRemoval(new Movie { Id = AFilm });
        held.LetGo();

        await handler.InFlight;

        Assert.Equal(2, log.Lines.Count(line => line.Contains("finished", StringComparison.Ordinal)));
    }

    /// <summary>
    /// An item of a kind no list can hold asks for nothing. A scan removes music tracks
    /// and images beside the things this plugin holds, and each of those would otherwise
    /// buy a walk over every user for an item no entry can point at.
    /// </summary>
    [Fact]
    public async Task AKindNoListCanHoldAsksForNoPass()
    {
        var store = AStore();
        Add(store, AFilm);

        var server = new APlaylistServerOf();
        var handler = AHandler(store, server);
        var library = new ALibraryOf();
        var subscription = new LibraryRemovalSubscription(library, handler);
        await subscription.StartAsync(CancellationToken.None);

        library.RaiseRemoval(new Audio { Id = Guid.Parse("dddddddd-0000-0000-0000-000000000001") });
        await handler.InFlight;

        Assert.Empty(server.Calls);
    }

    /// <summary>
    /// And an event carrying no item at all is nothing rather than a null reference.
    /// </summary>
    [Fact]
    public async Task AnEventWithNoItemAsksForNoPass()
    {
        var store = AStore();
        Add(store, AFilm);

        var server = new APlaylistServerOf();
        var handler = AHandler(store, server);
        var library = new ALibraryOf();
        var subscription = new LibraryRemovalSubscription(library, handler);
        await subscription.StartAsync(CancellationToken.None);

        library.RaiseRemoval(null);
        await handler.InFlight;

        Assert.Empty(server.Calls);
    }

    /// <summary>
    /// The three kinds a list holds are all worth a pass, and everything else is not.
    /// Asked of the reading directly, because the whole set is cheaper to pin here than
    /// through an event apiece.
    /// </summary>
    [Fact]
    public void WhatIsWorthAPassIsTheAcceptedSetAndNothingElse()
    {
        Assert.True(LibraryRemovalSubscription.CouldBeOnAList(Removed(new Movie())));
        Assert.True(LibraryRemovalSubscription.CouldBeOnAList(Removed(new MediaBrowser.Controller.Entities.TV.Series())));
        Assert.True(LibraryRemovalSubscription.CouldBeOnAList(Removed(new MediaBrowser.Controller.Entities.TV.Episode())));
        Assert.False(LibraryRemovalSubscription.CouldBeOnAList(Removed(new Audio())));
        Assert.False(LibraryRemovalSubscription.CouldBeOnAList(Removed(null)));
    }

    /// <summary>
    /// There is no handler and no subscription without what each of them reads.
    /// </summary>
    [Fact]
    public void ThereIsNoHandlerWithoutWhatItReads()
    {
        Assert.Throws<ArgumentNullException>(() => new LibraryRemovalHandler(null!));
        Assert.Throws<ArgumentNullException>(() =>
            new LibraryRemovalSubscription(null!, AHandler(AStore(), new APlaylistServerOf())));
        Assert.Throws<ArgumentNullException>(() => new LibraryRemovalSubscription(new ALibraryOf(), null!));
        Assert.Throws<ArgumentNullException>(() => LibraryRemovalSubscription.CouldBeOnAList(null!));
    }

    private static ItemChangeEventArgs Removed(BaseItem? item) => new() { Item = item! };

    private static StoppedClock AStoppedClock() =>
        new(new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero));

    private static Guid[] EntriesOf(WatchlistDocumentStore store) =>
        store.Read(AUser).Document!.Entries.Select(entry => entry.ItemId).OrderBy(id => id).ToArray();

    private static Guid[] RowsOf(WatchlistDocumentStore store, APlaylistServerOf server)
    {
        var projection = store.Read(AUser).Document!.Projection;

        return projection is null
            ? []
            : server.EntriesOf(projection.PlaylistId, AUser).Select(row => row.ItemId).ToArray();
    }

    private WatchlistDocumentStore AStore() => new(DataFolder, new RecordingLogger());

    private static void Add(WatchlistDocumentStore store, Guid itemId)
    {
        var result = store.Add(
            AUser,
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

    private LibraryRemovalHandler AHandler(
        WatchlistDocumentStore store,
        APlaylistServerOf server,
        IWatchlistItemDescriber? describer = null,
        RecordingPassLogger? log = null) => new(new WatchlistProjectionPass(
            store,
            new WatchlistProjector(server, new RecordingProjectorLogger()),
            new WatchlistReconciler(server, new RecordingReconcilerLogger()),
            server,
            describer ?? new ADescriberOf(
                (AFilm, AUser, WatchlistItemKind.Movie),
                (AnotherFilm, AUser, WatchlistItemKind.Movie)),
            new ASeriesLibraryOf(),
            AStoppedClock(),
            () => new PluginConfiguration(),
            log ?? new RecordingPassLogger()));

    /// <summary>
    /// A describer that stops inside its first call until it is let go, so a test can put
    /// the handler in the state "a pass is running" instead of racing for it.
    /// </summary>
    /// <remarks>
    /// It holds a semaphore rather than waiting on the clock, which the headless rule
    /// refuses. The first call signals that it has been entered and then blocks; every
    /// later call goes straight through, so only the first pass is held.
    /// </remarks>
    private sealed class ADescriberHeldOnItsFirstCall : IWatchlistItemDescriber, IDisposable
    {
        private readonly IWatchlistItemDescriber _inner;
        private readonly TaskCompletionSource _entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly SemaphoreSlim _release = new(0, 1);

        private int _calls;

        public ADescriberHeldOnItsFirstCall(IWatchlistItemDescriber inner)
        {
            _inner = inner;
        }

        /// <summary>
        /// Gets a task that completes once the first call has been entered.
        /// </summary>
        public Task Entered => _entered.Task;

        /// <summary>
        /// Lets the held call finish.
        /// </summary>
        public void LetGo() => _release.Release();

        public WatchlistItemDescription? Describe(Guid itemId, Guid userId)
        {
            if (Interlocked.Increment(ref _calls) == 1)
            {
                _entered.SetResult();
                _release.Wait();
            }

            return _inner.Describe(itemId, userId);
        }

        public void Dispose() => _release.Dispose();
    }
}

