using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Watchlist.Configuration;
using Jellyfin.Plugin.Watchlist.Store;
using Jellyfin.Plugin.Watchlist.Watched;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Audio;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;
using Xunit;

namespace Jellyfin.Plugin.Watchlist.Tests;

/// <summary>
/// The two halves that touch the server: what this plugin listens to, and what it asks
/// the library about a series.
/// </summary>
/// <remarks>
/// <para>
/// Neither needs a server. The event is an interface member, the library and the user
/// directory are interfaces, and the items the event carries are ordinary objects, so
/// both halves are driven here rather than argued about. That is why neither file is
/// on the coverage floor's exclusion list.
/// </para>
/// <para>
/// The subscription is where "marking an item unplayed does not put it back" is
/// decided, because a save that is not a play never becomes something the rule sees.
/// </para>
/// </remarks>
public sealed class WatchedSubscriptionTests : IDisposable
{
    private static readonly Guid TheViewer = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private static readonly Guid AMovie = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");

    private static readonly Guid ASeries = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000001");

    private static readonly Guid AnEpisode = Guid.Parse("cccccccc-0000-0000-0000-000000000001");

    private static readonly DateTimeOffset WhenItWasAdded =
        new(2026, 1, 2, 3, 4, 5, TimeSpan.Zero);

    private readonly TemporaryDirectory _sandbox = new("watchlist-watched-subscription");

    private string DataFolder => Path.Combine(_sandbox.FullPath, "plugin-data");

    /// <inheritdoc />
    public void Dispose()
    {
        _sandbox.Dispose();
    }

    /// <summary>
    /// The server starts it, the event reaches the list, the server stops it and the
    /// same event reaches nothing. The second half is the one that is easy to leave
    /// out, and a subscription that outlives the plugin is a handler running against a
    /// store nobody is using.
    /// </summary>
    [Fact]
    public async Task ItListensWhileItIsRunningAndStopsListeningWhenItIsStopped()
    {
        var store = new WatchlistDocumentStore(DataFolder);
        store.Add(TheViewer, Entry(AMovie, WatchlistItemKind.Movie), maxEntriesPerUser: 10);

        var userData = new AUserDataManagerOf();
        var subscription = new UserDataWatchedSubscription(userData, HandlerOver(store));

        Assert.Equal(0, userData.Listeners);

        await subscription.StartAsync(CancellationToken.None);

        Assert.Equal(1, userData.Listeners);

        userData.Raise(Saved(new Movie { Id = AMovie }, played: true));

        Assert.Empty(EntriesOf(store, TheViewer));

        await subscription.StopAsync(CancellationToken.None);

        Assert.Equal(0, userData.Listeners);

        store.Add(TheViewer, Entry(AMovie, WatchlistItemKind.Movie), maxEntriesPerUser: 10);
        userData.Raise(Saved(new Movie { Id = AMovie }, played: true));

        Assert.Equal([AMovie], EntriesOf(store, TheViewer));
    }

    /// <summary>
    /// Marking an item unplayed does not put it back, and it does not take anything
    /// off either. A save that is not a play is not something this plugin acts on.
    /// </summary>
    [Fact]
    public async Task MarkingAnItemUnplayedChangesNothing()
    {
        var store = new WatchlistDocumentStore(DataFolder);
        store.Add(TheViewer, Entry(AMovie, WatchlistItemKind.Movie), maxEntriesPerUser: 10);

        var userData = new AUserDataManagerOf();
        var subscription = new UserDataWatchedSubscription(userData, HandlerOver(store));
        await subscription.StartAsync(CancellationToken.None);

        userData.Raise(Saved(new Movie { Id = AMovie }, played: false));

        Assert.Equal([AMovie], EntriesOf(store, TheViewer));

        userData.Raise(Saved(new Movie { Id = AMovie }, played: true));

        Assert.Empty(EntriesOf(store, TheViewer));

        userData.Raise(Saved(new Movie { Id = AMovie }, played: false));

        Assert.Empty(EntriesOf(store, TheViewer));
    }

