using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Watchlist.Configuration;
using Jellyfin.Plugin.Watchlist.Projection;
using Jellyfin.Plugin.Watchlist.Store;
using Xunit;

namespace Jellyfin.Plugin.Watchlist.Tests;

/// <summary>
/// Which playlist a list is projected into, and what makes a second pass do nothing.
/// </summary>
/// <remarks>
/// Every test here drives the seam from #82 with a fake rather than a server type, so
/// nothing in this file knows which server line it would be running on. Each owns a
/// directory of its own and deletes it afterwards; nothing reads a shared temporary
/// path or the clock.
/// </remarks>
public sealed class ProjectionTests : IDisposable
{
    private static readonly Guid AUser = Guid.Parse("55555555-5555-5555-5555-555555555555");

    private static readonly Guid AnotherUser = Guid.Parse("66666666-6666-6666-6666-666666666666");

    private static readonly Guid TheFixtureUser = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private readonly TemporaryDirectory _sandbox = new("watchlist-projection");

    private string DataFolder => Path.Join(_sandbox.FullPath, "plugin-data");

    /// <inheritdoc />
    public void Dispose()
    {
        _sandbox.Dispose();
    }

    /// <summary>
    /// One user, one playlist, made for that user under the configured name, and the
    /// identifier written into that user's own document.
    /// </summary>
    [Fact]
    public async Task AUserWithNoPlaylistGetsExactlyOneUnderTheConfiguredName()
    {
        var store = new WatchlistDocumentStore(DataFolder, new RecordingLogger());
        var server = new APlaylistServerOf();
        var projector = new WatchlistProjector(server, new RecordingProjectorLogger());

        var result = await projector.EnsurePlaylistAsync(TargetFor(store, AUser), CancellationToken.None);

        Assert.Equal(ProjectionOutcome.Created, result.Outcome);
        Assert.Equal(1, server.Creations);
        Assert.Contains(
            server.Calls,
            call => call.StartsWith("create " + AUser, StringComparison.Ordinal)
                && call.EndsWith(PluginConfiguration.DefaultProjectedListName, StringComparison.Ordinal));

        var stored = store.Read(AUser).Document!.Projection;
        Assert.NotNull(stored);
        Assert.Equal(result.Projection!.PlaylistId, stored.PlaylistId);
        Assert.Equal(PluginConfiguration.DefaultProjectedListName, stored.LastNameWritten);
    }

    /// <summary>
    /// And a second pass creates none. This is the assertion the whole shape exists
    /// for: the identifier is read back out of the document rather than a playlist
    /// being looked up by its name.
    /// </summary>
    [Fact]
    public async Task ASecondPassOverTheSameUserCreatesNothing()
    {
        var store = new WatchlistDocumentStore(DataFolder, new RecordingLogger());
        var server = new APlaylistServerOf();
        var projector = new WatchlistProjector(server, new RecordingProjectorLogger());

        var first = await projector.EnsurePlaylistAsync(TargetFor(store, AUser), CancellationToken.None);
        var second = await projector.EnsurePlaylistAsync(TargetFor(store, AUser), CancellationToken.None);

        Assert.Equal(ProjectionOutcome.Created, first.Outcome);
        Assert.Equal(ProjectionOutcome.AlreadyProjected, second.Outcome);
        Assert.Equal(first.Projection!.PlaylistId, second.Projection!.PlaylistId);
        Assert.Equal(1, server.Creations);
    }

    /// <summary>
    /// A playlist the user deleted from a client is made again, and the document is
    /// moved onto the new one. A remembered identifier that is trusted rather than
    /// asked about leaves a user with no list and a reconciler writing into nothing.
    /// </summary>
    [Fact]
    public async Task APlaylistTheUserDeletedIsMadeAgainAndTheRecordMovesToIt()
    {
        var store = new WatchlistDocumentStore(DataFolder, new RecordingLogger());
        var server = new APlaylistServerOf();
        var projector = new WatchlistProjector(server, new RecordingProjectorLogger());

        var first = await projector.EnsurePlaylistAsync(TargetFor(store, AUser), CancellationToken.None);
        server.NoLongerHolds(AUser, first.Projection!.PlaylistId);

        var second = await projector.EnsurePlaylistAsync(TargetFor(store, AUser), CancellationToken.None);

        Assert.Equal(ProjectionOutcome.Created, second.Outcome);
        Assert.NotEqual(first.Projection.PlaylistId, second.Projection!.PlaylistId);
        Assert.Equal(2, server.Creations);
        Assert.Equal(second.Projection.PlaylistId, store.Read(AUser).Document!.Projection!.PlaylistId);
    }

