using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Claims;
using Jellyfin.Plugin.Watchlist.Api;
using Jellyfin.Plugin.Watchlist.Configuration;
using Jellyfin.Plugin.Watchlist.Store;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Jellyfin.Plugin.Watchlist.Tests;

/// <summary>
/// The two endpoints the whole plugin exists for, driven by calling them. No web host
/// and no server: the controller is constructed over a store in a directory this test
/// owns, a describer that answers from a table, and a clock that does not move.
/// </summary>
/// <remarks>
/// Both operations have to be safe to repeat, and that is most of what is checked
/// here. A client that retries after a timeout is the ordinary case rather than the
/// exotic one, and an add that is not idempotent turns one timeout into a list holding
/// the same film twice, which the user has to repair by hand.
/// </remarks>
public sealed class WatchlistApiWriteTests : IDisposable
{
    private static readonly Guid AUser = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid AnotherUser = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private static readonly DateTimeOffset WhenItWasAdded =
        new(2026, 1, 2, 3, 4, 5, TimeSpan.Zero);

    private readonly TemporaryDirectory _sandbox = new("watchlist-api-write");

    private string DataFolder => Path.Combine(_sandbox.FullPath, "plugin-data");

    /// <inheritdoc />
    public void Dispose()
    {
        _sandbox.Dispose();
    }

    /// <summary>
    /// The case everything else is a variation of.
    /// </summary>
    [Fact]
    public void AnAddPutsTheItemOnTheListAndRecordsWhatItIs()
    {
        var controller = ControllerOver(Visible(1, WatchlistItemKind.Movie));

        Assert.IsType<NoContentResult>(controller.AddFor(AUser, Item(1)));

        var entry = Assert.Single(StoredFor(AUser));

        Assert.Equal(Item(1), entry.ItemId);
        Assert.Equal(WatchlistItemKind.Movie, entry.Kind);
        Assert.Equal(WatchlistEntrySource.Api, entry.Source);
        Assert.Equal(WhenItWasAdded, entry.AddedAt);
    }

    /// <summary>
    /// The repeat. One entry and the same answer, so a client that could not tell
    /// whether its first call arrived does not have to read the list to find out.
    /// </summary>
    [Fact]
    public void AddingTheSameItemTwiceLeavesOneEntryAndReportsSuccess()
    {
        var controller = ControllerOver(Visible(1, WatchlistItemKind.Movie));

        Assert.IsType<NoContentResult>(controller.AddFor(AUser, Item(1)));
        Assert.IsType<NoContentResult>(controller.AddFor(AUser, Item(1)));

        Assert.Single(StoredFor(AUser));
    }

    /// <summary>
    /// And the entry the repeat did not write is the first one. A second write would
    /// move the instant the list says the item was added, which is the field a user
    /// sorts by.
    /// </summary>
    [Fact]
    public void ARepeatedAddDoesNotRestampTheEntry()
    {
        var clock = new StoppedClock(WhenItWasAdded);
        var controller = ControllerOver(Visible(1, WatchlistItemKind.Movie), clock);

        controller.AddFor(AUser, Item(1));
        clock.Advance(TimeSpan.FromDays(30));
        controller.AddFor(AUser, Item(1));

        Assert.Equal(WhenItWasAdded, Assert.Single(StoredFor(AUser)).AddedAt);
    }

    /// <summary>
    /// The removal, and its repeat. Taking something off that is not there is not an
    /// error: the caller asked for the list not to hold it and the list does not.
    /// </summary>
    [Fact]
    public void RemovingTwiceReportsSuccessBothTimesAndChangesNothingTheSecondTime()
    {
        var controller = ControllerOver(Visible(1, WatchlistItemKind.Movie));
        controller.AddFor(AUser, Item(1));

        Assert.IsType<NoContentResult>(controller.RemoveFor(AUser, Item(1)));
        Assert.Empty(StoredFor(AUser));

        Assert.IsType<NoContentResult>(controller.RemoveFor(AUser, Item(1)));
        Assert.Empty(StoredFor(AUser));
    }

    /// <summary>
    /// Removing something that was never on the list at all, which is the same answer
    /// again. A caller that could tell this apart from a real removal would be reading
    /// somebody's list by writing to it.
    /// </summary>
    [Fact]
    public void RemovingSomethingThatWasNeverThereReportsSuccess()
    {
        var controller = ControllerOver(Visible(1, WatchlistItemKind.Movie));

        Assert.IsType<NoContentResult>(controller.RemoveFor(AUser, Item(99)));
        Assert.Empty(StoredFor(AUser));
    }

