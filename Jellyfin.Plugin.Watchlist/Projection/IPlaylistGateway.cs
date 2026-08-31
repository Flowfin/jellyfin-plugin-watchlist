using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Jellyfin.Plugin.Watchlist.Projection;

/// <summary>
/// Every playlist operation this plugin performs, in this plugin's own words. It is
/// the only seam through which a server playlist type is reached.
/// </summary>
/// <remarks>
/// The difference this exists for is one parameter. Between the two server lines this
/// plugin supports, the playlist interface changed in exactly one place: the call that
/// adds items gained an insert position, and none of the request or result types
/// moved. So the seam is small today and is worth having anyway, because it is what
/// stops the second difference from spreading through the reconciler when it arrives.
///
/// <see cref="AddAsync"/> therefore takes the position as an optional value, and a
/// caller passes one or passes null without knowing which line it is running on. An
/// implementation for a line that cannot honour a position states what it does with
/// one instead of dropping it in silence.
///
/// It is deliberately narrow: what it carries is the operations the projection issues
/// and nothing else, because an interface holding a method nobody calls is a shape
/// nothing has tested against a real call. A guard over the plugin's own sources
/// refuses a server playlist type anywhere outside the one implementation, which is
/// what lets a reconciler and its tests be written with no server type in them.
/// </remarks>
public interface IPlaylistGateway
{
    /// <summary>
    /// Gets a value indicating whether <see cref="AddAsync"/> honours a position on
    /// this line, so a caller can be told rather than have to guess.
    /// </summary>
    /// <remarks>
    /// This is the answer the reconciler takes. Where it is true an item can be put
    /// where it belongs and the order is reached by inserting; where it is false the
    /// only way to an order that differs from the current one is to build the list
    /// again, and that is a cost the reconciler pays deliberately and rarely rather
    /// than on every pass.
    ///
    /// It is on the interface rather than derived from a server version, because the
    /// thing a caller needs to know is what THIS implementation does with a position.
    /// A line whose interface takes one and drops it would answer false here and be
    /// right; the version number would not.
    /// </remarks>
    bool CanInsertAtAPosition { get; }

    /// <summary>
    /// The playlists this user has, so one carrying the configured name can be adopted
    /// rather than a second one created beside it.
    /// </summary>
    /// <param name="userId">The user whose lists are wanted.</param>
    /// <returns>The playlists, which may be none.</returns>
    IReadOnlyList<ProjectedPlaylist> PlaylistsOf(Guid userId);

    /// <summary>
    /// The rows of one playlist, in the order the server holds them.
    /// </summary>
    /// <param name="playlistId">The playlist.</param>
    /// <param name="userId">The user asking, because what a playlist shows is per user.</param>
    /// <returns>The rows, which may be none.</returns>
    IReadOnlyList<ProjectedPlaylistEntry> EntriesOf(Guid playlistId, Guid userId);

    /// <summary>
    /// Creates a playlist owned by this user.
    /// </summary>
    /// <param name="userId">The owner.</param>
    /// <param name="name">The name it is created under.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The identifier of the playlist that was created.</returns>
    Task<Guid> CreateAsync(Guid userId, string name, CancellationToken cancellationToken);

    /// <summary>
    /// Renames an existing playlist, which is what the configured name moving does. It
    /// never creates a second list, because orphaning the first is the failure the
    /// rename exists against.
    /// </summary>
    /// <param name="playlistId">The playlist.</param>
    /// <param name="userId">The user the change is made as.</param>
    /// <param name="name">The name it takes.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task.</returns>
    Task RenameAsync(Guid playlistId, Guid userId, string name, CancellationToken cancellationToken);

    /// <summary>
    /// Adds items to a playlist, optionally at a position.
    /// </summary>
    /// <param name="playlistId">The playlist.</param>
    /// <param name="userId">The user the change is made as.</param>
    /// <param name="itemIds">The library items to add.</param>
    /// <param name="position">Where to insert them, or null to append.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task.</returns>
    /// <remarks>
    /// This is the one call the two server lines spell differently, and the parameter
    /// is optional here so a caller never has to know which one it is talking to. What
    /// an implementation does with a position it cannot honour is stated by that
    /// implementation.
    /// </remarks>
    Task AddAsync(Guid playlistId, Guid userId, IReadOnlyCollection<Guid> itemIds, int? position, CancellationToken cancellationToken);

    /// <summary>
    /// Removes rows from a playlist, by the row identifiers <see cref="EntriesOf"/>
    /// reported.
    /// </summary>
    /// <param name="playlistId">The playlist.</param>
    /// <param name="entryIds">The rows to remove.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task.</returns>
    Task RemoveAsync(Guid playlistId, IReadOnlyCollection<string> entryIds, CancellationToken cancellationToken);
}
