using System;
using System.Collections.Generic;
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
/// What a show on a list becomes in the playlist: one episode, which one, and what
/// happens to it when that episode is played.
/// </summary>
/// <remarks>
/// <para>
/// The rule is driven twice over. Once as a function of a list of episodes, where
/// every ordering case is cheap and total, and once through a target and a
/// reconciliation pass, where what is proven is that the chosen episode is what
/// actually reaches a playlist and that a second pass over an unchanged library writes
/// nothing.
/// </para>
/// <para>
/// Nothing here needs a server, a library or a file outside its own temporary
/// directory, and no test reads the machine clock.
/// </para>
/// </remarks>
public sealed class SeriesProjectionTests : IDisposable
{
    private static readonly Guid AUser = Guid.Parse("55555555-5555-5555-5555-555555555555");

    private static readonly Guid AShow = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000001");

    private static readonly Guid AFilm = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");

    private static readonly Guid FirstEpisode = Guid.Parse("cccccccc-0000-0000-0000-000000000001");

    private static readonly Guid SecondEpisode = Guid.Parse("cccccccc-0000-0000-0000-000000000002");

    private static readonly Guid ThirdEpisode = Guid.Parse("cccccccc-0000-0000-0000-000000000003");

    private readonly TemporaryDirectory _sandbox = new("watchlist-series-projection");

    private string DataFolder => Path.Join(_sandbox.FullPath, "plugin-data");

    /// <inheritdoc />
    public void Dispose()
    {
        _sandbox.Dispose();
    }

    /// <summary>
    /// Nothing played, so the row is the first episode. This is the condition the whole
    /// rule is written for: a show is one row and never every episode of it.
    /// </summary>
    [Fact]
    public void AShowNobodyHasStartedProjectsAsItsFirstEpisode()
    {
        var chosen = SeriesRow.OneEpisodeOf(
        [
            Episode(SecondEpisode, 1, 2, played: false),
            Episode(FirstEpisode, 1, 1, played: false),
            Episode(ThirdEpisode, 2, 1, played: false),
        ]);

        Assert.Equal(FirstEpisode, chosen);
    }

    /// <summary>
    /// Halfway through, so the row is the earliest episode not played rather than the
    /// one after the last one played. The two differ the moment somebody watches out of
    /// order, and skipping back to what they missed is the answer a watchlist owes.
    /// </summary>
    [Fact]
    public void AShowHalfWatchedProjectsAsTheEarliestEpisodeNotPlayed()
    {
        var chosen = SeriesRow.OneEpisodeOf(
        [
            Episode(FirstEpisode, 1, 1, played: true),
            Episode(SecondEpisode, 1, 2, played: false),
            Episode(ThirdEpisode, 1, 3, played: true),
        ]);

        Assert.Equal(SecondEpisode, chosen);
    }

    /// <summary>
    /// A season is read before an episode number, so the last episode of one season
    /// does not beat the first of the next.
    /// </summary>
    [Fact]
    public void TheSeasonIsReadBeforeTheEpisodeNumber()
    {
        var chosen = SeriesRow.OneEpisodeOf(
        [
            Episode(ThirdEpisode, 2, 1, played: false),
            Episode(SecondEpisode, 1, 12, played: false),
        ]);

        Assert.Equal(SecondEpisode, chosen);
    }

    /// <summary>
    /// Season zero is a season and sorts first, so an unplayed special is what a user
    /// who has one sees. Pinned because it is a consequence of the rule rather than a
    /// separate decision, and a later specials-last rule would have to change this test
    /// rather than arrive quietly beside it.
    /// </summary>
    [Fact]
    public void SeasonZeroSortsAheadOfSeasonOne()
    {
        var chosen = SeriesRow.OneEpisodeOf(
        [
            Episode(FirstEpisode, 1, 1, played: false),
            Episode(SecondEpisode, 0, 1, played: false),
        ]);

        Assert.Equal(SecondEpisode, chosen);
    }

    /// <summary>
    /// A missing number sorts behind every number there is, in both positions, and a
    /// set carrying both still produces exactly one answer. An unordered season is what
    /// a folder of loose files scans as, and the rule has to be total over it rather
    /// than throwing on the first show somebody did not match.
    /// </summary>
    [Fact]
    public void AnEpisodeWithNoNumberAndASeasonWithNoNumberSortLast()
    {
        var chosen = SeriesRow.OneEpisodeOf(
        [
            Episode(ThirdEpisode, null, null, played: false),
            Episode(SecondEpisode, 0, null, played: false),
            Episode(FirstEpisode, 0, 1, played: false),
        ]);

        Assert.Equal(FirstEpisode, chosen);

        var withoutTheNumbered = SeriesRow.OneEpisodeOf(
        [
            Episode(ThirdEpisode, null, null, played: false),
            Episode(SecondEpisode, 0, null, played: false),
        ]);

        Assert.Equal(SecondEpisode, withoutTheNumbered);
    }

