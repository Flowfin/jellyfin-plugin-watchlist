using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Mime;
using Jellyfin.Plugin.Watchlist.Store;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Watchlist.Api;

/// <summary>
/// The HTTP surface over a user's own watchlist.
/// </summary>
/// <remarks>
/// The projection is how a client shows the list without being changed. This is how
/// everything else reads it: a script, an integration, and this plugin's own page if
/// it ever needs more than a saved setting.
///
/// A plugin gets endpoints by carrying a controller type; the server scans the plugin
/// assembly for anything descended from <c>ControllerBase</c> and adds it to its own
/// route table. So the route prefix below is not a suggestion the server can decline,
/// it is a claim on the same namespace the server's own controllers sit in, which is
/// why it was checked against them rather than chosen.
///
/// Who may call it is the server's question rather than this plugin's, so the
/// authorisation attribute above the type is the whole of the answer and every
/// endpoint on it inherits that one. It carries no policy name, which is a decision
/// and not an omission. The server's default policy is the one that means an
/// authenticated user of this server and nothing further, and it is what the
/// server's own user-facing controllers take. Every policy the server publishes by
/// name means something narrower, so naming one here would demand a permission a
/// watchlist has no reason to demand. Reading your own list requires being somebody,
/// and that is the entire rule.
///
/// Nothing here asks for elevation. If an administrative surface is added later it
/// names the server's elevation policy at that endpoint and nowhere wider, and the
/// suite refuses a policy reaching an endpoint that its expected set does not carry.
/// </remarks>
[ApiController]
[Authorize]
[Route("Watchlist")]
[Produces(MediaTypeNames.Application.Json)]
public class WatchlistController : ControllerBase
{
    private readonly WatchlistDocumentStore _store;
    private readonly IWatchlistItemDescriber _describer;
    private readonly ILogger<WatchlistController> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="WatchlistController"/> class.
    /// </summary>
    /// <param name="store">Where the lists are kept.</param>
    /// <param name="describer">What the library will say about an item to a user.</param>
    /// <param name="logger">Where the one line about skipped entries goes.</param>
    public WatchlistController(
        WatchlistDocumentStore store,
        IWatchlistItemDescriber describer,
        ILogger<WatchlistController> logger)
    {
        _store = store;
        _describer = describer;
        _logger = logger;
    }

    /// <summary>
    /// Reads the calling user's list.
    /// </summary>
    /// <returns>The entries the caller may see, oldest first, as the document holds them.</returns>
    /// <response code="200">The list, which is empty for a user who never added anything.</response>
    /// <response code="401">The request carried no user identity this plugin could read.</response>
    /// <response code="503">The list exists and this plugin will not read it.</response>
    /// <remarks>
    /// No user identifier in the route and none in the query. There is deliberately no
    /// spelling of this request that names somebody else, and a reflection test over
    /// every endpoint in this assembly keeps it that way rather than a reading of this
    /// one signature.
    /// </remarks>
    [HttpGet("Items")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public ActionResult<IReadOnlyList<WatchlistEntryView>> GetWatchlistItems()
    {
        var userId = CallingUser.IdOf(User);

        if (userId is null)
        {
            return Unauthorized();
        }

        return ItemsFor(userId.Value);
    }

    /// <summary>
    /// The whole of the read, with the caller already decided. Separated from the
    /// route so the suite drives it with a user of its own and never a request.
    /// </summary>
    /// <param name="userId">Whose list to read.</param>
    /// <returns>The result the endpoint returns.</returns>
    internal ActionResult<IReadOnlyList<WatchlistEntryView>> ItemsFor(Guid userId)
    {
        var read = _store.Read(userId);

        if (read.Document is null)
        {
            // Not an empty list. A document this plugin refused to read is a list that
            // exists and is unavailable, and answering with an empty one is how a
            // refusal becomes an overwrite the next time something writes. Which code
            // says what, across every outcome this API has, is fixed on #29; this is
            // the one this endpoint can produce today.
            _logger.LogWarning(
                "Answering the watchlist request for user {UserId} with an unavailable list, because the stored document declares schema version {StoredVersion}.",
                userId,
                read.StoredSchemaVersion);

            return StatusCode(StatusCodes.Status503ServiceUnavailable);
        }

        var described = new DescribedItems(_describer, userId);

        var visible = WatchlistVisibility.Resolvable(
            read.Document.Entries,
            described,
            userId,
            _logger);

        // The bang is carried by the line above it: an entry the rule kept is one the
        // describer answered for, and it answered once because this remembers.
        return visible.Select(entry => ViewOf(entry, described.Describe(entry.ItemId)!)).ToList();
    }

    private static WatchlistEntryView ViewOf(WatchlistEntry entry, WatchlistItemDescription description) => new()
    {
        ItemId = entry.ItemId,
        Kind = entry.Kind,
        AddedAt = entry.AddedAt,
        Name = description.Name,
        ProductionYear = description.ProductionYear,
        SeriesName = description.SeriesName,
        SeasonNumber = description.SeasonNumber,
        EpisodeNumber = description.EpisodeNumber,
    };

    /// <summary>
    /// The describer seen as the resolver the visibility rule takes, so that the rule
    /// for an entry whose item does not resolve stays in the one place that holds it
    /// and this endpoint does not grow a second copy of it.
    /// </summary>
    /// <remarks>
    /// It remembers what it was told. Without that the same item is described twice
    /// per read, once to decide whether it is there and once to say what it is, and
    /// the second answer could differ from the first.
    /// </remarks>
    private sealed class DescribedItems : IWatchlistItemResolver
    {
        private readonly Dictionary<Guid, WatchlistItemDescription?> _asked = [];
        private readonly IWatchlistItemDescriber _describer;
        private readonly Guid _userId;

        public DescribedItems(IWatchlistItemDescriber describer, Guid userId)
        {
            _describer = describer;
            _userId = userId;
        }

        public bool Exists(Guid itemId) => Describe(itemId) is not null;

        public WatchlistItemDescription? Describe(Guid itemId)
        {
            if (!_asked.TryGetValue(itemId, out var description))
            {
                description = _describer.Describe(itemId, _userId);
                _asked[itemId] = description;
            }

            return description;
        }
    }
}
