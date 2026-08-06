using System;
using System.IO;
using System.Linq;
using Jellyfin.Plugin.Watchlist.Configuration;
using Jellyfin.Plugin.Watchlist.Store;
using Xunit;

namespace Jellyfin.Plugin.Watchlist.Tests;

/// <summary>
/// The bound on one user's list. A list with no bound is a memory and a disk problem
/// on a server this plugin does not own, and the reconciliation cost grows with it.
/// </summary>
/// <remarks>
/// Small caps are used throughout so the boundary can be walked in a test rather
/// than described. What the tests are about is the comparison at the edge, and that
/// is the same comparison at three as at ten thousand.
/// </remarks>
public sealed class WatchlistCapTests : IDisposable
{
    private static readonly Guid AUser = Guid.Parse("44444444-4444-4444-4444-444444444444");

    private readonly TemporaryDirectory _sandbox = new("watchlist-cap");

    private string DataFolder => Path.Combine(_sandbox.FullPath, "plugin-data");

    /// <inheritdoc />
    public void Dispose()
    {
        _sandbox.Dispose();
    }

    /// <summary>
    /// The default exists, is written down next to the setting, and is the number the
    /// upstream attempt chose for the same reason.
    /// </summary>
    [Fact]
    public void TheConfigurationCarriesADefaultCap()
    {
        Assert.Equal(10000, PluginConfiguration.DefaultMaxEntriesPerUser);
        Assert.Equal(PluginConfiguration.DefaultMaxEntriesPerUser, new PluginConfiguration().MaxEntriesPerUser);
    }

    /// <summary>
    /// The boundary, walked. One below the cap succeeds, the cap itself succeeds, and
    /// the one after it is refused. An off-by-one in either direction fails here.
    /// </summary>
    [Fact]
    public void TheCapMinusOneSucceedsTheCapSucceedsAndTheCapPlusOneIsRefused()
    {
        const int Cap = 3;
        var store = new WatchlistDocumentStore(DataFolder);

        var toTheCapMinusOne = store.Add(AUser, Entry(1), Cap);
        var toTheCap = Fill(store, Cap, from: 2, cap: Cap);
        var pastTheCap = store.Add(AUser, Entry(Cap + 1), Cap);

        Assert.True(toTheCapMinusOne.WasAdded);
        Assert.Equal(WatchlistAddOutcome.Added, toTheCap.Outcome);
        Assert.Equal(Cap, toTheCap.EntryCount);

        Assert.Equal(WatchlistAddOutcome.RefusedListIsFull, pastTheCap.Outcome);
        Assert.False(pastTheCap.WasAdded);
        Assert.Equal(Cap, pastTheCap.EntryCount);
        Assert.Equal(Cap, pastTheCap.Cap);
    }

    /// <summary>
    /// And the refusal writes nothing. The bytes on disk are the ones the last
    /// successful add left, so nothing was truncated, rewritten or reordered on the way
    /// to saying no.
    /// </summary>
    [Fact]
    public void ARefusedAddLeavesTheDocumentByteForByteAsItWas()
    {
        const int Cap = 3;
        var store = new WatchlistDocumentStore(DataFolder);
        Fill(store, Cap, from: 1, cap: Cap);

        var path = store.PathFor(AUser);
        var before = File.ReadAllBytes(path);

        var refused = store.Add(AUser, Entry(99), Cap);

        Assert.False(refused.WasAdded);
        Assert.Equal(before, File.ReadAllBytes(path));
        Assert.Equal(new[] { Path.GetFileName(path) }, Directory.GetFiles(DataFolder).Select(Path.GetFileName).ToArray());
    }

    /// <summary>
    /// Lowering the cap under an existing list deletes nothing. The list stops growing
    /// and every entry already on it stays, because a list that drops entries when a
    /// setting changes is a list a user cannot trust.
    /// </summary>
    [Fact]
    public void LoweringTheCapUnderAnExistingListRemovesNothing()
    {
        var store = new WatchlistDocumentStore(DataFolder);
        Fill(store, 5, from: 1, cap: 10);

        var refused = store.Add(AUser, Entry(6), maxEntriesPerUser: 2);
        var afterwards = store.Read(AUser).Document!;

        Assert.Equal(WatchlistAddOutcome.RefusedListIsFull, refused.Outcome);
        Assert.Equal(5, refused.EntryCount);
        Assert.Equal(2, refused.Cap);
        Assert.Equal(5, afterwards.Entries.Count);
        Assert.Equal(Enumerable.Range(1, 5).Select(Item).ToArray(), afterwards.Entries.Select(e => e.ItemId).ToArray());
    }

    /// <summary>
    /// And it says so, with both numbers in the sentence, because an administrator
    /// reading a refusal needs to know which of the two to change.
    /// </summary>
    [Fact]
    public void TheRefusalDescribesItselfWithBothNumbers()
    {
        var store = new WatchlistDocumentStore(DataFolder);
        Fill(store, 5, from: 1, cap: 10);

        var description = store.Add(AUser, Entry(6), maxEntriesPerUser: 2).Describe();

        Assert.Contains("5 entries", description, StringComparison.Ordinal);
        Assert.Contains("maximum is 2", description, StringComparison.Ordinal);
        Assert.Contains("nothing was removed", description, StringComparison.Ordinal);
    }

    /// <summary>
    /// A cap of zero refuses the first add rather than treating zero as no cap at all,
    /// which is the reading that turns a mistyped setting into an unbounded list.
    /// </summary>
    [Fact]
    public void ACapOfZeroRefusesTheFirstAdd()
    {
        var store = new WatchlistDocumentStore(DataFolder);

        var refused = store.Add(AUser, Entry(1), maxEntriesPerUser: 0);

        Assert.Equal(WatchlistAddOutcome.RefusedListIsFull, refused.Outcome);
        Assert.Equal(0, refused.EntryCount);
        Assert.False(File.Exists(store.PathFor(AUser)));
    }

    private WatchlistAddResult Fill(WatchlistDocumentStore store, int upTo, int from, int cap)
    {
        WatchlistAddResult? last = null;

        for (var n = from; n <= upTo; n++)
        {
            last = store.Add(AUser, Entry(n), cap);
            Assert.True(last.WasAdded, "Filling the list should not have been refused at " + n);
        }

        return last ?? store.Add(AUser, Entry(from), cap);
    }

    private static WatchlistEntry Entry(int n) => new()
    {
        ItemId = Item(n),
        Kind = WatchlistItemKind.Movie,
        AddedAt = new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero).AddSeconds(n),
        Source = WatchlistEntrySource.Api,
    };

    private static Guid Item(int n) => Guid.Parse($"bbbbbbbb-0000-0000-0000-{n:D12}");
}
