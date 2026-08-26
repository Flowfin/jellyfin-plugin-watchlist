using System;
using System.Collections.Generic;
using System.Net.Mime;
using System.Text.Json;
using Jellyfin.Plugin.Watchlist.Configuration;
using Jellyfin.Plugin.Watchlist.Export;
using Jellyfin.Plugin.Watchlist.Store;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Watchlist.Api;

/// <summary>
/// The two endpoints that carry a list out of one server and into another.
/// </summary>
/// <remarks>
/// <para>
/// A controller of its own rather than two more routes on
/// <see cref="WatchlistController"/>. What these two carry is a file rather than one
/// item, they are the only endpoints in this plugin that have to ask the library what
/// a title is called outside this server, and the pair of interfaces that answers that
/// is a dependency the six item routes have no use for.
/// </para>
/// <para>
/// The authorisation attribute is the one at the type and both endpoints inherit it,
/// exactly as on the item routes: the server's default policy, which means an
/// authenticated user of this server and nothing further. Neither endpoint takes a
/// user and neither can be spelled to name one. An export is the calling user's own
/// list going out and an import is the calling user's own list being written, so a
/// caller can move their own list and has no route here to anybody else's.
/// </para>
/// <para>
/// What is deliberately absent is the shared list. Reading it is a route of its own
/// already, and writing it is an administrative operation because it is a write to a
/// list other people read, so a shared list an import meets is counted and reported
/// rather than opened.
/// </para>
/// </remarks>
[ApiController]
[Authorize]
[Route("Watchlist")]
[Produces(MediaTypeNames.Application.Json)]
public class WatchlistTransferController : ControllerBase
{
    private readonly WatchlistDocumentStore _store;
    private readonly IWatchlistItemDescriber _describer;
    private readonly IProviderIdSource _providerIds;
    private readonly IProviderIdIndex _providerIndex;
    private readonly PluginConfiguration _configuration;
    private readonly ILogger<WatchlistTransferController> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="WatchlistTransferController"/> class.
    /// </summary>
    /// <param name="store">Where the lists are kept.</param>
    /// <param name="describer">What the library will say about an item to a user.</param>
    /// <param name="providerIds">What an item here is called outside this server.</param>
    /// <param name="providerIndex">Which item here an identifier from elsewhere names.</param>
    /// <param name="configuration">The settings a write is judged against.</param>
    /// <param name="logger">Where the lines about a refused file and a finished import go.</param>
    /// <remarks>
    /// No clock. An imported entry keeps the instant the exporting server recorded,
    /// because a move that reset every date would tell a user they added their whole
    /// list on the day they changed servers.
    /// </remarks>
    public WatchlistTransferController(
        WatchlistDocumentStore store,
        IWatchlistItemDescriber describer,
        IProviderIdSource providerIds,
        IProviderIdIndex providerIndex,
        PluginConfiguration configuration,
        ILogger<WatchlistTransferController> logger)
    {
        _store = store;
        _describer = describer;
        _providerIds = providerIds;
        _providerIndex = providerIndex;
        _configuration = configuration;
        _logger = logger;
    }

