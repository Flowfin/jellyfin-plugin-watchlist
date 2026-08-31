using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Watchlist.Configuration;
using Jellyfin.Plugin.Watchlist.Library;
using Jellyfin.Plugin.Watchlist.Projection;
using Jellyfin.Plugin.Watchlist.Store;
using Jellyfin.Plugin.Watchlist.Watched;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;
using Xunit;

namespace Jellyfin.Plugin.Watchlist.Tests;

/// <summary>
/// What a user loses by turning the plugin off, which has to be nothing, and what they
/// get back when they turn it on again.
/// </summary>
/// <remarks>
/// <para>
/// Disabling a plugin is a common way to find out whether it is the cause of something,
/// and somebody doing it is not asking for their lists to be cleared.
/// </para>
/// <para>
/// DISABLING IS NOT A CODE PATH OF ITS OWN AND THAT IS THE POINT OF THIS FILE. Nothing in
/// the plugin knows it has been disabled. The projection stops because the server stops
/// the things that run it, and re-enabling reconciles because reconciling is what a pass
/// does. Every test here drives that shape rather than a disable branch, and a disable
/// branch appearing later would be the reconciler having been built wrong rather than
/// this file needing another case.
/// </para>
/// <para>
/// What a disabled plugin looks like from inside the suite is exactly what a server does
/// to it: the hosted subscriptions are stopped, and nothing calls the scheduled pass.
/// Nothing else about the plugin changes, because there is nothing else the server turns
/// off.
/// </para>
/// <para>
/// Nothing here needs a server, a library or a file outside its own temporary directory,
/// and no test reads the machine clock.
/// </para>
/// </remarks>
public sealed class DisabledPluginTests : IDisposable
{
    private static readonly Guid AUser = Guid.Parse("55555555-5555-5555-5555-555555555555");

    private static readonly Guid AFilm = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");

    private static readonly Guid AnotherFilm = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000002");

    private static readonly Guid AThirdFilm = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000003");

    private static readonly DateTimeOffset WhenItWasAdded = new(2026, 1, 2, 3, 4, 5, TimeSpan.Zero);

    private readonly TemporaryDirectory _sandbox = new("watchlist-disabled-plugin");

    private string DataFolder => Path.Join(_sandbox.FullPath, "plugin-data");

    /// <inheritdoc />
    public void Dispose()
    {
        _sandbox.Dispose();
    }

    /// <summary>
    /// THE WHOLE CYCLE, which is the condition this issue is really about. A list is
    /// projected, the plugin is turned off, somebody edits the playlist on a client while
    /// it is off, and turning it back on takes those edits onto the list and loses
    /// nothing that was already there.
    /// </summary>
    /// <remarks>
    /// The edits are both directions in one go, because that is what a person does to a
    /// playlist: one row taken out and one put in. The rule that reads them is the one
    /// from M3 and is not a second rule for this case - which is the whole reason nothing
    /// here has to know the plugin was ever off.
    /// </remarks>
    [Fact]
    public async Task TheCycleTakesWhatHappenedWhileItWasOffAndLosesNothing()
    {
        var store = AStore();
        Add(store, AFilm);
        Add(store, AnotherFilm);

        var server = new APlaylistServerOf();
        var library = new ALibraryOf();
        var removals = new LibraryRemovalHandler(Pass(store, server));
        var subscription = new LibraryRemovalSubscription(library, removals);

        await subscription.StartAsync(CancellationToken.None);
        await Pass(store, server).RunAsync(null, CancellationToken.None);

        var playlistId = store.Read(AUser).Document!.Projection!.PlaylistId;
        Assert.Equal(new[] { AFilm, AnotherFilm }, RowsOf(server, playlistId).OrderBy(id => id).ToArray());

        // Turned off. The server stops the hosted subscription and calls no task.
        await subscription.StopAsync(CancellationToken.None);
        Assert.Equal(0, library.RemovalListeners);

        var whileItWasOff = File.ReadAllText(store.PathFor(AUser));

        // Somebody works the playlist on a television: one row out, one row in.
        server.Rows(playlistId, AnotherFilm, AThirdFilm);

        // And nothing happened to the document while it was off.
        Assert.Equal(whileItWasOff, File.ReadAllText(store.PathFor(AUser)));

        // Turned back on.
        await subscription.StartAsync(CancellationToken.None);
        await Pass(store, server).RunAsync(null, CancellationToken.None);

        Assert.Equal(new[] { AnotherFilm, AThirdFilm }, EntriesOf(store));
        Assert.Equal(
            WatchlistEntrySource.PlaylistEdit,
            store.Read(AUser).Document!.Entries.Single(entry => entry.ItemId == AThirdFilm).Source);
    }

