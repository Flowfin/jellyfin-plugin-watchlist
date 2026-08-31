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
/// What one reconciliation pass writes, and above all what it does not.
/// </summary>
/// <remarks>
/// Every test here drives the seam from #82 with a fake rather than a server type, so
/// nothing in this file knows which server line it would be running on - it is told,
/// through the one answer the two lines differ in. Each test that touches the store
/// owns a directory of its own and deletes it afterwards; nothing reads a shared
/// temporary path or the machine's clock.
/// </remarks>
public sealed class ReconciliationTests : IDisposable
{
    private static readonly Guid TheList = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");

    private static readonly Guid AUser = Guid.Parse("55555555-5555-5555-5555-555555555555");

    private static readonly Guid AnAdministrator = Guid.Parse("77777777-7777-7777-7777-777777777777");

    private static readonly Guid First = Guid.Parse("11111111-0000-0000-0000-000000000001");

    private static readonly Guid Second = Guid.Parse("11111111-0000-0000-0000-000000000002");

    private static readonly Guid Third = Guid.Parse("11111111-0000-0000-0000-000000000003");

    private static readonly Guid Fourth = Guid.Parse("11111111-0000-0000-0000-000000000004");

    private static readonly Guid Fifth = Guid.Parse("11111111-0000-0000-0000-000000000005");

    private static readonly Guid SomethingElse = Guid.Parse("99999999-0000-0000-0000-000000000009");

    private readonly TemporaryDirectory _sandbox = new("watchlist-reconciliation");

    private string DataFolder => Path.Join(_sandbox.FullPath, "plugin-data");

    /// <inheritdoc />
    public void Dispose()
    {
        _sandbox.Dispose();
    }

    /// <summary>
    /// The property the whole class is arranged around. A playlist that already holds
    /// the wanted items in the wanted order is not written to at all, on either line.
    /// </summary>
    /// <param name="canInsert">Which line the gateway stands for.</param>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ACorrectPlaylistIsNotWrittenTo(bool canInsert)
    {
        var server = AServerHolding(canInsert, First, Second, Third);

        var result = await Reconcile(server, new AListProjectedInto(AUser, First, Second, Third));

        Assert.Equal(0, server.Writes);
        Assert.Equal(0, result.Added);
        Assert.Equal(0, result.Removed);
        Assert.False(result.Rebuilt);
        Assert.Equal([First, Second, Third], server.ItemsOn(TheList));
    }

    /// <summary>
    /// The pass is bounded: it reads the rows of ONE playlist and asks nothing else.
    /// Nothing here walks a library or a user's other lists.
    /// </summary>
    [Fact]
    public async Task ThePassReadsOnePlaylistAndNothingElse()
    {
        var server = AServerHolding(false, First, SomethingElse);

        await Reconcile(server, new AListProjectedInto(AUser, First, Second));

        Assert.Single(server.Calls, call => call.StartsWith("entries ", StringComparison.Ordinal));
        Assert.DoesNotContain(server.Calls, call => call.StartsWith("list ", StringComparison.Ordinal));
    }

    /// <summary>
    /// Only the difference is issued: the row the list no longer holds goes, the entry
    /// the playlist does not have arrives, and the two rows that were already right are
    /// not touched.
    /// </summary>
    [Fact]
    public async Task OnlyTheAddsAndRemovesTheDifferenceNamesAreIssued()
    {
        var server = AServerHolding(false, First, Second, SomethingElse);

        var result = await Reconcile(server, new AListProjectedInto(AUser, First, Second, Third));

        Assert.Equal(1, result.Added);
        Assert.Equal(1, result.Removed);
        Assert.False(result.Rebuilt);
        Assert.Equal(2, server.Writes);
        Assert.Contains("remove " + TheList + " 1", server.Calls);
        Assert.Contains("add " + TheList + " 1 end", server.Calls);
        Assert.Equal([First, Second, Third], server.ItemsOn(TheList));
    }

