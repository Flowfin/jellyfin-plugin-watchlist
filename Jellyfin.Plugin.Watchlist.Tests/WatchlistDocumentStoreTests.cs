using System;
using System.IO;
using System.Linq;
using Jellyfin.Plugin.Watchlist.Store;
using Xunit;

namespace Jellyfin.Plugin.Watchlist.Tests;

/// <summary>
/// The store on disk. A half-written document is worse than a missing one, because a
/// missing one reads as an empty list and a truncated one reads as nothing at all.
/// </summary>
/// <remarks>
/// Every test here owns a directory of its own and deletes it afterwards. Nothing
/// reads a shared temporary path, a machine-wide path or the clock.
/// </remarks>
public sealed class WatchlistDocumentStoreTests : IDisposable
{
    private static readonly Guid AUser = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid AnotherUser = Guid.Parse("33333333-3333-3333-3333-333333333333");

    private readonly TemporaryDirectory _sandbox = new("watchlist-store");

    /// <summary>
    /// Gets the folder the store under test is given. It sits inside the sandbox with
    /// room beside it, so a write that escapes has somewhere visible to land.
    /// </summary>
    private string DataFolder => Path.Combine(_sandbox.FullPath, "plugin-data");

    /// <inheritdoc />
    public void Dispose()
    {
        _sandbox.Dispose();
    }

    /// <summary>
    /// A new user needs no provisioning step, so a file that is not there is a user
    /// with nothing on their list.
    /// </summary>
    [Fact]
    public void ReadingADocumentThatIsNotThereReturnsAnEmptyList()
    {
        var store = new WatchlistDocumentStore(DataFolder);

        var document = Available(store, AUser);

        Assert.Empty(document.Entries);
        Assert.Equal(AUser, document.UserId);
        Assert.Equal(WatchlistDocument.CurrentSchemaVersion, document.SchemaVersion);
        Assert.False(File.Exists(store.PathFor(AUser)));
    }

    /// <summary>
    /// One document per user, named by the user identifier, and one user's write does
    /// not touch another user's file.
    /// </summary>
    [Fact]
    public void EachUserGetsOneDocumentNamedByTheirIdentifier()
    {
        var store = new WatchlistDocumentStore(DataFolder);

        store.Write(DocumentFor(AUser, 1));
        store.Write(DocumentFor(AnotherUser, 2));

        Assert.Equal("22222222222222222222222222222222.json", Path.GetFileName(store.PathFor(AUser)));
        Assert.Equal(
            new[] { "22222222222222222222222222222222.json", "33333333333333333333333333333333.json" },
            Directory.GetFiles(DataFolder).Select(Path.GetFileName).Order(StringComparer.Ordinal).ToArray());
        Assert.Single(Available(store, AUser).Entries);
        Assert.Equal(2, Available(store, AnotherUser).Entries.Count);
    }

    /// <summary>
    /// The interruption the atomic write exists for. The staged file is on disk and the
    /// move has not happened, which is exactly where a crash leaves the tree, and a
    /// reader still gets the whole of the previous document.
    /// </summary>
    [Fact]
    public void AWriteInterruptedBeforeTheMoveLeavesThePreviousDocumentReadable()
    {
        var store = new WatchlistDocumentStore(DataFolder);
        store.Write(DocumentFor(AUser, 1));

        var staged = store.Stage(DocumentFor(AUser, 5));

        Assert.True(File.Exists(staged.StagedPath), "The staged file should be on disk before the move.");
        Assert.EndsWith(WatchlistDocumentStore.PendingSuffix, staged.StagedPath, StringComparison.Ordinal);

        var afterInterruption = Available(store, AUser);

        Assert.Single(afterInterruption.Entries);
        Assert.Equal(Item(1), afterInterruption.Entries[0].ItemId);
    }

    /// <summary>
    /// And the other half: once the move happens the new document is the one that is
    /// read, and the staged file is gone rather than left beside it.
    /// </summary>
    [Fact]
    public void CommittingTheStagedWriteReplacesTheDocumentInOneStep()
    {
        var store = new WatchlistDocumentStore(DataFolder);
        store.Write(DocumentFor(AUser, 1));

        var staged = store.Stage(DocumentFor(AUser, 5));
        WatchlistDocumentStore.Commit(staged);

        Assert.False(File.Exists(staged.StagedPath));
        Assert.Equal(5, Available(store, AUser).Entries.Count);
    }

