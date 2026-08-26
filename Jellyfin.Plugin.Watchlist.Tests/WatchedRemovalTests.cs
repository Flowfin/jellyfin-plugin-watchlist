using System;
using System.IO;
using System.Linq;
using Jellyfin.Plugin.Watchlist.Configuration;
using Jellyfin.Plugin.Watchlist.Store;
using Jellyfin.Plugin.Watchlist.Watched;
using Xunit;

namespace Jellyfin.Plugin.Watchlist.Tests;

/// <summary>
/// What a played item takes off a list, what it leaves, and whose list it reaches.
/// </summary>
/// <remarks>
/// <para>
/// A watchlist that keeps what has been watched becomes a chore, and a rule that
/// clears it too eagerly loses a series somebody is halfway through. The two failures
/// pull in opposite directions, so both are held here rather than one.
/// </para>
/// <para>
/// Every test owns a directory of its own and deletes it afterwards. Nothing reads a
/// shared temporary path, the clock or a server.
/// </para>
/// </remarks>
public sealed class WatchedRemovalTests : IDisposable
{
    private static readonly Guid TheViewer = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private static readonly Guid SomebodyElse = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private static readonly Guid AMovie = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");

    private static readonly Guid ASeries = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000001");

    private static readonly Guid AnotherSeries = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000002");

    private static readonly Guid AnEpisode = Guid.Parse("cccccccc-0000-0000-0000-000000000001");

    private static readonly Guid ASharedList = Guid.Parse("dddddddd-0000-0000-0000-000000000001");

    private static readonly DateTimeOffset WhenItWasAdded =
        new(2026, 1, 2, 3, 4, 5, TimeSpan.Zero);

    private readonly TemporaryDirectory _sandbox = new("watchlist-watched-removal");

    private string DataFolder => Path.Combine(_sandbox.FullPath, "plugin-data");

    /// <inheritdoc />
    public void Dispose()
    {
        _sandbox.Dispose();
    }

    /// <summary>
    /// A film marked played comes off the list, and the line that says so names a
    /// count and identifiers rather than anything read out of the library.
    /// </summary>
    [Fact]
    public void APlayedMovieLeavesThatUsersList()
    {
        var store = new WatchlistDocumentStore(DataFolder);
        store.Add(TheViewer, Entry(AMovie, WatchlistItemKind.Movie), maxEntriesPerUser: 10);

        var log = new RecordingWatchedLogger();

        HandlerOver(store, On(), new AFinishedSeriesSet(), log)
            .Handle(TheViewer, Played(AMovie, WatchlistItemKind.Movie));

        Assert.Empty(EntriesOf(store, TheViewer));
        Assert.Single(log.Lines);
        Assert.Contains("took 1 entries", log.Lines[0], StringComparison.Ordinal);
    }

    /// <summary>
    /// Something nobody put on the list changes nothing, and nothing is written or
    /// logged for it.
    /// </summary>
    [Fact]
    public void APlayedItemThatIsOnNoListChangesNothing()
    {
        var store = new WatchlistDocumentStore(DataFolder);
        store.Add(TheViewer, Entry(AMovie, WatchlistItemKind.Movie), maxEntriesPerUser: 10);

        var log = new RecordingWatchedLogger();

        HandlerOver(store, On(), new AFinishedSeriesSet(), log)
            .Handle(TheViewer, Played(AnEpisode, WatchlistItemKind.Episode));

        Assert.Equal([AMovie], EntriesOf(store, TheViewer));
        Assert.Empty(log.Lines);
    }

    /// <summary>
    /// One finished episode of a series somebody is halfway through. The episode entry
    /// goes and the series entry stays, which is the failure this rule exists against.
    /// </summary>
    [Fact]
    public void OneEpisodeOfAnUnfinishedSeriesTakesTheEpisodeAndLeavesTheSeries()
    {
        var store = new WatchlistDocumentStore(DataFolder);
        store.Add(TheViewer, Entry(ASeries, WatchlistItemKind.Series), maxEntriesPerUser: 10);
        store.Add(TheViewer, Entry(AnEpisode, WatchlistItemKind.Episode), maxEntriesPerUser: 10);

        var series = new AFinishedSeriesSet();

        HandlerOver(store, On(), series, new RecordingWatchedLogger())
            .Handle(TheViewer, Played(AnEpisode, WatchlistItemKind.Episode, ASeries));

        Assert.Equal([ASeries], EntriesOf(store, TheViewer));
        Assert.Equal([(ASeries, TheViewer)], series.Asked);
    }

