using System;
using System.Collections.Generic;
using System.Globalization;
using Jellyfin.Plugin.Watchlist.Api;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Audio;
using MediaBrowser.Controller.Entities.Movies;
using Xunit;

namespace Jellyfin.Plugin.Watchlist.Tests;

/// <summary>
/// The one class in this plugin that knows a library can be searched, driven over a
/// library this test built.
/// </summary>
/// <remarks>
/// It is here rather than on the coverage floor's exclusion list because it holds no
/// static and reaches nothing a test cannot build. What it costs is
/// <see cref="ALibraryOf"/>, which is wide because the interface is, and that is the
/// cost of typing rather than something the suite could not do.
/// </remarks>
public class LibraryProviderIdsTests
{
    /// <summary>
    /// The export half. What an item here is called elsewhere is what the library
    /// holds against it, handed on without being reinterpreted.
    /// </summary>
    [Fact]
    public void WhatAnItemIsCalledElsewhereIsWhatTheLibraryHolds()
    {
        var providers = new LibraryProviderIds(new ALibraryOf(AFilm(1, ("Imdb", "tt0000001"))));

        Assert.Equal(
            new KeyValuePair<string, string>("Imdb", "tt0000001"),
            Assert.Single(providers.ProviderIdsFor(Item(1))));
    }

    /// <summary>
    /// An item this server can no longer read answers with nothing rather than
    /// throwing. The entry still leaves in an export carrying no way to resolve it,
    /// which is what the export side asks for and is better than dropping it.
    /// </summary>
    [Fact]
    public void AnItemTheLibraryNoLongerHoldsAnswersWithNothing()
    {
        var providers = new LibraryProviderIds(new ALibraryOf());

        Assert.Empty(providers.ProviderIdsFor(Item(1)));
    }

    /// <summary>
    /// The import half, and the reason this class exists: an identifier written by
    /// another server, answered with the item this one holds under it.
    /// </summary>
    [Fact]
    public void TheItemHeldUnderAProviderIdentifierIsFound()
    {
        var providers = new LibraryProviderIds(new ALibraryOf(AFilm(1, ("Imdb", "tt0000001"))));

        Assert.Equal(Item(1), providers.ItemFor("Imdb", "tt0000001"));
    }

    /// <summary>
    /// An identifier nothing here holds answers null rather than an arbitrary item.
    /// </summary>
    [Fact]
    public void AnIdentifierNothingHereHoldsAnswersNothing()
    {
        var providers = new LibraryProviderIds(new ALibraryOf(AFilm(1, ("Imdb", "tt0000001"))));

        Assert.Null(providers.ItemFor("Imdb", "tt0000002"));
    }

    /// <summary>
    /// The empty provider name and the empty identifier are refused before the library
    /// is asked. They are what a hand-written file carries where it means nothing, and
    /// a query built from one asks a question nobody meant.
    /// </summary>
    /// <param name="provider">The provider name to ask under.</param>
    /// <param name="id">The identifier to ask for.</param>
    [Theory]
    [InlineData("", "tt0000001")]
    [InlineData("Imdb", "")]
    public void AnEmptyHalfOfThePairIsRefusedWithoutAskingTheLibrary(string provider, string id)
    {
        var providers = new LibraryProviderIds(new ALibraryOf(AFilm(1, ("Imdb", "tt0000001"))));

        Assert.Null(providers.ItemFor(provider, id));
    }

    /// <summary>
    /// The search is bounded to the three kinds a watchlist takes, so an identifier a
    /// music library also carries cannot answer here. Without the bound, a provider
    /// identifier shared between a film and its soundtrack would put the soundtrack on
    /// a list.
    /// </summary>
    [Fact]
    public void AKindAWatchlistDoesNotTakeIsNotOffered()
    {
        var track = new Audio { Id = Item(1) };

        track.ProviderIds["Imdb"] = "tt0000001";

        var providers = new LibraryProviderIds(new ALibraryOf(track));

        Assert.Null(providers.ItemFor("Imdb", "tt0000001"));
    }

    /// <summary>
    /// A library holding the same title twice answers the same way on two reads. The
    /// near miss is taking whichever the query returned first, which makes the answer a
    /// fact about the query plan rather than about the library.
    /// </summary>
    [Fact]
    public void ALibraryHoldingTheSameTitleTwiceAnswersTheSameWayTwice()
    {
        var onOneOrder = new LibraryProviderIds(new ALibraryOf(
            AFilm(2, ("Imdb", "tt0000001")),
            AFilm(1, ("Imdb", "tt0000001"))));

        var onTheOther = new LibraryProviderIds(new ALibraryOf(
            AFilm(1, ("Imdb", "tt0000001")),
            AFilm(2, ("Imdb", "tt0000001"))));

        Assert.Equal(onTheOther.ItemFor("Imdb", "tt0000001"), onOneOrder.ItemFor("Imdb", "tt0000001"));
    }

    private static Guid Item(int n) => Guid.Parse(
        string.Format(CultureInfo.InvariantCulture, "00000000-0000-0000-0000-{0:D12}", n));

    private static BaseItem AFilm(int n, params (string Provider, string Id)[] providerIds)
    {
        var film = new Movie { Id = Item(n) };

        foreach (var pair in providerIds)
        {
            film.ProviderIds[pair.Provider] = pair.Id;
        }

        return film;
    }
}