    /// <summary>
    /// What this plugin makes of one save, for each shape the event arrives in.
    /// </summary>
    /// <remarks>
    /// A save carrying no user data at all, and one carrying an item this plugin has
    /// no name for, are the two that would otherwise reach the rule as something it
    /// has to be told to ignore. They stop here instead.
    /// </remarks>
    [Fact]
    public void OnlyAPlayedItemOfAKindAWatchlistHoldsBecomesSomethingToActOn()
    {
        Assert.Null(UserDataWatchedSubscription.PlayedItemIn(new UserDataSaveEventArgs
        {
            UserId = TheViewer,
            Item = new Movie { Id = AMovie },
            UserData = null,
        }));

        Assert.Null(UserDataWatchedSubscription.PlayedItemIn(Saved(new Movie { Id = AMovie }, played: false)));
        Assert.Null(UserDataWatchedSubscription.PlayedItemIn(Saved(null, played: true)));
        Assert.Null(UserDataWatchedSubscription.PlayedItemIn(Saved(new Audio { Id = AMovie }, played: true)));

        var movie = UserDataWatchedSubscription.PlayedItemIn(Saved(new Movie { Id = AMovie }, played: true));

        Assert.Equal(AMovie, movie!.ItemId);
        Assert.Equal(WatchlistItemKind.Movie, movie.Kind);
        Assert.Null(movie.SeriesId);

        var series = UserDataWatchedSubscription.PlayedItemIn(Saved(new Series { Id = ASeries }, played: true));

        Assert.Equal(WatchlistItemKind.Series, series!.Kind);
        Assert.Null(series.SeriesId);

        var episode = UserDataWatchedSubscription.PlayedItemIn(
            Saved(new Episode { Id = AnEpisode, SeriesId = ASeries }, played: true));

        Assert.Equal(AnEpisode, episode!.ItemId);
        Assert.Equal(WatchlistItemKind.Episode, episode.Kind);
        Assert.Equal(ASeries, episode.SeriesId);
    }

    /// <summary>
    /// The translation refuses to be called with nothing.
    /// </summary>
    [Fact]
    public void TheTranslationRefusesAnAbsentEvent()
    {
        Assert.Throws<ArgumentNullException>(() => UserDataWatchedSubscription.PlayedItemIn(null!));
    }

    /// <summary>
    /// The completion answer, over a library that holds the episodes and their played
    /// state. A series with one episode left is not finished; the same series with
    /// that episode played is.
    /// </summary>
    /// <param name="unwatched">How many episodes of the series are still unplayed.</param>
    /// <param name="finished">What the answer should be.</param>
    [Theory]
    [InlineData(2, false)]
    [InlineData(1, false)]
    [InlineData(0, true)]
    public void ASeriesIsFinishedWhenNoEpisodeOfItIsUnplayed(int unwatched, bool finished)
    {
        var library = new ALibraryOf();

        for (var i = 0; i < 3; i++)
        {
            library.WithEpisode(ASeries, played: i >= unwatched);
        }

        var completion = new LibrarySeriesCompletion(library, new AUserDirectoryOf(TheViewer));

        Assert.Equal(finished, completion.EveryEpisodeIsPlayed(ASeries, TheViewer));
    }

    /// <summary>
    /// A series this user has no episodes of is not finished, it is unanswerable, and
    /// the two must not read the same. Vacuously every episode of it is played, and
    /// acting on that would take a series off a list the moment its files went
    /// missing.
    /// </summary>
    [Fact]
    public void ASeriesWithNoEpisodesIsNotFinished()
    {
        var completion = new LibrarySeriesCompletion(new ALibraryOf(), new AUserDirectoryOf(TheViewer));

        Assert.False(completion.EveryEpisodeIsPlayed(ASeries, TheViewer));
    }

    /// <summary>
    /// A user the server does not know is not finished either, and the library is not
    /// asked about them: a query with no user cannot answer a played state at all.
    /// </summary>
    [Fact]
    public void AUserTheServerDoesNotKnowIsNotFinished()
    {
        var library = new ALibraryOf().WithEpisode(ASeries, played: true);
        var completion = new LibrarySeriesCompletion(library, new AUserDirectoryOf());

        Assert.False(completion.EveryEpisodeIsPlayed(ASeries, TheViewer));
    }

    /// <summary>
    /// The episodes of one series only. A second series in the same library is not
    /// counted into the first one's answer.
    /// </summary>
    [Fact]
    public void EpisodesOfAnotherSeriesAreNotCountedIntoThisOne()
    {
        var another = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000002");

        var library = new ALibraryOf()
            .WithEpisode(ASeries, played: true)
            .WithEpisode(another, played: false);

        var completion = new LibrarySeriesCompletion(library, new AUserDirectoryOf(TheViewer));

        Assert.True(completion.EveryEpisodeIsPlayed(ASeries, TheViewer));
        Assert.False(completion.EveryEpisodeIsPlayed(another, TheViewer));
    }

    private static UserDataSaveEventArgs Saved(BaseItem? item, bool played) => new()
    {
        UserId = TheViewer,
        Item = item!,
        UserData = new UserItemData { Key = "watched", Played = played },
        SaveReason = UserDataSaveReason.TogglePlayed,
    };

    private static WatchedRemovalHandler HandlerOver(WatchlistDocumentStore store) =>
        new(
            store,
            static () => new PluginConfiguration { RemoveWhenWatched = true },
            new AFinishedSeriesSet(),
            new RecordingWatchedLogger());

    private static WatchlistEntry Entry(Guid itemId, WatchlistItemKind kind) => new()
    {
        ItemId = itemId,
        Kind = kind,
        AddedAt = WhenItWasAdded,
        Source = WatchlistEntrySource.Api,
    };

    private static Guid[] EntriesOf(WatchlistDocumentStore store, Guid userId) =>
        [.. store.Read(userId).Document!.Entries.Select(entry => entry.ItemId)];
}
