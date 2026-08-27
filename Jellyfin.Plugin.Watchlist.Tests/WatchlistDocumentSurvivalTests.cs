using System;
using System.IO;
using System.Text.Json;
using Jellyfin.Plugin.Watchlist.Store;
using Xunit;

namespace Jellyfin.Plugin.Watchlist.Tests;

/// <summary>
/// A stored list is never removed by anything this plugin does on its own account.
/// </summary>
/// <remarks>
/// Disabling a plugin is how somebody finds out whether it is the cause of
/// something, and it has to cost nothing. That the store deletes no document
/// was until now a property somebody could read out of the source, which is a
/// reading rather than a rule: the next path that empties a list is free to
/// tidy the file away, and nothing would say so. These tests hold it instead.
///
/// The case they are built around is the one a reasonable person writes on
/// purpose. Removing the last entry leaves a file describing an empty list,
/// which looks like litter, and deleting it there reads as housekeeping. It is
/// not: an emptied list and a list nobody has ever had are the same answer from
/// a read, and the difference between them is the file. Everything that comes
/// later and wants to know whether a user's list was emptied or never existed
/// has only that file to ask.
///
/// THE RULE IS NARROWED AND THE WORDS THAT NARROW IT ARE "ON ITS OWN ACCOUNT".
/// One path removes a document, it is the deletion of the user the document
/// belongs to, and it is reached only from the handler for that event. That is
/// the opposite case rather than an exception to this one: nothing here tidies a
/// document away because it looks like litter, and a user who is gone is
/// somebody asking. A guard rewritten to accommodate the change that broke it is
/// the shape that stops guarding anything, so what is kept is the counting: the
/// test below still runs the surface and counts the files, and the one call that
/// removes a document is named beside the count it is allowed to move.
/// </remarks>
public sealed class WatchlistDocumentSurvivalTests : IDisposable
{
    private static readonly Guid AUser = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private static readonly Guid AnotherUser = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private static readonly DateTimeOffset WhenItWasAdded =
        new(2026, 1, 2, 3, 4, 5, TimeSpan.Zero);

    private readonly TemporaryDirectory _sandbox = new("watchlist-document-survival");

    private string DataFolder => Path.Combine(_sandbox.FullPath, "plugin-data");

    /// <inheritdoc />
    public void Dispose()
    {
        _sandbox.Dispose();
    }

    /// <summary>
    /// The last entry comes off and the document stays.
    /// </summary>
    [Fact]
    public void RemovingTheLastEntryLeavesTheDocumentOnDisk()
    {
        var store = new WatchlistDocumentStore(DataFolder);

        store.Add(AUser, Entry(1), maxEntriesPerUser: 10);

        var removed = store.Remove(AUser, Item(1));

        Assert.True(removed.Removed);
        Assert.Equal(0, removed.EntryCount);
        Assert.True(
            File.Exists(store.PathFor(AUser)),
            "Emptying a list must leave the document behind. Removing it makes an emptied list indistinguishable from one that never existed.");
    }

    /// <summary>
    /// And what is left on disk is still that user's document at the version this
    /// plugin writes, rather than a file holding whatever the last write happened
    /// to leave in it.
    /// </summary>
    /// <remarks>
    /// Read from the file rather than through the store. A read of a document that
    /// is not there answers with an empty list for that user, which is the right
    /// answer and is also the answer this assertion would get from a folder the
    /// document had been deleted out of.
    /// </remarks>
    [Fact]
    public void AnEmptiedDocumentOnDiskStillDeclaresItsUserAndItsVersion()
    {
        var store = new WatchlistDocumentStore(DataFolder);

        store.Add(AUser, Entry(1), maxEntriesPerUser: 10);
        store.Remove(AUser, Item(1));

        using var stored = JsonDocument.Parse(File.ReadAllText(store.PathFor(AUser)));
        var root = stored.RootElement;

        Assert.Equal(AUser, root.GetProperty("UserId").GetGuid());
        Assert.Equal(WatchlistDocument.CurrentSchemaVersion, root.GetProperty("SchemaVersion").GetInt32());
        Assert.Equal(0, root.GetProperty("Entries").GetArrayLength());
    }

    /// <summary>
    /// The public surface of the store other than the deletion of a user, run over
    /// one user's list, against a folder holding two. Nothing it offers removes a
    /// document, including the document belonging to the user the call was not about.
    /// </summary>
    /// <remarks>
    /// Counted rather than asserted file by file, because the failure this is
    /// against is a path that removes a document nobody named, and a test naming
    /// the files it expects would only find the ones it thought of.
    /// </remarks>
    [Fact]
    public void NoPathThroughTheStoreExceptADeletedUserRemovesADocument()
    {
        var store = new WatchlistDocumentStore(DataFolder);

        store.Add(AUser, Entry(1), maxEntriesPerUser: 10);
        store.Add(AnotherUser, Entry(2), maxEntriesPerUser: 10);

        var untouched = File.ReadAllBytes(store.PathFor(AnotherUser));

        store.Read(AUser);
        store.Write(WatchlistDocumentStore.Empty(AUser));
        store.Add(AUser, Entry(3), maxEntriesPerUser: 10);
        store.Add(AUser, Entry(3), maxEntriesPerUser: 10);
        store.Add(AUser, Entry(4), maxEntriesPerUser: 1);
        store.Remove(AUser, Item(4));
        store.Remove(AUser, Item(3));
        store.Remove(AUser, Item(3));
        store.Read(AUser);

        Assert.Equal(2, Directory.GetFiles(DataFolder).Length);
        Assert.True(File.Exists(store.PathFor(AUser)));
        Assert.Equal(untouched, File.ReadAllBytes(store.PathFor(AnotherUser)));
    }

    /// <summary>
    /// The one call that does remove a document removes exactly one, and it is the
    /// document of the user it was given.
    /// </summary>
    /// <remarks>
    /// This is the same count from the other side, and it is what keeps the test
    /// above from being a claim about a surface that has quietly grown a second
    /// deleting path. The two together say the folder loses one file, for one named
    /// user, on one call.
    /// </remarks>
    [Fact]
    public void TheDeletionOfAUserRemovesThatOneDocumentAndNoOther()
    {
        var store = new WatchlistDocumentStore(DataFolder);

        store.Add(AUser, Entry(1), maxEntriesPerUser: 10);
        store.Add(AnotherUser, Entry(2), maxEntriesPerUser: 10);

        var untouched = File.ReadAllBytes(store.PathFor(AnotherUser));

        Assert.True(store.DeleteTheDocumentOfADeletedUser(AUser));

        Assert.Single(Directory.GetFiles(DataFolder));
        Assert.False(File.Exists(store.PathFor(AUser)));
        Assert.Equal(untouched, File.ReadAllBytes(store.PathFor(AnotherUser)));
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
