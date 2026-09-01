using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Claims;
using Jellyfin.Plugin.Watchlist.Api;
using Jellyfin.Plugin.Watchlist.Configuration;
using Jellyfin.Plugin.Watchlist.Store;
using MediaBrowser.Common.Api;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Jellyfin.Plugin.Watchlist.Tests;

/// <summary>
/// The three endpoints over the one list the whole server shares, driven by calling
/// them. No web host and no server: the controller sits over a store in a directory
/// this test owns, a describer answering from a table, a clock that does not move,
/// and an authorisation service whose answer the test fixes.
/// </summary>
/// <remarks>
/// What is different from the private list is what these are mostly about. One object
/// many people write, so an entry belongs to whoever put it there and a removal by
/// anybody else is refused; and a server can have no such list at all, which is not
/// the same as having an empty one.
/// </remarks>
public sealed class SharedWatchlistApiTests : IDisposable
{
    private static readonly Guid TheList = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid TheOwner = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly Guid AUser = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid AnotherUser = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private static readonly DateTimeOffset WhenItWasAdded =
        new(2026, 1, 2, 3, 4, 5, TimeSpan.Zero);

    private readonly TemporaryDirectory _sandbox = new("shared-watchlist-api");

    private string DataFolder => Path.Join(_sandbox.FullPath, "plugin-data");

    /// <inheritdoc />
    public void Dispose()
    {
        _sandbox.Dispose();
    }

    /// <summary>
    /// A server on which nobody has made a shared list answers that there is none,
    /// rather than answering with an empty one. A client told the list is empty would
    /// show a user a shared list that does not exist.
    /// </summary>
    [Fact]
    public void AServerWithNoSharedListIsNotAServerWithAnEmptyOne()
    {
        var controller = ControllerOver(Visible(1, AUser));

        Assert.IsType<NotFoundResult>(controller.SharedItemsFor(AUser).Result);
    }

    /// <summary>
    /// A list that exists and holds nothing is the other answer, and it is a success.
    /// </summary>
    [Fact]
    public void AListThatExistsAndHoldsNothingIsAnEmptyAnswerRatherThanARefusal()
    {
        MakeTheSharedList();

        var controller = ControllerOver(Visible(1, AUser));

        Assert.Empty(EntriesOf(controller.SharedItemsFor(AUser)));
    }

    /// <summary>
    /// The case the shared list exists for. Anybody may add, and the entry says who
    /// did.
    /// </summary>
    [Fact]
    public void AnAddRecordsWhoPutTheItemOnTheList()
    {
        MakeTheSharedList();

        var controller = ControllerOver(Visible(1, AnotherUser));

        Assert.IsType<NoContentResult>(controller.AddSharedFor(AnotherUser, Item(1)));

        var entry = Assert.Single(Stored());

        Assert.Equal(Item(1), entry.ItemId);
        Assert.Equal(AnotherUser, entry.AddedBy);
        Assert.Equal(WatchlistEntrySource.Api, entry.Source);
        Assert.Equal(WhenItWasAdded, entry.AddedAt);
    }

    /// <summary>
    /// And the read hands that back, which is the answer to question 8 on #1. A caller
    /// sees who asked for a title, and it is also how they know which entries they may
    /// take off again.
    /// </summary>
    [Fact]
    public void TheReadSaysWhoAddedEachEntry()
    {
        MakeTheSharedList();
        StoreShared(EntryAddedBy(1, AUser), EntryAddedBy(2, AnotherUser));

        var controller = ControllerOver(Visible(1, AUser), Visible(2, AUser));

        var entries = EntriesOf(controller.SharedItemsFor(AUser));

        Assert.Equal([AUser, AnotherUser], entries.Select(entry => entry.AddedBy));
    }

    /// <summary>
    /// A private list has one writer, so its answer carries no attribution at all
    /// rather than an empty one. The private surface is exactly what it was.
    /// </summary>
    [Fact]
    public void APrivateListSaysNothingAboutWhoAddedAnEntry()
    {
        var controller = ControllerOver(Visible(1, AUser));

        controller.AddFor(AUser, Item(1));

        Assert.Null(Assert.Single(EntriesOf(controller.ItemsFor(AUser))).AddedBy);
    }

