using System;
using System.Collections.Generic;
using System.Linq;
using Jellyfin.Plugin.Watchlist.Export;
using Jellyfin.Plugin.Watchlist.Store;
using Xunit;

namespace Jellyfin.Plugin.Watchlist.Tests;

/// <summary>
/// The matching rule an import runs on: a provider identifier first, the exporting
/// server's own identifier after it, and everything that matched neither kept and
/// counted.
/// </summary>
/// <remarks>
/// The rule is tested here rather than through an endpoint because it is a function
/// over an export and two lookups. Nothing in this file constructs a store, a document
/// or a server.
/// </remarks>
public class WatchlistImportMatchingTests
{
    private static readonly Guid TheFilmHere = new("3a7c0d21-1b44-4d0e-9f10-000000000001");
    private static readonly Guid TheFilmThere = new("3a7c0d21-1b44-4d0e-9f10-000000000002");
    private static readonly Guid TheSeriesHere = new("3a7c0d21-1b44-4d0e-9f10-000000000003");
    private static readonly Guid TheSurvivingIdentifier = new("3a7c0d21-1b44-4d0e-9f10-000000000004");
    private static readonly Guid TheStranger = new("3a7c0d21-1b44-4d0e-9f10-000000000005");

    private static readonly DateTimeOffset Added = new(2026, 3, 1, 9, 15, 0, TimeSpan.Zero);

    /// <summary>
    /// The order is the rule and not an implementation detail. This entry can be
    /// matched both ways and the provider identifier is the one that decides it,
    /// because the other identifier means the same item only on the server the file
    /// came from.
    /// </summary>
    [Fact]
    public void AProviderIdentifierIsTakenOverTheExportingServersOwn()
    {
        var entry = EntryFor(TheSurvivingIdentifier, new Dictionary<string, string> { ["Imdb"] = "tt0111161" });

        var plan = WatchlistImporter.Read(
            [entry],
            IndexOf(("Imdb", "tt0111161", TheFilmHere)),
            ResolverOf(TheSurvivingIdentifier));

        var only = Assert.Single(plan.Entries);
        Assert.Equal(WatchlistImportMatch.ByProviderId, only.Match);
        Assert.Equal(TheFilmHere, only.ItemId);
        Assert.Equal("Imdb", only.Provider);
    }

    /// <summary>
    /// The move case: the library here has the film, and the identifier the other
    /// server wrote resolves to nothing.
    /// </summary>
    [Fact]
    public void AnEntryIsMatchedByAProviderIdentifierAloneWhenTheOtherServersIdentifierIsGone()
    {
        var entry = EntryFor(TheFilmThere, new Dictionary<string, string> { ["Tmdb"] = "278" });

        var plan = WatchlistImporter.Read(
            [entry],
            IndexOf(("Tmdb", "278", TheFilmHere)),
            ResolverOf());

        var only = Assert.Single(plan.Entries);
        Assert.Equal(WatchlistImportMatch.ByProviderId, only.Match);
        Assert.Equal(TheFilmHere, only.ItemId);
        Assert.Equal("Tmdb", only.Provider);
    }

    /// <summary>
    /// The restore case: nothing outside knows this item, and the identifier the file
    /// carries still resolves, which is what happens when a list is read back onto the
    /// library it left.
    /// </summary>
    [Fact]
    public void AnEntryWithNoProviderIdentifiersFallsBackToTheOneTheExportCarried()
    {
        var entry = EntryFor(TheSeriesHere, new Dictionary<string, string>());

        var plan = WatchlistImporter.Read([entry], IndexOf(), ResolverOf(TheSeriesHere));

        var only = Assert.Single(plan.Entries);
        Assert.Equal(WatchlistImportMatch.ByItemId, only.Match);
        Assert.Equal(TheSeriesHere, only.ItemId);
        Assert.Null(only.Provider);
    }