    /// <summary>
    /// A write that has never been committed is never read as the document, however
    /// many times it is staged. The pending file is beside the target rather than over
    /// it, which is the whole point of the suffix.
    /// </summary>
    [Fact]
    public void AStagedWriteIsNeverTheDocument()
    {
        var store = new WatchlistDocumentStore(DataFolder);

        store.Stage(DocumentFor(AUser, 3));

        Assert.False(File.Exists(store.PathFor(AUser)));
        Assert.Empty(Available(store, AUser).Entries);
    }

    /// <summary>
    /// Every path is built from the folder the store was given. A user identifier is
    /// hexadecimal and carries no separator, so the file name cannot climb out, and the
    /// resolved path is asserted rather than assumed.
    /// </summary>
    [Fact]
    public void EveryPathTheStoreBuildsStaysInsideTheFolderItWasGiven()
    {
        var store = new WatchlistDocumentStore(DataFolder);
        var root = store.DataFolderPath + Path.DirectorySeparatorChar;

        foreach (var userId in new[] { AUser, AnotherUser, Guid.Empty, Guid.Parse("ffffffff-ffff-ffff-ffff-ffffffffffff") })
        {
            var path = store.PathFor(userId);

            Assert.Equal(Path.GetFullPath(path), path);
            Assert.StartsWith(root, path, StringComparison.Ordinal);
            Assert.Equal(store.DataFolderPath, Path.GetDirectoryName(path));
        }
    }

    /// <summary>
    /// The same claim measured rather than reasoned about: after reading, writing,
    /// staging and committing, every file anywhere under the sandbox is inside the
    /// folder the store was given.
    /// </summary>
    [Fact]
    public void NothingTheStoreDoesWritesOutsideThatFolder()
    {
        var store = new WatchlistDocumentStore(DataFolder);

        store.Read(AUser);
        store.Write(DocumentFor(AUser, 2));
        store.Stage(DocumentFor(AnotherUser, 1));
        WatchlistDocumentStore.Commit(store.Stage(DocumentFor(AnotherUser, 4)));

        var strays = Directory
            .GetFiles(_sandbox.FullPath, "*", SearchOption.AllDirectories)
            .Where(path => !path.StartsWith(store.DataFolderPath + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            .ToArray();

        Assert.Empty(strays);
        Assert.NotEmpty(Directory.GetFiles(store.DataFolderPath));
    }

    /// <summary>
    /// Reads a user's list and requires that it could be read at all. A test about
    /// paths or about the move should fail loudly rather than dereference a null,
    /// if the read ever starts refusing the document for some other reason.
    /// </summary>
    /// <param name="store">The store to read from.</param>
    /// <param name="userId">The user.</param>
    /// <returns>The document.</returns>
    private static WatchlistDocument Available(WatchlistDocumentStore store, Guid userId)
    {
        var result = store.Read(userId);

        Assert.True(result.IsAvailable, "The list read as unavailable for " + userId);

        return result.Document!;
    }

    /// <summary>
    /// A document with a known number of entries, so a test can tell one write from
    /// another by counting.
    /// </summary>
    /// <param name="userId">The user the document belongs to.</param>
    /// <param name="entryCount">How many entries it carries.</param>
    /// <returns>The document.</returns>
    private static WatchlistDocument DocumentFor(Guid userId, int entryCount) => new()
    {
        SchemaVersion = WatchlistDocument.CurrentSchemaVersion,
        UserId = userId,
        Entries = Enumerable.Range(1, entryCount).Select(n => new WatchlistEntry
        {
            ItemId = Item(n),
            Kind = WatchlistItemKind.Movie,
            AddedAt = new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero).AddSeconds(n),
            Source = WatchlistEntrySource.Api,
        }).ToArray(),
    };

    private static Guid Item(int n) => Guid.Parse($"aaaaaaaa-0000-0000-0000-{n:D12}");
}