    /// <summary>
    /// A playlist that belongs to somebody else and happens to carry the remembered
    /// identifier is not this user's. The server is asked what THIS user owns, so a
    /// projection cannot come to rest on a list the user does not own.
    /// </summary>
    [Fact]
    public async Task APlaylistOwnedBySomebodyElseDoesNotSatisfyThisUsersProjection()
    {
        var store = new WatchlistDocumentStore(DataFolder, new RecordingLogger());
        var server = new APlaylistServerOf();
        var projector = new WatchlistProjector(server, new RecordingProjectorLogger());

        var borrowed = Guid.Parse("cccccccc-0000-0000-0000-000000000001");
        server.AlreadyHolds(AnotherUser, borrowed, PluginConfiguration.DefaultProjectedListName);
        Assert.True(store.SetProjection(AUser, new WatchlistProjectionState
        {
            PlaylistId = borrowed,
            LastNameWritten = PluginConfiguration.DefaultProjectedListName,
        }));

        var result = await projector.EnsurePlaylistAsync(TargetFor(store, AUser), CancellationToken.None);

        Assert.Equal(ProjectionOutcome.Created, result.Outcome);
        Assert.NotEqual(borrowed, result.Projection!.PlaylistId);
    }

    /// <summary>
    /// The target names whose playlist it is, and the projector takes that answer.
    /// Nothing here assumes the list belongs to whoever asked, which is what lets one
    /// projector serve a list several users can see.
    /// </summary>
    [Fact]
    public async Task ThePlaylistIsMadeForTheOwnerTheTargetNames()
    {
        var server = new APlaylistServerOf();
        var projector = new WatchlistProjector(server, new RecordingProjectorLogger());
        var target = new ATargetOwnedBy(AnotherUser, "Shared Watchlist (plugin)");

        var result = await projector.EnsurePlaylistAsync(target, CancellationToken.None);

        Assert.Equal(ProjectionOutcome.Created, result.Outcome);
        Assert.Single(server.PlaylistsOf(AnotherUser));
        Assert.Empty(server.PlaylistsOf(AUser));
        Assert.Equal("Shared Watchlist (plugin)", target.Remembered!.LastNameWritten);
    }

    /// <summary>
    /// A pass is over one target and never over a population. Asking about one user
    /// leaves every other user of the server untouched, which is what "on demand"
    /// means where a server has a thousand users who never opened the plugin.
    /// </summary>
    [Fact]
    public async Task AskingAboutOneUserTouchesNoOtherUser()
    {
        var store = new WatchlistDocumentStore(DataFolder, new RecordingLogger());
        var server = new APlaylistServerOf();
        var projector = new WatchlistProjector(server, new RecordingProjectorLogger());

        await projector.EnsurePlaylistAsync(TargetFor(store, AUser), CancellationToken.None);

        Assert.DoesNotContain(server.Calls, call => call.Contains(AnotherUser.ToString(), StringComparison.Ordinal));
        Assert.False(File.Exists(store.PathFor(AnotherUser)));
    }

    /// <summary>
    /// A document this build refuses to read projects nothing and creates nothing. A
    /// projector that could not tell an unreadable document from a user with no
    /// playlist would make one for that user on every pass, for ever.
    /// </summary>
    [Fact]
    public async Task ADocumentThisBuildWillNotReadProjectsNothingAndCreatesNothing()
    {
        var store = new WatchlistDocumentStore(DataFolder, new RecordingLogger());
        Place(store.PathFor(TheFixtureUser), "watchlist-document-from-the-future.json");
        var server = new APlaylistServerOf();
        var projector = new WatchlistProjector(server, new RecordingProjectorLogger());

        var result = await projector.EnsurePlaylistAsync(TargetFor(store, TheFixtureUser), CancellationToken.None);

        Assert.Equal(ProjectionOutcome.RefusedRecordUnavailable, result.Outcome);
        Assert.Null(result.Projection);
        Assert.Empty(server.Calls);
    }