    /// <summary>
    /// A provider this server has never indexed answers nothing, which is not an error
    /// and does not stop the entry being matched the other way.
    /// </summary>
    [Fact]
    public void AProviderThisServerDoesNotKnowFallsThroughRatherThanFailing()
    {
        var entry = EntryFor(TheSeriesHere, new Dictionary<string, string> { ["Anidb"] = "979" });

        var plan = WatchlistImporter.Read(
            [entry],
            IndexOf(("Imdb", "tt0111161", TheFilmHere)),
            ResolverOf(TheSeriesHere));

        var only = Assert.Single(plan.Entries);
        Assert.Equal(WatchlistImportMatch.ByItemId, only.Match);
        Assert.Equal(TheSeriesHere, only.ItemId);
    }

    /// <summary>
    /// The entry nobody here can place. It is kept, it says so, and it carries no item
    /// rather than a plausible one.
    /// </summary>
    [Fact]
    public void AnUnmatchableEntryIsKeptAndSaysSo()
    {
        var entry = EntryFor(TheStranger, new Dictionary<string, string> { ["Imdb"] = "tt9999999" });

        var plan = WatchlistImporter.Read([entry], IndexOf(), ResolverOf());

        var only = Assert.Single(plan.Entries);
        Assert.Equal(WatchlistImportMatch.Unmatched, only.Match);
        Assert.Null(only.ItemId);
        Assert.Null(only.Provider);
        Assert.Same(entry, only.Entry);
    }

    /// <summary>
    /// The condition this rule exists to hold: nothing is dropped. Every entry that
    /// went in comes out, in the order it went in, whatever happened to it.
    /// </summary>
    [Fact]
    public void EveryEntryComesBackInTheOrderItWentIn()
    {
        var matched = EntryFor(TheFilmThere, new Dictionary<string, string> { ["Tmdb"] = "278" });
        var restored = EntryFor(TheSeriesHere, new Dictionary<string, string>());
        var stranger = EntryFor(TheStranger, new Dictionary<string, string>());

        var plan = WatchlistImporter.Read(
            [matched, restored, stranger],
            IndexOf(("Tmdb", "278", TheFilmHere)),
            ResolverOf(TheSeriesHere));

        Assert.Equal(3, plan.Entries.Count);
        Assert.Equal(
            [matched, restored, stranger],
            plan.Entries.Select(result => result.Entry));
        Assert.Equal(
            [WatchlistImportMatch.ByProviderId, WatchlistImportMatch.ByItemId, WatchlistImportMatch.Unmatched],
            plan.Entries.Select(result => result.Match));
    }

    /// <summary>
    /// The counts are what a person reads first, and they are derived from the entries
    /// rather than recorded beside them, so the two cannot disagree.
    /// </summary>
    [Fact]
    public void TheCountsAddUpToWhatWasRead()
    {
        var matched = EntryFor(TheFilmThere, new Dictionary<string, string> { ["Tmdb"] = "278" });
        var stranger = EntryFor(TheStranger, new Dictionary<string, string>());
        var anotherStranger = EntryFor(TheFilmThere, new Dictionary<string, string>());

        var plan = WatchlistImporter.Read(
            [matched, stranger, anotherStranger],
            IndexOf(("Tmdb", "278", TheFilmHere)),
            ResolverOf());

        Assert.Equal(1, plan.MatchedCount);
        Assert.Equal(2, plan.UnmatchedCount);
        Assert.Equal(plan.Entries.Count, plan.MatchedCount + plan.UnmatchedCount);
    }

    /// <summary>
    /// An entry can carry several identifiers that all match, and only one of them is
    /// reported. Which one has to be the same on two runs over the same file, so the
    /// keys are ordered rather than taken as the dictionary happens to hold them. Both
    /// dictionaries below hold the same three pairs and were filled in opposite orders.
    /// </summary>
    [Fact]
    public void TheProviderThatDecidesIsTheSameOnTwoReadsOfTheSameEntry()
    {
        var oneWay = new Dictionary<string, string> { ["Tvdb"] = "111", ["Imdb"] = "tt222", ["Tmdb"] = "333" };
        var theOther = new Dictionary<string, string> { ["Tmdb"] = "333", ["Imdb"] = "tt222", ["Tvdb"] = "111" };

        var index = IndexOf(
            ("Tvdb", "111", TheSeriesHere),
            ("Imdb", "tt222", TheFilmHere),
            ("Tmdb", "333", TheFilmThere));

        var first = WatchlistImporter.Read([EntryFor(TheStranger, oneWay)], index, ResolverOf());
        var second = WatchlistImporter.Read([EntryFor(TheStranger, theOther)], index, ResolverOf());

        Assert.Equal("Imdb", Assert.Single(first.Entries).Provider);
        Assert.Equal("Imdb", Assert.Single(second.Entries).Provider);
    }