    /// <summary>
    /// The repeat, by somebody else. The list holds one entry and it is still the
    /// first person's, because taking their name off a title they asked for is the
    /// opposite of what the attribution is for.
    /// </summary>
    [Fact]
    public void AddingWhatSomebodyElseAlreadyAddedLeavesTheirEntryAlone()
    {
        MakeTheSharedList();

        var controller = ControllerOver(Visible(1, AUser), Visible(1, AnotherUser));

        Assert.IsType<NoContentResult>(controller.AddSharedFor(AUser, Item(1)));
        Assert.IsType<NoContentResult>(controller.AddSharedFor(AnotherUser, Item(1)));

        Assert.Equal(AUser, Assert.Single(Stored()).AddedBy);
    }

    /// <summary>
    /// Adding to a server that has no shared list is refused, and the refusal is the
    /// same answer as an item that is not there. Making the list is a decision
    /// somebody takes, and an add is not where it gets taken.
    /// </summary>
    [Fact]
    public void AddingWhereThereIsNoSharedListIsRefusedAndMakesNoList()
    {
        var controller = ControllerOver(Visible(1, AUser));

        Assert.IsType<NotFoundResult>(controller.AddSharedFor(AUser, Item(1)));
        Assert.False(File.Exists(new WatchlistDocumentStore(DataFolder).SharedListPath));
    }

    /// <summary>
    /// An item this caller cannot see is refused with the answer an item that is not
    /// there gets, so the shared list cannot be used to learn what sits in a library
    /// the caller has no access to.
    /// </summary>
    [Fact]
    public void AddingAnItemTheCallerCannotSeeAnswersLikeAnItemThatIsNotThere()
    {
        MakeTheSharedList();

        var invisible = ControllerOver(Visible(1, AnotherUser));
        var absent = ControllerOver();

        Assert.IsType<NotFoundResult>(invisible.AddSharedFor(AUser, Item(1)));
        Assert.IsType<NotFoundResult>(absent.AddSharedFor(AUser, Item(1)));
        Assert.Empty(Stored());
    }

    /// <summary>
    /// And an item of a kind a watchlist does not hold is refused with the code that
    /// says so, since the caller may see it and the refusal discloses nothing.
    /// </summary>
    [Fact]
    public void AddingSomethingAWatchlistDoesNotHoldIsRefused()
    {
        MakeTheSharedList();

        var controller = ControllerOver(
            AServerThatOffersASharedList(),
            new DescriberFor((Item(1), AUser, Description(WatchlistItemKind.Other))));

        Assert.IsType<BadRequestResult>(controller.AddSharedFor(AUser, Item(1)));
        Assert.Empty(Stored());
    }

    /// <summary>
    /// The cap is the shared list's own, from the settings page, and a refusal writes
    /// nothing and removes nothing.
    /// </summary>
    [Fact]
    public void AddingToAFullSharedListIsRefusedAndRemovesNothing()
    {
        MakeTheSharedList();
        StoreShared(EntryAddedBy(1, AUser));

        var controller = ControllerOver(
            new PluginConfiguration { SharedListEnabled = true, MaxEntriesInSharedList = 1 },
            Visible(2, AUser));

        Assert.IsType<ConflictResult>(controller.AddSharedFor(AUser, Item(2)));
        Assert.Equal([Item(1)], Stored().Select(entry => entry.ItemId));
    }

    /// <summary>
    /// The person who put an entry there takes it off again, without being an
    /// administrator.
    /// </summary>
    [Fact]
    public void TheUserWhoAddedAnEntryMayTakeItOff()
    {
        MakeTheSharedList();
        StoreShared(EntryAddedBy(1, AUser));

        var controller = ControllerOver(Visible(1, AUser));

        Assert.IsType<NoContentResult>(controller.RemoveSharedFor(AUser, Item(1), callerMayRemoveAnyEntry: false));
        Assert.Empty(Stored());
    }