    /// <summary>
    /// A removal asks the library nothing, which is what makes the entry a user most
    /// wants to remove removable. An item deleted from the library resolves for
    /// nobody, and it still comes off the list.
    /// </summary>
    [Fact]
    public void AnEntryWhoseItemHasLeftTheLibraryCanStillBeRemoved()
    {
        var controller = ControllerOver(Visible(1, WatchlistItemKind.Movie));
        controller.AddFor(AUser, Item(1));

        var afterTheItemWentAway = ControllerOver(new DescriberFor());

        Assert.IsType<NoContentResult>(afterTheItemWentAway.RemoveFor(AUser, Item(1)));
        Assert.Empty(StoredFor(AUser));
    }

    /// <summary>
    /// The first refusal. An item this caller cannot see is refused, and so is an item
    /// that is not there, with the same answer.
    /// </summary>
    [Fact]
    public void AddingAnItemTheCallerCannotSeeIsRefusedAndWritesNothing()
    {
        var controller = ControllerOver(new DescriberFor(
            (Item(1), AnotherUser, Description(WatchlistItemKind.Movie))));

        Assert.IsType<NotFoundResult>(controller.AddFor(AUser, Item(1)));
        Assert.Empty(StoredFor(AUser));
    }

    /// <summary>
    /// The property that refusal has to have. An item somebody else may see and an
    /// item nobody may see are the same answer, so the endpoint is not a way of asking
    /// what sits in a library the caller has no access to.
    /// </summary>
    [Fact]
    public void TheRefusalDoesNotSayWhetherTheItemExists()
    {
        var invisible = ControllerOver(new DescriberFor(
            (Item(1), AnotherUser, Description(WatchlistItemKind.Movie))));
        var absent = ControllerOver(new DescriberFor());

        Assert.Equal(
            StatusOf(invisible.AddFor(AUser, Item(1))),
            StatusOf(absent.AddFor(AUser, Item(1))));
    }

    /// <summary>
    /// The second refusal. A library holds more than a watchlist takes, and everything
    /// outside the accepted set is refused rather than recorded.
    /// </summary>
    [Fact]
    public void AddingAKindTheListDoesNotTakeIsRefusedAndWritesNothing()
    {
        var controller = ControllerOver(Visible(1, WatchlistItemKind.Other));

        Assert.IsType<BadRequestResult>(controller.AddFor(AUser, Item(1)));
        Assert.Empty(StoredFor(AUser));
    }

    /// <summary>
    /// The near miss for that refusal. A describer that answered nothing about the kind
    /// leaves the default behind, and an entry whose kind nothing decided is worse than
    /// a refused add.
    /// </summary>
    [Fact]
    public void AnItemWhoseKindNothingDecidedIsRefused()
    {
        var controller = ControllerOver(new DescriberFor(
            (Item(1), AUser, new WatchlistItemDescription { Name = "Something" })));

        Assert.IsType<BadRequestResult>(controller.AddFor(AUser, Item(1)));
        Assert.Empty(StoredFor(AUser));
    }

    /// <summary>
    /// And the accepted set is not one that accepts nothing: each of the three kinds a
    /// watchlist is for goes on.
    /// </summary>
    /// <param name="kind">The kind under test.</param>
    [Theory]
    [InlineData(WatchlistItemKind.Movie)]
    [InlineData(WatchlistItemKind.Series)]
    [InlineData(WatchlistItemKind.Episode)]
    public void EveryAcceptedKindGoesOnTheList(WatchlistItemKind kind)
    {
        var controller = ControllerOver(Visible(1, kind));

        Assert.IsType<NoContentResult>(controller.AddFor(AUser, Item(1)));
        Assert.Equal(kind, Assert.Single(StoredFor(AUser)).Kind);
    }

    /// <summary>
    /// The set is what the endpoint reads, so it is worth reading back. Written the
    /// other way round, as a set of refusals, a kind the server adds later would go on
    /// a list without anybody deciding it should.
    /// </summary>
    [Fact]
    public void TheAcceptedKindsAreTheThreeAWatchlistIsFor()
    {
        Assert.Equal(
            [WatchlistItemKind.Episode, WatchlistItemKind.Movie, WatchlistItemKind.Series],
            AcceptedWatchlistItemKinds.All.OrderBy(kind => kind.ToString(), StringComparer.Ordinal).ToList());

        Assert.False(AcceptedWatchlistItemKinds.Accepts(WatchlistItemKind.Unknown));
        Assert.False(AcceptedWatchlistItemKinds.Accepts(WatchlistItemKind.Other));
    }

