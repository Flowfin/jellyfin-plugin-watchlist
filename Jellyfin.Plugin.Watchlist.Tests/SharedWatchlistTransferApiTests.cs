using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Claims;
using System.Text.Json;
using System.Threading.Tasks;
using Jellyfin.Plugin.Watchlist.Api;
using Jellyfin.Plugin.Watchlist.Configuration;
using Jellyfin.Plugin.Watchlist.Export;
using Jellyfin.Plugin.Watchlist.Store;
using MediaBrowser.Common.Api;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Jellyfin.Plugin.Watchlist.Tests;

/// <summary>
/// The two endpoints that carry the shared list between servers, driven by calling
/// them. No web host and no server: the controller sits over a store in a directory
/// this test owns, a describer answering from a table, a provider index answering from
/// another, and a fixed answer where the server's elevation policy would be.
/// </summary>
/// <remarks>
/// What separates these from the per-user pair is the direction each one is lossy in.
/// The export must not be filtered, because a restore has to carry the entries the
/// administrator taking it cannot see; the import must be, because writing an entry
/// this caller cannot resolve would be writing something they could not have read.
/// Both halves are asserted here rather than argued for in the controller.
/// </remarks>
public sealed class SharedWatchlistTransferApiTests : IDisposable
{
    private static readonly Guid AnAdministrator = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid SomebodyElse = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid TheList = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid TheOwner = Guid.Parse("44444444-4444-4444-4444-444444444444");

    private static readonly DateTimeOffset WhenItWasAdded =
        new(2026, 1, 2, 3, 4, 5, TimeSpan.Zero);

    private readonly TemporaryDirectory _sandbox = new("shared-watchlist-transfer-api");

    private string DataFolder => Path.Join(_sandbox.FullPath, "plugin-data");

    /// <inheritdoc />
    public void Dispose()
    {
        _sandbox.Dispose();
    }

    /// <summary>
    /// The export names the list, its owner and the name the page shows it under, and
    /// names every entry the way the rest of the world does.
    /// </summary>
    [Fact]
    public void TheExportCarriesTheListItsOwnerAndItsName()
    {
        StoreShared(EntryAddedBy(1, SomebodyElse));

        var controller = ControllerOver(new ProviderIdTable((Item(1), "Imdb", "tt0000001")));

        var list = Assert.Single(Exported(controller.ExportSharedFor(AnAdministrator, true)).Lists);
        var entry = Assert.Single(list.Entries);

        Assert.Equal(ExportedListKind.Shared, list.Kind);
        Assert.Equal(TheList, list.ListId);
        Assert.Equal(TheOwner, list.OwnerUserId);
        Assert.Equal(PluginConfiguration.DefaultSharedListName, list.Name);
        Assert.Equal(Item(1), entry.ItemId);
        Assert.Equal(new KeyValuePair<string, string>("Imdb", "tt0000001"), Assert.Single(entry.ProviderIds));
    }

    /// <summary>
    /// The reason this route is administrative, asserted rather than stated. The
    /// ordinary shared read shows a caller what they may see; this one shows everything
    /// on the list, including an entry the describer answers for nobody.
    /// </summary>
    [Fact]
    public void TheExportCarriesAnEntryTheCallerCouldNotSeeOnTheOrdinaryRead()
    {
        StoreShared(EntryAddedBy(1, SomebodyElse), EntryAddedBy(2, SomebodyElse));

        var controller = ControllerOver(new ProviderIdTable((Item(1), "Imdb", "tt0000001")));

        var entries = Assert.Single(Exported(controller.ExportSharedFor(AnAdministrator, true)).Lists).Entries;

        Assert.Equal([Item(1), Item(2)], entries.Select(entry => entry.ItemId));
        Assert.Empty(entries[1].ProviderIds);
    }

    /// <summary>
    /// A shared list nobody has put anything on exports as a list with no entries, which
    /// is a valid export of nothing rather than a refusal.
    /// </summary>
    [Fact]
    public void ASharedListWithNothingOnItExportsAnEmptyList()
    {
        MakeTheSharedList();

        var controller = ControllerOver(new ProviderIdTable());

        Assert.Empty(Assert.Single(Exported(controller.ExportSharedFor(AnAdministrator, true)).Lists).Entries);
    }