    /// <summary>
    /// Two episodes a library holds no numbers for at all still order, by the one thing
    /// they always have. Without this the order would depend on what the library
    /// happened to return first, and a reconciliation pass would rebuild a playlist
    /// every run for no reason.
    /// </summary>
    [Fact]
    public void TwoUnnumberedEpisodesStillOrderTotally()
    {
        var oneWay = SeriesRow.OneEpisodeOf(
        [
            Episode(SecondEpisode, null, null, played: false),
            Episode(FirstEpisode, null, null, played: false),
        ]);

        var theOther = SeriesRow.OneEpisodeOf(
        [
            Episode(FirstEpisode, null, null, played: false),
            Episode(SecondEpisode, null, null, played: false),
        ]);

        Assert.Equal(FirstEpisode, oneWay);
        Assert.Equal(oneWay, theOther);
    }

    /// <summary>
    /// A show whose episodes have all been played keeps its row, and the row is the
    /// first episode. The alternative is a show that vanishes from the playlist while
    /// sitting on the list, which is the disappearance this rule exists against; the
    /// watched rule is what takes such a show off the list, and only when the setting
    /// says so.
    /// </summary>
    [Fact]
    public void AShowPlayedRightThroughStillProjects()
    {
        var chosen = SeriesRow.OneEpisodeOf(
        [
            Episode(FirstEpisode, 1, 1, played: true),
            Episode(SecondEpisode, 1, 2, played: true),
        ]);

        Assert.Equal(FirstEpisode, chosen);
    }

    /// <summary>
    /// A show the library holds no episodes of is no row and no error. This is the case
    /// an exception would turn into a projection pass that stops at the first broken
    /// show and leaves every later user unreconciled.
    /// </summary>
    [Fact]
    public void AShowWithNoEpisodesIsNoRowAndNoError()
    {
        Assert.Null(SeriesRow.OneEpisodeOf([]));
    }

    /// <summary>
    /// And the same answer through the target: a show with nothing behind it in the
    /// library contributes no row, while a film on the same list still does.
    /// </summary>
    [Fact]
    public void AShowWithNoEpisodesLeavesTheRestOfTheListAlone()
    {
        var store = AStoreHolding((AFilm, WatchlistItemKind.Movie), (AShow, WatchlistItemKind.Series));

        var target = TargetOver(store, new ASeriesLibraryOf());

        Assert.Equal(new[] { AFilm }, target.Wanted.ToArray());
    }

    /// <summary>
    /// The chosen episode is what reaches the playlist, and it reaches it once. Driven
    /// through the reconciler rather than off the target, because a rule that picks the
    /// right episode and a pass that writes it are two different things.
    /// </summary>
    [Fact]
    public async Task TheChosenEpisodeIsTheRowThePlaylistGets()
    {
        var store = AStoreHolding((AShow, WatchlistItemKind.Series));
        var server = new APlaylistServerOf();
        var playlistId = Guid.Parse("dddddddd-0000-0000-0000-000000000001");
        server.AlreadyHolds(AUser, playlistId, PluginConfiguration.DefaultProjectedListName);

        var library = new ASeriesLibraryOf()
            .Holding(AShow, AUser, Episode(FirstEpisode, 1, 1, played: true))
            .Holding(AShow, AUser, Episode(SecondEpisode, 1, 2, played: false));

        var reconciler = new WatchlistReconciler(server, new RecordingReconcilerLogger());
        await reconciler.ReconcileAsync(TargetOver(store, library), playlistId, CancellationToken.None);

        Assert.Equal(new[] { SecondEpisode }, server.EntriesOf(playlistId, AUser).Select(row => row.ItemId).ToArray());
    }

    /// <summary>
    /// Playing the projected episode moves the row to the next one that is not played,
    /// in the ordinary pass rather than in a step of its own. This is the condition
    /// that makes the projection follow a show instead of pinning it to whichever
    /// episode was unplayed the day it was added.
    /// </summary>
    [Fact]
    public async Task PlayingTheProjectedEpisodeRepointsTheRowOnTheNextPass()
    {
        var store = AStoreHolding((AShow, WatchlistItemKind.Series));
        var server = new APlaylistServerOf();
        var playlistId = Guid.Parse("dddddddd-0000-0000-0000-000000000002");
        server.AlreadyHolds(AUser, playlistId, PluginConfiguration.DefaultProjectedListName);

        var reconciler = new WatchlistReconciler(server, new RecordingReconcilerLogger());

        await reconciler.ReconcileAsync(
            TargetOver(store, Watched(played: 0)),
            playlistId,
            CancellationToken.None);

        Assert.Equal(new[] { FirstEpisode }, server.EntriesOf(playlistId, AUser).Select(row => row.ItemId).ToArray());

        await reconciler.ReconcileAsync(
            TargetOver(store, Watched(played: 1)),
            playlistId,
            CancellationToken.None);

        Assert.Equal(new[] { SecondEpisode }, server.EntriesOf(playlistId, AUser).Select(row => row.ItemId).ToArray());
    }

