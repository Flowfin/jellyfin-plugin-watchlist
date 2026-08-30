using System;
using System.Net.Mime;
using System.Text.Json;
using System.Threading.Tasks;
using Jellyfin.Plugin.Watchlist.Configuration;
using Jellyfin.Plugin.Watchlist.Export;
using Jellyfin.Plugin.Watchlist.Store;
using MediaBrowser.Common.Api;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Watchlist.Api;

/// <summary>
/// The two endpoints that carry the shared list out of one server and into another.
/// </summary>
/// <remarks>
/// <para>
/// A controller of its own rather than two more routes beside the per-user pair. Both
/// of these are administrative, so both name the server's elevation policy in their
/// attribute, and an attribute demanding elevation is the one thing that must never
/// arrive on the per-user routes by sitting on a type they share. The dependency is
/// the same shape of argument: asking the server who is an administrator is
/// <see cref="IAuthorizationService"/>, and the two routes that move a user's own list
/// have no use for it.
/// </para>
/// <para>
/// Why an administrative route at all, when the shared list already has a read. That
/// read filters by what each caller may see, so two people on one server export two
/// different shared lists under one name and an import of either is a restore missing
/// entries nobody counted. A lossless export of the shared list is a read of
/// everything on it, which is a thing only an administrator may have.
/// </para>
/// <para>
/// What these two do not touch is anybody's private list. The export writes out the
/// shared record and nothing else, and the import writes the shared record and nothing
/// else, so neither is a route into a list somebody keeps for themselves.
/// </para>
/// <para>
/// Neither route consults <see cref="PluginConfiguration.SharedListEnabled"/>, which is
/// the answer the shared read and the shared add already give: the setting is consulted
/// where the list is made and nowhere else today. Whether turning it off should close
/// the routes that read an existing list is #277, and an answer taken at this route
/// alone would be a third place that one question is answered.
/// </para>
/// </remarks>
[ApiController]
[Authorize(Policy = Policies.RequiresElevation)]
[Route("Watchlist")]
[Produces(MediaTypeNames.Application.Json)]
public class SharedWatchlistTransferController : ControllerBase
{
    private readonly WatchlistDocumentStore _store;
    private readonly IWatchlistItemDescriber _describer;
    private readonly IProviderIdSource _providerIds;
    private readonly IProviderIdIndex _providerIndex;
    private readonly PluginConfiguration _configuration;
    private readonly IAuthorizationService _authorisation;
    private readonly ILogger<SharedWatchlistTransferController> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="SharedWatchlistTransferController"/> class.
    /// </summary>
    /// <param name="store">Where the lists are kept.</param>
    /// <param name="describer">What the library will say about an item to a user.</param>
    /// <param name="providerIds">What an item here is called outside this server.</param>
    /// <param name="providerIndex">Which item here an identifier from elsewhere names.</param>
    /// <param name="configuration">The cap a write is judged against, and the name the list is shown under.</param>
    /// <param name="authorisation">Who answers whether a caller is an administrator.</param>
    /// <param name="logger">Where the lines about a refused file and a finished import go.</param>
    /// <remarks>
    /// No clock, for the reason the per-user pair carries: an imported entry keeps the
    /// instant the exporting server recorded.
    /// </remarks>
    public SharedWatchlistTransferController(
        WatchlistDocumentStore store,
        IWatchlistItemDescriber describer,
        IProviderIdSource providerIds,
        IProviderIdIndex providerIndex,
        PluginConfiguration configuration,
        IAuthorizationService authorisation,
        ILogger<SharedWatchlistTransferController> logger)
    {
        _store = store;
        _describer = describer;
        _providerIds = providerIds;
        _providerIndex = providerIndex;
        _configuration = configuration;
        _authorisation = authorisation;
        _logger = logger;
    }