    /// <summary>
    /// A pass with nothing to remove issues no removal, and a pass with nothing to add
    /// issues no add. Two halves of the same rule, because a reconciler that always
    /// called both would satisfy the counts above and still write on every pass.
    /// </summary>
    [Fact]
    public async Task NeitherHalfIsIssuedWhenItIsEmpty()
    {
        var addsOnly = AServerHolding(false, First, Second);
        await Reconcile(addsOnly, new AListProjectedInto(AUser, First, Second, Third));

        Assert.DoesNotContain(addsOnly.Calls, call => call.StartsWith("remove ", StringComparison.Ordinal));

        var removesOnly = AServerHolding(false, First, SomethingElse);
        await Reconcile(removesOnly, new AListProjectedInto(AUser, First));

        Assert.DoesNotContain(removesOnly.Calls, call => call.StartsWith("add ", StringComparison.Ordinal));
    }

    /// <summary>
    /// A playlist holding one item twice loses the copy rather than both rows, and the
    /// item stays on the list. A playlist row and a list entry are not the same thing,
    /// which is why the removal names the row identifier and not the item.
    /// </summary>
    [Fact]
    public async Task ASecondRowPointingAtAHeldItemIsTheOneThatGoes()
    {
        var server = new APlaylistServerOf();
        server.Rows(TheList, First, First, Second);

        var result = await Reconcile(server, new AListProjectedInto(AUser, First, Second));

        Assert.Equal(1, result.Removed);
        Assert.Equal(0, result.Added);
        Assert.Equal([First, Second], server.ItemsOn(TheList));
    }

    /// <summary>
    /// A correctly ordered playlist is never rebuilt, on either line. This is the
    /// negative half of the ordering rule and the one worth spending a test on: a
    /// reconciler that rebuilt whenever the order MIGHT be wrong would pass every
    /// assertion about the resulting order and rewrite every playlist every pass.
    /// </summary>
    /// <param name="canInsert">Which line the gateway stands for.</param>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ACorrectlyOrderedPlaylistCausesNoRebuild(bool canInsert)
    {
        var server = AServerHolding(canInsert, First, Second);

        var result = await Reconcile(server, new AListProjectedInto(AUser, First, Second, Third));

        Assert.False(result.Rebuilt);
        Assert.DoesNotContain(server.Calls, call => call.StartsWith("remove ", StringComparison.Ordinal));
        Assert.Equal([First, Second, Third], server.ItemsOn(TheList));
    }

    /// <summary>
    /// An order that no add can reach is reached by building the list again, and the
    /// rebuild says so in the log. Rows that are in the wrong order relative to each
    /// other cannot be moved through this gateway on either line, so this is the one
    /// case where every row is written back.
    /// </summary>
    /// <param name="canInsert">Which line the gateway stands for.</param>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task RowsInTheWrongOrderAreRebuiltAndTheRebuildIsLogged(bool canInsert)
    {
        var server = AServerHolding(canInsert, Second, First);
        var logger = new RecordingReconcilerLogger();

        var result = await new WatchlistReconciler(server, logger)
            .ReconcileAsync(new AListProjectedInto(AUser, First, Second), TheList, CancellationToken.None);

        Assert.True(result.Rebuilt);
        Assert.Equal(2, result.Removed);
        Assert.Equal(2, result.Added);
        Assert.Equal([First, Second], server.ItemsOn(TheList));
        Assert.Contains(logger.Lines, line => line.Contains("Rebuilding playlist", StringComparison.Ordinal));
    }

    /// <summary>
    /// THE SAME STORE STATE UNDER BOTH ADAPTER BEHAVIOURS, and the order that comes out
    /// is the same one. What differs is the cost: the line that honours a position puts
    /// the missing item where it belongs in one write, and the line that appends can
    /// only get there by building the list again.
    /// </summary>
    [Fact]
    public async Task BothAdapterBehavioursReachTheSameOrderAndOnlyOneOfThemPaysForIt()
    {
        var inserting = AServerHolding(true, First, Third);
        var insertingResult = await Reconcile(inserting, new AListProjectedInto(AUser, First, Second, Third));

        var appending = AServerHolding(false, First, Third);
        var appendingResult = await Reconcile(appending, new AListProjectedInto(AUser, First, Second, Third));

        Assert.Equal([First, Second, Third], inserting.ItemsOn(TheList));
        Assert.Equal([First, Second, Third], appending.ItemsOn(TheList));

        Assert.False(insertingResult.Rebuilt);
        Assert.Equal(1, inserting.Writes);
        Assert.Contains("add " + TheList + " 1 1", inserting.Calls);

        Assert.True(appendingResult.Rebuilt);
        Assert.Equal(2, appending.Writes);
    }