    /// <summary>
    /// The last one. The same event shape as above against a series the library now
    /// answers is finished, and the series entry goes with the episode.
    /// </summary>
    [Fact]
    public void TheLastEpisodeOfASeriesTakesTheSeriesOffAsWell()
    {
        var store = new WatchlistDocumentStore(DataFolder);
        store.Add(TheViewer, Entry(ASeries, WatchlistItemKind.Series), maxEntriesPerUser: 10);
        store.Add(TheViewer, Entry(AnEpisode, WatchlistItemKind.Episode), maxEntriesPerUser: 10);

        HandlerOver(store, On(), new AFinishedSeriesSet(ASeries), new RecordingWatchedLogger())
            .Handle(TheViewer, Played(AnEpisode, WatchlistItemKind.Episode, ASeries));

        Assert.Empty(EntriesOf(store, TheViewer));
    }

    /// <summary>
    /// A whole series marked played reaches the same question rather than a second
    /// rule of its own, so a series the library does not call finished stays on the
    /// list even when the series item itself was marked played.
    /// </summary>
    /// <param name="finished">Whether the library answers that every episode is played.</param>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void AWholeSeriesMarkedPlayedStillAsksWhetherEveryEpisodeIs(bool finished)
    {
        var store = new WatchlistDocumentStore(DataFolder);
        store.Add(TheViewer, Entry(ASeries, WatchlistItemKind.Series), maxEntriesPerUser: 10);

        var series = finished ? new AFinishedSeriesSet(ASeries) : new AFinishedSeriesSet();

        HandlerOver(store, On(), series, new RecordingWatchedLogger())
            .Handle(TheViewer, Played(ASeries, WatchlistItemKind.Series));

        Assert.Equal(finished ? Array.Empty<Guid>() : [ASeries], EntriesOf(store, TheViewer));
        Assert.Equal([(ASeries, TheViewer)], series.Asked);
    }

    /// <summary>
    /// An episode of one series does not reach a different series on the same list,
    /// and the library is not asked about that one either.
    /// </summary>
    [Fact]
    public void AnEpisodeDoesNotReachADifferentSeriesOnTheSameList()
    {
        var store = new WatchlistDocumentStore(DataFolder);
        store.Add(TheViewer, Entry(AnotherSeries, WatchlistItemKind.Series), maxEntriesPerUser: 10);

        var series = new AFinishedSeriesSet(ASeries, AnotherSeries);

        HandlerOver(store, On(), series, new RecordingWatchedLogger())
            .Handle(TheViewer, Played(AnEpisode, WatchlistItemKind.Episode, ASeries));

        Assert.Equal([AnotherSeries], EntriesOf(store, TheViewer));
        Assert.Empty(series.Asked);
    }

    /// <summary>
    /// The setting off, from either side. Some people keep what they have watched on
    /// the list on purpose, so this is a switch rather than a rule.
    /// </summary>
    /// <param name="serverWide">What the server answers.</param>
    /// <param name="perUser">What the user answers, or null where they have not.</param>
    [Theory]
    [InlineData(false, null)]
    [InlineData(true, false)]
    public void WithTheSettingOffNothingLeavesTheList(bool serverWide, bool? perUser)
    {
        var store = new WatchlistDocumentStore(DataFolder);
        store.Add(TheViewer, Entry(AMovie, WatchlistItemKind.Movie), maxEntriesPerUser: 10);
        store.SetPreferences(
            TheViewer,
            perUser is null ? null : new WatchlistUserPreferences { RemoveWhenWatched = perUser });

        var series = new AFinishedSeriesSet();

        HandlerOver(store, Setting(serverWide), series, new RecordingWatchedLogger())
            .Handle(TheViewer, Played(AMovie, WatchlistItemKind.Movie));

        Assert.Equal([AMovie], EntriesOf(store, TheViewer));
        Assert.Empty(series.Asked);
    }

    /// <summary>
    /// A user's own answer of on, against a server answering off, is what removes the
    /// entry. The pair with the theory above is what makes the precedence visible
    /// rather than the value.
    /// </summary>
    [Fact]
    public void AUsersOwnAnswerOfOnBeatsAServerAnsweringOff()
    {
        var store = new WatchlistDocumentStore(DataFolder);
        store.Add(TheViewer, Entry(AMovie, WatchlistItemKind.Movie), maxEntriesPerUser: 10);
        store.SetPreferences(TheViewer, new WatchlistUserPreferences { RemoveWhenWatched = true });

        HandlerOver(store, Setting(false), new AFinishedSeriesSet(), new RecordingWatchedLogger())
            .Handle(TheViewer, Played(AMovie, WatchlistItemKind.Movie));

        Assert.Empty(EntriesOf(store, TheViewer));
    }

