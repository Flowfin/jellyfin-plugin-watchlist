using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Jellyfin.Plugin.Watchlist.Api;
using Jellyfin.Plugin.Watchlist.Configuration;
using Jellyfin.Plugin.Watchlist.Projection;
using Jellyfin.Plugin.Watchlist.Store;
using MediaBrowser.Common.Api;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
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
    private static readonly Guid AnAdministrator = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid AnotherAdministrator = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid AnOrdinaryUser = Guid.Parse("33333333-3333-3333-3333-333333333333");

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
    [Fact]
    public void AnAdministratorTakesTheSharedListAway()
    {
        MakeTheSharedList();
        StoreShared(AnEntryAddedBy(1, AnOrdinaryUser));

        var controller = ControllerOn(AServerThatOffersASharedList());

        Assert.IsType<NoContentResult>(controller.RemoveSharedListFor(callerIsAnAdministrator: true));

        Assert.False(Store().ReadShared().Exists);
        Assert.Empty(FilesInTheDataFolder());
    }

    /// <summary>
    /// Removed and never there are one answer, as everywhere else on this surface. A
    /// caller asked for a server without a shared list and that is what they have.
    /// </summary>
    [Fact]
    public void RemovingAListThatIsNotThereIsTheSameAnswer()
    {
        var controller = ControllerOn(AServerThatOffersASharedList());

        Assert.IsType<NoContentResult>(controller.RemoveSharedListFor(callerIsAnAdministrator: true));

        Assert.False(Store().ReadShared().Exists);
    }

    /// <summary>
    /// The second condition of this operation: it does the same thing every time rather
    /// than depending on who asked. Two administrators, the same starting state each,
    /// and the same answer and the same folder afterwards.
    /// </summary>
    [Fact]
    public void TheRemovalDoesTheSameThingWhicheverAdministratorAsks()
    {
        MakeTheSharedList();
        StoreShared(AnEntryAddedBy(1, AnOrdinaryUser));

        var first = ControllerOn(AServerThatOffersASharedList(), AsRequestFrom(AnAdministrator));

        Assert.IsType<NoContentResult>(first.RemoveSharedListFor(callerIsAnAdministrator: true));

        var afterTheFirst = FilesInTheDataFolder();

        MakeTheSharedList();
        StoreShared(AnEntryAddedBy(1, AnOrdinaryUser));

        var second = ControllerOn(AServerThatOffersASharedList(), AsRequestFrom(AnotherAdministrator));

        Assert.IsType<NoContentResult>(second.RemoveSharedListFor(callerIsAnAdministrator: true));

        Assert.Equal(afterTheFirst, FilesInTheDataFolder());
    }

    /// <summary>
    /// A caller the server does not answer for cannot take the list away, and the list
    /// and its entries are still there afterwards.
    /// </summary>
    [Fact]
    public void ACallerWithoutElevationCannotRemoveTheListAndItStays()
    {
        MakeTheSharedList();
        StoreShared(AnEntryAddedBy(1, AnOrdinaryUser));

        var controller = ControllerOn(AServerThatOffersASharedList());

        Assert.Equal(
            StatusCodes.Status403Forbidden,
            StatusOf(controller.RemoveSharedListFor(callerIsAnAdministrator: false)));

        Assert.Equal(Item(1), Assert.Single(Store().ReadShared().Document!.Entries).ItemId);
    }

    /// <summary>
    /// A staged write left behind by a run that died between staging and committing is
    /// removed with the list. Nothing reads it once the list is gone, and leaving it
    /// puts a file named for this list in a folder where the list does not exist.
    /// </summary>
    [Fact]
    public void TheRemovalTakesAStagedWriteWithIt()
    {
        MakeTheSharedList();

        var staged = Store().SharedListPath + WatchlistDocumentStore.PendingSuffix;

        File.WriteAllText(staged, "{}");

        var controller = ControllerOn(AServerThatOffersASharedList());

        Assert.IsType<NoContentResult>(controller.RemoveSharedListFor(callerIsAnAdministrator: true));

        Assert.False(File.Exists(staged));
        Assert.Empty(FilesInTheDataFolder());
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
    /// The half of the fifth condition that has a subject. A server where nobody made a
    /// shared list has no shared playlist, and the reason is that this plugin builds
    /// exactly one kind of projection target and it is a user's own list. The set is
    /// read off the assembly rather than off a sentence, so the day a shared target is
    /// added this goes red and the condition is read again.
    /// </summary>
    [Fact]
    public void TheOnlyProjectionTargetThisPluginBuildsIsAUsersOwnList()
    {
        Assert.Equal(["UserProjectionTarget"], ProjectionTargetsIn(PluginUnderTest.Assembly.GetTypes()));
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
            ["ASharedProjectionTargetSomebodyMightAdd", "UserProjectionTarget"],
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
        ControllerOn(configuration, AsRequestFrom(AnAdministrator), AuthorisationAnswering.Yes());

    private WatchlistController ControllerOn(
        PluginConfiguration configuration,
        ControllerContext context) =>
        ControllerOn(configuration, context, AuthorisationAnswering.Yes());

    private WatchlistController ControllerOn(
        PluginConfiguration configuration,
        ControllerContext context,
        AuthorisationAnswering server) => new(
        Store(),
        new DescribesNothing(),
        configuration,
        new StoppedClock(WhenItWasAdded),
        server,
        NullLogger<WatchlistController>.Instance)
    {
        ControllerContext = context,
    };

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

        public WatchlistProjectionState? Remembered => null;

        public bool Remember(WatchlistProjectionState projection) => false;

        public int Adopt(IReadOnlyList<Guid> itemIds) => 0;

        public PlaylistEditsTaken TakeEdits(IReadOnlyList<Guid> rows) =>
            new() { Added = 0, Removed = 0 };
    }
}