    /// <summary>
    /// A record that cannot be written after the playlist was made is the one outcome
    /// that leaves something behind, so it is refused loudly and named in the log. A
    /// silent one is a playlist nothing points at and a second one on the next pass.
    /// </summary>
    [Fact]
    public async Task APlaylistThatCouldNotBeRecordedIsRefusedAndSaidOnce()
    {
        var server = new APlaylistServerOf();
        var log = new RecordingProjectorLogger();
        var projector = new WatchlistProjector(server, log);
        var target = new ATargetThatWillNotRemember(AUser, PluginConfiguration.DefaultProjectedListName);

        var result = await projector.EnsurePlaylistAsync(target, CancellationToken.None);

        Assert.Equal(ProjectionOutcome.RefusedRecordUnavailable, result.Outcome);
        Assert.Null(result.Projection);
        Assert.Equal(1, server.Creations);

        var line = Assert.Single(log.Lines);
        Assert.StartsWith("Error", line, StringComparison.Ordinal);
        Assert.Contains(AUser.ToString(), line, StringComparison.Ordinal);
    }

    /// <summary>
    /// The near miss for the line above: the same pass with a record that accepts the
    /// write says one thing, at information rather than error, and names the playlist.
    /// </summary>
    [Fact]
    public async Task ARecordedPlaylistIsSaidOnceAndNotAsAFailure()
    {
        var store = new WatchlistDocumentStore(DataFolder, new RecordingLogger());
        var log = new RecordingProjectorLogger();
        var projector = new WatchlistProjector(new APlaylistServerOf(), log);

        var result = await projector.EnsurePlaylistAsync(TargetFor(store, AUser), CancellationToken.None);

        var line = Assert.Single(log.Lines);
        Assert.StartsWith("Information", line, StringComparison.Ordinal);
        Assert.Contains(result.Projection!.PlaylistId.ToString(), line, StringComparison.Ordinal);
    }

    /// <summary>
    /// A pass over a target that already has its playlist writes nothing to the log at
    /// all, because the ordinary case is the one that runs on every server on every
    /// pass and a line per user per pass is a log nobody reads.
    /// </summary>
    [Fact]
    public async Task APassThatChangesNothingSaysNothing()
    {
        var store = new WatchlistDocumentStore(DataFolder, new RecordingLogger());
        var projector = new WatchlistProjector(new APlaylistServerOf(), new RecordingProjectorLogger());
        await projector.EnsurePlaylistAsync(TargetFor(store, AUser), CancellationToken.None);

        var log = new RecordingProjectorLogger();
        var quiet = new WatchlistProjector(ThePlaylistsOf(store, AUser), log);

        var result = await quiet.EnsurePlaylistAsync(TargetFor(store, AUser), CancellationToken.None);

        Assert.Equal(ProjectionOutcome.AlreadyProjected, result.Outcome);
        Assert.Empty(log.Lines);
    }

    /// <summary>
    /// There is no pass without a target.
    /// </summary>
    [Fact]
    public async Task ThereIsNoPassWithoutATarget()
    {
        var projector = new WatchlistProjector(new APlaylistServerOf(), new RecordingProjectorLogger());

        await Assert.ThrowsAsync<ArgumentNullException>(
            () => projector.EnsurePlaylistAsync(null!, CancellationToken.None));
    }

    /// <summary>
    /// A user who has never used the plugin has no document, and that is a target with
    /// nothing remembered rather than one that cannot be read. A projector that read
    /// the two as one would make a playlist for that user on every pass.
    /// </summary>
    [Fact]
    public void AUserWithNoDocumentIsATargetWithNothingRememberedRatherThanAnUnreadableOne()
    {
        var store = new WatchlistDocumentStore(DataFolder, new RecordingLogger());

        var target = TargetFor(store, AUser);

        Assert.True(target.IsRecordAvailable);
        Assert.Null(target.Remembered);
        Assert.Equal(AUser, target.OwnerUserId);
        Assert.Equal(PluginConfiguration.DefaultProjectedListName, target.ConfiguredName);
    }

    /// <summary>
    /// And a document this build refuses is the other answer, so the projector has
    /// something to refuse rather than a null it reads as a fresh user.
    /// </summary>
    [Fact]
    public void ADocumentThisBuildWillNotReadIsATargetWhoseRecordIsUnavailable()
    {
        var store = new WatchlistDocumentStore(DataFolder, new RecordingLogger());
        Place(store.PathFor(TheFixtureUser), "watchlist-document-from-the-future.json");

        var target = TargetFor(store, TheFixtureUser);

        Assert.False(target.IsRecordAvailable);
        Assert.Null(target.Remembered);
    }