    /// <summary>
    /// A list at its cap refuses rather than dropping something to make room, and the
    /// endpoint says so with a code of its own rather than reporting success.
    /// </summary>
    [Fact]
    public void AddingToAListAtItsCapIsRefusedAndRemovesNothing()
    {
        var full = Enumerable
            .Range(1, PluginConfiguration.DefaultMaxEntriesPerUser)
            .Select(n => Entry(n))
            .ToArray();

        Store(AUser, full);

        var controller = ControllerOver(Visible(PluginConfiguration.DefaultMaxEntriesPerUser + 1, WatchlistItemKind.Movie));

        Assert.IsType<ConflictResult>(controller.AddFor(AUser, Item(PluginConfiguration.DefaultMaxEntriesPerUser + 1)));
        Assert.Equal(PluginConfiguration.DefaultMaxEntriesPerUser, StoredFor(AUser).Count);
    }

    /// <summary>
    /// An item already on a full list is still on it, which is what the repeat answer
    /// is for. Refusing here would tell a client to make room for something that is
    /// already there.
    /// </summary>
    [Fact]
    public void AnItemAlreadyOnAFullListReportsSuccessRatherThanTheCap()
    {
        var full = Enumerable
            .Range(1, PluginConfiguration.DefaultMaxEntriesPerUser)
            .Select(n => Entry(n))
            .ToArray();

        Store(AUser, full);

        var controller = ControllerOver(Visible(1, WatchlistItemKind.Movie));

        Assert.IsType<NoContentResult>(controller.AddFor(AUser, Item(1)));
        Assert.Equal(PluginConfiguration.DefaultMaxEntriesPerUser, StoredFor(AUser).Count);
    }

    /// <summary>
    /// A document this plugin refused to read is a list that exists and is unavailable.
    /// Adding to it would write over it, which is how a refusal becomes a deletion.
    /// </summary>
    [Fact]
    public void AddingToAListThatCouldNotBeReadIsRefusedAndWritesNothing()
    {
        var unreadable = UnreadableDocumentFor(AUser);
        var controller = ControllerOver(Visible(1, WatchlistItemKind.Movie));

        Assert.Equal(
            StatusCodes.Status503ServiceUnavailable,
            StatusOf(controller.AddFor(AUser, Item(1))));

        Assert.Equal(unreadable, File.ReadAllText(new WatchlistDocumentStore(DataFolder).PathFor(AUser)));
    }

    /// <summary>
    /// And the same for a removal, for the same reason.
    /// </summary>
    [Fact]
    public void RemovingFromAListThatCouldNotBeReadIsRefusedAndWritesNothing()
    {
        var unreadable = UnreadableDocumentFor(AUser);
        var controller = ControllerOver(Visible(1, WatchlistItemKind.Movie));

        Assert.Equal(
            StatusCodes.Status503ServiceUnavailable,
            StatusOf(controller.RemoveFor(AUser, Item(1))));

        Assert.Equal(unreadable, File.ReadAllText(new WatchlistDocumentStore(DataFolder).PathFor(AUser)));
    }

    /// <summary>
    /// A request with no identity this plugin will use, which is what the endpoint
    /// meets before it ever reaches a list. The server refuses an unauthenticated
    /// request ahead of this; the check stays because an endpoint that trusted that
    /// alone would be one attribute away from serving anybody.
    /// </summary>
    [Fact]
    public void ARequestWithNoIdentityIsRefusedByBothEndpoints()
    {
        var controller = ControllerOver(Visible(1, WatchlistItemKind.Movie));
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext(),
        };