    /// <summary>
    /// A server on which nobody made a shared list answers that there is none. An empty
    /// export would restore onto another server as a shared list, on a server whose
    /// administrator never asked for one.
    /// </summary>
    [Fact]
    public void AServerWithNoSharedListIsNotExportedAsAnEmptyOne()
    {
        var controller = ControllerOver(new ProviderIdTable());

        Assert.IsType<NotFoundResult>(controller.ExportSharedFor(AnAdministrator, true));
    }

    /// <summary>
    /// A list this plugin will not read is refused rather than exported empty, which is
    /// the rule the per-user export follows for the same reason: a file to restore from
    /// that is missing everything is worse than no file.
    /// </summary>
    [Fact]
    public void ASharedListThisPluginWillNotReadIsRefusedRatherThanExportedEmpty()
    {
        var bytes = UnreadableSharedList();

        var controller = ControllerOver(new ProviderIdTable());

        Assert.Equal(
            StatusCodes.Status503ServiceUnavailable,
            StatusOf(controller.ExportSharedFor(AnAdministrator, true)));

        Assert.Equal(bytes, File.ReadAllText(new WatchlistDocumentStore(DataFolder).SharedListPath));
    }

    /// <summary>
    /// A caller the server's elevation policy does not answer for reads nothing, and is
    /// told nothing about whether this server even has a shared list.
    /// </summary>
    [Fact]
    public void ACallerWhoIsNotAnAdministratorExportsNothing()
    {
        StoreShared(EntryAddedBy(1, SomebodyElse));

        var controller = ControllerOver(new ProviderIdTable((Item(1), "Imdb", "tt0000001")));

        Assert.Equal(
            StatusCodes.Status403Forbidden,
            StatusOf(controller.ExportSharedFor(SomebodyElse, false)));
    }

    /// <summary>
    /// The import writes what it matched on to the shared list and reports every entry,
    /// including the one nothing here answered to.
    /// </summary>
    [Fact]
    public void TheImportWritesWhatItMatchedAndReportsWhatItDidNot()
    {
        MakeTheSharedList();

        var controller = ControllerOver(
            new ProviderIdTable(),
            new ProviderIndexTable(("Imdb", "tt0000001", Item(1))),
            Visible(1, AnAdministrator));

        var report = Reported(controller.ImportSharedFor(
            AnAdministrator,
            FileWith(SharedListOf(WithProviderId(1, "Imdb", "tt0000001"), Bare(9))),
            true));

        Assert.Equal(1, report.Added);
        Assert.Equal(1, report.Unmatched);
        Assert.Equal([Item(1)], Stored().Select(entry => entry.ItemId));

        var written = Assert.Single(Stored());

        Assert.Equal(WhenItWasAdded, written.AddedAt);
        Assert.Equal(WatchlistEntrySource.Import, written.Source);
    }

    /// <summary>
    /// An imported entry records nobody as having put it there. The file carries no such
    /// thing to copy, and stamping the importing administrator on it would say they
    /// added a title they did not.
    /// </summary>
    [Fact]
    public void AnImportedEntryRecordsNobodyAsHavingAddedIt()
    {
        MakeTheSharedList();

        var controller = ControllerOver(
            new ProviderIdTable(),
            new ProviderIndexTable(("Imdb", "tt0000001", Item(1))),
            Visible(1, AnAdministrator));

        controller.ImportSharedFor(
            AnAdministrator,
            FileWith(SharedListOf(WithProviderId(1, "Imdb", "tt0000001"))),
            true);

        Assert.Null(Assert.Single(Stored()).AddedBy);
    }

    /// <summary>
    /// The mirror image of what the per-user import does with a shared list. A private
    /// list in the file is counted with its entries and nothing on it is written, so a
    /// file that carried one does not read back as a file that carried nothing.
    /// </summary>
    [Fact]
    public void APrivateListInTheFileIsCountedAndLeftAlone()
    {
        MakeTheSharedList();

        var controller = ControllerOver(
            new ProviderIdTable(),
            new ProviderIndexTable(("Imdb", "tt0000001", Item(1))),
            Visible(1, AnAdministrator));

        var report = Reported(controller.ImportSharedFor(
            AnAdministrator,
            FileWith(PrivateListOf(WithProviderId(1, "Imdb", "tt0000001"))),
            true));

        Assert.Equal(1, report.ListsRead);
        Assert.Equal(1, report.ListsNotImported);
        Assert.Equal(1, report.EntriesNotImported);
        Assert.Empty(report.Entries);
        Assert.Empty(Stored());
    }

