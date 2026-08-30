using System;
using System.IO;
using Jellyfin.Plugin.Watchlist.Store;
using Xunit;

namespace Jellyfin.Plugin.Watchlist.Tests;

/// <summary>
/// Adding the same item twice, at the level that decides it.
/// </summary>
/// <remarks>
/// The check is inside the store rather than in the endpoint above it, and that is
/// the whole point of these tests. A caller that reads the list and then adds is two
/// changes with a gap between them, and two clients each retrying one timeout would
/// both read a list without the item and both write it. The store holds one gate per
/// user and does the comparison inside it, so the second write never happens.
/// </remarks>
public sealed class WatchlistStoreRepeatTests : IDisposable
{
    private static readonly Guid AUser = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private static readonly DateTimeOffset WhenItWasAdded =
        new(2026, 1, 2, 3, 4, 5, TimeSpan.Zero);

    private readonly TemporaryDirectory _sandbox = new("watchlist-store-repeat");

    private string DataFolder => Path.Join(_sandbox.FullPath, "plugin-data");

    /// <inheritdoc />
    public void Dispose()
    {
        _sandbox.Dispose();
    }

    /// <summary>
    /// One entry, and an answer that says the item is on the list without claiming
    /// this call put it there.
    /// </summary>
    [Fact]
    public void AddingTheSameItemTwiceLeavesOneEntry()
    {
        var store = new WatchlistDocumentStore(DataFolder);

        var first = store.Add(AUser, Entry(1), maxEntriesPerUser: 10);
        var second = store.Add(AUser, Entry(1), maxEntriesPerUser: 10);

        Assert.Equal(WatchlistAddOutcome.Added, first.Outcome);
        Assert.Equal(WatchlistAddOutcome.AlreadyOnTheList, second.Outcome);

        Assert.True(first.WasAdded);
        Assert.False(second.WasAdded);
        Assert.True(first.IsOnTheList);
        Assert.True(second.IsOnTheList);

        Assert.Single(store.Read(AUser).Document!.Entries);
    }

    /// <summary>
    /// The near miss, and the reason the comparison is on the item and not on the
    /// whole entry. A second add of the same item carries a later instant, and an
    /// entry compared whole would be a different entry every time.
    /// </summary>
    [Fact]
    public void ARepeatWithADifferentStampIsStillTheSameItem()
    {
        var store = new WatchlistDocumentStore(DataFolder);

        store.Add(AUser, Entry(1), maxEntriesPerUser: 10);

        var later = Entry(1) with { AddedAt = WhenItWasAdded.AddDays(30) };
        var second = store.Add(AUser, later, maxEntriesPerUser: 10);

        Assert.Equal(WatchlistAddOutcome.AlreadyOnTheList, second.Outcome);
        Assert.Equal(WhenItWasAdded, Assert.Single(store.Read(AUser).Document!.Entries).AddedAt);
    }

    /// <summary>
    /// And the other direction, so the comparison is not one that refuses every second
    /// add: a different item goes on beside the first.
    /// </summary>
    [Fact]
    public void AddingADifferentItemStillGoesOnTheList()
    {
        var store = new WatchlistDocumentStore(DataFolder);

        store.Add(AUser, Entry(1), maxEntriesPerUser: 10);
        var second = store.Add(AUser, Entry(2), maxEntriesPerUser: 10);

        Assert.Equal(WatchlistAddOutcome.Added, second.Outcome);
        Assert.Equal(2, store.Read(AUser).Document!.Entries.Count);
    }

    /// <summary>
    /// An item already on a list that is at its cap. The list holds what the caller
    /// asked it to hold, so telling them to make room would be telling them to remove
    /// something to fit something that is already there.
    /// </summary>
    [Fact]
    public void AnItemAlreadyOnAFullListIsNotRefusedForTheCap()
    {
        var store = new WatchlistDocumentStore(DataFolder);

        store.Add(AUser, Entry(1), maxEntriesPerUser: 2);
        store.Add(AUser, Entry(2), maxEntriesPerUser: 2);

        var again = store.Add(AUser, Entry(1), maxEntriesPerUser: 2);

        Assert.Equal(WatchlistAddOutcome.AlreadyOnTheList, again.Outcome);
        Assert.Equal(2, again.EntryCount);
        Assert.Equal(2, again.Cap);
    }

    /// <summary>
    /// The sentence an operator reads. It says nothing was written, because the
    /// question somebody asks of a log line here is whether the list changed.
    /// </summary>
    [Fact]
    public void TheRepeatDescribesItself()
    {
        var store = new WatchlistDocumentStore(DataFolder);

        store.Add(AUser, Entry(1), maxEntriesPerUser: 10);

        Assert.Equal(
            "Already on the list. Nothing was written. The list holds 1 of at most 10 entries.",
            store.Add(AUser, Entry(1), maxEntriesPerUser: 10).Describe());
    }

    private static Guid Item(int n) => Guid.Parse($"00000000-0000-0000-0000-{n:D12}");

    private static WatchlistEntry Entry(int n) => new()
    {
        ItemId = Item(n),
        Kind = WatchlistItemKind.Movie,
        AddedAt = WhenItWasAdded,
        Source = WatchlistEntrySource.Api,
    };
}