        Assert.IsType<UnauthorizedResult>(controller.AddWatchlistItem(Item(1)));
        Assert.IsType<UnauthorizedResult>(controller.RemoveWatchlistItem(Item(1)));
        Assert.Empty(StoredFor(AUser));
    }

    /// <summary>
    /// The route methods with a request that does carry an identity, so the two lines
    /// that read the caller out of it are the ones this exercises. Everything below
    /// them is the same code the tests above drive directly.
    /// </summary>
    [Fact]
    public void ARequestCarryingAnIdentityReachesTheCallersOwnList()
    {
        var controller = ControllerOver(Visible(1, WatchlistItemKind.Movie));
        controller.ControllerContext = AsRequestFrom(AUser);

        Assert.IsType<NoContentResult>(controller.AddWatchlistItem(Item(1)));
        Assert.Single(StoredFor(AUser));

        Assert.IsType<NoContentResult>(controller.RemoveWatchlistItem(Item(1)));
        Assert.Empty(StoredFor(AUser));
    }

    /// <summary>
    /// One user's write reaches one user's list. The store is keyed by user and the
    /// endpoint never takes one, so this is a check that the two agree.
    /// </summary>
    [Fact]
    public void AnAddReachesOnlyTheCallersOwnList()
    {
        var controller = ControllerOver(new DescriberFor(
            (Item(1), AUser, Description(WatchlistItemKind.Movie)),
            (Item(1), AnotherUser, Description(WatchlistItemKind.Movie))));

        controller.AddFor(AUser, Item(1));

        Assert.Single(StoredFor(AUser));
        Assert.Empty(StoredFor(AnotherUser));
    }

    /// <summary>
    /// A request from one user, as the server would present it: one claim and nothing
    /// else, because the identity is the only thing this plugin reads off a request.
    /// </summary>
    /// <param name="userId">Who the request is from.</param>
    /// <returns>The context to give the controller.</returns>
    private static ControllerContext AsRequestFrom(Guid userId)
    {
        var identity = new ClaimsIdentity();
        identity.AddClaim(new Claim(CallingUser.Claim, userId.ToString()));

        return new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(identity) },
        };
    }

    private static Guid Item(int n) => Guid.Parse($"00000000-0000-0000-0000-{n:D12}");

    private static WatchlistItemDescription Description(WatchlistItemKind kind) =>
        new() { Name = "An item", Kind = kind };

    private static WatchlistEntry Entry(int n) => new()
    {
        ItemId = Item(n),
        Kind = WatchlistItemKind.Movie,
        AddedAt = WhenItWasAdded,
        Source = WatchlistEntrySource.Api,
    };

    private static int StatusOf(ActionResult result) => result switch
    {
        StatusCodeResult status => status.StatusCode,
        _ => throw new InvalidOperationException("This result carries no status code: " + result.GetType().Name),
    };

    private static DescriberFor Visible(int n, WatchlistItemKind kind) =>
        new((Item(n), AUser, Description(kind)));

    private WatchlistController ControllerOver(IWatchlistItemDescriber describer) =>
        ControllerOver(describer, new StoppedClock(WhenItWasAdded));

    private WatchlistController ControllerOver(IWatchlistItemDescriber describer, TimeProvider clock) => new(
        new WatchlistDocumentStore(DataFolder),
        describer,
        new PluginConfiguration(),
        clock,
        AuthorisationAnswering.No(),
        NullLogger<WatchlistController>.Instance);

    private IReadOnlyList<WatchlistEntry> StoredFor(Guid userId) =>
        new WatchlistDocumentStore(DataFolder).Read(userId).Document?.Entries ?? [];

    private void Store(Guid userId, params WatchlistEntry[] entries)
    {
        new WatchlistDocumentStore(DataFolder).Write(new WatchlistDocument
        {
            SchemaVersion = WatchlistDocument.CurrentSchemaVersion,
            UserId = userId,
            Entries = entries,
        });
    }

    /// <summary>
    /// A document on disk that this plugin will not read, which is what a list written
    /// by a newer version looks like to an older one.
    /// </summary>
    /// <param name="userId">Whose document.</param>
    /// <returns>The bytes written, so a test can prove they did not move.</returns>
    private string UnreadableDocumentFor(Guid userId)
    {
        var assembly = Assembly.GetExecutingAssembly();
        const string Resource = "fixture/watchlist-document-from-the-future.json";

        using var stream = assembly.GetManifestResourceStream(Resource)
            ?? throw new InvalidOperationException(
                Resource + " is not embedded in the test assembly. The assembly carries: "
                + string.Join(", ", assembly.GetManifestResourceNames()));

        using var reader = new StreamReader(stream);
        var text = reader.ReadToEnd();

        Directory.CreateDirectory(DataFolder);
        File.WriteAllText(new WatchlistDocumentStore(DataFolder).PathFor(userId), text);

        return text;
    }

    /// <summary>
    /// A describer that answers from a table and says nothing about anything else,
    /// which is what lets the endpoints be driven with no library present.
    /// </summary>
    private sealed class DescriberFor : IWatchlistItemDescriber
    {
        private readonly Dictionary<(Guid ItemId, Guid UserId), WatchlistItemDescription> _table = [];

        public DescriberFor(params (Guid ItemId, Guid UserId, WatchlistItemDescription Description)[] rows)
        {
            foreach (var row in rows)
            {
                _table[(row.ItemId, row.UserId)] = row.Description;
            }
        }

        public WatchlistItemDescription? Describe(Guid itemId, Guid userId) =>
            _table.TryGetValue((itemId, userId), out var description) ? description : null;
    }
}
