using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Watchlist.Configuration;
using Jellyfin.Plugin.Watchlist.Projection;
using Jellyfin.Plugin.Watchlist.Store;
using Xunit;

namespace Jellyfin.Plugin.Watchlist.Tests;

/// <summary>
/// What a pass makes of a playlist somebody edited on a client: the four states a row
/// and an entry can be in between them, and which of the four means delete.
/// </summary>
/// <remarks>
/// <para>
/// The whole difficulty is that three of the four look identical to anything comparing
/// the list against the playlist. A row the projector wrote and that is gone, a row it
/// never wrote, and an entry it has not written yet are three different intentions with
/// one shape, and one of them is a person removing something. What separates them is the
/// set the record carries of what the projector last wrote, and every test here is about
/// that set being read correctly.
/// </para>
/// <para>
/// Nothing here needs a server, a library or a file outside its own temporary directory,
/// and no test reads the machine clock.
/// </para>
/// </remarks>
public sealed class PlaylistEditAdoptionTests : IDisposable
{
    private static readonly Guid AUser = Guid.Parse("55555555-5555-5555-5555-555555555555");

    private static readonly Guid AFilm = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");

    private static readonly Guid AnotherFilm = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000002");

    private static readonly Guid AThirdFilm = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000003");

    private static readonly Guid ASong = Guid.Parse("eeeeeeee-0000-0000-0000-000000000001");

    private static readonly Guid AShow = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000001");

    private static readonly Guid FirstEpisode = Guid.Parse("cccccccc-0000-0000-0000-000000000001");

    private readonly TemporaryDirectory _sandbox = new("watchlist-playlist-edits");

    private string DataFolder => Path.Join(_sandbox.FullPath, "plugin-data");

    /// <inheritdoc />
    public void Dispose()
    {
        _sandbox.Dispose();
    }

    /// <summary>
    /// A pass writes down what it put in the playlist. Without this the other three
    /// states cannot be told apart at all, so it is asserted on its own rather than only
    /// through what it enables.
    /// </summary>
    [Fact]
    public async Task APassRecordsWhatItWrote()
    {
        var store = AStore();
        Add(store, AFilm);
        Add(store, AnotherFilm);

        var server = new APlaylistServerOf();
        await Pass(store, server).RunAsync(null, CancellationToken.None);

        var projection = store.Read(AUser).Document!.Projection!;

        Assert.Equal(
            new[] { AFilm, AnotherFilm },
            projection.ProjectedItemIds.OrderBy(itemId => itemId).ToArray());
        Assert.Equal(AStoppedInstant, projection.WrittenAt);
    }

    /// <summary>
    /// TRANSITION ONE. A row the projector wrote and that is now missing is somebody
    /// taking it off on a client, and the entry leaves the list.
    /// </summary>
    [Fact]
    public async Task ARowTheProjectorWroteAndThatIsGoneIsARemoval()
    {
        var store = AStore();
        Add(store, AFilm);
        Add(store, AnotherFilm);

        var server = new APlaylistServerOf();
        var pass = Pass(store, server);
        await pass.RunAsync(null, CancellationToken.None);

        var playlistId = store.Read(AUser).Document!.Projection!.PlaylistId;
        server.Rows(playlistId, AnotherFilm);

        await pass.RunAsync(null, CancellationToken.None);

        Assert.Equal(new[] { AnotherFilm }, EntriesOf(store));
        Assert.Equal(new[] { AnotherFilm }, RowsOf(server, playlistId));
    }

    /// <summary>
    /// TRANSITION TWO. A row the projector never wrote is somebody adding on a client,
    /// and it goes onto the list recorded as having come from there.
    /// </summary>
    [Fact]
    public async Task ARowTheProjectorNeverWroteIsAnAddition()
    {
        var store = AStore();
        Add(store, AFilm);

        var server = new APlaylistServerOf();
        var pass = Pass(store, server);
        await pass.RunAsync(null, CancellationToken.None);

        var playlistId = store.Read(AUser).Document!.Projection!.PlaylistId;
        server.Rows(playlistId, AFilm, AnotherFilm);

        await pass.RunAsync(null, CancellationToken.None);

        var entries = store.Read(AUser).Document!.Entries;

        Assert.Equal(new[] { AFilm, AnotherFilm }, entries.Select(entry => entry.ItemId).OrderBy(id => id).ToArray());
        Assert.Equal(
            WatchlistEntrySource.PlaylistEdit,
            entries.Single(entry => entry.ItemId == AnotherFilm).Source);
    }

