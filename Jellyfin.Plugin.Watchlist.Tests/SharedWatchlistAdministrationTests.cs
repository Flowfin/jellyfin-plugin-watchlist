using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Watchlist.Api;
using Jellyfin.Plugin.Watchlist.Configuration;
using Jellyfin.Plugin.Watchlist.Projection;
using Jellyfin.Plugin.Watchlist.Store;
using MediaBrowser.Common.Api;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Jellyfin.Plugin.Watchlist.Tests;

/// <summary>
/// What an administrator does to the shared list, rather than what a user does with
/// it: making the one list this server can have, and taking it away again. Driven by
/// calling the endpoints, with no web host and no server.
/// </summary>
/// <remarks>
/// These are the first two endpoints of this plugin that require elevation, and the
/// refusal is what most of this file is about. Every one of them runs against a store
/// in a directory the test owns, so a refusal that is supposed to write nothing is
/// checked by looking at the folder rather than by trusting the return value.
/// </remarks>
public sealed class SharedWatchlistAdministrationTests : IDisposable
{
    private static readonly Guid TheList = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid TheOwner = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly Guid ThePlaylist = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
    private static readonly Guid AnAdministrator = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid AnotherAdministrator = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid AnOrdinaryUser = Guid.Parse("33333333-3333-3333-3333-333333333333");

    private const string Unreachable = "This server's playlists cannot be reached.";

    private static readonly DateTimeOffset WhenItWasAdded =
        new(2026, 1, 2, 3, 4, 5, TimeSpan.Zero);

    private readonly TemporaryDirectory _sandbox = new("shared-watchlist-administration");

    private string DataFolder => Path.Join(_sandbox.FullPath, "plugin-data");

    /// <inheritdoc />
    public void Dispose()
    {
        _sandbox.Dispose();
    }

    /// <summary>
    /// The state a fresh server is in. Nobody has made a shared list, so there is no
    /// record and no file, which is what every other answer on this surface is
    /// measured against.
    /// </summary>
    [Fact]
    public void AServerWhereNobodyMadeOneHasNoSharedRecord()
    {
        Assert.False(Store().ReadShared().Exists);
        Assert.False(File.Exists(Store().SharedListPath));
    }

    /// <summary>
    /// The operation this issue exists for. An administrator on a server that offers a
    /// shared list makes it, and afterwards there is one.
    /// </summary>
    [Fact]
    public void AnAdministratorMakesTheSharedList()
    {
        var controller = ControllerOn(AServerThatOffersASharedList());

        Assert.IsType<NoContentResult>(
            controller.CreateSharedListFor(AnAdministrator, callerIsAnAdministrator: true));

        var read = Store().ReadShared();

        Assert.True(read.Exists);
        Assert.NotNull(read.Document);
        Assert.Empty(read.Document!.Entries);
        Assert.Equal(AnAdministrator, read.Document.OwnerUserId);
    }

    /// <summary>
    /// The second call, which is the one an administrator makes when they cannot
    /// remember whether the first worked. It answers the same and it takes nothing off
    /// the list, because a create that emptied a list people had been adding to would
    /// be the worst possible answer to a repeated request.
    /// </summary>
    [Fact]
    public void MakingItASecondTimeLeavesTheListThatIsThere()
    {
        MakeTheSharedList();
        StoreShared(AnEntryAddedBy(1, AnOrdinaryUser));

        var controller = ControllerOn(AServerThatOffersASharedList());

        Assert.IsType<NoContentResult>(
            controller.CreateSharedListFor(AnotherAdministrator, callerIsAnAdministrator: true));

        var read = Store().ReadShared();

        Assert.Equal(TheList, read.Document!.ListId);
        Assert.Equal(TheOwner, read.Document.OwnerUserId);
        Assert.Equal(Item(1), Assert.Single(read.Document.Entries).ItemId);
    }