    /// <summary>
    /// There is no target without a store to read.
    /// </summary>
    [Fact]
    public void ThereIsNoTargetWithoutAnyOfWhatItReads()
    {
        var store = new WatchlistDocumentStore(DataFolder, new RecordingLogger());

        Assert.Throws<ArgumentNullException>(
            () => UserProjectionTarget.For(null!, new PluginConfiguration(), new ADescriberOf(), AStoppedClock(), AUser));
        Assert.Throws<ArgumentNullException>(
            () => UserProjectionTarget.For(store, null!, new ADescriberOf(), AStoppedClock(), AUser));
        Assert.Throws<ArgumentNullException>(
            () => UserProjectionTarget.For(store, new PluginConfiguration(), null!, AStoppedClock(), AUser));
        Assert.Throws<ArgumentNullException>(
            () => UserProjectionTarget.For(store, new PluginConfiguration(), new ADescriberOf(), null!, AUser));
    }

    /// <summary>
    /// A document written before version 3 reads as a user with no projection, which
    /// is what the step from version 2 says it is, with its entries intact.
    /// </summary>
    [Fact]
    public void ADocumentFromBeforeTheBlockReadsAsAUserWithNoProjection()
    {
        var store = new WatchlistDocumentStore(DataFolder, new RecordingLogger());
        Place(store.PathFor(TheFixtureUser), "watchlist-document-v2.json");

        var read = store.Read(TheFixtureUser);

        Assert.True(read.IsAvailable);
        Assert.Equal(WatchlistDocument.CurrentSchemaVersion, read.Document!.SchemaVersion);
        Assert.Equal(3, read.Document.Entries.Count);
        Assert.Null(read.Document.Projection);
    }

    /// <summary>
    /// The block is written for a user who has one and left out entirely for a user
    /// who does not, which is a property of the bytes rather than of a reader.
    /// </summary>
    [Fact]
    public void AUserWithNoProjectionHasNoSuchBlockOnDisk()
    {
        var store = new WatchlistDocumentStore(DataFolder, new RecordingLogger());
        store.Add(AUser, AnEntry(), PluginConfiguration.DefaultMaxEntriesPerUser);

        Assert.DoesNotContain("Projection", File.ReadAllText(store.PathFor(AUser)), StringComparison.Ordinal);

        Assert.True(store.SetProjection(AUser, new WatchlistProjectionState
        {
            PlaylistId = Guid.Parse("cccccccc-0000-0000-0000-000000000002"),
            LastNameWritten = "Watchlist",
        }));

        Assert.Contains("Projection", File.ReadAllText(store.PathFor(AUser)), StringComparison.Ordinal);

        Assert.True(store.SetProjection(AUser, null));

        Assert.DoesNotContain("Projection", File.ReadAllText(store.PathFor(AUser)), StringComparison.Ordinal);
        Assert.Single(store.Read(AUser).Document!.Entries);
    }

    /// <summary>
    /// A document this build cannot read is left alone by a projection write exactly as
    /// it is by a read. Writing here would replace a newer plugin's document with a
    /// shape this one understands, which is the entry loss the read path refuses.
    /// </summary>
    [Fact]
    public void ADocumentThisBuildCannotReadIsNotWrittenTo()
    {
        var store = new WatchlistDocumentStore(DataFolder, new RecordingLogger());
        Place(store.PathFor(TheFixtureUser), "watchlist-document-from-the-future.json");
        var before = File.ReadAllBytes(store.PathFor(TheFixtureUser));

        Assert.False(store.SetProjection(TheFixtureUser, new WatchlistProjectionState
        {
            PlaylistId = Guid.Parse("cccccccc-0000-0000-0000-000000000003"),
            LastNameWritten = "Watchlist",
        }));

        Assert.Equal(before, File.ReadAllBytes(store.PathFor(TheFixtureUser)));
    }

