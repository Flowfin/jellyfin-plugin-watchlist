using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Claims;
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
/// The two endpoints that carry a list between servers, driven by calling them. No web
/// host and no server: the controller sits over a store in a directory this test owns,
/// a describer answering from a table, and a provider index answering from another.
/// </summary>
/// <remarks>
/// What these are mostly about is the move rather than the write. A file made on one
/// server names its items the way that server named them, and that identifier is
/// assigned by the exporting library, so on any other server a match on it is a
/// coincidence. The rule that puts provider identifiers first is exercised on #146;
/// what is exercised here is what the endpoints do with its answers.
/// </remarks>
public sealed class WatchlistTransferApiTests : IDisposable
{
    private static readonly Guid AUser = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid AnotherUser = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private static readonly DateTimeOffset WhenItWasAdded =
        new(2026, 1, 2, 3, 4, 5, TimeSpan.Zero);

    private readonly TemporaryDirectory _sandbox = new("watchlist-transfer-api");

    private string DataFolder => Path.Join(_sandbox.FullPath, "plugin-data");

    /// <inheritdoc />
    public void Dispose()
    {
        _sandbox.Dispose();
    }

    /// <summary>
    /// The export is the caller's own list, named twice: once the way this server names
    /// it and once the way the rest of the world does.
    /// </summary>
    [Fact]
    public void TheExportNamesEveryEntryTheWayTheRestOfTheWorldDoes()
    {
        Store(AUser, Entry(1));

        var controller = ControllerOver(new ProviderIdTable((Item(1), "Imdb", "tt0000001")));

        var list = Assert.Single(Exported(controller.ExportFor(AUser)).Lists);
        var entry = Assert.Single(list.Entries);

        Assert.Equal(ExportedListKind.Private, list.Kind);
        Assert.Equal(AUser, list.OwnerUserId);
        Assert.Equal(Item(1), entry.ItemId);
        Assert.Equal(new KeyValuePair<string, string>("Imdb", "tt0000001"), Assert.Single(entry.ProviderIds));
    }

    /// <summary>
    /// An entry whose media has been deleted leaves in the export carrying nothing a
    /// reader elsewhere can resolve it by. Dropping it would make an entry that could
    /// not be described indistinguishable from one that was never on the list.
    /// </summary>
    [Fact]
    public void AnEntryTheLibraryCannotDescribeStillLeavesInTheExport()
    {
        Store(AUser, Entry(1), Entry(2));

        var controller = ControllerOver(new ProviderIdTable((Item(1), "Imdb", "tt0000001")));

        var entries = Assert.Single(Exported(controller.ExportFor(AUser)).Lists).Entries;

        Assert.Equal([Item(1), Item(2)], entries.Select(entry => entry.ItemId));
        Assert.Empty(entries[1].ProviderIds);
    }

    /// <summary>
    /// A user who never added anything exports a list with no entries, which is a valid
    /// export of nothing rather than a refusal.
    /// </summary>
    [Fact]
    public void AUserWithNothingOnTheirListExportsAnEmptyList()
    {
        var controller = ControllerOver(new ProviderIdTable());

        Assert.Empty(Assert.Single(Exported(controller.ExportFor(AUser)).Lists).Entries);
    }

    /// <summary>
    /// A document this plugin will not read is a list that exists and is unavailable.
    /// Exporting it as an empty one would hand somebody a file to restore from that is
    /// missing everything they had.
    /// </summary>
    [Fact]
    public void AListThisPluginWillNotReadIsRefusedRatherThanExportedEmpty()
    {
        UnreadableList(AUser);

        var controller = ControllerOver(new ProviderIdTable());

        Assert.Equal(
            StatusCodes.Status503ServiceUnavailable,
            Assert.IsType<StatusCodeResult>(controller.ExportFor(AUser)).StatusCode);
    }

