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
/// What happens to a user's projected playlist when a library is taken away from them
/// while their list still names something in it.
/// </summary>
/// <remarks>
/// <para>
/// This is the half of #23 that is not about a user arriving or leaving. A user stays,
/// their list stays, and what moves is what the server will tell them about an item.
/// The answer is that the next reconciliation takes the row off their playlist and
/// leaves the entry on their list, and both halves matter: a row they cannot open is a
/// list that lies to them, and an entry deleted because a library was unmounted for an
/// afternoon is a list that lost something nobody meant to throw away.
/// </para>
/// <para>
/// Nothing here fakes a user manager or a library. Withdrawn access is exactly what the
/// describer answering nothing about an item FOR THAT USER looks like, which is the
/// interface's own rule: an item that is gone and an item this user may not see produce
/// one answer, so a caller cannot tell them apart. Driving it through the describer is
/// therefore driving the real condition and not a stand-in for it.
/// </para>
/// <para>
/// Each test owns a directory of its own and deletes it afterwards; nothing reads a
/// shared temporary path or the machine's clock.
/// </para>
/// </remarks>
public sealed class WithdrawnAccessTests : IDisposable
{
    private static readonly Guid TheList = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000023");

    private static readonly Guid AUser = Guid.Parse("55555555-5555-5555-5555-555555555555");

    private static readonly Guid InALibraryTheyKeep = Guid.Parse("22222222-0000-0000-0000-000000000001");

    private static readonly Guid InTheLibraryWithdrawn = Guid.Parse("22222222-0000-0000-0000-000000000002");

    private readonly TemporaryDirectory _sandbox = new("watchlist-withdrawn-access");

    private string DataFolder => Path.Join(_sandbox.FullPath, "plugin-data");

    /// <inheritdoc />
    public void Dispose()
    {
        _sandbox.Dispose();
    }

    /// <summary>
    /// The whole sequence in one test: both items projected, access to one library
    /// withdrawn, and the next pass taking that row off the playlist and no other.
    /// </summary>
    [Fact]
    public async Task AWithdrawnLibraryLeavesThePlaylistAtTheNextReconciliation()
    {
        var store = new WatchlistDocumentStore(DataFolder, new RecordingLogger());
        var clock = AStoppedClock();

        Add(store, clock, InTheLibraryWithdrawn);
        clock.Advance(TimeSpan.FromMinutes(1));
        Add(store, clock, InALibraryTheyKeep);

        var server = new APlaylistServerOf();

        var before = await Reconcile(server, TargetSeeing(store, clock, InALibraryTheyKeep, InTheLibraryWithdrawn));

        Assert.Equal(2, before.Added);
        Assert.Equal([InALibraryTheyKeep, InTheLibraryWithdrawn], server.ItemsOn(TheList));

        // The library goes. The describer stops answering for what was in it, which is
        // the one answer the server gives for an item this user may no longer see.
        var after = await Reconcile(server, TargetSeeing(store, clock, InALibraryTheyKeep));

        Assert.Equal(1, after.Removed);
        Assert.Equal(0, after.Added);
        Assert.False(after.Rebuilt);
        Assert.Equal([InALibraryTheyKeep], server.ItemsOn(TheList));
    }

    /// <summary>
    /// And the entry stays on the list. A library that comes back brings the row back at
    /// the pass after it, and nothing in between deleted anything.
    /// </summary>
    [Fact]
    public async Task TheEntryItselfSurvivesTheWithdrawalAndComesBackWithTheLibrary()
    {
        var store = new WatchlistDocumentStore(DataFolder, new RecordingLogger());
        var clock = AStoppedClock();

        Add(store, clock, InTheLibraryWithdrawn);
        clock.Advance(TimeSpan.FromMinutes(1));
        Add(store, clock, InALibraryTheyKeep);

        var server = new APlaylistServerOf();

        await Reconcile(server, TargetSeeing(store, clock, InALibraryTheyKeep, InTheLibraryWithdrawn));
        await Reconcile(server, TargetSeeing(store, clock, InALibraryTheyKeep));

        var kept = store.Read(AUser).Document!.Entries;
        Assert.Equal(
            [InTheLibraryWithdrawn, InALibraryTheyKeep],
            kept.Select(entry => entry.ItemId).ToList());

        var back = await Reconcile(server, TargetSeeing(store, clock, InALibraryTheyKeep, InTheLibraryWithdrawn));

        Assert.Equal(1, back.Added);
        Assert.Equal([InALibraryTheyKeep, InTheLibraryWithdrawn], server.ItemsOn(TheList));
    }

    /// <summary>
    /// A pass over a user who lost nothing writes nothing. Without this the two tests
    /// above would be satisfied by a projection that rewrites the playlist every time
    /// and happens to leave the right rows behind.
    /// </summary>
    [Fact]
    public async Task AUserWhoLostNothingIsNotWrittenTo()
    {
        var store = new WatchlistDocumentStore(DataFolder, new RecordingLogger());
        var clock = AStoppedClock();

        Add(store, clock, InTheLibraryWithdrawn);
        clock.Advance(TimeSpan.FromMinutes(1));
        Add(store, clock, InALibraryTheyKeep);

        var server = new APlaylistServerOf();

        await Reconcile(server, TargetSeeing(store, clock, InALibraryTheyKeep, InTheLibraryWithdrawn));
        var writesAfterTheFirstPass = server.Writes;

        await Reconcile(server, TargetSeeing(store, clock, InALibraryTheyKeep, InTheLibraryWithdrawn));

        Assert.Equal(writesAfterTheFirstPass, server.Writes);
    }

    private static Task<ReconciliationResult> Reconcile(APlaylistServerOf server, IProjectionTarget target) =>
        new WatchlistReconciler(server, new RecordingReconcilerLogger())
            .ReconcileAsync(target, TheList, CancellationToken.None);

    /// <summary>
    /// A target made for one pass, over a library set this user can see right now.
    /// </summary>
    /// <param name="store">The store.</param>
    /// <param name="clock">The clock.</param>
    /// <param name="visible">The items the server would tell this user about.</param>
    /// <returns>The target.</returns>
    /// <remarks>
    /// A target of its own per pass rather than one reused, because a target is a
    /// snapshot taken when it is made. Reusing one would be asking the second pass what
    /// the first pass was told.
    /// </remarks>
    private static UserProjectionTarget TargetSeeing(
        WatchlistDocumentStore store,
        TimeProvider clock,
        params Guid[] visible) => UserProjectionTarget.For(
            store,
            new PluginConfiguration(),
            new ADescriberOf([.. visible.Select(itemId => (itemId, AUser, WatchlistItemKind.Movie))]),
            clock,
            AUser);

    private static StoppedClock AStoppedClock() =>
        new(new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero));

    private static void Add(WatchlistDocumentStore store, TimeProvider clock, Guid itemId)
    {
        var result = store.Add(
            AUser,
            new WatchlistEntry
            {
                ItemId = itemId,
                Kind = WatchlistItemKind.Movie,
                AddedAt = clock.GetUtcNow(),
                Source = WatchlistEntrySource.Api,
            },
            PluginConfiguration.DefaultMaxEntriesPerUser);

        Assert.Equal(WatchlistAddOutcome.Added, result.Outcome);
    }
}
