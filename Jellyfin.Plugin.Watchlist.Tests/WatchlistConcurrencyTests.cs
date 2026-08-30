using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Watchlist.Store;
using Xunit;

namespace Jellyfin.Plugin.Watchlist.Tests;

/// <summary>
/// The store is changed from three places, and two of them can run at once on an
/// ordinary server: an endpoint, an event handler and a scheduled reconciliation.
/// </summary>
/// <remarks>
/// Every wait here is bounded. A deadlock has to fail the run rather than hang it,
/// because a hung suite on a machine nobody is watching is a suite that reports
/// nothing at all.
/// </remarks>
public sealed class WatchlistConcurrencyTests : IDisposable
{
    private static readonly TimeSpan LongEnoughToBeADeadlock = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan LongEnoughToBeBlocked = TimeSpan.FromSeconds(5);

    private static readonly Guid AUser = Guid.Parse("66666666-6666-6666-6666-666666666666");
    private static readonly Guid AnotherUser = Guid.Parse("77777777-7777-7777-7777-777777777777");

    private readonly TemporaryDirectory _sandbox = new("watchlist-concurrency");

    private string DataFolder => Path.Join(_sandbox.FullPath, "plugin-data");

    /// <inheritdoc />
    public void Dispose()
    {
        _sandbox.Dispose();
    }

    /// <summary>
    /// Twenty adds at once, and the list holds twenty entries afterwards. Without the
    /// gate this is the lost update: two callers read a list of four, both write a list
    /// of five, and one entry is gone with both callers told it was added.
    /// </summary>
    [Fact]
    public void ConcurrentAddsForOneUserLoseNothing()
    {
        var store = new WatchlistDocumentStore(DataFolder);
        var results = RunTogether(Enumerable.Range(1, 20).Select(n => new Func<object>(
            () => store.Add(AUser, Entry(n), maxEntriesPerUser: 100))));

        var entries = store.Read(AUser).Document!.Entries;

        Assert.All(results.Cast<WatchlistAddResult>(), result => Assert.True(result.WasAdded));
        Assert.Equal(20, entries.Count);
        Assert.Equal(
            Enumerable.Range(1, 20).Select(Item).OrderBy(id => id).ToArray(),
            entries.Select(entry => entry.ItemId).OrderBy(id => id).ToArray());
    }

    /// <summary>
    /// Adds and removes interleaved. The operations touch different items, so what the
    /// document must hold afterwards is the same whatever order they ran in, and any
    /// order that loses one is a lost update rather than a race the test invented.
    /// </summary>
    [Fact]
    public void ConcurrentAddsAndRemovesForOneUserLeaveExactlyWhatTheOperationsImply()
    {
        var store = new WatchlistDocumentStore(DataFolder);

        for (var n = 1; n <= 20; n++)
        {
            Assert.True(store.Add(AUser, Entry(n), maxEntriesPerUser: 100).WasAdded);
        }

        var work = new List<Func<object>>();
        for (var n = 1; n <= 10; n++)
        {
            var toRemove = n;
            work.Add(() => store.Remove(AUser, Item(toRemove)));
        }

        for (var n = 21; n <= 30; n++)
        {
            var toAdd = n;
            work.Add(() => store.Add(AUser, Entry(toAdd), maxEntriesPerUser: 100));
        }

        RunTogether(work);

        var entries = store.Read(AUser).Document!.Entries;

        Assert.Equal(
            Enumerable.Range(11, 20).Select(Item).OrderBy(id => id).ToArray(),
            entries.Select(entry => entry.ItemId).OrderBy(id => id).ToArray());
    }

    /// <summary>
    /// A read never sees a partly applied change. Every read taken while twenty adds
    /// are running returns a whole document, and the count it reports only ever goes up.
    /// </summary>
    [Fact]
    public async Task AReadTakenDuringWritesAlwaysSeesAWholeDocument()
    {
        var store = new WatchlistDocumentStore(DataFolder);
        var counts = new List<int>();
        using var writesDone = new ManualResetEventSlim(false);

        var reader = Task.Run(() =>
        {
            while (!writesDone.IsSet)
            {
                var read = store.Read(AUser);

                Assert.True(read.IsAvailable);
                lock (counts)
                {
                    counts.Add(read.Document!.Entries.Count);
                }
            }
        });

        RunTogether(Enumerable.Range(1, 20).Select(n => new Func<object>(
            () => store.Add(AUser, Entry(n), maxEntriesPerUser: 100))));

        writesDone.Set();

        await Finishes(reader, LongEnoughToBeADeadlock, "The reader did not finish, which is a deadlock rather than a slow machine.");

        Assert.NotEmpty(counts);
        Assert.Equal(counts.OrderBy(count => count).ToArray(), counts.ToArray());
        Assert.Equal(20, store.Read(AUser).Document!.Entries.Count);
    }