    /// <summary>
    /// Writes the whole shared list out in the exchange format.
    /// </summary>
    /// <returns>The export document.</returns>
    /// <response code="200">The shared list, in the format docs/export-format.md fixes.</response>
    /// <response code="401">The request carried no user identity this plugin could read.</response>
    /// <response code="403">The caller is not an administrator. Nothing was read.</response>
    /// <response code="404">Nobody has made a shared list on this server.</response>
    /// <response code="503">The shared list exists and this plugin will not read it.</response>
    /// <remarks>
    /// Everything on the list rather than the part the caller may see. That is the whole
    /// point of the route and it is why it is administrative: a restore has to carry the
    /// entries whose media the person taking the export happens to have no access to,
    /// and a filtered export would leave them out without counting them.
    ///
    /// Elevation is asked twice, exactly as on the shared list's creation. The attribute
    /// is the server's own gate, refusing the request before this method runs; the
    /// question in the body is the same question asked where a caller that reaches the
    /// method can be refused by it, and the suite drives the second one.
    /// </remarks>
    [HttpGet("Shared/Export")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult> ExportSharedWatchlist()
    {
        var userId = CallingUser.IdOf(User);

        if (userId is null)
        {
            return Unauthorized();
        }

        var elevated = await _authorisation
            .AuthorizeAsync(User, Policies.RequiresElevation)
            .ConfigureAwait(false);

        return ExportSharedFor(userId.Value, elevated.Succeeded);
    }

    /// <summary>
    /// Reads an exported file against this server and puts what it matched on the
    /// shared list.
    /// </summary>
    /// <param name="export">The exported file, as its JSON.</param>
    /// <returns>What happened, entry by entry.</returns>
    /// <response code="200">The report. Entries that matched nothing here are in it too.</response>
    /// <response code="400">The body is not an export this plugin can read.</response>
    /// <response code="401">The request carried no user identity this plugin could read.</response>
    /// <response code="403">The caller is not an administrator. Nothing was written.</response>
    /// <response code="404">This server has no shared list to write into.</response>
    /// <response code="503">The shared list exists and this plugin will not write to it.</response>
    /// <remarks>
    /// It writes into the shared list this server already has and never makes one.
    /// Making the shared list is a decision an administrator takes on its own route, and
    /// an import that made one would take that decision as a side effect of restoring a
    /// file.
    ///
    /// The private lists in the file are counted and left alone, which is the mirror
    /// image of what the per-user import does with a shared one. Writing somebody's
    /// private list out of a file is a write to a list its owner did not ask about, and
    /// there is no route here that does it.
    /// </remarks>
    [HttpPost("Shared/Import")]
    [Consumes(MediaTypeNames.Application.Json)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<WatchlistImportReport>> ImportSharedWatchlist([FromBody] JsonElement export)
    {
        var userId = CallingUser.IdOf(User);

        if (userId is null)
        {
            return Unauthorized();
        }

        var elevated = await _authorisation
            .AuthorizeAsync(User, Policies.RequiresElevation)
            .ConfigureAwait(false);

        return ImportSharedFor(userId.Value, export.GetRawText(), elevated.Succeeded);
    }

    /// <summary>
    /// The whole of the export, with the caller and the server's answer already in hand.
    /// Separated from the route for the same reason the item reads are: the suite drives
    /// it with a user of its own and never a request.
    /// </summary>
    /// <param name="userId">Who is asking, which decides nothing about what comes back.</param>
    /// <param name="callerIsAnAdministrator">The server's answer about this caller.</param>
    /// <returns>The result the endpoint returns.</returns>
    /// <remarks>
    /// The text comes from <see cref="WatchlistExportFormat"/> and is handed back as it
    /// stands rather than by returning the object for the server to serialise, for the
    /// reason the per-user export carries: that format is the one place that decides what
    /// an export looks like, and the server's own serialiser omits a member whose value
    /// is null.
    ///
    /// What the file says about the list is the identity the record holds and the name
    /// the configuration page shows it under. The record deliberately holds no name, so
    /// the name is read from the setting, and the format already tells a reader that a
    /// name is a label rather than an identity.
    /// </remarks>
    internal ActionResult ExportSharedFor(Guid userId, bool callerIsAnAdministrator)
    {
        if (!callerIsAnAdministrator)
        {
            _logger.LogInformation(
                "Refusing to export the shared watchlist for user {UserId}: the server's elevation policy does not answer for this caller.",
                userId);

            return StatusCode(StatusCodes.Status403Forbidden);
        }

        var read = _store.ReadShared();

        if (!read.Exists)
        {
            // Nobody has made one. Not an empty export: a file describing a shared list
            // with no entries would restore onto another server as a shared list, on a
            // server whose administrator never asked for one.
            return NotFound();
        }

        if (read.Document is null)
        {
            _logger.LogWarning(
                "Refusing to export the shared watchlist for user {UserId}, because the stored document declares schema version {StoredVersion}.",
                userId,
                read.StoredSchemaVersion);

            return StatusCode(StatusCodes.Status503ServiceUnavailable);
        }

        var export = WatchlistExporter.Export(
        [
            WatchlistExporter.SharedList(
                read.Document.ListId,
                _configuration.SharedListName,
                read.Document.OwnerUserId,
                read.Document.Entries,
                _providerIds),
        ]);

        return Content(WatchlistExportFormat.Write(export), MediaTypeNames.Application.Json);
    }

    /// <summary>
    /// The whole of the import, with the caller and the server's answer already in hand.
    /// </summary>
    /// <param name="userId">Who is importing, whose access decides what an entry may become here.</param>
    /// <param name="text">The file, as it arrived.</param>
    /// <param name="callerIsAnAdministrator">The server's answer about this caller.</param>
    /// <returns>The result the endpoint returns.</returns>
    internal ActionResult<WatchlistImportReport> ImportSharedFor(
        Guid userId,
        string text,
        bool callerIsAnAdministrator)
    {
        if (!callerIsAnAdministrator)
        {
            _logger.LogInformation(
                "Refusing an import into the shared watchlist for user {UserId}: the server's elevation policy does not answer for this caller.",
                userId);

            return StatusCode(StatusCodes.Status403Forbidden);
        }

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
                "Refusing an import into the shared watchlist for user {UserId}: the body is not an export this plugin can read.",
                userId);

            return BadRequest();
        }

        if (export is null || export.FormatVersion != WatchlistExport.CurrentFormatVersion)
        {
            // A version this plugin does not write is refused rather than read as far as
            // it goes, for the reason the per-user import carries: the format promises a
            // reader that meets a number it does not know will stop.
            _logger.LogInformation(
                "Refusing an import into the shared watchlist for user {UserId}: the body declares no export format version this plugin knows, and this plugin writes version {Version}.",
                userId,
                WatchlistExport.CurrentFormatVersion);

            return BadRequest();
        }

        var read = _store.ReadShared();

        if (!read.Exists)
        {
            return NotFound();
        }

        if (!read.IsAvailable)
        {
            _logger.LogWarning(
                "Refusing an import into the shared watchlist for user {UserId}, because the stored document declares schema version {StoredVersion}.",
                userId,
                read.StoredSchemaVersion);

            return StatusCode(StatusCodes.Status503ServiceUnavailable);
        }

        return Written(userId, export);
    }