    /// <summary>
    /// A plugin that is off runs nothing. Both subscriptions are stopped, both events are
    /// raised at them, and neither the store nor the playlist moves.
    /// </summary>
    /// <remarks>
    /// Both, in one test, because the promise is about the plugin rather than about
    /// either of them: a reader wants to know that nothing at all is listening, and two
    /// tests each proving their own half leave the question of whether there is a third
    /// listener unasked. The count of hosted registrations is asserted where the
    /// registrations are.
    /// </remarks>
    [Fact]
    public async Task NothingListensWhileItIsOffAndNothingMoves()
    {
        var store = AStore();
        Add(store, AFilm);

        var server = new APlaylistServerOf();
        await Pass(store, server).RunAsync(null, CancellationToken.None);

        var library = new ALibraryOf();
        var userData = new AUserDataManagerOf();
        var removals = new LibraryRemovalSubscription(library, new LibraryRemovalHandler(Pass(store, server)));
        var watched = new UserDataWatchedSubscription(userData, AWatchedHandler(store));

        await removals.StartAsync(CancellationToken.None);
        await watched.StartAsync(CancellationToken.None);
        await removals.StopAsync(CancellationToken.None);
        await watched.StopAsync(CancellationToken.None);

        Assert.Equal(0, library.RemovalListeners);
        Assert.Equal(0, userData.Listeners);

        var whenItStopped = File.ReadAllText(store.PathFor(AUser));
        var callsWhenItStopped = server.Calls.Count;

        library.RaiseRemoval(new Movie { Id = AFilm });
        userData.Raise(new UserDataSaveEventArgs
        {
            UserId = AUser,
            Item = new Movie { Id = AFilm },
            UserData = new UserItemData { Key = "watched", Played = true },
            SaveReason = UserDataSaveReason.TogglePlayed,
        });

        Assert.Equal(whenItStopped, File.ReadAllText(store.PathFor(AUser)));
        Assert.Equal(callsWhenItStopped, server.Calls.Count);
    }

    /// <summary>
    /// The document is still there after the cycle, read off the file rather than through
    /// the store. A read of a document that is not there answers with an empty list for
    /// the same user, so asking the store would satisfy the assertion either way.
    /// </summary>
    [Fact]
    public async Task TheDocumentIsStillOnDiskAfterTheCycle()
    {
        var store = AStore();
        Add(store, AFilm);

        var server = new APlaylistServerOf();
        await Pass(store, server).RunAsync(null, CancellationToken.None);

        var playlistId = store.Read(AUser).Document!.Projection!.PlaylistId;

        // Off, everything taken out of the playlist by hand, on again.
        server.Rows(playlistId);
        await Pass(store, server).RunAsync(null, CancellationToken.None);

        var onDisk = WatchlistDocumentFormat.Read(File.ReadAllText(store.PathFor(AUser)));

        Assert.Equal(AUser, onDisk.UserId);
        Assert.Equal(WatchlistDocument.CurrentSchemaVersion, onDisk.SchemaVersion);
        Assert.Empty(onDisk.Entries);
    }

    /// <summary>
    /// And the projection setting is the same promise one size smaller: turning it off
    /// stops the pass and changes no document, and turning it on again reconciles from
    /// the store rather than from anything the pass kept in memory.
    /// </summary>
    [Fact]
    public async Task TurningTheProjectionOffAndOnAgainReconcilesFromTheStore()
    {
        var store = AStore();
        Add(store, AFilm);

        var server = new APlaylistServerOf();
        var off = new PluginConfiguration { ProjectionEnabled = false };
        var beforeTheRun = File.ReadAllText(store.PathFor(AUser));

        await Pass(store, server, () => off).RunAsync(null, CancellationToken.None);

        Assert.Empty(server.Calls);
        Assert.Equal(beforeTheRun, File.ReadAllText(store.PathFor(AUser)));

        var entries = EntriesOf(store);

        var on = new PluginConfiguration();
        await Pass(store, server, () => on).RunAsync(null, CancellationToken.None);

        var playlistId = store.Read(AUser).Document!.Projection!.PlaylistId;

        Assert.Equal(entries, EntriesOf(store));
        Assert.Equal(new[] { AFilm }, RowsOf(server, playlistId));
    }

    private static Guid[] EntriesOf(WatchlistDocumentStore store) =>
        store.Read(AUser).Document!.Entries.Select(entry => entry.ItemId).OrderBy(id => id).ToArray();

    private static Guid[] RowsOf(APlaylistServerOf server, Guid playlistId) =>
        server.EntriesOf(playlistId, AUser).Select(row => row.ItemId).ToArray();

    private static WatchedRemovalHandler AWatchedHandler(WatchlistDocumentStore store) =>
        new(
            store,
            static () => new PluginConfiguration { RemoveWhenWatched = true },
            new AFinishedSeriesSet(),
            new RecordingWatchedLogger());

    private static void Add(WatchlistDocumentStore store, Guid itemId)
    {
        var result = store.Add(
            AUser,
            new WatchlistEntry
            {
                ItemId = itemId,
                Kind = WatchlistItemKind.Movie,
                AddedAt = WhenItWasAdded,
                Source = WatchlistEntrySource.Api,
            },
            PluginConfiguration.DefaultMaxEntriesPerUser);

        Assert.Equal(WatchlistAddOutcome.Added, result.Outcome);
    }

    private WatchlistDocumentStore AStore() => new(DataFolder, new RecordingLogger());

    private WatchlistProjectionPass Pass(
        WatchlistDocumentStore store,
        APlaylistServerOf server,
        Func<PluginConfiguration>? configuration = null) => new(
            store,
            new WatchlistProjector(server, new RecordingProjectorLogger()),
            new WatchlistReconciler(server, new RecordingReconcilerLogger()),
            server,
            new ADescriberOf(
                (AFilm, AUser, WatchlistItemKind.Movie),
                (AnotherFilm, AUser, WatchlistItemKind.Movie),
                (AThirdFilm, AUser, WatchlistItemKind.Movie)),
            new ASeriesLibraryOf(),
            new StoppedClock(WhenItWasAdded),
            configuration ?? (static () => new PluginConfiguration()),
            new RecordingPassLogger());
}