    /// <summary>
    /// Somebody else may not, and the entry stays exactly where it was.
    /// </summary>
    [Fact]
    public void AnotherUserMayNotTakeSomebodyElsesEntryOff()
    {
        MakeTheSharedList();
        StoreShared(EntryAddedBy(1, AUser));

        var controller = ControllerOver(Visible(1, AnotherUser));

        Assert.Equal(
            StatusCodes.Status403Forbidden,
            StatusOf(controller.RemoveSharedFor(AnotherUser, Item(1), callerMayRemoveAnyEntry: false)));

        Assert.Equal(AUser, Assert.Single(Stored()).AddedBy);
    }

    /// <summary>
    /// An administrator may, which is the other half of the answer to question 7 on
    /// #1. The same call and the same entry, with the server's answer to the elevation
    /// question the only thing that differs.
    /// </summary>
    [Fact]
    public void AnAdministratorMayTakeAnybodysEntryOff()
    {
        MakeTheSharedList();
        StoreShared(EntryAddedBy(1, AUser));

        var controller = ControllerOver(Visible(1, AnotherUser));

        Assert.IsType<NoContentResult>(controller.RemoveSharedFor(AnotherUser, Item(1), callerMayRemoveAnyEntry: true));
        Assert.Empty(Stored());
    }

    /// <summary>
    /// An entry on the shared list carrying no attribution belongs to nobody, so no
    /// ordinary user may take it off and an administrator may.
    /// </summary>
    /// <remarks>
    /// The endpoints cannot make such an entry: every add through them records the
    /// caller. It arrives from a hand-edited document, from a restore, or from a route
    /// somebody writes later, and the rule has to answer it either way. Refusing the
    /// ordinary user is the safe direction of the two, because an entry nobody claims
    /// is exactly the one a stranger should not be able to take off a list everybody
    /// sees.
    /// </remarks>
    [Fact]
    public void AnEntryThatNamesNobodyIsRemovableOnlyByAnAdministrator()
    {
        MakeTheSharedList();
        StoreShared(EntryAddedBy(1, AUser) with { AddedBy = null });

        var controller = ControllerOver(Visible(1, AUser));

        Assert.Equal(
            StatusCodes.Status403Forbidden,
            StatusOf(controller.RemoveSharedFor(AUser, Item(1), callerMayRemoveAnyEntry: false)));

        Assert.Single(Stored());

        Assert.IsType<NoContentResult>(controller.RemoveSharedFor(AUser, Item(1), callerMayRemoveAnyEntry: true));
        Assert.Empty(Stored());
    }

    /// <summary>
    /// Removing something that is not on the list is the same answer as removing
    /// something that was, exactly as it is on a private list.
    /// </summary>
    [Fact]
    public void RemovingSomethingThatWasNeverThereReportsSuccess()
    {
        MakeTheSharedList();

        var controller = ControllerOver();

        Assert.IsType<NoContentResult>(controller.RemoveSharedFor(AUser, Item(1), callerMayRemoveAnyEntry: false));
    }

    /// <summary>
    /// Removing on a server with no shared list says there is none.
    /// </summary>
    [Fact]
    public void RemovingWhereThereIsNoSharedListSaysSo()
    {
        var controller = ControllerOver();

        Assert.IsType<NotFoundResult>(controller.RemoveSharedFor(AUser, Item(1), callerMayRemoveAnyEntry: true));
    }

    /// <summary>
    /// A shared list this plugin will not read is unavailable on all three endpoints
    /// rather than empty on one of them, and the file is left exactly as it was.
    /// </summary>
    [Fact]
    public void AnUnreadableSharedListIsUnavailableOnEveryEndpointAndIsNotWrittenOver()
    {
        var unreadable = UnreadableSharedList();
        var controller = ControllerOver(Visible(1, AUser));

        Assert.Equal(
            StatusCodes.Status503ServiceUnavailable,
            StatusOf(controller.SharedItemsFor(AUser).Result!));

        Assert.Equal(
            StatusCodes.Status503ServiceUnavailable,
            StatusOf(controller.AddSharedFor(AUser, Item(1))));

        Assert.Equal(
            StatusCodes.Status503ServiceUnavailable,
            StatusOf(controller.RemoveSharedFor(AUser, Item(1), callerMayRemoveAnyEntry: true)));

        Assert.Equal(
            unreadable,
            File.ReadAllText(new WatchlistDocumentStore(DataFolder).SharedListPath),
            StringComparer.Ordinal);
    }

