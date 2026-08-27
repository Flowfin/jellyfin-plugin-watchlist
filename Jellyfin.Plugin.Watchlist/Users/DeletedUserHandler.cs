using System;
using Jellyfin.Plugin.Watchlist.Store;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Watchlist.Users;

/// <summary>
/// What this plugin does with what it holds for a user the server has deleted.
/// </summary>
/// <remarks>
/// <para>
/// It removes that user's document and nothing else. The rule and the readings it
/// rests on are on issue #23: on the one route this plugin's supported server lines
/// delete a user through, the user's playlists are removed a line before the user is,
/// and the event that reaches here is raised by the call that removes the user. So a
/// playlist removal taken here would be a second owner of a removal that has already
/// happened.
/// </para>
/// <para>
/// The residual is stated rather than repaired. Where a server deletes a user by some
/// path that does not go through that route, a projected playlist survives with an
/// owner who no longer exists. No such path is measured, none is claimed to exist, and
/// this plugin does not answer it by deleting library objects on its own account.
/// </para>
/// <para>
/// A user being created needs nothing at all and has no handler for that reason. A
/// document that is not there reads as an empty list and the first add creates one, so
/// provisioning a new user would be writing a file to say what its absence already
/// says.
/// </para>
/// </remarks>
public sealed class DeletedUserHandler
{
    private readonly WatchlistDocumentStore _store;
    private readonly ILogger<DeletedUserHandler> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="DeletedUserHandler"/> class.
    /// </summary>
    /// <param name="store">The store.</param>
    /// <param name="logger">The logger.</param>
    public DeletedUserHandler(WatchlistDocumentStore store, ILogger<DeletedUserHandler> logger)
    {
        _store = store;
        _logger = logger;
    }

    /// <summary>
    /// Acts on one deleted user.
    /// </summary>
    /// <param name="userId">The user the server deleted.</param>
    /// <remarks>
    /// It says nothing about a user who had no list. A server deletes users that never
    /// opened a watchlist, and a log line per one of those describes the plugin rather
    /// than the server.
    /// </remarks>
    public void Handle(Guid userId)
    {
        if (!_store.DeleteTheDocumentOfADeletedUser(userId))
        {
            return;
        }

        _logger.LogInformation(
            "Removed the stored list of user {UserId}, who has been deleted from this server",
            userId);
    }
}