    /// <summary>
    /// An entry this administrator cannot resolve comes back unmatched rather than being
    /// written. It is the one place this import is narrower than the export beside it,
    /// and it is narrower in the direction that discloses nothing.
    /// </summary>
    [Fact]
    public void AnEntryThisAdministratorCannotSeeComesBackUnmatched()
    {
        MakeTheSharedList();

        var controller = ControllerOver(
            new ProviderIdTable(),
            new ProviderIndexTable(("Imdb", "tt0000001", Item(1))),
            Visible(1, SomebodyElse));

        var report = Reported(controller.ImportSharedFor(
            AnAdministrator,
            FileWith(SharedListOf(WithProviderId(1, "Imdb", "tt0000001"))),
            true));

        Assert.Equal(WatchlistImportOutcome.Unmatched, Assert.Single(report.Entries).Outcome);
        Assert.Empty(Stored());
    }

    /// <summary>
    /// A title already on the shared list is reported rather than written twice, and the
    /// entry that is there keeps the name of whoever put it there.
    /// </summary>
    [Fact]
    public void AnEntryTheListAlreadyHoldsKeepsTheAttributionItHas()
    {
        StoreShared(EntryAddedBy(1, SomebodyElse));

        var controller = ControllerOver(
            new ProviderIdTable(),
            new ProviderIndexTable(("Imdb", "tt0000001", Item(1))),
            Visible(1, AnAdministrator));

        var report = Reported(controller.ImportSharedFor(
            AnAdministrator,
            FileWith(SharedListOf(WithProviderId(1, "Imdb", "tt0000001"))),
            true));

        Assert.Equal(1, report.AlreadyOnTheList);
        Assert.Equal(SomebodyElse, Assert.Single(Stored()).AddedBy);
    }

    /// <summary>
    /// An entry the cap will not take is refused and reported as refused. Nothing about
    /// it is dropped, which is the condition this whole surface is judged on.
    /// </summary>
    [Fact]
    public void AnEntryPastTheCapIsRefusedAndSaidSo()
    {
        StoreShared(EntryAddedBy(1, SomebodyElse));

        var controller = ControllerOver(
            new PluginConfiguration { MaxEntriesInSharedList = 1 },
            new ProviderIdTable(),
            new ProviderIndexTable(("Imdb", "tt0000002", Item(2))),
            Visible(2, AnAdministrator));

        var report = Reported(controller.ImportSharedFor(
            AnAdministrator,
            FileWith(SharedListOf(WithProviderId(2, "Imdb", "tt0000002"))),
            true));

        Assert.Equal(1, report.Refused);
        Assert.Equal([Item(1)], Stored().Select(entry => entry.ItemId));
    }

    /// <summary>
    /// A body that is not an export is refused with a status code and nothing else, and
    /// nothing is written on the way to that answer.
    /// </summary>
    [Fact]
    public void ABodyThatIsNotAnExportIsRefused()
    {
        MakeTheSharedList();

        var controller = ControllerOver(new ProviderIdTable());

        Assert.IsType<BadRequestResult>(
            controller.ImportSharedFor(AnAdministrator, "{", true).Result);

        Assert.Empty(Stored());
    }

    /// <summary>
    /// A version this plugin does not write is refused rather than read as far as it
    /// goes, because a partial import is what the format's version field exists to
    /// prevent.
    /// </summary>
    [Fact]
    public void AFileDeclaringAVersionThisPluginDoesNotKnowIsRefused()
    {
        MakeTheSharedList();

        var controller = ControllerOver(new ProviderIdTable());

        var text = FileWith(SharedListOf(Bare(1))).Replace(
            "\"FormatVersion\": " + WatchlistExport.CurrentFormatVersion.ToString(CultureInfo.InvariantCulture),
            "\"FormatVersion\": " + (WatchlistExport.CurrentFormatVersion + 1).ToString(CultureInfo.InvariantCulture),
            StringComparison.Ordinal);

        Assert.IsType<BadRequestResult>(controller.ImportSharedFor(AnAdministrator, text, true).Result);
        Assert.Empty(Stored());
    }