    /// <summary>
    /// One user's changes do not block another's. The gate for one document is held for
    /// the whole of this test, and the other user's add still completes.
    /// </summary>
    [Fact]
    public async Task OneUsersGateDoesNotBlockAnother()
    {
        var store = new WatchlistDocumentStore(DataFolder);
        using var held = new ManualResetEventSlim(false);
        using var release = new ManualResetEventSlim(false);

        var holder = Task.Run(() =>
        {
            lock (store.GateFor(AUser))
            {
                held.Set();
                release.Wait(LongEnoughToBeADeadlock);
            }
        });

        Assert.True(held.Wait(LongEnoughToBeBlocked), "The gate was never taken.");

        var addForTheOtherUser = Task.Run(() => store.Add(AnotherUser, Entry(1), maxEntriesPerUser: 100));

        var result = await Finishes(
            addForTheOtherUser,
            LongEnoughToBeBlocked,
            "An add for one user waited on another user's gate.");

        Assert.True(result.WasAdded);

        release.Set();

        await Finishes(holder, LongEnoughToBeADeadlock, "The holder never let go of the gate.");
    }

    /// <summary>
    /// Waits for a task and turns running out of patience into a failure that says what
    /// it means, rather than into a run that never ends.
    /// </summary>
    /// <typeparam name="T">What the task produces.</typeparam>
    /// <param name="task">The task.</param>
    /// <param name="within">How long is too long.</param>
    /// <param name="whatItMeans">What a timeout would mean.</param>
    /// <returns>What the task produced.</returns>
    private static async Task<T> Finishes<T>(Task<T> task, TimeSpan within, string whatItMeans)
    {
        try
        {
            return await task.WaitAsync(within).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            Assert.Fail(whatItMeans);
            throw;
        }
    }

    /// <summary>
    /// The same, for a task that produces nothing.
    /// </summary>
    /// <param name="task">The task.</param>
    /// <param name="within">How long is too long.</param>
    /// <param name="whatItMeans">What a timeout would mean.</param>
    /// <returns>The wait.</returns>
    private static async Task Finishes(Task task, TimeSpan within, string whatItMeans)
    {
        try
        {
            await task.WaitAsync(within).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            Assert.Fail(whatItMeans);
            throw;
        }
    }

    /// <summary>
    /// And the gate really is per document. Two users are two objects, and one user is
    /// the same object every time it is asked for.
    /// </summary>
    [Fact]
    public void TheGateIsOnePerDocument()
    {
        var store = new WatchlistDocumentStore(DataFolder);

        Assert.Same(store.GateFor(AUser), store.GateFor(AUser));
        Assert.NotSame(store.GateFor(AUser), store.GateFor(AnotherUser));
        Assert.Same(store.GateFor(AUser), new WatchlistDocumentStore(DataFolder).GateFor(AUser));
    }

    /// <summary>
    /// Runs everything at once and requires it all to finish. The bound is what turns a
    /// deadlock into a failure instead of a run that never ends.
    /// </summary>
    /// <param name="work">What to run.</param>
    /// <returns>What each piece returned.</returns>
    private static IReadOnlyList<object> RunTogether(IEnumerable<Func<object>> work)
    {
        using var ready = new ManualResetEventSlim(false);
        var tasks = work
            .Select(piece => Task.Run(() =>
            {
                ready.Wait(LongEnoughToBeADeadlock);
                return piece();
            }))
            .ToArray();

        ready.Set();

        Assert.True(
            Task.WaitAll(tasks, LongEnoughToBeADeadlock),
            "Not every operation finished, which is a deadlock rather than a slow machine.");

        return tasks.Select(task => task.Result).ToArray();
    }

    private static WatchlistEntry Entry(int n) => new()
    {
        ItemId = Item(n),
        Kind = WatchlistItemKind.Movie,
        AddedAt = new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero).AddSeconds(n),
        Source = WatchlistEntrySource.Api,
    };

    private static Guid Item(int n) => Guid.Parse($"dddddddd-0000-0000-0000-{n:D12}");
}