    /// <summary>
    /// Items that belong next to each other go in together, and a call is spent only
    /// where the run breaks. Four missing items in two runs are two writes rather than
    /// four, and the list still comes out in the wanted order.
    /// </summary>
    [Fact]
    public async Task ItemsThatBelongNextToEachOtherAreInsertedInOneCall()
    {
        var server = AServerHolding(true, Third);

        var result = await Reconcile(
            server,
            new AListProjectedInto(AUser, First, Second, Third, Fourth, Fifth));

        Assert.False(result.Rebuilt);
        Assert.Equal(4, result.Added);
        Assert.Equal(2, server.Writes);
        Assert.Contains("add " + TheList + " 2 0", server.Calls);
        Assert.Contains("add " + TheList + " 2 3", server.Calls);
        Assert.Equal([First, Second, Third, Fourth, Fifth], server.ItemsOn(TheList));
    }

    /// <summary>
    /// An item this user cannot see is never added to their playlist. The describer
    /// gives one answer for an item that is gone and an item they may not see, so the
    /// projection asks it and adds nothing it says nothing about.
    /// </summary>
    [Fact]
    public async Task AnItemTheUserCannotSeeIsNeverAdded()
    {
        var store = new WatchlistDocumentStore(DataFolder, new RecordingLogger());
        var clock = AStoppedClock();
        Put(store, clock, First, WatchlistItemKind.Movie);
        Put(store, clock, SomethingElse, WatchlistItemKind.Movie);

        // The describer answers for one of the two, which is what an item behind a
        // rating this user is under, or in a library they were never given, looks like.
        var describer = new ADescriberOf((First, AUser, WatchlistItemKind.Movie));
        var target = UserProjectionTarget.For(store, new PluginConfiguration(), describer, clock, AUser);
        var server = new APlaylistServerOf();

        var result = await Reconcile(server, target);

        Assert.Equal(1, result.Added);
        Assert.Equal([First], server.ItemsOn(TheList));
    }

    /// <summary>
    /// The order is the newest addition first, and it is the same order on the playlist
    /// as in the wanted set.
    /// </summary>
    [Fact]
    public async Task TheNewestAdditionIsAtTheHeadOfTheList()
    {
        var store = new WatchlistDocumentStore(DataFolder, new RecordingLogger());
        var clock = AStoppedClock();

        Put(store, clock, First, WatchlistItemKind.Movie);
        clock.Advance(TimeSpan.FromMinutes(1));
        Put(store, clock, Second, WatchlistItemKind.Episode);
        clock.Advance(TimeSpan.FromMinutes(1));
        Put(store, clock, Third, WatchlistItemKind.Movie);

        var target = UserProjectionTarget.For(store, new PluginConfiguration(), Sees(First, Second, Third), clock, AUser);

        Assert.Equal([Third, Second, First], target.Wanted);

        var server = new APlaylistServerOf();
        await Reconcile(server, target);

        Assert.Equal([Third, Second, First], server.ItemsOn(TheList));
    }