    /// <summary>
    /// A body that reads as no document at all is the same answer as one that will not
    /// parse. `null` is valid JSON and it is not an export.
    /// </summary>
    [Fact]
    public void ABodyThatIsNoDocumentAtAllIsRefused()
    {
        MakeTheSharedList();

        var controller = ControllerOver(new ProviderIdTable());

        Assert.IsType<BadRequestResult>(controller.ImportSharedFor(AnAdministrator, "null", true).Result);
    }

    /// <summary>
    /// An import onto a server with no shared list says so and makes none. Making the
    /// list is a decision an administrator takes on its own route, and an import that
    /// made one would take that decision as a side effect of restoring a file.
    /// </summary>
    [Fact]
    public void AnImportOntoAServerWithNoSharedListMakesNone()
    {
        var controller = ControllerOver(
            new ProviderIdTable(),
            new ProviderIndexTable(("Imdb", "tt0000001", Item(1))),
            Visible(1, AnAdministrator));

        Assert.IsType<NotFoundResult>(
            controller.ImportSharedFor(
                AnAdministrator,
                FileWith(SharedListOf(WithProviderId(1, "Imdb", "tt0000001"))),
                true).Result);

        Assert.Empty(FilesInTheDataFolder());
    }

    /// <summary>
    /// A shared list this plugin will not read is not written over. The bytes on disk are
    /// compared rather than the answer alone, because the failure this is against is a
    /// write that replaced a document it could not read.
    /// </summary>
    [Fact]
    public void AnImportIntoAListThisPluginWillNotReadChangesNothing()
    {
        var bytes = UnreadableSharedList();

        var controller = ControllerOver(
            new ProviderIdTable(),
            new ProviderIndexTable(("Imdb", "tt0000001", Item(1))),
            Visible(1, AnAdministrator));

        Assert.Equal(
            StatusCodes.Status503ServiceUnavailable,
            StatusOf(controller.ImportSharedFor(
                AnAdministrator,
                FileWith(SharedListOf(WithProviderId(1, "Imdb", "tt0000001"))),
                true).Result!));

        Assert.Equal(bytes, File.ReadAllText(new WatchlistDocumentStore(DataFolder).SharedListPath));
    }

    /// <summary>
    /// A caller the server's elevation policy does not answer for writes nothing, and is
    /// refused before the file is read at all.
    /// </summary>
    [Fact]
    public void ACallerWhoIsNotAnAdministratorImportsNothing()
    {
        MakeTheSharedList();

        var controller = ControllerOver(
            new ProviderIdTable(),
            new ProviderIndexTable(("Imdb", "tt0000001", Item(1))),
            Visible(1, SomebodyElse));

        Assert.Equal(
            StatusCodes.Status403Forbidden,
            StatusOf(controller.ImportSharedFor(
                SomebodyElse,
                FileWith(SharedListOf(WithProviderId(1, "Imdb", "tt0000001"))),
                false).Result!));

        Assert.Empty(Stored());
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
        MakeTheSharedList();

        var server = AuthorisationAnswering.Yes();
        var controller = ControllerAsking(server, AsRequestFrom(AnAdministrator));

        await controller.ExportSharedWatchlist().ConfigureAwait(true);
        await controller.ImportSharedWatchlist(JsonOf(FileWith(SharedListOf()))).ConfigureAwait(true);

        Assert.Equal([Policies.RequiresElevation, Policies.RequiresElevation], server.Asked);
    }

    /// <summary>
    /// A request carrying no identity this plugin can read is refused before the server
    /// is asked anything, on both endpoints.
    /// </summary>
    /// <returns>The running test.</returns>
    [Fact]
    public async Task ARequestWithNoIdentityIsRefusedBeforeTheServerIsAsked()
    {
        MakeTheSharedList();

        var server = AuthorisationAnswering.Yes();
        var controller = ControllerAsking(server, AsRequestFrom(null));

        Assert.IsType<UnauthorizedResult>(await controller.ExportSharedWatchlist().ConfigureAwait(true));
        Assert.IsType<UnauthorizedResult>(
            (await controller.ImportSharedWatchlist(JsonOf(FileWith(SharedListOf()))).ConfigureAwait(true)).Result);

        Assert.Empty(server.Asked);
    }

