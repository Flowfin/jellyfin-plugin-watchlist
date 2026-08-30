using System;
using System.IO;
using System.Threading.Tasks;
using Jellyfin.Data.Events.Users;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Plugin.Watchlist.Store;
using Jellyfin.Plugin.Watchlist.Users;
using Xunit;

namespace Jellyfin.Plugin.Watchlist.Tests;

/// <summary>
/// A user is created and a user is deleted on a running server, and this plugin holds
/// one document per user.
/// </summary>
/// <remarks>
/// <para>
/// The two cases are not symmetrical and these tests say so rather than pairing them.
/// Creation needs nothing: a document that is not there reads as an empty list and the
/// first add writes one, so the property to hold is that nothing provisions anything.
/// Deletion needs a removal, because the list was that person's and the server no
/// longer has the account it belonged to.
/// </para>
/// <para>
/// What is not covered here is the projected playlist, and the reason is that there is
/// none in this tree. The rule taken on issue #23 is that the server removes it on the
/// route a user is deleted through, before the event that reaches this plugin is
/// raised, so nothing here has a playlist call to assert the absence of.
/// </para>
/// </remarks>
public sealed class DeletedUserTests : IDisposable
{
    private static readonly Guid TheDeletedUser = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private static readonly Guid AUserWhoStays = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private static readonly DateTimeOffset WhenItWasAdded =
        new(2026, 1, 2, 3, 4, 5, TimeSpan.Zero);

    private readonly TemporaryDirectory _sandbox = new("watchlist-deleted-user");

    private string DataFolder => Path.Join(_sandbox.FullPath, "plugin-data");

    /// <inheritdoc />
    public void Dispose()
    {
        _sandbox.Dispose();
    }

    /// <summary>
    /// The deleted user's list is gone and everyone else's is exactly as it was.
    /// </summary>
    [Fact]
    public void DeletingAUserRemovesThatUsersListAndNobodyElses()
    {
        var store = new WatchlistDocumentStore(DataFolder);
        store.Add(TheDeletedUser, Entry(1), maxEntriesPerUser: 10);
        store.Add(AUserWhoStays, Entry(2), maxEntriesPerUser: 10);

        var untouched = File.ReadAllBytes(store.PathFor(AUserWhoStays));

        HandlerOver(store, out var log).Handle(TheDeletedUser);

        Assert.False(File.Exists(store.PathFor(TheDeletedUser)));
        Assert.Equal(untouched, File.ReadAllBytes(store.PathFor(AUserWhoStays)));
        Assert.Single(Directory.GetFiles(DataFolder));
        Assert.Single(log.Lines);
    }

    /// <summary>
    /// A server deletes users who never opened a watchlist. That writes nothing,
    /// creates nothing and says nothing.
    /// </summary>
    [Fact]
    public void DeletingAUserWhoNeverHadAListDoesNothingAtAll()
    {
        var store = new WatchlistDocumentStore(DataFolder);
        store.Add(AUserWhoStays, Entry(2), maxEntriesPerUser: 10);

        HandlerOver(store, out var log).Handle(TheDeletedUser);

        Assert.False(File.Exists(store.PathFor(TheDeletedUser)));
        Assert.Single(Directory.GetFiles(DataFolder));
        Assert.Empty(log.Lines);
    }

    /// <summary>
    /// The same deletion twice. The server may publish an event more than once and a
    /// second pass must not fail, must write nothing and must say nothing a second
    /// time.
    /// </summary>
    [Fact]
    public void TheSameDeletionTwiceIsTheSameAsOnce()
    {
        var store = new WatchlistDocumentStore(DataFolder);
        store.Add(TheDeletedUser, Entry(1), maxEntriesPerUser: 10);

        var handler = HandlerOver(store, out var log);

        handler.Handle(TheDeletedUser);
        handler.Handle(TheDeletedUser);

        Assert.False(File.Exists(store.PathFor(TheDeletedUser)));
        Assert.Empty(Directory.GetFiles(DataFolder));
        Assert.Single(log.Lines);
    }