    /// <summary>
    /// Writes the calling user's list out in the exchange format.
    /// </summary>
    /// <returns>The export document.</returns>
    /// <response code="200">The list, in the format docs/export-format.md fixes.</response>
    /// <response code="401">The request carried no user identity this plugin could read.</response>
    /// <response code="503">The list exists and this plugin will not read it.</response>
    /// <remarks>
    /// Nothing is left out. An entry whose media has since been deleted leaves in the
    /// export carrying no provider identifier at all, which is what tells a reader on
    /// the other server that the entry was there and could not be described, rather
    /// than the entry simply not appearing.
    /// </remarks>
    [HttpGet("Export")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public ActionResult ExportWatchlist()
    {
        var userId = CallingUser.IdOf(User);

        if (userId is null)
        {
            return Unauthorized();
        }

        return ExportFor(userId.Value);
    }

    /// <summary>
    /// Reads an exported file against this server and puts what it matched on the
    /// calling user's list.
    /// </summary>
    /// <param name="export">The exported file, as its JSON.</param>
    /// <returns>What happened, entry by entry.</returns>
    /// <response code="200">The report. Entries that matched nothing here are in it too.</response>
    /// <response code="400">The body is not an export this plugin can read.</response>
    /// <response code="401">The request carried no user identity this plugin could read.</response>
    /// <response code="503">The list exists and this plugin will not write to it.</response>
    /// <remarks>
    /// The list the file names is not the list this writes. An export made on another
    /// server carries that server's identifier for its owner, and the person importing
    /// it is somebody else there, so what an import writes is always the caller's own
    /// list. That is what makes this the calling user's own operation rather than an
    /// administrative one.
    /// </remarks>
    [HttpPost("Import")]
    [Consumes(MediaTypeNames.Application.Json)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public ActionResult<WatchlistImportReport> ImportWatchlist([FromBody] JsonElement export)
    {
        var userId = CallingUser.IdOf(User);

        if (userId is null)
        {
            return Unauthorized();
        }

        return ImportFor(userId.Value, export.GetRawText());
    }

    /// <summary>
    /// The whole of the export, with the caller already decided. Separated from the
    /// route for the same reason the item reads are: the suite drives it with a user of
    /// its own and never a request.
    /// </summary>
    /// <param name="userId">Whose list to write out.</param>
    /// <returns>The result the endpoint returns.</returns>
    /// <remarks>
    /// The text comes from <see cref="WatchlistExportFormat"/> and is handed back as it
    /// stands rather than by returning the object for the server to serialise. That
    /// format is the one place that decides what an export looks like, and the server's
    /// own serialiser omits a member whose value is null, so letting it write this
    /// document would quietly drop fields the format says are always present.
    /// </remarks>
    internal ActionResult ExportFor(Guid userId)
    {
        var read = _store.Read(userId);

        if (read.Document is null)
        {
            _logger.LogWarning(
                "Refusing to export the watchlist of user {UserId}, because the stored document declares schema version {StoredVersion}.",
                userId,
                read.StoredSchemaVersion);

            return StatusCode(StatusCodes.Status503ServiceUnavailable);
        }

        var export = WatchlistExporter.Export(
            [WatchlistExporter.PrivateList(read.Document, _providerIds)]);

        return Content(WatchlistExportFormat.Write(export), MediaTypeNames.Application.Json);
    }

    /// <summary>
    /// The whole of the import, with the caller already decided.
    /// </summary>
    /// <param name="userId">Whose list to write.</param>
    /// <param name="text">The file, as it arrived.</param>
    /// <returns>The result the endpoint returns.</returns>
    internal ActionResult<WatchlistImportReport> ImportFor(Guid userId, string text)
    {
        WatchlistExport? export;

        try
        {
            export = WatchlistExportFormat.Read(text);
        }
        catch (JsonException reason)
        {
            // One answer for every unreadable body, and the detail goes to the log
            // rather than into the answer. A parser message quotes the bytes it choked
            // on, and those bytes arrived from somewhere this endpoint does not control.
            _logger.LogInformation(
                reason,
                "Refusing an import for user {UserId}: the body is not an export this plugin can read.",
                userId);

            return BadRequest();
        }

        if (export is null || export.FormatVersion != WatchlistExport.CurrentFormatVersion)
        {
            // A version this plugin does not write is refused rather than read as far
            // as it goes. The format promises a reader that meets a number it does not
            // know will stop, and a partial import is the outcome that promise exists
            // to prevent.
            _logger.LogInformation(
                "Refusing an import for user {UserId}: the body declares no export format version this plugin knows, and this plugin writes version {Version}.",
                userId,
                WatchlistExport.CurrentFormatVersion);

            return BadRequest();
        }

        if (_store.Read(userId).Document is null)
        {
            _logger.LogWarning(
                "Refusing an import for user {UserId}, because the stored document could not be read.",
                userId);

            return StatusCode(StatusCodes.Status503ServiceUnavailable);
        }

        return Written(userId, export);
    }

    /// <summary>
    /// The lists of a readable export, walked in the order the file holds them.
    /// </summary>
    /// <param name="userId">Whose list is being written.</param>
    /// <param name="export">The export.</param>
    /// <returns>The report.</returns>
    private WatchlistImportReport Written(Guid userId, WatchlistExport export)
    {
        var importable = new ImportableTo(_providerIndex, _describer, userId);
        var lines = new List<WatchlistImportEntryReport>();
        var listsNotImported = 0;
        var entriesNotImported = 0;

        foreach (var list in export.Lists)
        {
            if (list.Kind != ExportedListKind.Private)
            {
                // A shared list, or one whose kind the file did not declare. Neither is
                // written here: the first is a write to a list other people read, and
                // the second is a claim about who may see the list that nobody made.
                // Both are counted with their entries, so a file that carried them does
                // not read back as a file that carried nothing.
                listsNotImported++;
                entriesNotImported += list.Entries.Count;
                continue;
            }

            foreach (var match in WatchlistImporter.Read(list.Entries, importable, importable).Entries)
            {
                lines.Add(Written(userId, match, importable));
            }
        }

        var report = new WatchlistImportReport
        {
            ListsRead = export.Lists.Count,
            ListsNotImported = listsNotImported,
            EntriesNotImported = entriesNotImported,
            Entries = lines,
        };

        // Counts and no titles. The log of a server is read by an administrator, and a
        // title is what a user put on a list of their own.
        _logger.LogInformation(
            "Imported into the watchlist of user {UserId}: {Added} added, {AlreadyOnTheList} already there, {Unmatched} matched nothing here, {Refused} refused, and {ListsNotImported} of {ListsRead} lists not imported.",
            userId,
            report.Added,
            report.AlreadyOnTheList,
            report.Unmatched,
            report.Refused,
            report.ListsNotImported,
            report.ListsRead);

        return report;
    }

    /// <summary>
    /// One entry of a private list, written to the caller's list, and what came of it.
    /// </summary>
    /// <param name="userId">Whose list to write.</param>
    /// <param name="match">What the matching rule said about the entry.</param>
    /// <param name="importable">The lookup that already answered for the item.</param>
    /// <returns>The line the report carries for this entry.</returns>
    private WatchlistImportEntryReport Written(
        Guid userId,
        ImportedEntryMatch match,
        ImportableTo importable)
    {
        if (match.ItemId is not { } itemId)
        {
            return LineFor(match, WatchlistImportOutcome.Unmatched);
        }

        // The bang is carried by the line above it: an entry the rule matched is one
        // this lookup answered for, and it answered once because it remembers.
        var result = _store.Add(
            userId,
            new WatchlistEntry
            {
                ItemId = itemId,
                Kind = importable.Importable(itemId)!.Kind,
                AddedAt = match.Entry.AddedAt,
                Source = WatchlistEntrySource.Import,
            },
            _configuration.MaxEntriesPerUser);

        if (result.Outcome == WatchlistAddOutcome.Added)
        {
            return LineFor(match, WatchlistImportOutcome.Added);
        }

        return LineFor(
            match,
            result.Outcome == WatchlistAddOutcome.AlreadyOnTheList
                ? WatchlistImportOutcome.AlreadyOnTheList
                : WatchlistImportOutcome.Refused);
    }

    private static WatchlistImportEntryReport LineFor(
        ImportedEntryMatch match,
        WatchlistImportOutcome outcome) => new()
        {
            ItemId = match.Entry.ItemId,
            Match = match.Match,
            Provider = match.Provider,
            MatchedItemId = match.ItemId,
            Outcome = outcome,
        };

    /// <summary>
    /// The library lookup as this caller may use it: an item answers here only when
    /// this user may see it and a watchlist would take it.
    /// </summary>
    /// <remarks>
    /// Both halves are one rule rather than two answers. An entry pointing at something
    /// this caller may not see, and an entry pointing at a music track, both come back
    /// unmatched, which is what a caller is told about an item that is not here at all.
    /// So an import cannot be used to ask what sits in a library the caller has no
    /// access to, and the add endpoint answers the same way for the same reason.
    /// </remarks>
    private sealed class ImportableTo : IProviderIdIndex, IWatchlistItemResolver
    {
        private readonly Dictionary<Guid, WatchlistItemDescription?> _asked = [];
        private readonly IProviderIdIndex _index;
        private readonly IWatchlistItemDescriber _describer;
        private readonly Guid _userId;

        public ImportableTo(IProviderIdIndex index, IWatchlistItemDescriber describer, Guid userId)
        {
            _index = index;
            _describer = describer;
            _userId = userId;
        }

        public Guid? ItemFor(string provider, string id) =>
            _index.ItemFor(provider, id) is { } itemId && Importable(itemId) is not null
                ? itemId
                : null;

        public bool Exists(Guid itemId) => Importable(itemId) is not null;

        /// <summary>
        /// What this caller may put on a list under that identifier, or null.
        /// </summary>
        /// <param name="itemId">The item on this server.</param>
        /// <returns>The description, or null where the entry may not be written.</returns>
        /// <remarks>
        /// It remembers what it was told. Without that the same item is described twice
        /// per entry, once to decide whether it may be written and once to record what
        /// kind it is, and the second answer could differ from the first.
        /// </remarks>
        public WatchlistItemDescription? Importable(Guid itemId)
        {
            if (!_asked.TryGetValue(itemId, out var description))
            {
                var answered = _describer.Describe(itemId, _userId);

                description = answered is not null && AcceptedWatchlistItemKinds.Accepts(answered.Kind)
                    ? answered
                    : null;

                _asked[itemId] = description;
            }

            return description;
        }
    }
}
