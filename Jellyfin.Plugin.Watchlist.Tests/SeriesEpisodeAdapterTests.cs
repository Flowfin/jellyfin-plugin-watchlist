using System;
using System.Linq;
using Jellyfin.Plugin.Watchlist.Projection;
using Xunit;

namespace Jellyfin.Plugin.Watchlist.Tests;

/// <summary>
/// The adapter that asks the library what a series holds, driven with no server.
/// </summary>
/// <remarks>
/// <para>
/// It is here rather than behind a coverage waiver because it holds no process-wide
/// static and reaches nothing a test cannot build. That is the same reading the
/// completion adapter beside it stands on, and the two waivers this suite does carry
/// say in their own words that width is not unreachability.
/// </para>
/// <para>
/// What is proven is what the adapter READS. It sets a played state on each episode out
/// of a second query rather than off the item, so a rule that dropped that query would
/// mark every episode unplayed and every one of these assertions would still pass on a
/// library of unplayed episodes. The played cases below are what stops that.
/// </para>
/// </remarks>
public sealed class SeriesEpisodeAdapterTests
{
    private static readonly Guid TheViewer = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private static readonly Guid ASeries = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000001");

    private static readonly Guid FirstEpisode = Guid.Parse("cccccccc-0000-0000-0000-000000000001");

    private static readonly Guid SecondEpisode = Guid.Parse("cccccccc-0000-0000-0000-000000000002");

    /// <summary>
    /// The two numbers and the played state come back per episode, and the played state
    /// is the one the library holds rather than a default.
    /// </summary>
    [Fact]
    public void EachEpisodeComesBackWithItsNumbersAndItsPlayedState()
    {
        var library = new ALibraryOf()
            .WithEpisode(ASeries, FirstEpisode, season: 1, number: 1, played: true)
            .WithEpisode(ASeries, SecondEpisode, season: 1, number: 2, played: false);

        var episodes = new LibrarySeriesEpisodes(library, new AUserDirectoryOf(TheViewer))
            .Of(ASeries, TheViewer);

        Assert.Equal(2, episodes.Count);

        var first = episodes.Single(episode => episode.ItemId == FirstEpisode);
        Assert.Equal(1, first.SeasonNumber);
        Assert.Equal(1, first.EpisodeNumber);
        Assert.True(first.IsPlayed);

        var second = episodes.Single(episode => episode.ItemId == SecondEpisode);
        Assert.Equal(2, second.EpisodeNumber);
        Assert.False(second.IsPlayed);
    }

    /// <summary>
    /// An episode the library holds no numbers for comes back with none, rather than
    /// with a number this adapter invented. The rule above it is written to order such
    /// an episode and cannot do that if the absence is filled in here.
    /// </summary>
    [Fact]
    public void AnEpisodeWithNoNumbersComesBackWithNone()
    {
        var library = new ALibraryOf()
            .WithEpisode(ASeries, FirstEpisode, season: null, number: null, played: false);

        var episode = Assert.Single(
            new LibrarySeriesEpisodes(library, new AUserDirectoryOf(TheViewer)).Of(ASeries, TheViewer));

        Assert.Null(episode.SeasonNumber);
        Assert.Null(episode.EpisodeNumber);
    }

    /// <summary>
    /// The episodes of one series only. A second series in the same library does not
    /// leak into the first one's answer.
    /// </summary>
    [Fact]
    public void EpisodesOfAnotherSeriesAreNotInThisOnes()
    {
        var another = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000002");

        var library = new ALibraryOf()
            .WithEpisode(ASeries, FirstEpisode, season: 1, number: 1, played: false)
            .WithEpisode(another, SecondEpisode, season: 1, number: 1, played: false);

        var episodes = new LibrarySeriesEpisodes(library, new AUserDirectoryOf(TheViewer))
            .Of(ASeries, TheViewer);

        Assert.Equal(new[] { FirstEpisode }, episodes.Select(episode => episode.ItemId).ToArray());
    }

    /// <summary>
    /// A series the library holds nothing of is no episodes rather than an exception,
    /// which is what the rule above turns into no playlist row.
    /// </summary>
    [Fact]
    public void ASeriesTheLibraryHoldsNothingOfIsNoEpisodes()
    {
        Assert.Empty(
            new LibrarySeriesEpisodes(new ALibraryOf(), new AUserDirectoryOf(TheViewer)).Of(ASeries, TheViewer));
    }

    /// <summary>
    /// A user the server does not know is no episodes, and the library is never asked:
    /// a query with no user cannot answer a played state at all, and this library
    /// refuses one rather than answering it.
    /// </summary>
    [Fact]
    public void AUserTheServerDoesNotKnowIsNoEpisodes()
    {
        var library = new ALibraryOf()
            .WithEpisode(ASeries, FirstEpisode, season: 1, number: 1, played: false);

        Assert.Empty(
            new LibrarySeriesEpisodes(library, new AUserDirectoryOf()).Of(ASeries, TheViewer));
    }
}