    /// <summary>
    /// A caller the server does not answer for is refused, and the folder is looked at
    /// rather than the return value, because refused and refused-having-written-nothing
    /// are two claims.
    /// </summary>
    [Fact]
    public void ACallerWithoutElevationCannotMakeOneAndNothingIsWritten()
    {
        var controller = ControllerOn(AServerThatOffersASharedList());

        Assert.Equal(
            StatusCodes.Status403Forbidden,
            StatusOf(controller.CreateSharedListFor(AnOrdinaryUser, callerIsAnAdministrator: false)));

        Assert.False(Store().ReadShared().Exists);
        Assert.Empty(FilesInTheDataFolder());
    }

    /// <summary>
    /// The switch and the record are one answer rather than two. SharedListEnabled is
    /// the server's statement about whether it offers a shared list, so a record made
    /// while it says no would leave the settings page telling an administrator the
    /// server has none while every user could see one.
    /// </summary>
    [Fact]
    public void AServerConfiguredNotToOfferOneRefusesTheCreation()
    {
        var controller = ControllerOn(new PluginConfiguration());

        Assert.IsType<ConflictResult>(
            controller.CreateSharedListFor(AnAdministrator, callerIsAnAdministrator: true));

        Assert.False(Store().ReadShared().Exists);
        Assert.Empty(FilesInTheDataFolder());
    }

    /// <summary>
    /// The one-change neighbour of the refusal above: the same call on a server whose
    /// only difference is that the switch is on. Without this the test above would
    /// prove that something refused rather than that the switch is what refused.
    /// </summary>
    [Fact]
    public void TheSameCallOnAServerThatOffersOneIsAccepted()
    {
        var controller = ControllerOn(AServerThatOffersASharedList());

        Assert.IsType<NoContentResult>(
            controller.CreateSharedListFor(AnAdministrator, callerIsAnAdministrator: true));

        Assert.True(Store().ReadShared().Exists);
    }

    /// <summary>
    /// The removal, which takes the record away and leaves the folder with nothing of
    /// the shared list in it.
    /// </summary>
    /// <returns>The running test.</returns>
    [Fact]
    public async Task AnAdministratorTakesTheSharedListAway()
    {
        MakeTheSharedList();
        StoreShared(AnEntryAddedBy(1, AnOrdinaryUser));

        var controller = ControllerOn(AServerThatOffersASharedList());

        Assert.IsType<NoContentResult>(await controller.RemoveSharedListFor(callerIsAnAdministrator: true, CancellationToken.None).ConfigureAwait(true));

        Assert.False(Store().ReadShared().Exists);
        Assert.Empty(FilesInTheDataFolder());
    }

    /// <summary>
    /// Removed and never there are one answer, as everywhere else on this surface. A
    /// caller asked for a server without a shared list and that is what they have.
    /// </summary>
    /// <returns>The running test.</returns>
    [Fact]
    public async Task RemovingAListThatIsNotThereIsTheSameAnswer()
    {
        var controller = ControllerOn(AServerThatOffersASharedList());

        Assert.IsType<NoContentResult>(await controller.RemoveSharedListFor(callerIsAnAdministrator: true, CancellationToken.None).ConfigureAwait(true));

        Assert.False(Store().ReadShared().Exists);
    }

    /// <summary>
    /// The second condition of this operation: it does the same thing every time rather
    /// than depending on who asked. Two administrators, the same starting state each,
    /// and the same answer and the same folder afterwards.
    /// </summary>
    /// <returns>The running test.</returns>
    [Fact]
    public async Task TheRemovalDoesTheSameThingWhicheverAdministratorAsks()
    {
        MakeTheSharedList();
        StoreShared(AnEntryAddedBy(1, AnOrdinaryUser));

        var first = ControllerOn(AServerThatOffersASharedList(), AsRequestFrom(AnAdministrator));

        Assert.IsType<NoContentResult>(await first.RemoveSharedListFor(callerIsAnAdministrator: true, CancellationToken.None).ConfigureAwait(true));

        var afterTheFirst = FilesInTheDataFolder();

        MakeTheSharedList();
        StoreShared(AnEntryAddedBy(1, AnOrdinaryUser));

        var second = ControllerOn(AServerThatOffersASharedList(), AsRequestFrom(AnotherAdministrator));

        Assert.IsType<NoContentResult>(await second.RemoveSharedListFor(callerIsAnAdministrator: true, CancellationToken.None).ConfigureAwait(true));

        Assert.Equal(afterTheFirst, FilesInTheDataFolder());
    }