    /// <summary>
    /// A write that was staged and never committed goes with the document.
    /// </summary>
    /// <remarks>
    /// An interrupted write leaves that file beside the document, named for the same
    /// user. Leaving it behind would leave this plugin holding the entries of a user
    /// the server no longer has, under a different suffix, which is the thing the
    /// removal exists to prevent.
    /// </remarks>
    [Fact]
    public void AStagedWriteBesideTheDocumentGoesWithIt()
    {
        var store = new WatchlistDocumentStore(DataFolder);
        store.Add(TheDeletedUser, Entry(1), maxEntriesPerUser: 10);

        store.Stage(WatchlistDocumentStore.Empty(TheDeletedUser));

        Assert.Equal(2, Directory.GetFiles(DataFolder).Length);

        HandlerOver(store, out _).Handle(TheDeletedUser);

        Assert.Empty(Directory.GetFiles(DataFolder));
    }

    /// <summary>
    /// The server's event, through the type this plugin registers, reaches the rule
    /// with the identifier of the user that was deleted.
    /// </summary>
    /// <remarks>
    /// This is the translation and nothing else. It is driven rather than read because
    /// the consumer is the one place that knows a deleted user arrives as a user entity
    /// rather than as an identifier.
    /// </remarks>
    [Fact]
    public async Task TheServersDeletionEventReachesTheRule()
    {
        var store = new WatchlistDocumentStore(DataFolder);
        store.Add(TheDeletedUser, Entry(1), maxEntriesPerUser: 10);
        store.Add(AUserWhoStays, Entry(2), maxEntriesPerUser: 10);

        var subscription = new UserDeletedSubscription(HandlerOver(store, out _));

        await subscription.OnEvent(new UserDeletedEventArgs(UserWithId(TheDeletedUser))).ConfigureAwait(true);

        Assert.False(File.Exists(store.PathFor(TheDeletedUser)));
        Assert.True(File.Exists(store.PathFor(AUserWhoStays)));
    }

    /// <summary>
    /// A user the server has just created needs nothing from this plugin: their list
    /// reads as empty and no file is written for them.
    /// </summary>
    /// <remarks>
    /// The first Done-when of issue #23 is a property of the store rather than of a
    /// handler, and it is asserted here as the created half of the pair so that a
    /// handler added for user creation later has to red this test to arrive.
    /// </remarks>
    [Fact]
    public void ANewUserIsProvisionedWithNothing()
    {
        var store = new WatchlistDocumentStore(DataFolder);
        var newUser = UserWithId(Guid.Parse("33333333-3333-3333-3333-333333333333"));

        var read = store.Read(newUser.Id);

        Assert.True(read.IsAvailable);
        Assert.Empty(read.Document!.Entries);
        Assert.False(File.Exists(store.PathFor(newUser.Id)));
        Assert.False(Directory.Exists(DataFolder));
    }

    private static User UserWithId(Guid id) =>
        new("someone", "Jellyfin.Server.Implementations.Users.DefaultAuthenticationProvider", "Jellyfin.Server.Implementations.Users.DefaultPasswordResetProvider")
        {
            Id = id,
        };

    private static Guid Item(int n) => Guid.Parse($"00000000-0000-0000-0000-{n:D12}");

    private static WatchlistEntry Entry(int n) => new()
    {
        ItemId = Item(n),
        Kind = WatchlistItemKind.Movie,
        AddedAt = WhenItWasAdded,
        Source = WatchlistEntrySource.Api,
    };

    private static DeletedUserHandler HandlerOver(WatchlistDocumentStore store, out RecordingDeletedUserLogger log)
    {
        log = new RecordingDeletedUserLogger();

        return new DeletedUserHandler(store, log);
    }
}