    /// <summary>
    /// An entry whose item this caller cannot see is left out of the answer and stays
    /// in the document, so one shared list gives two callers two answers and neither
    /// of them learns about the other's libraries.
    /// </summary>
    [Fact]
    public void AnEntryTheCallerCannotSeeIsLeftOutAndStaysOnTheList()
    {
        MakeTheSharedList();
        StoreShared(EntryAddedBy(1, AUser), EntryAddedBy(2, AnotherUser));

        var controller = ControllerOver(Visible(1, AUser), Visible(1, AnotherUser), Visible(2, AnotherUser));

        Assert.Equal([Item(1)], EntriesOf(controller.SharedItemsFor(AUser)).Select(entry => entry.ItemId));
        Assert.Equal([Item(1), Item(2)], EntriesOf(controller.SharedItemsFor(AnotherUser)).Select(entry => entry.ItemId));
        Assert.Equal(2, Stored().Count);
    }

    /// <summary>
    /// A request with no identity this plugin will use is refused by all three routes
    /// before any of them reaches a list.
    /// </summary>
    [Fact]
    public async System.Threading.Tasks.Task ARequestWithNoIdentityIsRefusedByAllThreeRoutes()
    {
        MakeTheSharedList();

        var controller = ControllerOver(Visible(1, AUser));
        controller.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() };