    /// <summary>
    /// The event names one user and the rule acts for that one. Two people with the
    /// same film on their lists, one of them watches it, and the other's list is
    /// untouched down to the bytes.
    /// </summary>
    [Fact]
    public void TheHandlerActsForTheUserTheEventNamedAndForNobodyElse()
    {
        var store = new WatchlistDocumentStore(DataFolder);
        store.Add(TheViewer, Entry(AMovie, WatchlistItemKind.Movie), maxEntriesPerUser: 10);
        store.Add(SomebodyElse, Entry(AMovie, WatchlistItemKind.Movie), maxEntriesPerUser: 10);

        var untouched = File.ReadAllBytes(store.PathFor(SomebodyElse));

        HandlerOver(store, On(), new AFinishedSeriesSet(), new RecordingWatchedLogger())
            .Handle(TheViewer, Played(AMovie, WatchlistItemKind.Movie));

        Assert.Empty(EntriesOf(store, TheViewer));
        Assert.Equal(untouched, File.ReadAllBytes(store.PathFor(SomebodyElse)));
    }

    /// <summary>
    /// The shared list is not pruned by watching, ever. It is a list everybody sees,
    /// and taking a title off it because one person finished it would take from one
    /// person what another still wants to see.
    /// </summary>
    /// <remarks>
    /// Asserted over the bytes of the shared document rather than over its entries,
    /// because the failure this is against is a write nobody meant to make, and a
    /// rewrite that happens to preserve the entries is still a write.
    /// </remarks>
    [Fact]
    public void APlayedItemChangesNoSharedRecord()
    {
        var store = new WatchlistDocumentStore(DataFolder);
        store.WriteShared(WatchlistDocumentStore.EmptyShared(ASharedList, TheViewer));
        store.AddShared(Entry(AMovie, WatchlistItemKind.Movie), maxEntriesInSharedList: 10);
        store.Add(TheViewer, Entry(AMovie, WatchlistItemKind.Movie), maxEntriesPerUser: 10);

        var untouched = File.ReadAllBytes(store.SharedListPath);

        HandlerOver(store, On(), new AFinishedSeriesSet(), new RecordingWatchedLogger())
            .Handle(TheViewer, Played(AMovie, WatchlistItemKind.Movie));

        Assert.Empty(EntriesOf(store, TheViewer));
        Assert.Equal(untouched, File.ReadAllBytes(store.SharedListPath));
    }

    /// <summary>
    /// A list this plugin will not read is left alone. It does not know what is on it,
    /// so removing nothing is the only answer that cannot destroy something.
    /// </summary>
    [Fact]
    public void AListThatCouldNotBeReadIsLeftAlone()
    {
        var store = new WatchlistDocumentStore(DataFolder);
        Directory.CreateDirectory(DataFolder);
        File.WriteAllText(store.PathFor(TheViewer), ADocumentFromTheFuture);

        var untouched = File.ReadAllBytes(store.PathFor(TheViewer));
        var series = new AFinishedSeriesSet();

        HandlerOver(store, On(), series, new RecordingWatchedLogger())
            .Handle(TheViewer, Played(AMovie, WatchlistItemKind.Movie));

        Assert.Equal(untouched, File.ReadAllBytes(store.PathFor(TheViewer)));
        Assert.Empty(series.Asked);
    }

    /// <summary>
    /// The rule and the handler refuse to be called with nothing rather than treating
    /// it as an empty answer.
    /// </summary>
    [Fact]
    public void TheRuleAndTheHandlerRefuseAnAbsentArgument()
    {
        var store = new WatchlistDocumentStore(DataFolder);
        var series = new AFinishedSeriesSet();
        var played = Played(AMovie, WatchlistItemKind.Movie);

        Assert.Throws<ArgumentNullException>(
            () => WatchedRemoval.EntriesRetiredBy(null!, played, TheViewer, series));
        Assert.Throws<ArgumentNullException>(
            () => WatchedRemoval.EntriesRetiredBy([], null!, TheViewer, series));
        Assert.Throws<ArgumentNullException>(
            () => WatchedRemoval.EntriesRetiredBy([], played, TheViewer, null!));
        Assert.Throws<ArgumentNullException>(
            () => HandlerOver(store, On(), series, new RecordingWatchedLogger()).Handle(TheViewer, null!));
    }

    private static string ADocumentFromTheFuture =>
        "{\"SchemaVersion\":" + (WatchlistDocument.CurrentSchemaVersion + 1)
        + ",\"UserId\":\"" + TheViewer + "\",\"Entries\":[]}";

    private static WatchedRemovalHandler HandlerOver(
        WatchlistDocumentStore store,
        PluginConfiguration configuration,
        ISeriesCompletion series,
        RecordingWatchedLogger log) =>
        new(store, () => configuration, series, log);

    private static PluginConfiguration On() => Setting(true);

    private static PluginConfiguration Setting(bool removeWhenWatched) =>
        new() { RemoveWhenWatched = removeWhenWatched };

    private static WatchedItem Played(Guid itemId, WatchlistItemKind kind, Guid? seriesId = null) => new()
    {
        ItemId = itemId,
        Kind = kind,
        SeriesId = seriesId,
    };

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
