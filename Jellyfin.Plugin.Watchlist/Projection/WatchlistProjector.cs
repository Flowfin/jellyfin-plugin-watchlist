using System;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Watchlist.Store;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Watchlist.Projection;

/// <summary>
/// Makes sure one target has exactly one playlist, and that the plugin remembers
/// which playlist that is.
/// </summary>
/// <remarks>
/// <para>
/// This is the half of the projection that decides WHICH playlist a list belongs in.
/// What goes inside it is the reconciler on #19, and the two are separate because the
/// question this answers is asked once per target per pass and the other is asked of
/// every entry.
/// </para>
/// <para>
/// IT CREATES ON DEMAND AND NEVER FOR A POPULATION. There is no route through this
/// that walks the users of a server, and a caller hands it one target at a time, so a
/// server where nobody has used the plugin has no playlists and does no work. That is
/// a property of the shape rather than of a flag: nothing here can be given a set.
/// </para>
/// <para>
/// The identifier is the identity and the name never is. A server appends a digit to a
/// colliding playlist directory, so a projection that found its playlist by name would
/// meet two lists with one name and adopt whichever came first. What is remembered is
/// the identifier, and every later operation names it.
/// </para>
/// <para>
/// ADOPTING A PLAYLIST THAT ALREADY CARRIES THE CONFIGURED NAME IS NOT DONE HERE. A
/// first pass over a target with nothing remembered creates. Matching an existing list
/// by name, and refusing to guess when more than one matches, is #41, and it arrives as
/// a decision taken before the creation below rather than as a change to it.
/// </para>
/// <para>
/// Nothing here reaches a server playlist type. Every call goes through
/// <see cref="IPlaylistGateway"/>, which is the seam the two supported server lines
/// differ behind, and a guard over the plugin's own sources refuses such a type
/// anywhere but the one implementation of it.
/// </para>
/// </remarks>
public sealed class WatchlistProjector
{
    private readonly IPlaylistGateway _playlists;
    private readonly ILogger<WatchlistProjector> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="WatchlistProjector"/> class.
    /// </summary>
    /// <param name="playlists">The seam every playlist operation goes through.</param>
    /// <param name="logger">The logger.</param>
    public WatchlistProjector(IPlaylistGateway playlists, ILogger<WatchlistProjector> logger)
    {
        _playlists = playlists;
        _logger = logger;
    }

    /// <summary>
    /// Makes sure this target has a playlist, creating one only where it has none that
    /// the server still holds.
    /// </summary>
    /// <param name="target">The target.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>Which playlist the target is projected into, and how that came about.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="target"/> is null.</exception>
    /// <remarks>
    /// A second call over an unchanged target creates nothing. What makes that true is
    /// the remembered identifier being asked of the server rather than trusted: a
    /// playlist the user deleted is gone, and a target that kept pointing at it would
    /// have a list nobody can see and a reconciler writing into nothing.
    /// </remarks>
    public async Task<ProjectionResult> EnsurePlaylistAsync(IProjectionTarget target, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(target);

        if (!target.IsRecordAvailable)
        {
            // The store has already said why it will not read the document. Creating a
            // playlist here would make one this plugin could never write the identity
            // of, and the next pass would make another beside it.
            return ProjectionResult.RefusedRecordUnavailable();
        }

        var remembered = target.Remembered;

        if (remembered is not null && IsStillOnTheServer(target.OwnerUserId, remembered.PlaylistId))
        {
            return ProjectionResult.AlreadyProjected(remembered);
        }

        var name = target.ConfiguredName;
        var playlistId = await _playlists
            .CreateAsync(target.OwnerUserId, name, cancellationToken)
            .ConfigureAwait(false);

        var projection = new WatchlistProjectionState
        {
            PlaylistId = playlistId,
            LastNameWritten = name,
        };

        if (!target.Remember(projection))
        {
            // The document was readable when this pass started and is not now. The
            // playlist exists and nothing records it, which is the one outcome worth
            // a loud line: a later pass makes a second one and only this line says
            // where the first came from.
            _logger.LogError(
                "Created playlist {PlaylistId} for user {UserId} and could not write it into their document, so it is not recorded anywhere",
                playlistId,
                target.OwnerUserId);

            return ProjectionResult.RefusedRecordUnavailable();
        }

        _logger.LogInformation(
            "Projecting the list of user {UserId} into playlist {PlaylistId}",
            target.OwnerUserId,
            playlistId);

        return ProjectionResult.Created(projection);
    }

    /// <summary>
    /// Whether the server still holds this playlist for this user.
    /// </summary>
    /// <param name="ownerUserId">The user whose playlists are asked for.</param>
    /// <param name="playlistId">The playlist the record remembers.</param>
    /// <returns>True where the server lists it.</returns>
    private bool IsStillOnTheServer(Guid ownerUserId, Guid playlistId)
    {
        foreach (var playlist in _playlists.PlaylistsOf(ownerUserId))
        {
            if (playlist.PlaylistId == playlistId)
            {
                return true;
            }
        }

        return false;
    }
}