    /// <summary>
    /// The bytes come from the format rather than from whatever serialiser happens to
    /// be on the way out. The near miss is the server's own one, which omits a member
    /// whose value is null, so the two fields a private list always carries as null
    /// would silently leave the document.
    /// </summary>
    [Fact]
    public void TheExportIsWrittenByTheFormatRatherThanByTheServersSerialiser()
    {
        Store(AUser, Entry(1));

        var controller = ControllerOver(new ProviderIdTable((Item(1), "Imdb", "tt0000001")));
        var content = Assert.IsType<ContentResult>(controller.ExportFor(AUser));

        Assert.Equal("application/json", content.ContentType);
        Assert.Contains("\"ListId\": null", content.Content, StringComparison.Ordinal);
        Assert.Contains("\"Name\": null", content.Content, StringComparison.Ordinal);
        Assert.Contains("\"Kind\": \"Private\"", content.Content, StringComparison.Ordinal);
    }

    /// <summary>
    /// The move this whole surface exists for: a file made somewhere else, matched here
    /// by an identifier that means the same film on both servers.
    /// </summary>
    [Fact]
    public void AnEntryIsMatchedByProviderIdentifierAndPutOnTheCallersOwnList()
    {
        var controller = ControllerOver(
            new ProviderIdTable(),
            new ProviderIndexTable(("Imdb", "tt0000001", Item(7))),
            Visible(7, AUser));

        var report = Reported(controller.ImportFor(AUser, FileWith(Away(1, ("Imdb", "tt0000001")))));

        Assert.Equal(1, report.Added);
        Assert.Equal(WatchlistImportMatch.ByProviderId, Assert.Single(report.Entries).Match);
        Assert.Equal("Imdb", report.Entries[0].Provider);
        Assert.Equal(Item(7), report.Entries[0].MatchedItemId);
        Assert.Equal(Item(1), report.Entries[0].ItemId);
        Assert.Equal([Item(7)], Stored(AUser).Select(entry => entry.ItemId));
    }

    /// <summary>
    /// The entry keeps the instant the exporting server recorded. A move that reset
    /// every date would tell a user they added their whole list on the day they changed
    /// servers, and it is recorded as an import rather than as a call to the add
    /// endpoint, because a support question starts with which route put it there.
    /// </summary>
    [Fact]
    public void AnImportedEntryKeepsItsOriginalInstantAndSaysItArrivedByImport()
    {
        var controller = ControllerOver(
            new ProviderIdTable(),
            new ProviderIndexTable(("Imdb", "tt0000001", Item(7))),
            Visible(7, AUser));

        controller.ImportFor(AUser, FileWith(Away(1, ("Imdb", "tt0000001"))));

        var stored = Assert.Single(Stored(AUser));

        Assert.Equal(WhenItWasAdded, stored.AddedAt);
        Assert.Equal(WatchlistEntrySource.Import, stored.Source);
        Assert.Equal(WatchlistItemKind.Movie, stored.Kind);
    }

    /// <summary>
    /// The exporting server's own identifier is the second leg and never the first. It
    /// is what a restore onto the same library matches on, and it is a coincidence
    /// anywhere else.
    /// </summary>
    [Fact]
    public void TheExportingServersOwnIdentifierIsTheSecondLeg()
    {
        var controller = ControllerOver(
            new ProviderIdTable(),
            new ProviderIndexTable(),
            Visible(1, AUser));

        var report = Reported(controller.ImportFor(AUser, FileWith(Away(1, ("Imdb", "tt0000001")))));

        Assert.Equal(WatchlistImportMatch.ByItemId, Assert.Single(report.Entries).Match);
        Assert.Null(report.Entries[0].Provider);
        Assert.Equal([Item(1)], Stored(AUser).Select(entry => entry.ItemId));
    }

    /// <summary>
    /// An entry nothing here answers to is reported and counted. This is the condition
    /// the whole report exists for: a file whose entries went missing without a line
    /// saying so is a restore somebody trusts and should not.
    /// </summary>
    [Fact]
    public void AnEntryNothingHereAnswersToIsReportedRatherThanDropped()
    {
        var controller = ControllerOver(new ProviderIdTable(), new ProviderIndexTable());

        var report = Reported(controller.ImportFor(AUser, FileWith(Away(1, ("Imdb", "tt0000001")))));

        Assert.Equal(1, report.Unmatched);
        Assert.Equal(WatchlistImportOutcome.Unmatched, Assert.Single(report.Entries).Outcome);
        Assert.Null(report.Entries[0].MatchedItemId);
        Assert.Empty(Stored(AUser));
    }