    /// <summary>
    /// The lists of a readable export, walked in the order the file holds them.
    /// </summary>
    /// <param name="userId">Who is importing.</param>
    /// <param name="export">The export.</param>
    /// <returns>The report.</returns>
    /// <remarks>
    /// An entry is matched only where this administrator may see the item and a
    /// watchlist would take it, which is the rule the per-user import applies to its own
    /// caller. So an entry pointing at a library even this caller has no access to comes
    /// back unmatched rather than being written, and it is reported rather than dropped.
    /// That is the one place this import is narrower than the export it mirrors, and it
    /// is narrower in the direction that discloses nothing.
    /// </remarks>
    private ActionResult<WatchlistImportReport> Written(Guid userId, WatchlistExport export)
    {
        var report = ImportedFile.Read(
            export,
            ExportedListKind.Shared,
            new ImportableTo(_providerIndex, _describer, userId),
            entry => _store.AddShared(entry, _configuration.MaxEntriesInSharedList));

        // Counts and no titles. The log of a server is read by an administrator, and a
        // title on the shared list is one somebody put there for everybody.
        _logger.LogInformation(
            "Imported into the shared watchlist for user {UserId}: {Added} added, {AlreadyOnTheList} already there, {Unmatched} matched nothing here, {Refused} refused, and {ListsNotImported} of {ListsRead} lists not imported.",
            userId,
            report.Added,
            report.AlreadyOnTheList,
            report.Unmatched,
            report.Refused,
            report.ListsNotImported,
            report.ListsRead);

        return report;
    }
}