    /// <summary>
    /// The identifier is the identity. Two playlists of one user carrying one name is
    /// a thing a server produces on its own, so what the record holds has to tell them
    /// apart and the name cannot.
    /// </summary>
    /// <remarks>
    /// The second list arrives after the projection exists, which is the order that
    /// makes this about the identifier. A list carrying the configured name that is
    /// already there when the first pass runs is adopted rather than duplicated, and
    /// that is #41 rather than this.
    /// </remarks>
    [Fact]
    public async Task TheIdentifierTellsApartTwoPlaylistsOfOneUserSharingAName()
    {
        var store = new WatchlistDocumentStore(DataFolder, new RecordingLogger());
        var server = new APlaylistServerOf();
        var projector = new WatchlistProjector(server, new RecordingProjectorLogger());

        var made = await projector.EnsurePlaylistAsync(TargetFor(store, AUser), CancellationToken.None);

        server.AlreadyHolds(
            AUser,
            Guid.Parse("cccccccc-0000-0000-0000-000000000004"),
            PluginConfiguration.DefaultProjectedListName);

        var again = await projector.EnsurePlaylistAsync(TargetFor(store, AUser), CancellationToken.None);

        var owned = server.PlaylistsOf(AUser);
        Assert.Equal(2, owned.Count);
        Assert.Single(owned.Select(playlist => playlist.Name).Distinct(StringComparer.Ordinal));
        Assert.Equal(ProjectionOutcome.AlreadyProjected, again.Outcome);
        Assert.Equal(made.Projection!.PlaylistId, store.Read(AUser).Document!.Projection!.PlaylistId);
    }

    private static UserProjectionTarget TargetFor(WatchlistDocumentStore store, Guid userId) =>
        UserProjectionTarget.For(store, new PluginConfiguration(), new ADescriberOf(), AStoppedClock(), userId);

    private static StoppedClock AStoppedClock() =>
        new(new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero));

    /// <summary>
    /// A server holding exactly the playlist a user's document already names, so a
    /// pass over it changes nothing.
    /// </summary>
    /// <param name="store">The store.</param>
    /// <param name="userId">The user.</param>
    /// <returns>The server.</returns>
    private static APlaylistServerOf ThePlaylistsOf(WatchlistDocumentStore store, Guid userId)
    {
        var remembered = store.Read(userId).Document!.Projection!;
        var server = new APlaylistServerOf();
        server.AlreadyHolds(userId, remembered.PlaylistId, remembered.LastNameWritten);

        return server;
    }

    private static WatchlistEntry AnEntry() => new()
    {
        ItemId = Guid.Parse("aaaaaaaa-0000-0000-0000-00000000000a"),
        Kind = WatchlistItemKind.Movie,
        AddedAt = new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero),
        Source = WatchlistEntrySource.Api,
    };

    private static void Place(string path, string fixture)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        var assembly = Assembly.GetExecutingAssembly();
        var resource = "fixture/" + fixture;
        using var stream = assembly.GetManifestResourceStream(resource)
            ?? throw new InvalidOperationException(resource + " is not embedded in the test assembly.");

        using var reader = new StreamReader(stream);
        File.WriteAllText(path, reader.ReadToEnd());
    }

    /// <summary>
    /// A target that names an owner and remembers in memory, for the cases that are
    /// about the projector rather than about a user's document.
    /// </summary>
    private sealed class ATargetOwnedBy : IProjectionTarget
    {
        public ATargetOwnedBy(Guid ownerUserId, string configuredName)
        {
            OwnerUserId = ownerUserId;
            ConfiguredName = configuredName;
        }

        public Guid OwnerUserId { get; }

        public string ConfiguredName { get; }

        public bool IsRecordAvailable => true;

        public WatchlistProjectionState? Remembered { get; private set; }

        public IReadOnlyList<Guid> Adopted { get; private set; } = [];

        public bool Remember(WatchlistProjectionState projection)
        {
            Remembered = projection;

            return true;
        }

        public int Adopt(IReadOnlyList<Guid> itemIds)
        {
            Adopted = itemIds;

            return itemIds.Count;
        }
    }

    /// <summary>
    /// A target whose record was readable when the pass started and cannot be written
    /// when the pass tries to. It is the window between the two, which is narrow and
    /// is the only way a playlist is created and not recorded.
    /// </summary>
    private sealed class ATargetThatWillNotRemember : IProjectionTarget
    {
        public ATargetThatWillNotRemember(Guid ownerUserId, string configuredName)
        {
            OwnerUserId = ownerUserId;
            ConfiguredName = configuredName;
        }

        public Guid OwnerUserId { get; }

        public string ConfiguredName { get; }

        public bool IsRecordAvailable => true;

        public WatchlistProjectionState? Remembered => null;

        public bool Remember(WatchlistProjectionState projection) => false;

        public int Adopt(IReadOnlyList<Guid> itemIds) => 0;
    }
}