    /// <summary>
    /// An item this caller may not see answers exactly as an item that is not here. A
    /// caller who could tell those apart could send crafted files until they learned
    /// what sits in a library they have no access to.
    /// </summary>
    [Fact]
    public void AnItemThisCallerMayNotSeeIsUnmatchedRatherThanRefused()
    {
        var controller = ControllerOver(
            new ProviderIdTable(),
            new ProviderIndexTable(("Imdb", "tt0000001", Item(7))),
            Visible(7, AnotherUser));

        var report = Reported(controller.ImportFor(AUser, FileWith(Away(1, ("Imdb", "tt0000001")))));

        Assert.Equal(1, report.Unmatched);
        Assert.Empty(Stored(AUser));
    }

    /// <summary>
    /// And the other half of the same rule. A watchlist takes a film, a show and an
    /// episode, so an identifier that lands on a music track is not an entry this
    /// import may write, and it is answered the same way for the same reason.
    /// </summary>
    [Fact]
    public void AnItemOfAKindAWatchlistDoesNotTakeIsUnmatched()
    {
        var controller = ControllerOver(
            new ProviderIdTable(),
            new ProviderIndexTable(("Imdb", "tt0000001", Item(7))),
            (Item(7), AUser, Description(WatchlistItemKind.Other)));

        var report = Reported(controller.ImportFor(AUser, FileWith(Away(1, ("Imdb", "tt0000001")))));

        Assert.Equal(1, report.Unmatched);
        Assert.Empty(Stored(AUser));
    }

    /// <summary>
    /// Importing one file twice leaves one entry and says so, exactly as calling the
    /// add endpoint twice does. A restore repeated after a timeout must not double a
    /// list.
    /// </summary>
    [Fact]
    public void ImportingTheSameFileTwiceLeavesOneEntryAndSaysSo()
    {
        var controller = ControllerOver(
            new ProviderIdTable(),
            new ProviderIndexTable(("Imdb", "tt0000001", Item(7))),
            Visible(7, AUser));

        var file = FileWith(Away(1, ("Imdb", "tt0000001")));

        controller.ImportFor(AUser, file);

        var second = Reported(controller.ImportFor(AUser, file));

        Assert.Equal(1, second.AlreadyOnTheList);
        Assert.Equal(0, second.Added);
        Assert.Single(Stored(AUser));
    }

    /// <summary>
    /// An entry the cap refuses is reported rather than lost. The store writes nothing
    /// and removes nothing when a list is full, so what a caller needs is the line
    /// saying which entries did not arrive.
    /// </summary>
    [Fact]
    public void AnEntryTheCapRefusesIsReportedRatherThanLost()
    {
        var controller = ControllerOver(
            new PluginConfiguration { MaxEntriesPerUser = 1 },
            new ProviderIdTable(),
            new ProviderIndexTable(("Imdb", "tt0000001", Item(7)), ("Imdb", "tt0000002", Item(8))),
            Visible(7, AUser),
            Visible(8, AUser));

        var report = Reported(controller.ImportFor(
            AUser,
            FileWith(Away(1, ("Imdb", "tt0000001")), Away(2, ("Imdb", "tt0000002")))));

        Assert.Equal(1, report.Added);
        Assert.Equal(1, report.Refused);
        Assert.Equal(WatchlistImportOutcome.Refused, report.Entries[1].Outcome);
        Assert.Single(Stored(AUser));
    }

