using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Watchlist.Api;
using Jellyfin.Plugin.Watchlist.Configuration;
using Jellyfin.Plugin.Watchlist.Export;
using Jellyfin.Plugin.Watchlist.Store;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Jellyfin.Plugin.Watchlist.Tests;

/// <summary>
/// What `SharedListEnabled` saying no does to a server that already has a shared list.
/// </summary>
/// <remarks>
/// <para>
/// The sequence this file exists for is one an administrator can perform in three
/// steps: turn the switch on, make the list, turn the switch off. Until #277 the page
/// then said the server offers no shared list while every user of that server could
/// still read it, add to it and take their own entries off it, and nothing refused the
/// sequence or reported it.
/// </para>
/// <para>
/// The whole sequence is driven here rather than each route being poked at in
/// isolation, because what the issue is about is the STATE those three steps leave and
/// not the behaviour of any one endpoint in it.
/// </para>
/// <para>
/// Every route is called directly, with the caller and the server's answer about them
/// already in hand, so nothing here needs a web host.
/// </para>
/// </remarks>
public sealed class SharedListSwitchTests : IDisposable
{
    private static readonly Guid TheList = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid TheOwner = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly Guid AUser = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid AFilm = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private static readonly DateTimeOffset WhenItWasAdded =
        new(2026, 1, 2, 3, 4, 5, TimeSpan.Zero);

    private readonly TemporaryDirectory _sandbox = new("shared-list-switch");

    private string DataFolder => Path.Join(_sandbox.FullPath, "plugin-data");

    /// <inheritdoc />
    public void Dispose()
    {
        _sandbox.Dispose();
    }

    /// <summary>
    /// THE SEQUENCE THE ISSUE IS ABOUT, END TO END. Switch on, list made, entry added,
    /// switch off - and every route over the list's contents then answers the way it
    /// answers on a server that has no shared list.
    /// </summary>
    [Fact]
    public void TurningTheSwitchOffClosesEveryRouteOverTheContents()
    {
        var exported = MakeTheListAndExportIt();

        var closed = ControllerWith(Off());
        var closedTransfer = TransferWith(Off());

        Assert.IsType<NotFoundResult>(closed.SharedItemsFor(AUser).Result);
        Assert.IsType<NotFoundResult>(closed.AddSharedFor(AUser, AFilm));
        Assert.IsType<NotFoundResult>(closed.RemoveSharedFor(AUser, AFilm, callerMayRemoveAnyEntry: false));
        Assert.IsType<NotFoundResult>(closedTransfer.ExportSharedFor(TheOwner, callerIsAnAdministrator: true));
        Assert.IsType<NotFoundResult>(
            closedTransfer.ImportSharedFor(TheOwner, exported, callerIsAnAdministrator: true).Result);
    }

    /// <summary>
    /// The same server with no shared list at all answers each of those routes the same
    /// way, which is what makes the sentence above literal rather than approximate. A
    /// caller that could tell a list switched off from a list never made would learn
    /// from the difference that the list is there.
    /// </summary>
    [Fact]
    public void AListSwitchedOffAndAListNeverMadeAnswerTheSame()
    {
        var exported = MakeTheListAndExportIt();

        var switchedOff = Answers(ControllerWith(Off()), TransferWith(Off()), exported);

        Assert.True(new WatchlistDocumentStore(DataFolder).DeleteShared());

        var neverMade = Answers(ControllerWith(On()), TransferWith(On()), exported);

        Assert.Equal(switchedOff, neverMade);
    }

    /// <summary>
    /// THE LIST ON DISK IS LEFT ALONE. The switch governs visibility and never
    /// existence, so nothing about the record moves while it is off and turning it back
    /// on restores exactly what was there, attribution and all.
    /// </summary>
    [Fact]
    public void NothingStoredIsTouchedAndTurningItBackOnRestoresTheList()
    {
        MakeTheListAndExportIt();

        var closed = ControllerWith(Off());

        closed.SharedItemsFor(AUser);
        closed.AddSharedFor(AUser, AFilm);
        closed.RemoveSharedFor(AUser, AFilm, callerMayRemoveAnyEntry: true);

        var stored = new WatchlistDocumentStore(DataFolder).ReadShared();

        Assert.True(stored.Exists);
        Assert.Equal(TheList, stored.Document!.ListId);
        Assert.Equal(AFilm, Assert.Single(stored.Document.Entries).ItemId);

        var back = ControllerWith(On()).SharedItemsFor(AUser);

        Assert.Equal(AFilm, Assert.Single(back.Value!).ItemId);
    }

    /// <summary>
    /// THE ADMINISTRATIVE PAIR IS NOT CLOSED, and the removal is the half that matters:
    /// it is how a list made before the switch moved is taken away, so closing it would
    /// leave an administrator with a list they cannot read and cannot remove.
    /// </summary>
    /// <returns>The running test.</returns>
    [Fact]
    public async Task TheRemovalStaysReachableWhileTheSwitchIsOff()
    {
        MakeTheListAndExportIt();

        var removed = await ControllerWith(Off())
            .RemoveSharedListFor(callerIsAnAdministrator: true, CancellationToken.None)
            .ConfigureAwait(true);

        Assert.IsType<NoContentResult>(removed);
        Assert.False(new WatchlistDocumentStore(DataFolder).ReadShared().Exists);
    }

    /// <summary>
    /// The creation keeps its own answer for its own reason. It refuses with a conflict
    /// because the switch and a record that exists would be two answers to one question;
    /// that is a different refusal from the contents routes' and is not folded into
    /// this one.
    /// </summary>
    [Fact]
    public void TheCreationStillRefusesWithAConflictRatherThanWithThisAnswer()
    {
        Assert.IsType<ConflictResult>(
            ControllerWith(Off()).CreateSharedListFor(TheOwner, callerIsAnAdministrator: true));
    }