    /// <summary>
    /// TRANSITION THREE. An entry the projector has not written yet is projected on this
    /// pass and is never read as a removal. This is the state every entry added through
    /// the endpoint is in, so reading it as a removal would empty a list the moment
    /// anybody used the plugin.
    /// </summary>
    [Fact]
    public async Task AnEntryNotYetProjectedIsAddedToThePlaylistAndNeverRemoved()
    {
        var store = AStore();
        Add(store, AFilm);

        var server = new APlaylistServerOf();
        var pass = Pass(store, server);
        await pass.RunAsync(null, CancellationToken.None);

        Add(store, AnotherFilm);
        await pass.RunAsync(null, CancellationToken.None);

        var playlistId = store.Read(AUser).Document!.Projection!.PlaylistId;

        Assert.Equal(new[] { AFilm, AnotherFilm }, EntriesOf(store));
        Assert.Equal(new[] { AFilm, AnotherFilm }, RowsOf(server, playlistId).OrderBy(id => id).ToArray());
    }

    /// <summary>
    /// TRANSITION FOUR. A row the projector wrote and that is still there is neither an
    /// addition nor a removal, and a pass over it writes nothing at all.
    /// </summary>
    [Fact]
    public async Task ARowThatIsWhereItWasLeftMovesNothing()
    {
        var store = AStore();
        Add(store, AFilm);
        Add(store, AnotherFilm);

        var server = new APlaylistServerOf();
        var pass = Pass(store, server);
        await pass.RunAsync(null, CancellationToken.None);

        var afterTheFirst = server.Writes;
        var entries = EntriesOf(store);

        var second = await pass.RunAsync(null, CancellationToken.None);

        Assert.Equal(0, second.Writes);
        Assert.Equal(afterTheFirst, server.Writes);
        Assert.Equal(entries, EntriesOf(store));
    }

    /// <summary>
    /// The addition and the removal in one pass, which is what a person editing a
    /// playlist actually does. Held together because a rule that reads each in isolation
    /// can still get the pair wrong.
    /// </summary>
    [Fact]
    public async Task AnAdditionAndARemovalInOnePassAreBothTaken()
    {
        var store = AStore();
        Add(store, AFilm);
        Add(store, AnotherFilm);

        var server = new APlaylistServerOf();
        var pass = Pass(store, server);
        await pass.RunAsync(null, CancellationToken.None);

        var playlistId = store.Read(AUser).Document!.Projection!.PlaylistId;
        server.Rows(playlistId, AnotherFilm, AThirdFilm);

        await pass.RunAsync(null, CancellationToken.None);

        Assert.Equal(new[] { AnotherFilm, AThirdFilm }, EntriesOf(store));
    }

    /// <summary>
    /// A row of a kind a list does not hold is left where it is. Somebody can put a music
    /// track in a playlist, and adoption is not a way past the rule the endpoints
    /// enforce: it stays in the playlist and never becomes an entry.
    /// </summary>
    [Fact]
    public async Task ARowOfAKindAListDoesNotHoldIsNotAdopted()
    {
        var store = AStore();
        Add(store, AFilm);

        var server = new APlaylistServerOf();
        var pass = Pass(store, server);
        await pass.RunAsync(null, CancellationToken.None);

        var playlistId = store.Read(AUser).Document!.Projection!.PlaylistId;
        server.Rows(playlistId, AFilm, ASong);

        await pass.RunAsync(null, CancellationToken.None);

        Assert.Equal(new[] { AFilm }, EntriesOf(store));
    }

    /// <summary>
    /// Taking away the episode a show projects as does not take the show off the list.
    /// The row carries an episode's identifier and the list holds the show's, so the
    /// store is asked to remove an entry it does not have and nothing happens - which is
    /// the answer rather than an oversight, because what the person put on the list was
    /// the show.
    /// </summary>
    [Fact]
    public async Task RemovingTheEpisodeAShowProjectsAsDoesNotRemoveTheShow()
    {
        var store = AStore();
        Add(store, AShow, WatchlistItemKind.Series);

        var server = new APlaylistServerOf();
        var library = new ASeriesLibraryOf().Holding(AShow, AUser, new SeriesEpisode
        {
            ItemId = FirstEpisode,
            SeasonNumber = 1,
            EpisodeNumber = 1,
            IsPlayed = false,
        });

        var pass = Pass(store, server, library);
        await pass.RunAsync(null, CancellationToken.None);

        var playlistId = store.Read(AUser).Document!.Projection!.PlaylistId;
        Assert.Equal(new[] { FirstEpisode }, RowsOf(server, playlistId));

        server.Rows(playlistId);
        await pass.RunAsync(null, CancellationToken.None);

        Assert.Equal(new[] { AShow }, EntriesOf(store));
        Assert.Equal(new[] { FirstEpisode }, RowsOf(server, playlistId));
    }