    /// <summary>
    /// A shared list in the file is counted with its entries and left alone. Writing it
    /// is a write to a list other people read, which is an administrative operation
    /// rather than this one, and counting it is what stops the absence of its entries
    /// from reading as a file that carried none.
    /// </summary>
    [Fact]
    public void ASharedListIsCountedAndLeftAlone()
    {
        var controller = ControllerOver(
            new ProviderIdTable(),
            new ProviderIndexTable(("Imdb", "tt0000001", Item(7))),
            Visible(7, AUser));

        var report = Reported(controller.ImportFor(
            AUser,
            FileOf(
                PrivateListOf(Away(1, ("Imdb", "tt0000001"))),
                SharedListOf(Away(2, ("Imdb", "tt0000002"))))));

        Assert.Equal(2, report.ListsRead);
        Assert.Equal(1, report.ListsNotImported);
        Assert.Equal(1, report.EntriesNotImported);
        Assert.Equal(1, report.Added);
        Assert.Single(report.Entries);
        Assert.False(new WatchlistDocumentStore(DataFolder).ReadShared().Exists);
    }

    /// <summary>
    /// A list whose kind the file did not declare is left alone for the same reason
    /// rather than read as a private one. The kind is a claim about who may see the
    /// list, and a list carrying none is a claim nobody made.
    /// </summary>
    [Fact]
    public void AListWhoseKindTheFileDidNotDeclareIsLeftAlone()
    {
        var controller = ControllerOver(
            new ProviderIdTable(),
            new ProviderIndexTable(("Imdb", "tt0000001", Item(7))),
            Visible(7, AUser));

        var report = Reported(controller.ImportFor(
            AUser,
            FileOf(UndeclaredListOf(Away(1, ("Imdb", "tt0000001"))))));

        Assert.Equal(1, report.ListsNotImported);
        Assert.Empty(report.Entries);
        Assert.Empty(Stored(AUser));
    }

    /// <summary>
    /// A body that is not an export is refused, and nothing is written on the way to
    /// finding out.
    /// </summary>
    [Fact]
    public void ABodyThatIsNotAnExportIsRefused()
    {
        var controller = ControllerOver(new ProviderIdTable(), new ProviderIndexTable());

        Assert.IsType<BadRequestResult>(controller.ImportFor(AUser, "{\"Lists\": 3}").Result);
        Assert.Empty(Stored(AUser));
    }

    /// <summary>
    /// The JSON literal null parses and is not an export. It is the one readable body
    /// that leaves nothing to read.
    /// </summary>
    [Fact]
    public void ABodyThatIsTheJsonNullIsRefused()
    {
        var controller = ControllerOver(new ProviderIdTable(), new ProviderIndexTable());

        Assert.IsType<BadRequestResult>(controller.ImportFor(AUser, "null").Result);
    }

    /// <summary>
    /// A version this plugin does not know is refused rather than read as far as it
    /// goes. A partial import is the outcome the format's version field exists to
    /// prevent, and it is the one a person would not notice.
    /// </summary>
    [Fact]
    public void AFormatVersionThisPluginDoesNotKnowIsRefused()
    {
        var controller = ControllerOver(
            new ProviderIdTable(),
            new ProviderIndexTable(("Imdb", "tt0000001", Item(7))),
            Visible(7, AUser));

        var file = FileWith(Away(1, ("Imdb", "tt0000001"))).Replace(
            "\"FormatVersion\": " + WatchlistExport.CurrentFormatVersion.ToString(CultureInfo.InvariantCulture),
            "\"FormatVersion\": " + (WatchlistExport.CurrentFormatVersion + 1).ToString(CultureInfo.InvariantCulture),
            StringComparison.Ordinal);

        Assert.IsType<BadRequestResult>(controller.ImportFor(AUser, file).Result);
        Assert.Empty(Stored(AUser));
    }

    /// <summary>
    /// A list this plugin will not read is not written to either. Importing into it
    /// would answer a refusal with an overwrite.
    /// </summary>
    [Fact]
    public void AListThisPluginWillNotReadIsNotWrittenTo()
    {
        var bytes = UnreadableList(AUser);

        var controller = ControllerOver(
            new ProviderIdTable(),
            new ProviderIndexTable(("Imdb", "tt0000001", Item(7))),
            Visible(7, AUser));

        Assert.Equal(
            StatusCodes.Status503ServiceUnavailable,
            Assert.IsType<StatusCodeResult>(
                controller.ImportFor(AUser, FileWith(Away(1, ("Imdb", "tt0000001")))).Result).StatusCode);

        Assert.Equal(bytes, File.ReadAllText(new WatchlistDocumentStore(DataFolder).PathFor(AUser)));
    }