    /// <summary>
    /// A caller the server does not answer for cannot take the list away, and the list
    /// and its entries are still there afterwards.
    /// </summary>
    /// <returns>The running test.</returns>
    [Fact]
    public async Task ACallerWithoutElevationCannotRemoveTheListAndItStays()
    {
        MakeTheSharedList();
        StoreShared(AnEntryAddedBy(1, AnOrdinaryUser));

        var controller = ControllerOn(AServerThatOffersASharedList());

        Assert.Equal(
            StatusCodes.Status403Forbidden,
            StatusOf(await controller.RemoveSharedListFor(callerIsAnAdministrator: false, CancellationToken.None).ConfigureAwait(true)));

        Assert.Equal(Item(1), Assert.Single(Store().ReadShared().Document!.Entries).ItemId);
    }

    /// <summary>
    /// A staged write left behind by a run that died between staging and committing is
    /// removed with the list. Nothing reads it once the list is gone, and leaving it
    /// puts a file named for this list in a folder where the list does not exist.
    /// </summary>
    /// <returns>The running test.</returns>
    [Fact]
    public async Task TheRemovalTakesAStagedWriteWithIt()
    {
        MakeTheSharedList();

        var staged = Store().SharedListPath + WatchlistDocumentStore.PendingSuffix;

        File.WriteAllText(staged, "{}");

        var controller = ControllerOn(AServerThatOffersASharedList());

        Assert.IsType<NoContentResult>(await controller.RemoveSharedListFor(callerIsAnAdministrator: true, CancellationToken.None).ConfigureAwait(true));

        Assert.False(File.Exists(staged));
        Assert.Empty(FilesInTheDataFolder());
    }

    /// <summary>
    /// #301'S FIRST CONDITION. The shared list is projected into one playlist every
    /// user of the server may see, and a removal takes that playlist with it. Left
    /// behind it would stand on the server holding what the list held at the moment it
    /// went, open to everybody, and no later pass would reach it - the record naming
    /// which playlist it is is the thing being removed.
    /// </summary>
    /// <returns>The running test.</returns>
    [Fact]
    public async Task TheRemovalTakesThePlaylistWithIt()
    {
        var server = AServerHoldingTheProjectedPlaylist();
        var controller = ControllerOn(AServerThatOffersASharedList(), server);

        Assert.IsType<NoContentResult>(
            await controller.RemoveSharedListFor(callerIsAnAdministrator: true, CancellationToken.None)
                .ConfigureAwait(true));

        Assert.False(Store().ReadShared().Exists);
        Assert.Empty(server.PlaylistsOf(TheOwner));
        Assert.Contains("delete " + ThePlaylist, server.Calls, StringComparer.Ordinal);
    }

    /// <summary>
    /// #301'S FOURTH CONDITION, and the case that needs this most. Turning the switch
    /// off leaves the record and the playlist alone deliberately, so that turning it on
    /// again picks the same playlist up. A record REMOVED while the switch is off is the
    /// one case where nothing will ever come back to tidy up, so the removal does not
    /// consult the switch.
    /// </summary>
    /// <returns>The running test.</returns>
    [Fact]
    public async Task ARecordRemovedWhileTheProjectionIsOffStillTakesItsPlaylist()
    {
        var server = AServerHoldingTheProjectedPlaylist();
        var controller = ControllerOn(new PluginConfiguration { SharedListEnabled = false }, server);

        Assert.IsType<NoContentResult>(
            await controller.RemoveSharedListFor(callerIsAnAdministrator: true, CancellationToken.None)
                .ConfigureAwait(true));

        Assert.False(Store().ReadShared().Exists);
        Assert.Empty(server.PlaylistsOf(TheOwner));
    }