    /// <summary>
    /// The two routes together, which is what the issue this landed on is about: a list
    /// carried off one server and onto another arrives with the same titles on it.
    /// </summary>
    [Fact]
    public void AListCarriedOutAndBackArrivesWithTheSameTitlesOnIt()
    {
        StoreShared(EntryAddedBy(1, SomebodyElse), EntryAddedBy(2, SomebodyElse));

        var exported = Exported(ControllerOver(new ProviderIdTable(
            (Item(1), "Imdb", "tt0000001"),
            (Item(2), "Imdb", "tt0000002"))).ExportSharedFor(AnAdministrator, true));

        // The far server: a data folder of its own, a shared list of its own, and the
        // two titles under identifiers it assigned rather than the ones in the file.
        using var far = new TemporaryDirectory("shared-watchlist-transfer-far");

        var folder = Path.Join(far.FullPath, "plugin-data");

        new WatchlistDocumentStore(folder).WriteShared(
            WatchlistDocumentStore.EmptyShared(Guid.NewGuid(), TheOwner));

        var there = new SharedWatchlistTransferController(
            new WatchlistDocumentStore(folder),
            new DescriberFor(Visible(11, AnAdministrator), Visible(12, AnAdministrator)),
            new ProviderIdTable(),
            new ProviderIndexTable(("Imdb", "tt0000001", Item(11)), ("Imdb", "tt0000002", Item(12))),
            new PluginConfiguration(),
            AuthorisationAnswering.Yes(),
            NullLogger<SharedWatchlistTransferController>.Instance);

        var report = Reported(there.ImportSharedFor(
            AnAdministrator,
            WatchlistExportFormat.Write(exported),
            true));

        Assert.Equal(2, report.Added);
        Assert.Equal(
            [Item(11), Item(12)],
            new WatchlistDocumentStore(folder).ReadShared().Document!.Entries.Select(entry => entry.ItemId));
    }

    private static Guid Item(int n) => Guid.Parse(
        string.Format(CultureInfo.InvariantCulture, "00000000-0000-0000-0000-{0:D12}", n));

    private static WatchlistItemDescription Description() =>
        new() { Name = "An item", Kind = WatchlistItemKind.Movie };

    private static (Guid ItemId, Guid UserId, WatchlistItemDescription Description) Visible(int n, Guid userId) =>
        (Item(n), userId, Description());

    private static WatchlistEntry EntryAddedBy(int n, Guid whoAddedIt) => new()
    {
        ItemId = Item(n),
        Kind = WatchlistItemKind.Movie,
        AddedAt = WhenItWasAdded,
        Source = WatchlistEntrySource.Api,
        AddedBy = whoAddedIt,
    };

    private static ExportedEntry Bare(int n) => new()
    {
        ItemId = Item(n),
        Kind = WatchlistItemKind.Movie,
        AddedAt = WhenItWasAdded,
        ProviderIds = new Dictionary<string, string>(StringComparer.Ordinal),
    };

    private static ExportedEntry WithProviderId(int n, string provider, string id) => new()
    {
        ItemId = Item(n),
        Kind = WatchlistItemKind.Movie,
        AddedAt = WhenItWasAdded,
        ProviderIds = new Dictionary<string, string>(StringComparer.Ordinal) { [provider] = id },
    };

    private static ExportedList SharedListOf(params ExportedEntry[] entries) => new()
    {
        Kind = ExportedListKind.Shared,
        OwnerUserId = TheOwner,
        ListId = TheList,
        Name = PluginConfiguration.DefaultSharedListName,
        Entries = entries,
    };

    private static ExportedList PrivateListOf(params ExportedEntry[] entries) => new()
    {
        Kind = ExportedListKind.Private,
        OwnerUserId = SomebodyElse,
        ListId = null,
        Name = null,
        Entries = entries,
    };

    private static string FileWith(params ExportedList[] lists) =>
        WatchlistExportFormat.Write(WatchlistExporter.Export(lists));

    private static WatchlistExport Exported(ActionResult result) =>
        WatchlistExportFormat.Read(Assert.IsType<ContentResult>(result).Content!)!;