    /// <summary>
    /// A request this plugin cannot read an identity out of reaches neither endpoint's
    /// body. Both routes are the calling user's own operation and there is no other
    /// user for them to fall back to.
    /// </summary>
    [Fact]
    public void NeitherEndpointRunsWithoutAnIdentity()
    {
        var controller = ControllerOver(new ProviderIdTable(), new ProviderIndexTable());

        controller.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() };

        Assert.IsType<UnauthorizedResult>(controller.ExportWatchlist());
        Assert.IsType<UnauthorizedResult>(controller.ImportWatchlist(JsonOf("null")).Result);
    }

    /// <summary>
    /// And the same two with an identity, so the pair above is a test of the identity
    /// arm rather than of a controller that refuses everything.
    /// </summary>
    [Fact]
    public void BothEndpointsRunForACallerTheServerNamed()
    {
        var controller = ControllerOver(new ProviderIdTable(), new ProviderIndexTable());

        controller.ControllerContext = RequestFrom(AUser);

        Assert.IsType<ContentResult>(controller.ExportWatchlist());
        Assert.IsType<BadRequestResult>(controller.ImportWatchlist(JsonOf("null")).Result);
    }

    /// <summary>
    /// The whole round trip on one server, which is the restore case rather than the
    /// move case: what the export wrote is what an import of it puts back.
    /// </summary>
    [Fact]
    public void WhatTheExportWroteIsWhatAnImportOfItPutsBack()
    {
        Store(AUser, Entry(1), Entry(2));

        var exporting = ControllerOver(new ProviderIdTable((Item(1), "Imdb", "tt0000001")));
        var file = Assert.IsType<ContentResult>(exporting.ExportFor(AUser)).Content!;

        var importing = ControllerOver(
            new ProviderIdTable(),
            new ProviderIndexTable(("Imdb", "tt0000001", Item(1))),
            Visible(1, AnotherUser),
            Visible(2, AnotherUser));

        var report = Reported(importing.ImportFor(AnotherUser, file));

        Assert.Equal(2, report.Added);
        Assert.Equal([Item(1), Item(2)], Stored(AnotherUser).Select(entry => entry.ItemId));
    }

    private static Guid Item(int n) => Guid.Parse(
        string.Format(CultureInfo.InvariantCulture, "00000000-0000-0000-0000-{0:D12}", n));

    private static WatchlistItemDescription Description(WatchlistItemKind kind) =>
        new() { Name = "An item", Kind = kind };

    private static (Guid ItemId, Guid UserId, WatchlistItemDescription Description) Visible(int n, Guid userId) =>
        (Item(n), userId, Description(WatchlistItemKind.Movie));

    private static WatchlistEntry Entry(int n) => new()
    {
        ItemId = Item(n),
        Kind = WatchlistItemKind.Movie,
        AddedAt = WhenItWasAdded,
        Source = WatchlistEntrySource.Api,
    };

    /// <summary>
    /// An entry as another server wrote it: its own identifier for the item, and what
    /// the item is called outside it.
    /// </summary>
    /// <param name="n">Which item, as the exporting server numbered it.</param>
    /// <param name="providerIds">What that server said the item is called elsewhere.</param>
    /// <returns>The entry.</returns>
    private static ExportedEntry Away(int n, params (string Provider, string Id)[] providerIds) => new()
    {
        ItemId = Item(n),
        Kind = WatchlistItemKind.Movie,
        AddedAt = WhenItWasAdded,
        ProviderIds = providerIds.ToDictionary(
            pair => pair.Provider,
            pair => pair.Id,
            StringComparer.Ordinal),
    };

    private static ExportedList PrivateListOf(params ExportedEntry[] entries) => new()
    {
        Kind = ExportedListKind.Private,
        OwnerUserId = AnotherUser,
        ListId = null,
        Name = null,
        Entries = entries,
    };

    private static ExportedList SharedListOf(params ExportedEntry[] entries) => new()
    {
        Kind = ExportedListKind.Shared,
        OwnerUserId = AnotherUser,
        ListId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
        Name = "Theirs",
        Entries = entries,
    };

    private static ExportedList UndeclaredListOf(params ExportedEntry[] entries) => new()
    {
        Kind = ExportedListKind.Unknown,
        OwnerUserId = null,
        ListId = null,
        Name = null,
        Entries = entries,
    };

    private static string FileWith(params ExportedEntry[] entries) =>
        FileOf(PrivateListOf(entries));

    private static string FileOf(params ExportedList[] lists) =>
        WatchlistExportFormat.Write(WatchlistExporter.Export(lists));

    private static WatchlistExport Exported(ActionResult result) =>
        WatchlistExportFormat.Read(Assert.IsType<ContentResult>(result).Content!)!;

    private static WatchlistImportReport Reported(ActionResult<WatchlistImportReport> result) =>
        result.Value!;

    private static System.Text.Json.JsonElement JsonOf(string text) =>
        System.Text.Json.JsonDocument.Parse(text).RootElement;

    private static ControllerContext RequestFrom(Guid userId)
    {
        var identity = new ClaimsIdentity();

        identity.AddClaim(new Claim(CallingUser.Claim, userId.ToString()));

        return new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(identity) },
        };
    }

    private WatchlistTransferController ControllerOver(
        IProviderIdSource providerIds,
        params (Guid ItemId, Guid UserId, WatchlistItemDescription Description)[] rows) =>
        ControllerOver(new PluginConfiguration(), providerIds, new ProviderIndexTable(), rows);

    private WatchlistTransferController ControllerOver(
        IProviderIdSource providerIds,
        IProviderIdIndex providerIndex,
        params (Guid ItemId, Guid UserId, WatchlistItemDescription Description)[] rows) =>
        ControllerOver(new PluginConfiguration(), providerIds, providerIndex, rows);

    private WatchlistTransferController ControllerOver(
        PluginConfiguration configuration,
        IProviderIdSource providerIds,
        IProviderIdIndex providerIndex,
        params (Guid ItemId, Guid UserId, WatchlistItemDescription Description)[] rows) => new(
        new WatchlistDocumentStore(DataFolder),
        new DescriberFor(rows),
        providerIds,
        providerIndex,
        configuration,
        NullLogger<WatchlistTransferController>.Instance);

    private IReadOnlyList<WatchlistEntry> Stored(Guid userId) =>
        new WatchlistDocumentStore(DataFolder).Read(userId).Document?.Entries ?? [];

    private void Store(Guid userId, params WatchlistEntry[] entries) =>
        new WatchlistDocumentStore(DataFolder).Write(new WatchlistDocument
        {
            SchemaVersion = WatchlistDocument.CurrentSchemaVersion,
            UserId = userId,
            Entries = entries,
        });

    /// <summary>
    /// A document on disk that this plugin will not read, which is what one written by
    /// a newer version looks like to an older one.
    /// </summary>
    /// <param name="userId">Whose document.</param>
    /// <returns>The bytes written, so a test can prove they did not move.</returns>
    private string UnreadableList(Guid userId)
    {
        var text = WatchlistDocumentFormat
            .Write(new WatchlistDocument
            {
                SchemaVersion = WatchlistDocument.CurrentSchemaVersion,
                UserId = userId,
                Entries = [Entry(1)],
            })
            .Replace(
                "\"SchemaVersion\": " + WatchlistDocument.CurrentSchemaVersion.ToString(CultureInfo.InvariantCulture),
                "\"SchemaVersion\": " + (WatchlistDocument.CurrentSchemaVersion + 1).ToString(CultureInfo.InvariantCulture),
                StringComparison.Ordinal);

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