        Assert.IsType<UnauthorizedResult>(controller.GetSharedWatchlistItems().Result);
        Assert.IsType<UnauthorizedResult>(controller.AddSharedWatchlistItem(Item(1)));
        Assert.IsType<UnauthorizedResult>(await controller.RemoveSharedWatchlistItem(Item(1)));
        Assert.Empty(Stored());
    }

    /// <summary>
    /// The routes with a request that does carry an identity, which is what exercises
    /// the lines that read the caller out of one.
    /// </summary>
    [Fact]
    public async System.Threading.Tasks.Task ARequestCarryingAnIdentityReachesTheSharedList()
    {
        MakeTheSharedList();

        var controller = ControllerOver(Visible(1, AUser));
        controller.ControllerContext = AsRequestFrom(AUser);

        Assert.IsType<NoContentResult>(controller.AddSharedWatchlistItem(Item(1)));
        Assert.Single(Stored());

        Assert.Single(EntriesOf(controller.GetSharedWatchlistItems()));

        Assert.IsType<NoContentResult>(await controller.RemoveSharedWatchlistItem(Item(1)));
        Assert.Empty(Stored());
    }

    /// <summary>
    /// The removal route asks the server whether the caller is an administrator, and it
    /// asks by naming the server's own elevation policy rather than by deciding for
    /// itself what an administrator is. The question is asserted, not only the answer.
    /// </summary>
    [Fact]
    public async System.Threading.Tasks.Task TheRemovalRouteAsksTheServersOwnElevationPolicy()
    {
        MakeTheSharedList();
        StoreShared(EntryAddedBy(1, AUser));

        var server = AuthorisationAnswering.Yes();
        var controller = ControllerOver(AServerThatOffersASharedList(), server, Visible(1, AnotherUser));
        controller.ControllerContext = AsRequestFrom(AnotherUser);

        Assert.IsType<NoContentResult>(await controller.RemoveSharedWatchlistItem(Item(1)));
        Assert.Equal([Policies.RequiresElevation], server.Asked);
        Assert.Empty(Stored());
    }

    /// <summary>
    /// The same call with the server answering no, which is the one change between the
    /// two and the reason the answer above is the server's rather than this plugin's.
    /// </summary>
    [Fact]
    public async System.Threading.Tasks.Task TheSameRemovalIsRefusedWhenTheServerSaysTheCallerIsNotElevated()
    {
        MakeTheSharedList();
        StoreShared(EntryAddedBy(1, AUser));

        var server = AuthorisationAnswering.No();
        var controller = ControllerOver(AServerThatOffersASharedList(), server, Visible(1, AnotherUser));
        controller.ControllerContext = AsRequestFrom(AnotherUser);

        Assert.Equal(
            StatusCodes.Status403Forbidden,
            StatusOf(await controller.RemoveSharedWatchlistItem(Item(1))));

        Assert.Single(Stored());
    }

    /// <summary>
    /// The store's own answers, read directly. The endpoint maps them to codes and the
    /// mapping is above; these are the outcomes it maps from.
    /// </summary>
    [Fact]
    public void TheStoreSeparatesTheFiveWaysARemovalCanEnd()
    {
        var store = new WatchlistDocumentStore(DataFolder);

        Assert.Equal(
            SharedWatchlistRemoveOutcome.NoSharedList,
            store.RemoveShared(Item(1), AUser, callerMayRemoveAnyEntry: true).Outcome);

        MakeTheSharedList();
        StoreShared(EntryAddedBy(1, AUser));

        Assert.Equal(
            SharedWatchlistRemoveOutcome.NotOnTheList,
            store.RemoveShared(Item(2), AUser, callerMayRemoveAnyEntry: false).Outcome);

        Assert.Equal(
            SharedWatchlistRemoveOutcome.RefusedNotTheirEntry,
            store.RemoveShared(Item(1), AnotherUser, callerMayRemoveAnyEntry: false).Outcome);

        var removed = store.RemoveShared(Item(1), AUser, callerMayRemoveAnyEntry: false);

        Assert.Equal(SharedWatchlistRemoveOutcome.Removed, removed.Outcome);
        Assert.Equal(0, removed.EntryCount);
        Assert.True(removed.IsOffTheList);
    }

    /// <summary>
    /// And the unreadable case, which the four above cannot reach because they all read
    /// a list this plugin wrote.
    /// </summary>
    [Fact]
    public void AnUnreadableSharedListIsItsOwnRemovalOutcome()
    {
        UnreadableSharedList();

        var store = new WatchlistDocumentStore(DataFolder);
        var result = store.RemoveShared(Item(1), AUser, callerMayRemoveAnyEntry: true);

        Assert.Equal(SharedWatchlistRemoveOutcome.RefusedListUnavailable, result.Outcome);
        Assert.False(result.IsOffTheList);
    }

    /// <summary>
    /// The add's own new outcome says what it is, in the sentence an operator reads.
    /// </summary>
    [Fact]
    public void AnAddToAServerWithNoSharedListSaysWhatWentWrong()
    {
        var store = new WatchlistDocumentStore(DataFolder);
        var result = store.AddShared(EntryAddedBy(1, AUser), maxEntriesInSharedList: 10);

        Assert.Equal(WatchlistAddOutcome.RefusedNoSharedList, result.Outcome);
        Assert.False(result.IsOnTheList);
        Assert.Equal(
            "Refused: this server has no shared list, so nothing was added to one.",
            result.Describe(),
            StringComparer.Ordinal);
    }

    /// <summary>
    /// Nothing is written for an entry that is not there.
    /// </summary>
    [Fact]
    public void NothingIsAddedForAnEntryThatIsNotThere()
    {
        var store = new WatchlistDocumentStore(DataFolder);

        Assert.Throws<ArgumentNullException>(() => store.AddShared(null!, maxEntriesInSharedList: 10));
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

    private static Guid Item(int n) => Guid.Parse(
        string.Format(CultureInfo.InvariantCulture, "00000000-0000-0000-0000-{0:D12}", n));

    private static WatchlistItemDescription Description(WatchlistItemKind kind) =>
        new() { Name = "An item", Kind = kind };

    private static WatchlistEntry EntryAddedBy(int n, Guid whoAddedIt) => new()
    {
        ItemId = Item(n),
        Kind = WatchlistItemKind.Movie,
        AddedAt = WhenItWasAdded,
        Source = WatchlistEntrySource.Api,
        AddedBy = whoAddedIt,
    };

    private static (Guid ItemId, Guid UserId, WatchlistItemDescription Description) Visible(int n, Guid userId) =>
        (Item(n), userId, Description(WatchlistItemKind.Movie));

    private static int StatusOf(ActionResult result) => result switch
    {
        StatusCodeResult status => status.StatusCode,
        _ => throw new InvalidOperationException("This result carries no status code: " + result.GetType().Name),
    };

    /// <summary>
    /// The settings a server serving a shared list is in. Every route over the list's
    /// CONTENTS reads this switch since #277 and answers as though there were no list
    /// while it says no, so a fixture leaving it at its default would be driving the
    /// closed surface rather than the open one.
    /// </summary>
    /// <returns>The settings.</returns>
    private static PluginConfiguration AServerThatOffersASharedList() =>
        new() { SharedListEnabled = true };

    private static IReadOnlyList<WatchlistEntryView> EntriesOf(
        ActionResult<IReadOnlyList<WatchlistEntryView>> result) => result.Value!;

    private WatchlistController ControllerOver(
        params (Guid ItemId, Guid UserId, WatchlistItemDescription Description)[] rows) =>
        ControllerOver(AServerThatOffersASharedList(), new DescriberFor(rows));

    private WatchlistController ControllerOver(
        PluginConfiguration configuration,
        params (Guid ItemId, Guid UserId, WatchlistItemDescription Description)[] rows) =>
        ControllerOver(configuration, new DescriberFor(rows));

    private WatchlistController ControllerOver(
        PluginConfiguration configuration,
        AuthorisationAnswering server,
        params (Guid ItemId, Guid UserId, WatchlistItemDescription Description)[] rows) =>
        ControllerOver(configuration, new DescriberFor(rows), server);

    private WatchlistController ControllerOver(
        PluginConfiguration configuration,
        IWatchlistItemDescriber describer) =>
        ControllerOver(configuration, describer, AuthorisationAnswering.No());

    private WatchlistController ControllerOver(
        PluginConfiguration configuration,
        IWatchlistItemDescriber describer,
        AuthorisationAnswering server) => new(
        new WatchlistDocumentStore(DataFolder),
        describer,
        configuration,
        new StoppedClock(WhenItWasAdded),
        server,
        new APlaylistServerOf(),
        NullLogger<WatchlistController>.Instance);

    private IReadOnlyList<WatchlistEntry> Stored() =>
        new WatchlistDocumentStore(DataFolder).ReadShared().Document?.Entries ?? [];

    private void MakeTheSharedList() =>
        new WatchlistDocumentStore(DataFolder).WriteShared(
            WatchlistDocumentStore.EmptyShared(TheList, TheOwner));

    private void StoreShared(params WatchlistEntry[] entries) =>
        new WatchlistDocumentStore(DataFolder).WriteShared(new SharedWatchlistDocument
        {
            SchemaVersion = SharedWatchlistDocument.CurrentSchemaVersion,
            ListId = TheList,
            OwnerUserId = TheOwner,
            Entries = entries,
        });

    /// <summary>
    /// A shared list on disk that this plugin will not read, which is what one written
    /// by a newer version looks like to an older one.
    /// </summary>
    /// <returns>The bytes written, so a test can prove they did not move.</returns>
    private string UnreadableSharedList()
    {
        var text = WatchlistDocumentFormat
            .Write(new SharedWatchlistDocument
            {
                SchemaVersion = SharedWatchlistDocument.CurrentSchemaVersion,
                ListId = TheList,
                OwnerUserId = TheOwner,
                Entries = [EntryAddedBy(1, AUser)],
            })
            .Replace(
                "\"SchemaVersion\": " + SharedWatchlistDocument.CurrentSchemaVersion.ToString(CultureInfo.InvariantCulture),
                "\"SchemaVersion\": " + (SharedWatchlistDocument.CurrentSchemaVersion + 1).ToString(CultureInfo.InvariantCulture),
                StringComparison.Ordinal);

        Directory.CreateDirectory(DataFolder);
        File.WriteAllText(new WatchlistDocumentStore(DataFolder).SharedListPath, text);

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