    private static WatchlistImportReport Reported(ActionResult<WatchlistImportReport> result) =>
        result.Value!;

    private static JsonElement JsonOf(string text) => JsonDocument.Parse(text).RootElement;

    private static int StatusOf(ActionResult result) => result switch
    {
        StatusCodeResult status => status.StatusCode,
        _ => throw new InvalidOperationException("This result carries no status code: " + result.GetType().Name),
    };

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

    private IReadOnlyList<string> FilesInTheDataFolder() =>
        Directory.Exists(DataFolder) ? Directory.GetFiles(DataFolder) : [];

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
                Entries = [EntryAddedBy(1, SomebodyElse)],
            })
            .Replace(
                "\"SchemaVersion\": " + SharedWatchlistDocument.CurrentSchemaVersion.ToString(CultureInfo.InvariantCulture),
                "\"SchemaVersion\": " + (SharedWatchlistDocument.CurrentSchemaVersion + 1).ToString(CultureInfo.InvariantCulture),
                StringComparison.Ordinal);

        Directory.CreateDirectory(DataFolder);
        File.WriteAllText(new WatchlistDocumentStore(DataFolder).SharedListPath, text);

        return text;
    }

    private SharedWatchlistTransferController ControllerAsking(
        AuthorisationAnswering server,
        ControllerContext context) => new(
        new WatchlistDocumentStore(DataFolder),
        new DescriberFor(),
        new ProviderIdTable(),
        new ProviderIndexTable(),
        new PluginConfiguration(),
        server,
        NullLogger<SharedWatchlistTransferController>.Instance)
    {
        ControllerContext = context,
    };

    private SharedWatchlistTransferController ControllerOver(
        IProviderIdSource providerIds,
        params (Guid ItemId, Guid UserId, WatchlistItemDescription Description)[] rows) =>
        ControllerOver(new PluginConfiguration(), providerIds, new ProviderIndexTable(), rows);

    private SharedWatchlistTransferController ControllerOver(
        IProviderIdSource providerIds,
        IProviderIdIndex providerIndex,
        params (Guid ItemId, Guid UserId, WatchlistItemDescription Description)[] rows) =>
        ControllerOver(new PluginConfiguration(), providerIds, providerIndex, rows);

    private SharedWatchlistTransferController ControllerOver(
        PluginConfiguration configuration,
        IProviderIdSource providerIds,
        IProviderIdIndex providerIndex,
        params (Guid ItemId, Guid UserId, WatchlistItemDescription Description)[] rows) => new(
        new WatchlistDocumentStore(DataFolder),
        new DescriberFor(rows),
        providerIds,
        providerIndex,
        configuration,
        AuthorisationAnswering.Yes(),
        NullLogger<SharedWatchlistTransferController>.Instance);

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

    /// <summary>
    /// What this server says its items are called elsewhere, from a table.
    /// </summary>
    private sealed class ProviderIdTable : IProviderIdSource
    {
        private readonly Dictionary<Guid, Dictionary<string, string>> _table = [];

        public ProviderIdTable(params (Guid ItemId, string Provider, string Id)[] rows)
        {
            foreach (var row in rows)
            {
                if (!_table.TryGetValue(row.ItemId, out var ids))
                {
                    ids = new Dictionary<string, string>(StringComparer.Ordinal);
                    _table[row.ItemId] = ids;
                }

                ids[row.Provider] = row.Id;
            }
        }

        public IReadOnlyDictionary<string, string> ProviderIdsFor(Guid itemId) =>
            _table.TryGetValue(itemId, out var ids)
                ? ids
                : new Dictionary<string, string>(StringComparer.Ordinal);
    }

    /// <summary>
    /// Which item this server holds under an identifier from elsewhere, from a table.
    /// </summary>
    private sealed class ProviderIndexTable : IProviderIdIndex
    {
        private readonly Dictionary<(string Provider, string Id), Guid> _table = [];

        public ProviderIndexTable(params (string Provider, string Id, Guid ItemId)[] rows)
        {
            foreach (var row in rows)
            {
                _table[(row.Provider, row.Id)] = row.ItemId;
            }
        }

        public Guid? ItemFor(string provider, string id) =>
            _table.TryGetValue((provider, id), out var itemId) ? itemId : null;
    }
}