    /// <summary>
    /// A document upgraded from before the record existed carries an empty set, and the
    /// first pass over it reads every row as somebody's addition rather than as a
    /// removal. That is the direction the upgrade step chose deliberately, and it is
    /// asserted here rather than only argued there.
    /// </summary>
    [Fact]
    public async Task AnUpgradedRecordAdoptsRatherThanDeletes()
    {
        var store = AStore();
        Add(store, AFilm);

        var server = new APlaylistServerOf();
        var pass = Pass(store, server);
        await pass.RunAsync(null, CancellationToken.None);

        var playlistId = store.Read(AUser).Document!.Projection!.PlaylistId;

        // What an upgrade leaves: a playlist that is remembered and a record that does
        // not know what was put in it.
        Assert.True(store.SetProjection(AUser, store.Read(AUser).Document!.Projection! with
        {
            ProjectedItemIds = [],
            WrittenAt = null,
        }));

        server.Rows(playlistId, AnotherFilm);

        await pass.RunAsync(null, CancellationToken.None);

        // AFilm was never in the playlist and is NOT read as a removal, and AnotherFilm
        // is taken as an addition.
        Assert.Equal(new[] { AFilm, AnotherFilm }, EntriesOf(store));
    }

    /// <summary>
    /// The edits are taken BEFORE the list is written back, and the order is the whole
    /// of it: a pass that reconciled first would undo on a television exactly what
    /// somebody had just done there.
    /// </summary>
    [Fact]
    public async Task TheEditIsTakenBeforeTheListIsWrittenBack()
    {
        var store = AStore();
        Add(store, AFilm);

        var server = new APlaylistServerOf();
        var pass = Pass(store, server);
        await pass.RunAsync(null, CancellationToken.None);

        var playlistId = store.Read(AUser).Document!.Projection!.PlaylistId;
        var afterTheFirst = server.Writes;

        server.Rows(playlistId, AFilm, AnotherFilm);

        await pass.RunAsync(null, CancellationToken.None);

        // The added row is on the list now, so the reconciler wants it in the playlist
        // and it is already there: the pass takes the edit and then writes nothing.
        Assert.Equal(new[] { AFilm, AnotherFilm }, EntriesOf(store));
        Assert.Equal(afterTheFirst, server.Writes);
    }

    /// <summary>
    /// The counts a pass returns are of the rows it moved, and taking an edit is not one
    /// of them: adopting a row nobody has to write again writes nothing.
    /// </summary>
    [Fact]
    public async Task TakingAnEditIsNotCountedAsAPlaylistWrite()
    {
        var store = AStore();
        Add(store, AFilm);

        var server = new APlaylistServerOf();
        var pass = Pass(store, server);
        await pass.RunAsync(null, CancellationToken.None);

        var playlistId = store.Read(AUser).Document!.Projection!.PlaylistId;
        server.Rows(playlistId, AFilm, AnotherFilm);

        var second = await pass.RunAsync(null, CancellationToken.None);

        Assert.Equal(0, second.Writes);
    }

    private static readonly DateTimeOffset AStoppedInstant =
        new(2026, 1, 2, 3, 4, 5, TimeSpan.Zero);

    private static Guid[] EntriesOf(WatchlistDocumentStore store) =>
        store.Read(AUser).Document!.Entries.Select(entry => entry.ItemId).OrderBy(id => id).ToArray();

    private static Guid[] RowsOf(APlaylistServerOf server, Guid playlistId) =>
        server.EntriesOf(playlistId, AUser).Select(row => row.ItemId).ToArray();

    private static void Add(WatchlistDocumentStore store, Guid itemId, WatchlistItemKind kind = WatchlistItemKind.Movie)
    {
        var result = store.Add(
            AUser,
            new WatchlistEntry
            {
                ItemId = itemId,
                Kind = kind,
                AddedAt = AStoppedInstant,
                Source = WatchlistEntrySource.Api,
            },
            PluginConfiguration.DefaultMaxEntriesPerUser);

        Assert.Equal(WatchlistAddOutcome.Added, result.Outcome);
    }

    private WatchlistDocumentStore AStore() => new(DataFolder, new RecordingLogger());

    private WatchlistProjectionPass Pass(
        WatchlistDocumentStore store,
        APlaylistServerOf server,
        ASeriesLibraryOf? library = null) => new(
            store,
            new WatchlistProjector(server, new RecordingProjectorLogger()),
            new WatchlistReconciler(server, new RecordingReconcilerLogger()),
            server,
            new ADescriberOf(
                (AFilm, AUser, WatchlistItemKind.Movie),
                (AnotherFilm, AUser, WatchlistItemKind.Movie),
                (AThirdFilm, AUser, WatchlistItemKind.Movie),
                (ASong, AUser, WatchlistItemKind.Other),
                (AShow, AUser, WatchlistItemKind.Series),
                (FirstEpisode, AUser, WatchlistItemKind.Episode)),
            library ?? new ASeriesLibraryOf(),
            new StoppedClock(AStoppedInstant),
            () => new PluginConfiguration(),
            new RecordingPassLogger());
}