    /// <summary>
    /// A second pass over a library nothing has moved in writes nothing at all. The
    /// series rule is asked again and answers the same, which is what makes it safe to
    /// run on a schedule.
    /// </summary>
    [Fact]
    public async Task ASecondPassOverAnUnchangedShowWritesNothing()
    {
        var store = AStoreHolding((AShow, WatchlistItemKind.Series));
        var server = new APlaylistServerOf();
        var playlistId = Guid.Parse("dddddddd-0000-0000-0000-000000000003");
        server.AlreadyHolds(AUser, playlistId, PluginConfiguration.DefaultProjectedListName);

        var reconciler = new WatchlistReconciler(server, new RecordingReconcilerLogger());

        await reconciler.ReconcileAsync(TargetOver(store, Watched(played: 0)), playlistId, CancellationToken.None);
        var afterTheFirst = server.Writes;

        await reconciler.ReconcileAsync(TargetOver(store, Watched(played: 0)), playlistId, CancellationToken.None);

        Assert.Equal(afterTheFirst, server.Writes);
    }

    /// <summary>
    /// A list holding no show asks the library about no series. A rule that asked
    /// anyway would be a library query per film per pass, which is the cost the whole
    /// projection is arranged to avoid.
    /// </summary>
    [Fact]
    public void AListWithNoShowOnItAsksTheLibraryNothing()
    {
        var store = AStoreHolding((AFilm, WatchlistItemKind.Movie));
        var library = new ASeriesLibraryOf();

        var target = TargetOver(store, library);

        Assert.Equal(new[] { AFilm }, target.Wanted.ToArray());
        Assert.Empty(library.Asked);
    }

    /// <summary>
    /// A user holding a show and separately the episode it projects as asks for one row
    /// twice and gets it once. A playlist carrying the same item in two rows is what a
    /// later pass cannot tell from a duplicate somebody made by hand.
    /// </summary>
    [Fact]
    public void AShowAndTheEpisodeItProjectsAsAreOneRow()
    {
        var store = AStoreHolding((AShow, WatchlistItemKind.Series), (FirstEpisode, WatchlistItemKind.Episode));

        var target = TargetOver(store, Watched(played: 0));

        Assert.Equal(new[] { FirstEpisode }, target.Wanted.ToArray());
    }

    private static SeriesEpisode Episode(Guid itemId, int? season, int? number, bool played) =>
        new()
        {
            ItemId = itemId,
            SeasonNumber = season,
            EpisodeNumber = number,
            IsPlayed = played,
        };

    /// <summary>
    /// A show of two episodes with the first so many of them played.
    /// </summary>
    /// <param name="played">How many of the two have been played.</param>
    /// <returns>The library.</returns>
    private static ASeriesLibraryOf Watched(int played) => new ASeriesLibraryOf()
        .Holding(AShow, AUser, Episode(FirstEpisode, 1, 1, played: played >= 1))
        .Holding(AShow, AUser, Episode(SecondEpisode, 1, 2, played: played >= 2));

    private static StoppedClock AStoppedClock() =>
        new(new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero));

    private UserProjectionTarget TargetOver(WatchlistDocumentStore store, ISeriesEpisodes library) =>
        UserProjectionTarget.For(
            store,
            new PluginConfiguration(),
            new ADescriberOf(
                (AFilm, AUser, WatchlistItemKind.Movie),
                (AShow, AUser, WatchlistItemKind.Series),
                (FirstEpisode, AUser, WatchlistItemKind.Episode),
                (SecondEpisode, AUser, WatchlistItemKind.Episode)),
            library,
            AStoppedClock(),
            AUser);

    /// <summary>
    /// A store holding one user's list, in the order the entries are given, oldest
    /// first.
    /// </summary>
    /// <param name="entries">What is on the list.</param>
    /// <returns>The store.</returns>
    private WatchlistDocumentStore AStoreHolding(params (Guid ItemId, WatchlistItemKind Kind)[] entries)
    {
        var store = new WatchlistDocumentStore(DataFolder, new RecordingLogger());
        var added = new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero);

        foreach (var entry in entries)
        {
            var result = store.Add(
                AUser,
                new WatchlistEntry
                {
                    ItemId = entry.ItemId,
                    Kind = entry.Kind,
                    AddedAt = added,
                    Source = WatchlistEntrySource.Api,
                },
                PluginConfiguration.DefaultMaxEntriesPerUser);

            Assert.Equal(WatchlistAddOutcome.Added, result.Outcome);
            added = added.AddMinutes(1);
        }

        return store;
    }
}