    /// <summary>
    /// A list nothing ever projected asks the server nothing. There is no identifier to
    /// ask about, and a removal that went looking anyway would be guessing at one.
    /// </summary>
    /// <returns>The running test.</returns>
    [Fact]
    public async Task AListThatWasNeverProjectedAsksTheServerNothing()
    {
        MakeTheSharedList();

        var server = new APlaylistServerOf();
        var controller = ControllerOn(AServerThatOffersASharedList(), server);

        Assert.IsType<NoContentResult>(
            await controller.RemoveSharedListFor(callerIsAnAdministrator: true, CancellationToken.None)
                .ConfigureAwait(true));

        Assert.False(Store().ReadShared().Exists);
        Assert.Empty(server.Calls);
    }

    /// <summary>
    /// A playlist somebody already deleted from a client is a server that is already in
    /// the state the caller asked for. The record goes, nothing is refused, and the line
    /// that is logged says which of the two happened.
    /// </summary>
    /// <returns>The running test.</returns>
    [Fact]
    public async Task APlaylistThatIsAlreadyGoneIsNotAFailedRemoval()
    {
        RememberAProjectionOf(ThePlaylist);

        var log = new RecordingControllerLogger();
        var controller = ControllerOn(AServerThatOffersASharedList(), new APlaylistServerOf(), log);

        Assert.IsType<NoContentResult>(
            await controller.RemoveSharedListFor(callerIsAnAdministrator: true, CancellationToken.None)
                .ConfigureAwait(true));

        Assert.False(Store().ReadShared().Exists);
        Assert.Equal(
            "Information Removed the shared watchlist. The playlist " + ThePlaylist + " it was projected into was already gone from this server.",
            Assert.Single(log.Lines),
            StringComparer.Ordinal);
    }

    /// <summary>
    /// #301'S SECOND CONDITION. A server whose playlists cannot be reached still loses
    /// the record, because a record that cannot be deleted is worse than a playlist that
    /// outlives one: every endpoint on this surface answers from the record, so a
    /// removal that refused here would leave every user reading and writing a list an
    /// administrator has taken away. What it leaves is named in the log.
    /// </summary>
    /// <returns>The running test.</returns>
    [Fact]
    public async Task AServerThatCannotRemoveThePlaylistStillLosesTheRecord()
    {
        RememberAProjectionOf(ThePlaylist);

        var log = new RecordingControllerLogger();
        var controller = ControllerOn(AServerThatOffersASharedList(), new APlaylistServerThatRefuses(), log);

        Assert.IsType<NoContentResult>(
            await controller.RemoveSharedListFor(callerIsAnAdministrator: true, CancellationToken.None)
                .ConfigureAwait(true));

        Assert.False(Store().ReadShared().Exists);
        Assert.Empty(FilesInTheDataFolder());

        var line = Assert.Single(log.Lines);

        Assert.StartsWith("Error Removed the shared watchlist, but the playlist ", line, StringComparison.Ordinal);
        Assert.Contains(ThePlaylist.ToString(), line, StringComparison.Ordinal);
        Assert.Contains(TheOwner.ToString(), line, StringComparison.Ordinal);
        Assert.Contains("has to be removed by hand", line, StringComparison.Ordinal);
    }

    /// <summary>
    /// A record this build will not read carries no projection this can see, so there is
    /// no playlist to name and none is removed. The record still goes, and the line that
    /// is logged says what was left rather than reporting a clean removal.
    /// </summary>
    /// <returns>The running test.</returns>
    [Fact]
    public async Task ARecordThisBuildCannotReadLosesTheRecordAndSaysWhatItLeft()
    {
        var server = AServerHoldingTheProjectedPlaylist();

        FromTheFuture();

        var log = new RecordingControllerLogger();
        var controller = ControllerOn(AServerThatOffersASharedList(), server, log);

        Assert.IsType<NoContentResult>(
            await controller.RemoveSharedListFor(callerIsAnAdministrator: true, CancellationToken.None)
                .ConfigureAwait(true));

        Assert.False(Store().ReadShared().Exists);
        Assert.Empty(server.Calls);
        Assert.Single(server.PlaylistsOf(TheOwner));
        Assert.Equal(
            "Warning Removed the shared watchlist record without reading it, so any playlist it was projected into is left on this server and has to be removed by hand.",
            Assert.Single(log.Lines),
            StringComparer.Ordinal);
    }

