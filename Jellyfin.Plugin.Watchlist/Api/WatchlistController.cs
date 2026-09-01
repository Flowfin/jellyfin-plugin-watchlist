using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Mime;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Watchlist.Configuration;
using Jellyfin.Plugin.Watchlist.Projection;
using Jellyfin.Plugin.Watchlist.Store;
using MediaBrowser.Common.Api;
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
/// No endpoint here asks for elevation in its attribute, and one of them asks the
/// server about it in its body. The shared list's removal may be made by the person
/// who put the entry there or by an administrator, so an attribute demanding
/// elevation would lock out the first of those and an attribute demanding nothing
/// would not know about the second. It asks the server's own elevation policy through
/// <see cref="IAuthorizationService"/> instead, so the answer to who is an
/// administrator is the server's rather than a rule copied into this plugin. If an
/// administrative surface is added later it names that policy at its own endpoint and
/// nowhere wider, and the suite refuses a policy reaching an endpoint that its
/// expected set does not carry.
/// </remarks>
[ApiController]
[Authorize]
[Route("Watchlist")]
[Produces(MediaTypeNames.Application.Json)]
public class WatchlistController : ControllerBase
{
    private readonly WatchlistDocumentStore _store;
    private readonly IWatchlistItemDescriber _describer;
    private readonly PluginConfiguration _configuration;
    private readonly TimeProvider _clock;
    private readonly IAuthorizationService _authorisation;
    private readonly IPlaylistGateway _playlists;
    private readonly ILogger<WatchlistController> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="WatchlistController"/> class.
    /// </summary>
    /// <param name="store">Where the lists are kept.</param>
    /// <param name="describer">What the library will say about an item to a user.</param>
    /// <param name="configuration">The settings a write is judged against.</param>
    /// <param name="clock">What stamps an entry with the moment it was added.</param>
    /// <param name="authorisation">Who answers whether a caller is an administrator.</param>
    /// <param name="playlists">The seam every playlist operation goes through, which
    /// this surface uses for one thing: taking the shared list's playlist off the
    /// server when the shared list is removed.</param>
    /// <param name="logger">Where the one line about skipped entries goes.</param>
    /// <remarks>
    /// The clock is a dependency rather than a call. An entry carries the instant it
    /// was added, and a test that reads the machine's clock to check that is refused
    /// by this repository's own rule. The server's container supplies the system one.
    ///
    /// The configuration is a dependency for the same reason and one more. Reaching
    /// the plugin instance from inside an endpoint would be a branch nothing without a
    /// server can take, so the cap a write is judged against would be the one line of
    /// this file no test could reach. It is resolved per request rather than once, so
    /// a cap changed on the configuration page applies to the next call.
    ///
    /// THE PLAYLIST SEAM IS HERE FOR ONE OPERATION AND THE NARROWNESS IS THE POINT.
    /// Nothing on this surface projects anything; the scheduled pass does that. What
    /// this needs it for is the removal of the shared list, which is the one moment a
    /// playlist has to stop existing and the one moment no later pass will ever come
    /// back to tidy up - the record that would tell a pass which playlist to tidy is
    /// the thing being deleted. It is the interface rather than the server's
    /// implementation, so this file names no server playlist type and the suite drives
    /// the removal with a fake.
    /// </remarks>
    public WatchlistController(
        WatchlistDocumentStore store,
        IWatchlistItemDescriber describer,
        PluginConfiguration configuration,
        TimeProvider clock,
        IAuthorizationService authorisation,
        IPlaylistGateway playlists,
        ILogger<WatchlistController> logger)
    {
        _store = store;
        _describer = describer;
        _configuration = configuration;
        _clock = clock;
        _authorisation = authorisation;
        _playlists = playlists;
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
    /// Puts one library item on the calling user's list.
    /// </summary>
    /// <param name="itemId">The library item to add.</param>
    /// <returns>Nothing but the outcome.</returns>
    /// <response code="204">The item is on the list. It was already there, or this call put it there.</response>
    /// <response code="400">The item is not of a kind a watchlist holds. A film, a series and an episode are; anything else is not.</response>
    /// <response code="401">The request carried no user identity this plugin could read.</response>
    /// <response code="404">There is nothing here for this caller to add.</response>
    /// <response code="409">The list is at its cap. Nothing was added and nothing was removed.</response>
    /// <response code="503">The list exists and this plugin will not write to it.</response>
    /// <remarks>
    /// Safe to repeat. A second call with the same item leaves one entry and answers
    /// the same way as the first, so a client retrying after a timeout does not put the
    /// item on the list twice and does not have to read the list to find out.
    /// </remarks>
    [HttpPost("Items/{itemId}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public ActionResult AddWatchlistItem(Guid itemId)
    {
        var userId = CallingUser.IdOf(User);

        if (userId is null)
        {
            return Unauthorized();
        }

        return AddFor(userId.Value, itemId);
    }

    /// <summary>
    /// Takes one item off the calling user's list.
    /// </summary>
    /// <param name="itemId">The library item to take off.</param>
    /// <returns>Nothing but the outcome.</returns>
    /// <response code="204">The item is not on the list. It was taken off, or it was never there.</response>
    /// <response code="401">The request carried no user identity this plugin could read.</response>
    /// <response code="503">The list exists and this plugin will not write to it.</response>
    /// <remarks>
    /// Safe to repeat, and it asks the library nothing. An entry whose item has been
    /// deleted from the library is the one a user most wants to be able to remove, and
    /// a removal that first asked whether the item still resolves would refuse exactly
    /// that. Nothing leaks either way: the caller names an identifier and is told what
    /// happened to their own list, which they could have learned by reading it.
    /// </remarks>
    [HttpDelete("Items/{itemId}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public ActionResult RemoveWatchlistItem(Guid itemId)
    {
        var userId = CallingUser.IdOf(User);

        if (userId is null)
        {
            return Unauthorized();
        }

        return RemoveFor(userId.Value, itemId);
    }

    /// <summary>
    /// Reads the one list the whole server shares.
    /// </summary>
    /// <returns>The entries this caller may see, oldest first, each naming who added it.</returns>
    /// <response code="200">The shared list, which is empty when nobody has put anything on it.</response>
    /// <response code="401">The request carried no user identity this plugin could read.</response>
    /// <response code="404">Nobody has made a shared list on this server.</response>
    /// <response code="503">The shared list exists and this plugin will not read it.</response>
    /// <remarks>
    /// A route of its own rather than a parameter on the private one. An endpoint that
    /// took a list identifier and decided from it whether the caller is allowed would
    /// be one line away from serving somebody else's private list, and there is no
    /// such line here because there is no such parameter.
    ///
    /// The entries are filtered by what this caller may see, so a shared list cannot
    /// be used to learn what sits in a library the caller has no access to. Two
    /// callers therefore get different answers from one list, which is a property of
    /// the library rather than of the list.
    /// </remarks>
    [HttpGet("Shared/Items")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public ActionResult<IReadOnlyList<WatchlistEntryView>> GetSharedWatchlistItems()
    {
        var userId = CallingUser.IdOf(User);

        if (userId is null)
        {
            return Unauthorized();
        }

        return SharedItemsFor(userId.Value);
    }

    /// <summary>
    /// Puts one library item on the shared list.
    /// </summary>
    /// <param name="itemId">The library item to add.</param>
    /// <returns>Nothing but the outcome.</returns>
    /// <response code="204">The item is on the shared list. It was already there, or this call put it there.</response>
    /// <response code="400">The item is not of a kind a watchlist holds.</response>
    /// <response code="401">The request carried no user identity this plugin could read.</response>
    /// <response code="404">There is nothing here for this caller to add, or this server has no shared list.</response>
    /// <response code="409">The shared list is at its cap. Nothing was added and nothing was removed.</response>
    /// <response code="503">The shared list exists and this plugin will not write to it.</response>
    /// <remarks>
    /// Anybody who may use this server may add, which is the answer to question 7 on
    /// #1, and the entry records who did. Safe to repeat, and a repeat by a second
    /// person leaves the first person's entry as it is rather than taking their name
    /// off a title they put there.
    /// </remarks>
    [HttpPost("Shared/Items/{itemId}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public ActionResult AddSharedWatchlistItem(Guid itemId)
    {
        var userId = CallingUser.IdOf(User);

        if (userId is null)
        {
            return Unauthorized();
        }

        return AddSharedFor(userId.Value, itemId);
    }

    /// <summary>
    /// Takes one item off the shared list.
    /// </summary>
    /// <param name="itemId">The library item to take off.</param>
    /// <returns>Nothing but the outcome.</returns>
    /// <response code="204">The item is not on the shared list. It was taken off, or it was never there.</response>
    /// <response code="401">The request carried no user identity this plugin could read.</response>
    /// <response code="403">The entry is somebody else's and this caller is not an administrator.</response>
    /// <response code="404">This server has no shared list.</response>
    /// <response code="503">The shared list exists and this plugin will not write to it.</response>
    /// <remarks>
    /// Who may remove is the other half of the answer to question 7 on #1: the person
    /// who put the entry there, and an administrator. Whether this caller is one is
    /// the server's answer rather than this plugin's, asked through the server's own
    /// elevation policy.
    ///
    /// The refusal discloses nothing a reader of the list does not already have. Every
    /// entry names who added it, so a caller who is told an entry is not theirs could
    /// have read the same thing off the list a moment earlier.
    /// </remarks>
    [HttpDelete("Shared/Items/{itemId}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult> RemoveSharedWatchlistItem(Guid itemId)
    {
        var userId = CallingUser.IdOf(User);

        if (userId is null)
        {
            return Unauthorized();
        }

        var elevated = await _authorisation
            .AuthorizeAsync(User, Policies.RequiresElevation)
            .ConfigureAwait(false);

        return RemoveSharedFor(userId.Value, itemId, elevated.Succeeded);
    }

    /// <summary>
    /// Makes the one shared list this server can have.
    /// </summary>
    /// <returns>Nothing but the outcome.</returns>
    /// <response code="204">This server has a shared list. This call made it, or one was already there.</response>
    /// <response code="401">The request carried no user identity this plugin could read.</response>
    /// <response code="403">The caller is not an administrator. Nothing was written.</response>
    /// <response code="409">This server is configured not to offer a shared list. Nothing was written.</response>
    /// <remarks>
    /// An administrative operation, and the first this plugin carries. Who is an
    /// administrator is the server's answer rather than this plugin's, and it is asked
    /// twice on purpose. The attribute is the server's own gate, refusing the request
    /// before this method runs; the question in the body is the same question asked
    /// where a caller that reaches the method can be refused by it. The suite drives
    /// the second one, so the refusal is a property of this method rather than of a
    /// pipeline no test here stands up.
    ///
    /// It never overwrites an existing list. The second call is the one an
    /// administrator makes when they cannot remember whether the first one worked, and
    /// a create that emptied a list people had been adding to would be the worst
    /// possible answer to that. So both outcomes answer 204: what the caller asked for
    /// is that this server has a shared list, and it does either way.
    /// </remarks>
    [HttpPost("Shared")]
    [Authorize(Policy = Policies.RequiresElevation)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult> CreateSharedWatchlist()
    {
        var userId = CallingUser.IdOf(User);

        if (userId is null)
        {
            return Unauthorized();
        }

        var elevated = await _authorisation
            .AuthorizeAsync(User, Policies.RequiresElevation)
            .ConfigureAwait(false);

        return CreateSharedListFor(userId.Value, elevated.Succeeded);
    }

    /// <summary>
    /// Takes the shared list off this server.
    /// </summary>
    /// <returns>Nothing but the outcome.</returns>
    /// <response code="204">This server has no shared list. This call removed it, or there was none.</response>
    /// <response code="401">The request carried no user identity this plugin could read.</response>
    /// <response code="403">The caller is not an administrator. Nothing was removed.</response>
    /// <remarks>
    /// Elevation is asked the same way, and for the same reason, as on the creation
    /// above. What follows the answer takes no caller at all: the removal is handed no
    /// identity, so it cannot do one thing for one administrator and another for
    /// another, which is what the second condition of this operation asks for and is
    /// visible in the signature rather than argued for in a sentence.
    ///
    /// IT TAKES THE PLAYLIST WITH IT. The shared list is projected into one playlist
    /// that every user of the server may see, so a removal that left it behind would
    /// leave that playlist on the server holding whatever the list held at the moment
    /// it went, open to everybody and managed by nothing - and no later pass would
    /// tidy it, because the record naming which playlist it is is the thing being
    /// removed. That is #301 and this is where it is answered.
    ///
    /// A list that was not there and a list that has been removed are one answer, as
    /// everywhere else on this surface. A caller asked for a server without a shared
    /// list, and that is what they have.
    /// </remarks>
    [HttpDelete("Shared")]
    [Authorize(Policy = Policies.RequiresElevation)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult> RemoveSharedWatchlist()
    {
        if (CallingUser.IdOf(User) is null)
        {
            return Unauthorized();
        }

        var elevated = await _authorisation
            .AuthorizeAsync(User, Policies.RequiresElevation)
            .ConfigureAwait(false);

        return await RemoveSharedListFor(elevated.Succeeded, HttpContext.RequestAborted)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// The whole of the creation, with the caller and the server's answer already in
    /// hand.
    /// </summary>
    /// <param name="userId">Who is making it, which becomes the list's owner.</param>
    /// <param name="callerIsAnAdministrator">The server's answer about this caller.</param>
    /// <returns>The result the endpoint returns.</returns>
    internal ActionResult CreateSharedListFor(Guid userId, bool callerIsAnAdministrator)
    {
        if (!callerIsAnAdministrator)
        {
            _logger.LogInformation(
                "Refusing to make the shared watchlist for user {UserId}: the server's elevation policy does not answer for this caller.",
                userId);

            return StatusCode(StatusCodes.Status403Forbidden);
        }

        if (!_configuration.SharedListEnabled)
        {
            // docs/settings.md said what this switch does to a creation is decided by
            // #87, and this is that decision. The setting is the server's answer to
            // whether it offers a shared list; a record made while it says no would be
            // a second answer to the same question, and the page would go on telling
            // an administrator that the server has none while every user could see it.
            // So the switch is turned on first and this endpoint makes the record the
            // switch is about, rather than the two being able to disagree.
            return Conflict();
        }

        _store.CreateShared(Guid.NewGuid(), userId);

        return NoContent();
    }

    /// <summary>
    /// The whole of the removal, with the server's answer already in hand and no
    /// caller.
    /// </summary>
    /// <param name="callerIsAnAdministrator">The server's answer about this caller.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The result the endpoint returns.</returns>
    /// <remarks>
    /// <para>
    /// THE ORDER IS THE RECORD FIRST AND THE PLAYLIST AFTER IT, and it is the order
    /// #301's second condition asks for. A record that could not be deleted is worse
    /// than a playlist that outlived one: the record is what every endpoint on this
    /// surface answers from, so a removal that refused because the server's playlists
    /// could not be reached would leave every user still reading and writing a list an
    /// administrator has taken away. So the record goes unconditionally, and what
    /// happens to the playlist afterwards cannot put it back.
    /// </para>
    /// <para>
    /// WHICH PLAYLIST IS READ BEFORE THE RECORD GOES, because the record is the only
    /// place it is written down. A record this build cannot read carries no projection
    /// this can see, so nothing is removed and the line below says so - which is the
    /// honest answer rather than a guess at an identifier.
    /// </para>
    /// <para>
    /// THE SWITCH IS NOT CONSULTED, and that is #301's fourth condition rather than an
    /// omission. Turning the shared list off leaves the record and the playlist alone
    /// deliberately, so that turning it back on picks the same playlist up. A record
    /// REMOVED while the switch is off is the case where nothing will ever come back to
    /// tidy up, which makes it the case that needs this most.
    /// </para>
    /// <para>
    /// A SEAM THAT THROWS LEAVES A PLAYLIST AND NEVER A RECORD. The catch is broad on
    /// purpose: what a server does when its library cannot be written is not something
    /// this plugin can enumerate, and every one of those outcomes has the same right
    /// answer here, which is that the record has already gone and the playlist that
    /// outlived it is named in the log so an administrator can find it. Narrowing it to
    /// the exception types seen so far would turn the next one into a removal that
    /// refused.
    /// </para>
    /// </remarks>
    internal async Task<ActionResult> RemoveSharedListFor(
        bool callerIsAnAdministrator,
        CancellationToken cancellationToken)
    {
        if (!callerIsAnAdministrator)
        {
            _logger.LogInformation(
                "Refusing to remove the shared watchlist: the server's elevation policy does not answer for this caller.");

            return StatusCode(StatusCodes.Status403Forbidden);
        }

        var read = _store.ReadShared();
        var projected = read.Document?.Projection;
        var owner = read.Document?.OwnerUserId ?? Guid.Empty;

        var removed = _store.DeleteShared();

        if (projected is null)
        {
            if (removed && !read.IsAvailable)
            {
                _logger.LogWarning(
                    "Removed the shared watchlist record without reading it, so any playlist it was projected into is left on this server and has to be removed by hand.");
            }

            return NoContent();
        }

        try
        {
            var wentWithIt = await _playlists
                .DeleteAsync(projected.PlaylistId, owner, cancellationToken)
                .ConfigureAwait(false);

            if (wentWithIt)
            {
                _logger.LogInformation(
                    "Removed the shared watchlist and the playlist {PlaylistId} it was projected into.",
                    projected.PlaylistId);
            }
            else
            {
                _logger.LogInformation(
                    "Removed the shared watchlist. The playlist {PlaylistId} it was projected into was already gone from this server.",
                    projected.PlaylistId);
            }
        }
        catch (Exception failure)
        {
            _logger.LogError(
                failure,
                "Removed the shared watchlist, but the playlist {PlaylistId} of user {UserId} it was projected into could not be removed. It is still on this server, still visible to every user, and has to be removed by hand.",
                projected.PlaylistId,
                owner);
        }

        return NoContent();
    }

    /// <summary>
    /// The whole of the shared read, with the caller already decided.
    /// </summary>
    /// <param name="userId">Who is asking, which decides what they may see.</param>
    /// <returns>The result the endpoint returns.</returns>
    internal ActionResult<IReadOnlyList<WatchlistEntryView>> SharedItemsFor(Guid userId)
    {
        if (SharedListSwitch.ClosesTheList(_configuration))
        {
            // The switch says this server offers no shared list, so this route answers
            // the way it answers on a server that has none. It is the same answer rather
            // than a neighbouring one deliberately: a caller that could tell a list
            // switched off from a list never made would learn that the list is there.
            return NotFound();
        }

        var read = _store.ReadShared();

        if (!read.Exists)
        {
            // Nobody has made one. Not an empty list: a caller told the list is empty
            // would show a user an empty shared list on a server that has none.
            return NotFound();
        }

        if (read.Document is null)
        {
            _logger.LogWarning(
                "Answering the shared watchlist request for user {UserId} with an unavailable list, because the stored document declares schema version {StoredVersion}.",
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

        return visible.Select(entry => ViewOf(entry, described.Describe(entry.ItemId)!)).ToList();
    }

    /// <summary>
    /// The whole of the shared add, with the caller already decided.
    /// </summary>
    /// <param name="userId">Who is adding.</param>
    /// <param name="itemId">The item to add.</param>
    /// <returns>The result the endpoint returns.</returns>
    internal ActionResult AddSharedFor(Guid userId, Guid itemId)
    {
        var description = _describer.Describe(itemId, userId);

        if (description is null)
        {
            // The same one answer for two questions as the private add: the item may be
            // gone and the item may be one this caller may not see. On a shared list it
            // matters more rather than less, because what goes on it is read by
            // everybody.
            return NotFound();
        }

        if (!AcceptedWatchlistItemKinds.Accepts(description.Kind))
        {
            _logger.LogInformation(
                "Refusing to add item {ItemId} to the shared watchlist for user {UserId}: the library holds it as {Kind}, which a watchlist does not take.",
                itemId,
                userId,
                description.Kind);

            return BadRequest();
        }

        var entry = new WatchlistEntry
        {
            ItemId = itemId,
            Kind = description.Kind,
            AddedAt = _clock.GetUtcNow(),
            Source = WatchlistEntrySource.Api,
            AddedBy = userId,
        };

        if (SharedListSwitch.ClosesTheList(_configuration))
        {
            // The switch says this server offers no shared list, so this route answers
            // the way it answers on a server that has none. It is the same answer rather
            // than a neighbouring one deliberately: a caller that could tell a list
            // switched off from a list never made would learn that the list is there.
            return NotFound();
        }

        var result = _store.AddShared(entry, _configuration.MaxEntriesInSharedList);

        if (result.IsOnTheList)
        {
            return NoContent();
        }

        if (result.Outcome == WatchlistAddOutcome.RefusedNoSharedList)
        {
            return NotFound();
        }

        if (result.Outcome == WatchlistAddOutcome.RefusedListIsFull)
        {
            return Conflict();
        }

        _logger.LogWarning(
            "Refusing to add to the shared watchlist for user {UserId}, because the stored document could not be read.",
            userId);

        return StatusCode(StatusCodes.Status503ServiceUnavailable);
    }

    /// <summary>
    /// The whole of the shared removal, with the caller and their standing already
    /// decided.
    /// </summary>
    /// <param name="userId">Who is removing.</param>
    /// <param name="itemId">The item to take off.</param>
    /// <param name="callerMayRemoveAnyEntry">Whether the server calls this caller an administrator.</param>
    /// <returns>The result the endpoint returns.</returns>
    internal ActionResult RemoveSharedFor(Guid userId, Guid itemId, bool callerMayRemoveAnyEntry)
    {
        if (SharedListSwitch.ClosesTheList(_configuration))
        {
            // The switch says this server offers no shared list, so this route answers
            // the way it answers on a server that has none. It is the same answer rather
            // than a neighbouring one deliberately: a caller that could tell a list
            // switched off from a list never made would learn that the list is there.
            return NotFound();
        }

        var result = _store.RemoveShared(itemId, userId, callerMayRemoveAnyEntry);

        if (result.IsOffTheList)
        {
            // Removed and never there are one answer, as they are on a private list.
            return NoContent();
        }

        if (result.Outcome == SharedWatchlistRemoveOutcome.NoSharedList)
        {
            return NotFound();
        }

        if (result.Outcome == SharedWatchlistRemoveOutcome.RefusedNotTheirEntry)
        {
            _logger.LogInformation(
                "Refusing to remove item {ItemId} from the shared watchlist for user {UserId}: another user added it and this one is not an administrator.",
                itemId,
                userId);

            return StatusCode(StatusCodes.Status403Forbidden);
        }

        _logger.LogWarning(
            "Refusing to remove from the shared watchlist for user {UserId}, because the stored document could not be read.",
            userId);

        return StatusCode(StatusCodes.Status503ServiceUnavailable);
    }

    /// <summary>
    /// The whole of the add, with the caller already decided. Separated from the route
    /// for the same reason the read is: the suite drives it with a user of its own and
    /// never a request.
    /// </summary>
    /// <param name="userId">Whose list to add to.</param>
    /// <param name="itemId">The item to add.</param>
    /// <returns>The result the endpoint returns.</returns>
    internal ActionResult AddFor(Guid userId, Guid itemId)
    {
        var description = _describer.Describe(itemId, userId);

        if (description is null)
        {
            // One answer for two questions. The item may be gone, and the item may be
            // one this user is not allowed to see. A caller that could tell those apart
            // could ask about identifiers until it learned what sits in a library it
            // has no access to.
            return NotFound();
        }

        if (!AcceptedWatchlistItemKinds.Accepts(description.Kind))
        {
            _logger.LogInformation(
                "Refusing to add item {ItemId} to the watchlist of user {UserId}: the library holds it as {Kind}, which a watchlist does not take.",
                itemId,
                userId,
                description.Kind);

            return BadRequest();
        }

        var entry = new WatchlistEntry
        {
            ItemId = itemId,
            Kind = description.Kind,
            AddedAt = _clock.GetUtcNow(),
            Source = WatchlistEntrySource.Api,
        };

        // Through the store and never straight to a playlist. One write is what keeps
        // the two sides from disagreeing, and the projection is what carries it out.
        var result = _store.Add(userId, entry, _configuration.MaxEntriesPerUser);

        if (result.IsOnTheList)
        {
            return NoContent();
        }

        if (result.Outcome == WatchlistAddOutcome.RefusedListIsFull)
        {
            return Conflict();
        }

        _logger.LogWarning(
            "Refusing to add to the watchlist of user {UserId}, because the stored document could not be read.",
            userId);

        return StatusCode(StatusCodes.Status503ServiceUnavailable);
    }

    /// <summary>
    /// The whole of the removal, with the caller already decided.
    /// </summary>
    /// <param name="userId">Whose list to take from.</param>
    /// <param name="itemId">The item to take off.</param>
    /// <returns>The result the endpoint returns.</returns>
    internal ActionResult RemoveFor(Guid userId, Guid itemId)
    {
        var result = _store.Remove(userId, itemId);

        if (result.ListWasAvailable)
        {
            // Removed and never there are one answer. The list does not hold the item
            // either way, which is what the caller asked for, and separating them would
            // be a caller learning what is on a list by trying to take things off it.
            return NoContent();
        }

        _logger.LogWarning(
            "Refusing to remove from the watchlist of user {UserId}, because the stored document could not be read.",
            userId);

        return StatusCode(StatusCodes.Status503ServiceUnavailable);
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
        AddedBy = entry.AddedBy,
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