    /// <summary>
    /// An index answering the empty identifier is answering "nothing" without saying
    /// so. Taking it would leave an entry counted as matched and pointing at no item,
    /// which is worse than the unmatched it really is.
    /// </summary>
    [Fact]
    public void TheEmptyIdentifierFromTheIndexIsNotAMatch()
    {
        var entry = EntryFor(TheStranger, new Dictionary<string, string> { ["Imdb"] = "tt0111161" });

        var plan = WatchlistImporter.Read(
            [entry],
            IndexOf(("Imdb", "tt0111161", Guid.Empty)),
            ResolverOf());

        var only = Assert.Single(plan.Entries);
        Assert.Equal(WatchlistImportMatch.Unmatched, only.Match);
        Assert.Null(only.ItemId);
    }

    /// <summary>
    /// A file written by hand can carry the empty identifier where an export writes a
    /// real one. The library is not asked about it, because whatever it answers is an
    /// answer to a question nobody meant to ask.
    /// </summary>
    [Fact]
    public void TheEmptyIdentifierInTheFileIsNeverPutToTheLibrary()
    {
        var resolver = ResolverOf(Guid.Empty);

        var plan = WatchlistImporter.Read(
            [EntryFor(Guid.Empty, new Dictionary<string, string>())],
            IndexOf(),
            resolver);

        Assert.Equal(WatchlistImportMatch.Unmatched, Assert.Single(plan.Entries).Match);
        Assert.Empty(resolver.Asked);
    }

    /// <summary>
    /// The three arguments are what the rule is made of and none of them has a
    /// sensible absence.
    /// </summary>
    [Fact]
    public void TheReadRefusesAMissingArgument()
    {
        var entries = new[] { EntryFor(TheStranger, new Dictionary<string, string>()) };

        Assert.Throws<ArgumentNullException>(() => WatchlistImporter.Read(null!, IndexOf(), ResolverOf()));
        Assert.Throws<ArgumentNullException>(() => WatchlistImporter.Read(entries, null!, ResolverOf()));
        Assert.Throws<ArgumentNullException>(() => WatchlistImporter.Read(entries, IndexOf(), null!));
    }

    private static ExportedEntry EntryFor(Guid itemId, IReadOnlyDictionary<string, string> providerIds) => new()
    {
        ItemId = itemId,
        Kind = WatchlistItemKind.Movie,
        AddedAt = Added,
        ProviderIds = providerIds,
    };

    private static ProviderIndexTable IndexOf(params (string Provider, string Id, Guid ItemId)[] known) =>
        new(known.ToDictionary(pair => pair.Provider + " " + pair.Id, pair => pair.ItemId, StringComparer.Ordinal));

    private static ResolverFor ResolverOf(params Guid[] present) => new(present);

    /// <summary>
    /// Answers from a table, which is the whole of what the running server's library
    /// does for this rule.
    /// </summary>
    private sealed class ProviderIndexTable(IReadOnlyDictionary<string, Guid> known) : IProviderIdIndex
    {
        public Guid? ItemFor(string provider, string id) =>
            known.TryGetValue(provider + " " + id, out var itemId) ? itemId : null;
    }

    /// <summary>
    /// Answers from a set and records what it was asked, so a test can assert that a
    /// question was never put rather than only that its answer was ignored.
    /// </summary>
    private sealed class ResolverFor(IReadOnlyCollection<Guid> present) : IWatchlistItemResolver
    {
        private readonly List<Guid> _asked = [];

        public IReadOnlyList<Guid> Asked => _asked;

        public bool Exists(Guid itemId)
        {
            _asked.Add(itemId);
            return present.Contains(itemId);
        }
    }
}