    /// <summary>
    /// Both endpoints ask the server's own elevation policy rather than deciding for
    /// themselves who is an administrator, and this reads which question was asked
    /// rather than only what was done with the answer.
    /// </summary>
    /// <returns>The running test.</returns>
    [Fact]
    public async Task BothEndpointsAskTheServersElevationPolicy()
    {
        var server = AuthorisationAnswering.Yes();
        var controller = ControllerOn(AServerThatOffersASharedList(), AsRequestFrom(AnAdministrator), server);

        await controller.CreateSharedWatchlist().ConfigureAwait(true);
        await controller.RemoveSharedWatchlist().ConfigureAwait(true);

        Assert.Equal(
            [Policies.RequiresElevation, Policies.RequiresElevation],
            server.Asked);
    }

    /// <summary>
    /// A request carrying no identity this plugin can read is refused before the server
    /// is asked anything, on both endpoints.
    /// </summary>
    /// <returns>The running test.</returns>
    [Fact]
    public async Task ARequestWithNoIdentityIsRefusedBeforeTheServerIsAsked()
    {
        var server = AuthorisationAnswering.Yes();
        var controller = ControllerOn(AServerThatOffersASharedList(), AsRequestFrom(null), server);

        Assert.IsType<UnauthorizedResult>(await controller.CreateSharedWatchlist().ConfigureAwait(true));
        Assert.IsType<UnauthorizedResult>(await controller.RemoveSharedWatchlist().ConfigureAwait(true));

        Assert.Empty(server.Asked);
        Assert.Empty(FilesInTheDataFolder());
    }

    /// <summary>
    /// THIS SAID THE ONLY PROJECTION TARGET IS A USER'S OWN LIST, AND THE DAY IT WENT RED
    /// IS THE DAY IT WAS FOR. It stood in for the fifth condition of #84 - a server where
    /// nobody made a shared list gets no shared playlist - by reading off the assembly
    /// that no target for one existed. One exists now.
    ///
    /// The condition it stood in for is proven where it belongs, by counting the calls a
    /// pass makes on a server with no shared list, in `SharedProjectionTests`. What is
    /// left here is the set itself, which is still worth reading off the assembly: a
    /// third kind of target is a third answer to who may see a playlist, and it should
    /// turn this red.
    /// </summary>
    [Fact]
    public void TheProjectionTargetsThisPluginBuildsAreTheTwoKindsOfList()
    {
        Assert.Equal(
            ["SharedProjectionTarget", "UserProjectionTarget"],
            ProjectionTargetsIn(PluginUnderTest.Assembly.GetTypes()));
    }

    /// <summary>
    /// The bite. A reader returning nothing whatever it was handed would pass the test
    /// above over a plugin that projects the shared list, so it is watched naming a
    /// target of the shape a shared projection would arrive in.
    /// </summary>
    [Fact]
    public void TheReaderNamesATargetSomebodyMightAdd()
    {
        Assert.Equal(
            ["ASharedProjectionTargetSomebodyMightAdd", "SharedProjectionTarget", "UserProjectionTarget"],
            ProjectionTargetsIn([.. PluginUnderTest.Assembly.GetTypes(), typeof(ASharedProjectionTargetSomebodyMightAdd)]));
    }

    private static IReadOnlyList<string> ProjectionTargetsIn(IReadOnlyList<Type> types) => types
        .Where(t => !t.IsAbstract && !t.IsInterface && typeof(IProjectionTarget).IsAssignableFrom(t))
        .Select(t => t.Name)
        .OrderBy(name => name, StringComparer.Ordinal)
        .ToList();