    /// <summary>
    /// A show is on the list and is in no playlist, which is the gap #18 closes. A
    /// server handed a folder adds its non-folder children, so projecting a show as
    /// itself would put every episode of it into the list; until the rule that makes a
    /// show one episode exists, the entry is kept and not projected.
    /// </summary>
    [Fact]
    public async Task AShowIsKeptOnTheListAndProjectedIntoNothing()
    {
        var store = new WatchlistDocumentStore(DataFolder, new RecordingLogger());
        var clock = AStoppedClock();

        Put(store, clock, First, WatchlistItemKind.Series);
        Put(store, clock, Second, WatchlistItemKind.Movie);

        var target = UserProjectionTarget.For(store, new PluginConfiguration(), Sees(First, Second), clock, AUser);

        Assert.Equal([Second], target.Wanted);
        Assert.Equal(2, store.Read(AUser).Document!.Entries.Count);

        var server = new APlaylistServerOf();
        await Reconcile(server, target);

        Assert.Equal([Second], server.ItemsOn(TheList));
    }

    /// <summary>
    /// ONE DIFFERENCE CALCULATION OVER TWO TARGETS. A user's own list and a list whose
    /// owner is not the holder of its record go through the same pass from the same
    /// wanted set, and the calls and the resulting order are the same on both.
    /// </summary>
    /// <remarks>
    /// What this does NOT drive is the shared target from #84, which is not in this
    /// tree. It drives a target of that SHAPE, which is what says the reconciler has no
    /// private-list branch in it; that the shared list's own target behaves this way is
    /// #84's to prove when it lands.
    /// </remarks>
    [Fact]
    public async Task OnePassOverAUsersOwnListAndOneOverAListOwnedElsewhereAgree()
    {
        var store = new WatchlistDocumentStore(DataFolder, new RecordingLogger());
        var clock = AStoppedClock();

        Put(store, clock, First, WatchlistItemKind.Movie);
        clock.Advance(TimeSpan.FromMinutes(1));
        Put(store, clock, Second, WatchlistItemKind.Movie);

        var own = UserProjectionTarget.For(store, new PluginConfiguration(), Sees(First, Second), clock, AUser);
        Assert.Equal([Second, First], own.Wanted);

        var mine = AServerHolding(false, SomethingElse);
        var ownResult = await Reconcile(mine, own);

        var elsewhere = AServerHolding(false, SomethingElse);
        var elsewhereResult = await Reconcile(
            elsewhere,
            new AListProjectedInto(AnAdministrator, [.. own.Wanted]));

        Assert.Equal(ownResult, elsewhereResult);
        Assert.Equal(mine.ItemsOn(TheList), elsewhere.ItemsOn(TheList));
        Assert.Equal([Second, First], elsewhere.ItemsOn(TheList));
    }

    /// <summary>
    /// A pass with no target is a mistake in the caller rather than an empty list.
    /// </summary>
    [Fact]
    public async Task APassWithNoTargetRefuses()
    {
        var reconciler = new WatchlistReconciler(new APlaylistServerOf(), new RecordingReconcilerLogger());

        await Assert.ThrowsAsync<ArgumentNullException>(
            () => reconciler.ReconcileAsync(null!, TheList, CancellationToken.None));
    }

    private static Task<ReconciliationResult> Reconcile(APlaylistServerOf server, IProjectionTarget target) =>
        new WatchlistReconciler(server, new RecordingReconcilerLogger())
            .ReconcileAsync(target, TheList, CancellationToken.None);

    private static APlaylistServerOf AServerHolding(bool canInsert, params Guid[] itemIds)
    {
        var server = new APlaylistServerOf { CanInsertAtAPosition = canInsert };
        server.Rows(TheList, itemIds);

        return server;
    }

    private static ADescriberOf Sees(params Guid[] itemIds) => new(
        [.. itemIds.Select(itemId => (itemId, AUser, WatchlistItemKind.Movie))]);

    private static StoppedClock AStoppedClock() =>
        new(new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero));

    private static void Put(WatchlistDocumentStore store, TimeProvider clock, Guid itemId, WatchlistItemKind kind)
    {
        var result = store.Add(
            AUser,
            new WatchlistEntry
            {
                ItemId = itemId,
                Kind = kind,
                AddedAt = clock.GetUtcNow(),
                Source = WatchlistEntrySource.Api,
            },
            PluginConfiguration.DefaultMaxEntriesPerUser);

        Assert.Equal(WatchlistAddOutcome.Added, result.Outcome);
    }
}
