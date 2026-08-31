using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json.Nodes;
using Jellyfin.Plugin.Watchlist.Store;
using Xunit;

namespace Jellyfin.Plugin.Watchlist.Tests;

/// <summary>
/// The refusals the store and its neighbours make when they are handed nothing.
/// </summary>
/// <remarks>
/// <para>
/// Every one of these lines was executed by the suite already and none of them was
/// asserted, which is a distinction a coverage number cannot draw and a mutation run
/// can: deleting the guard left the suite green. That is what these tests are for, and
/// they are ordinary tests rather than an instrument's bookkeeping - a guard nothing
/// asserts is a guard somebody can delete while tidying.
/// </para>
/// <para>
/// Why the guards are worth keeping at all rather than deleted for the same reason:
/// each of these takes a document, a set of steps or a path that everything after it
/// dereferences, so without the guard the failure arrives further in, wearing the name
/// of whatever touched the value first. A store built on a blank path would create a
/// directory wherever the process happens to be standing.
/// </para>
/// </remarks>
public sealed class AbsentArgumentTests : IDisposable
{
    private static readonly Guid AUser = Guid.Parse("55555555-5555-5555-5555-555555555555");

    private readonly TemporaryDirectory _sandbox = new("watchlist-absent-argument");

    private string DataFolder => Path.Join(_sandbox.FullPath, "plugin-data");

    /// <inheritdoc />
    public void Dispose()
    {
        _sandbox.Dispose();
    }

    /// <summary>
    /// A store with no folder to write into refuses at construction rather than when
    /// something first tries to write.
    /// </summary>
    /// <param name="dataFolderPath">What the caller handed in.</param>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void AStoreWithNoFolderRefuses(string? dataFolderPath)
    {
        var refusal = Assert.ThrowsAny<ArgumentException>(() => new WatchlistDocumentStore(dataFolderPath!));

        // The parameter and not only the type. Without the guard the path still fails,
        // one line further in and under the name the framework's own argument carries,
        // so a test that asked only for an ArgumentException would pass over a store
        // that had no guard at all.
        Assert.Equal("dataFolderPath", refusal.ParamName);
    }

    /// <summary>
    /// The three writes and the staging behind them refuse an absent document.
    /// </summary>
    [Fact]
    public void EveryWriteRefusesAnAbsentDocument()
    {
        var store = new WatchlistDocumentStore(DataFolder);

        Assert.Throws<ArgumentNullException>(() => store.Write(null!));
        Assert.Throws<ArgumentNullException>(() => store.WriteShared(null!));
        Assert.Throws<ArgumentNullException>(() => store.Stage((WatchlistDocument)null!));
        Assert.Throws<ArgumentNullException>(() => store.Stage((SharedWatchlistDocument)null!));
    }

    /// <summary>
    /// An add with no entry refuses rather than writing a list with a hole in it.
    /// </summary>
    [Fact]
    public void AnAddWithNoEntryRefuses()
    {
        var store = new WatchlistDocumentStore(DataFolder);

        Assert.Throws<ArgumentNullException>(() => store.Add(AUser, null!, 10));
        Assert.Throws<ArgumentNullException>(() => store.AddShared(null!, 10));
    }

    /// <summary>
    /// The upgrade chain refuses an absent document and an absent set of steps, on both
    /// of the two entry points it offers.
    /// </summary>
    [Fact]
    public void TheUpgradeChainRefusesWhatItCannotWalk()
    {
        var steps = new Dictionary<int, Func<JsonObject, JsonObject>>();

        Assert.Throws<ArgumentNullException>(() => WatchlistDocumentUpgrades.Covers(0, 1, null!));
        Assert.Throws<ArgumentNullException>(() => WatchlistDocumentUpgrades.Apply(null!, 0, 1, steps));
        Assert.Throws<ArgumentNullException>(() => WatchlistDocumentUpgrades.Apply(new JsonObject(), 0, 1, null!));
    }

    /// <summary>
    /// A read result that says a list is available refuses to say so about nothing.
    /// </summary>
    /// <remarks>
    /// This is the one whose absence would be quietest. `IsAvailable` is the document
    /// not being null, so a result built from null is an unavailable list wearing the
    /// name of an available one, and every caller that branches on it takes the wrong
    /// arm without an exception anywhere.
    /// </remarks>
    [Fact]
    public void AnAvailableResultRefusesAnAbsentDocument()
    {
        Assert.Throws<ArgumentNullException>(() => WatchlistReadResult.Available(null!));
    }

    /// <summary>
    /// The gate every read path goes through refuses an absent set of entries and an
    /// absent resolver.
    /// </summary>
    [Fact]
    public void TheVisibilityGateRefusesWhatItCannotJudge()
    {
        // Named parameters rather than bare types, for the reason the store's folder
        // carries above: without the guard the entries still fail, inside the query
        // this runs and under the name that query gives its own source.
        var entries = Assert.Throws<ArgumentNullException>(
            () => WatchlistVisibility.Resolvable(null!, new EverythingResolves(), AUser));

        Assert.Equal("entries", entries.ParamName);

        var resolver = Assert.Throws<ArgumentNullException>(
            () => WatchlistVisibility.Resolvable([], null!, AUser));

        Assert.Equal("resolver", resolver.ParamName);
    }

    private sealed class EverythingResolves : IWatchlistItemResolver
    {
        public bool Exists(Guid itemId) => true;
    }
}