    private static ControllerContext AsRequestFrom(Guid? userId)
    {
        var identity = new ClaimsIdentity();

        if (userId is not null)
        {
            identity.AddClaim(new Claim(CallingUser.Claim, userId.Value.ToString()));
        }

        return new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(identity) },
        };
    }

    private static PluginConfiguration AServerThatOffersASharedList() =>
        new() { SharedListEnabled = true };

    private static Guid Item(int n) => Guid.Parse(
        string.Format(CultureInfo.InvariantCulture, "00000000-0000-0000-0000-{0:D12}", n));

    private static WatchlistEntry AnEntryAddedBy(int n, Guid whoAddedIt) => new()
    {
        ItemId = Item(n),
        Kind = WatchlistItemKind.Movie,
        AddedAt = WhenItWasAdded,
        Source = WatchlistEntrySource.Api,
        AddedBy = whoAddedIt,
    };

    private static int StatusOf(ActionResult result) => result switch
    {
        StatusCodeResult status => status.StatusCode,
        _ => throw new InvalidOperationException("This result carries no status code: " + result.GetType().Name),
    };

    private WatchlistDocumentStore Store() => new(DataFolder);

    private IReadOnlyList<string> FilesInTheDataFolder() => Directory.Exists(DataFolder)
        ? [.. Directory.GetFiles(DataFolder).Select(file => Path.GetFileName(file)!).OrderBy(name => name, StringComparer.Ordinal)]
        : [];

    private void MakeTheSharedList() =>
        Store().WriteShared(WatchlistDocumentStore.EmptyShared(TheList, TheOwner));

    private void StoreShared(params WatchlistEntry[] entries) =>
        Store().WriteShared(new SharedWatchlistDocument
        {
            SchemaVersion = SharedWatchlistDocument.CurrentSchemaVersion,
            ListId = TheList,
            OwnerUserId = TheOwner,
            Entries = entries,
        });

    private WatchlistController ControllerOn(PluginConfiguration configuration) =>
        ControllerOn(configuration, new APlaylistServerOf());

    private WatchlistController ControllerOn(
        PluginConfiguration configuration,
        IPlaylistGateway playlists) =>
        ControllerOn(configuration, playlists, NullLogger<WatchlistController>.Instance);

    private WatchlistController ControllerOn(
        PluginConfiguration configuration,
        IPlaylistGateway playlists,
        ILogger<WatchlistController> logger) =>
        ControllerOn(configuration, AsRequestFrom(AnAdministrator), AuthorisationAnswering.Yes(), playlists, logger);

    private WatchlistController ControllerOn(
        PluginConfiguration configuration,
        ControllerContext context) =>
        ControllerOn(configuration, context, AuthorisationAnswering.Yes());

    private WatchlistController ControllerOn(
        PluginConfiguration configuration,
        ControllerContext context,
        AuthorisationAnswering server) =>
        ControllerOn(configuration, context, server, new APlaylistServerOf());

    private WatchlistController ControllerOn(
        PluginConfiguration configuration,
        ControllerContext context,
        AuthorisationAnswering server,
        IPlaylistGateway playlists) =>
        ControllerOn(configuration, context, server, playlists, NullLogger<WatchlistController>.Instance);

    private WatchlistController ControllerOn(
        PluginConfiguration configuration,
        ControllerContext context,
        AuthorisationAnswering server,
        IPlaylistGateway playlists,
        ILogger<WatchlistController> logger) => new(
        Store(),
        new DescribesNothing(),
        configuration,
        new StoppedClock(WhenItWasAdded),
        server,
        playlists,
        logger)
    {
        ControllerContext = context,
    };

    /// <summary>
    /// The state a server is in once the shared list has been projected: a record that
    /// remembers one playlist, and a server holding that playlist for the list's owner.
    /// </summary>
    /// <returns>The server.</returns>
    private APlaylistServerOf AServerHoldingTheProjectedPlaylist()
    {
        RememberAProjectionOf(ThePlaylist);

        var server = new APlaylistServerOf();

        server.AlreadyHolds(TheOwner, ThePlaylist, "Shared watchlist");

        return server;
    }

    /// <summary>
    /// Stamps the record with a schema version this build does not understand, which is
    /// how a record it will not read is made: the file is left alone by every read, so
    /// what a caller gets back is a result that is not available rather than a document.
    /// </summary>
    private void FromTheFuture()
    {
        var path = Store().SharedListPath;
        var text = File.ReadAllText(path);

        File.WriteAllText(
            path,
            text.Replace(
                "\"SchemaVersion\": " + SharedWatchlistDocument.CurrentSchemaVersion.ToString(CultureInfo.InvariantCulture),
                "\"SchemaVersion\": 9999",
                StringComparison.Ordinal));
    }

    private void RememberAProjectionOf(Guid playlistId)
    {
        MakeTheSharedList();

        Assert.True(Store().SetSharedProjection(new WatchlistProjectionState
        {
            PlaylistId = playlistId,
            LastNameWritten = "Shared watchlist",
            ProjectedItemIds = [],
            WrittenAt = WhenItWasAdded,
        }));
    }

    /// <summary>
    /// A seam that cannot reach the server's playlists. What a server does when its
    /// library cannot be written is not something this plugin enumerates, so the fake
    /// throws the plainest thing there is: what the test is about is that the removal
    /// survives an exception rather than that it survives one particular exception.
    /// </summary>
    private sealed class APlaylistServerThatRefuses : IPlaylistGateway
    {
        public bool CanInsertAtAPosition => false;

        public IReadOnlyList<ProjectedPlaylist> PlaylistsOf(Guid userId) => [];

        public IReadOnlyList<ProjectedPlaylistEntry> EntriesOf(Guid playlistId, Guid userId) => [];

        public Task<Guid> CreateAsync(Guid userId, string name, CancellationToken cancellationToken) =>
            throw new InvalidOperationException(Unreachable);

        public Task RenameAsync(Guid playlistId, Guid userId, string name, CancellationToken cancellationToken) =>
            throw new InvalidOperationException(Unreachable);

        public Task AddAsync(Guid playlistId, Guid userId, IReadOnlyCollection<Guid> itemIds, int? position, CancellationToken cancellationToken) =>
            throw new InvalidOperationException(Unreachable);

        public Task RemoveAsync(Guid playlistId, IReadOnlyCollection<string> entryIds, CancellationToken cancellationToken) =>
            throw new InvalidOperationException(Unreachable);

        public Task<bool> DeleteAsync(Guid playlistId, Guid ownerUserId, CancellationToken cancellationToken) =>
            throw new InvalidOperationException(Unreachable);

        public bool IsOpenToEveryone(Guid playlistId, Guid ownerUserId) => false;

        public Task OpenToEveryoneAsync(Guid playlistId, Guid ownerUserId, CancellationToken cancellationToken) =>
            throw new InvalidOperationException(Unreachable);
    }

    /// <summary>
    /// A describer for a surface that describes nothing. Neither endpoint here reads a
    /// library item, so a table of rows would be a fixture nothing looks at.
    /// </summary>
    private sealed class DescribesNothing : IWatchlistItemDescriber
    {
        public WatchlistItemDescription? Describe(Guid itemId, Guid userId) => null;
    }

    /// <summary>
    /// Not shipped. The shape a projection of the shared list would arrive in, so the
    /// reader above is watched naming one rather than assumed to.
    /// </summary>
    private sealed class ASharedProjectionTargetSomebodyMightAdd : IProjectionTarget
    {
        public Guid OwnerUserId => Guid.Empty;

        public string ConfiguredName => string.Empty;

        public IReadOnlyList<Guid> Wanted => [];

        public bool IsRecordAvailable => false;

        public bool IsOpenToEveryone => false;

        public IProjectionTarget Reread() => this;

        public WatchlistProjectionState? Remembered => null;

        public bool Remember(WatchlistProjectionState projection) => false;

        public int Adopt(IReadOnlyList<Guid> itemIds) => 0;

        public PlaylistEditsTaken TakeEdits(IReadOnlyList<Guid> rows) =>
            new() { Added = 0, Removed = 0 };
    }
}