    /// <summary>
    /// FAILS CLOSED WHEN A ROUTE IS ADDED. The routes under the shared list are read off
    /// the assembly and compared with the table below, which places each one as a
    /// contents route this closes or as an administrative route it must not. A shared
    /// route added later is in neither column and reds this until somebody says which it
    /// is.
    /// </summary>
    [Fact]
    public void EveryRouteUnderTheSharedListIsPlacedAsClosedOrAdministrative()
    {
        var declared = ApiSurface.RoutesOf(ApiSurface.Controllers())
            .Where(route => route.Contains("Watchlist/Shared", StringComparison.Ordinal))
            .OrderBy(route => route, StringComparer.Ordinal)
            .ToList();

        var placed = ClosedByTheSwitch
            .Concat(NotClosedByTheSwitch)
            .OrderBy(route => route, StringComparer.Ordinal)
            .ToList();

        Assert.Equal(placed, declared);
    }

    /// <summary>
    /// The routes over the shared list's contents, which answer as though there were no
    /// list while the switch says no. Every one of them is driven above.
    /// </summary>
    private static IReadOnlyList<string> ClosedByTheSwitch =>
    [
        "GET Watchlist/Shared/Export",
        "GET Watchlist/Shared/Items",
        "POST Watchlist/Shared/Import",
        "POST Watchlist/Shared/Items/{itemId}",
        "DELETE Watchlist/Shared/Items/{itemId}",
    ];

    /// <summary>
    /// The administrative pair, which is not closed: the creation asks the switch its
    /// own question and the removal must stay reachable while it is off.
    /// </summary>
    private static IReadOnlyList<string> NotClosedByTheSwitch =>
    [
        "POST Watchlist/Shared",
        "DELETE Watchlist/Shared",
    ];

    private static PluginConfiguration On() => new() { SharedListEnabled = true };

    private static PluginConfiguration Off() => new() { SharedListEnabled = false };

    private static IReadOnlyList<int> Answers(
        WatchlistController controller,
        SharedWatchlistTransferController transfer,
        string exported) =>
    [
        StatusOf(controller.SharedItemsFor(AUser).Result!),
        StatusOf(controller.AddSharedFor(AUser, AFilm)),
        StatusOf(controller.RemoveSharedFor(AUser, AFilm, callerMayRemoveAnyEntry: false)),
        StatusOf(transfer.ExportSharedFor(TheOwner, callerIsAnAdministrator: true)),
        StatusOf(transfer.ImportSharedFor(TheOwner, exported, callerIsAnAdministrator: true).Result!),
    ];

    private static int StatusOf(ActionResult result) => result switch
    {
        NoContentResult => StatusCodes.Status204NoContent,
        NotFoundResult => StatusCodes.Status404NotFound,
        BadRequestResult => StatusCodes.Status400BadRequest,
        ConflictResult => StatusCodes.Status409Conflict,
        ObjectResult carrying => carrying.StatusCode ?? StatusCodes.Status200OK,
        ContentResult content => content.StatusCode ?? StatusCodes.Status200OK,
        StatusCodeResult code => code.StatusCode,
        _ => throw new InvalidOperationException("This result carries no status code: " + result.GetType().Name),
    };

    private static ADescriberOf ADescriber() => new((AFilm, AUser, WatchlistItemKind.Movie), (AFilm, TheOwner, WatchlistItemKind.Movie));

    /// <summary>
    /// The first two steps of the sequence, and the export a closed server is later
    /// asked to take back.
    /// </summary>
    /// <returns>The export document, taken while the switch was still on.</returns>
    private string MakeTheListAndExportIt()
    {
        var store = new WatchlistDocumentStore(DataFolder);

        store.WriteShared(WatchlistDocumentStore.EmptyShared(TheList, TheOwner));

        Assert.IsType<NoContentResult>(ControllerWith(On()).AddSharedFor(AUser, AFilm));

        var exported = TransferWith(On()).ExportSharedFor(TheOwner, callerIsAnAdministrator: true);

        return Assert.IsType<ContentResult>(exported).Content!;
    }

    private WatchlistController ControllerWith(PluginConfiguration configuration) => new(
        new WatchlistDocumentStore(DataFolder),
        ADescriber(),
        configuration,
        new StoppedClock(WhenItWasAdded),
        AuthorisationAnswering.Yes(),
        new APlaylistServerOf(),
        NullLogger<WatchlistController>.Instance);

    private SharedWatchlistTransferController TransferWith(PluginConfiguration configuration) => new(
        new WatchlistDocumentStore(DataFolder),
        ADescriber(),
        new NoProviderIds(),
        new NoProviderIndex(),
        configuration,
        AuthorisationAnswering.Yes(),
        NullLogger<SharedWatchlistTransferController>.Instance);

    /// <summary>
    /// A server that knows no item by any outside identifier. Nothing here turns on what
    /// an export carries, so a table of rows would be a fixture nothing looks at.
    /// </summary>
    private sealed class NoProviderIds : IProviderIdSource
    {
        public IReadOnlyDictionary<string, string> ProviderIdsFor(Guid itemId) =>
            new Dictionary<string, string>(StringComparer.Ordinal);
    }

    /// <summary>
    /// The other direction of the same absence.
    /// </summary>
    private sealed class NoProviderIndex : IProviderIdIndex
    {
        public Guid? ItemFor(string provider, string id) => null;
    }
}
