using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Playlists;
using MediaBrowser.Model.Playlists;

namespace Jellyfin.Plugin.Watchlist.Projection;

/// <summary>
/// The gateway that asks the server. It is the only thing in this plugin that knows
/// there is a playlist type.
/// </summary>
/// <remarks>
/// Everything above it takes <see cref="IPlaylistGateway"/>, so the projection and its
/// tests never touch a server playlist type, and a guard over the plugin's own sources
/// refuses one anywhere else. The register that guard reads names this file, which is
/// how the scan is told where the seam is.
///
/// This is the implementation for the 10.11 line, the one line this tree builds an
/// artifact for. The 12.0 line spells one call differently and gets its own
/// implementation when there is a package set to compile it against; which one an
/// artifact uses is fixed by the target it was built for rather than by asking the
/// running server, and that selection arrives with the second artifact.
///
/// The coverage floor names this file, and the test project carries the measurement
/// behind that entry rather than an assertion of it.
/// </remarks>
public sealed class ServerPlaylistGateway : IPlaylistGateway
{
    private readonly IPlaylistManager _playlists;
    private readonly ILibraryManager _library;

    /// <summary>
    /// Initializes a new instance of the <see cref="ServerPlaylistGateway"/> class.
    /// </summary>
    /// <param name="playlists">The server's playlists.</param>
    /// <param name="library">The server's library, which is where an item is removed
    /// and therefore where a playlist is removed.</param>
    /// <remarks>
    /// TWO MANAGERS FOR ONE SEAM, because the server splits the operations that way. A
    /// playlist is made, renamed, filled and emptied through the playlist manager, and
    /// it is DELETED as a library item like anything else - the playlist manager on
    /// both supported lines offers a removal of every playlist a user has and none of
    /// one playlist:
    ///
    ///     git show v10.11.11:MediaBrowser.Controller/Playlists/IPlaylistManager.cs | grep -n 'RemovePlaylistsAsync'
    ///     105:        Task RemovePlaylistsAsync(Guid userId);
    ///
    /// Taking that one would delete every playlist the administrator owns to be rid of
    /// the one this plugin made, which is the reason the second manager is here rather
    /// than an argument about layering.
    /// </remarks>
    public ServerPlaylistGateway(IPlaylistManager playlists, ILibraryManager library)
    {
        _playlists = playlists;
        _library = library;
    }

    /// <inheritdoc />
    /// <remarks>
    /// False on this line, and the constant is the whole of the answer. The 10.11
    /// playlist interface takes no insert position, so nothing this implementation
    /// could do with one would put an item anywhere but the end.
    /// </remarks>
    public bool CanInsertAtAPosition => false;

    /// <inheritdoc />
    public IReadOnlyList<ProjectedPlaylist> PlaylistsOf(Guid userId) => _playlists
        .GetPlaylists(userId)
        .Select(playlist => new ProjectedPlaylist
        {
            PlaylistId = playlist.Id,
            Name = playlist.Name ?? string.Empty,
        })
        .ToList();

    /// <inheritdoc />
    /// <remarks>
    /// A row's own identifier and the item it points at are two different values here,
    /// and the server's removal takes the first. A row the server holds with no such
    /// identifier is left out rather than reported with the item's, because a removal
    /// spelled with the wrong one takes out whichever row happens to match.
    /// </remarks>
    public IReadOnlyList<ProjectedPlaylistEntry> EntriesOf(Guid playlistId, Guid userId)
    {
        var playlist = _playlists.GetPlaylistForUser(playlistId, userId);

        if (playlist is null)
        {
            return [];
        }

        return playlist
            .GetManageableItems()
            .Where(row => row.Item1.ItemId.HasValue)
            .Select(row => new ProjectedPlaylistEntry
            {
                EntryId = row.Item1.ItemId!.Value.ToString("N", CultureInfo.InvariantCulture),
                ItemId = row.Item2.Id,
            })
            .ToList();
    }

    /// <inheritdoc />
    public async Task<Guid> CreateAsync(Guid userId, string name, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var created = await _playlists
            .CreatePlaylist(new PlaylistCreationRequest { Name = name, UserId = userId })
            .ConfigureAwait(false);

        return Guid.Parse(created.Id, CultureInfo.InvariantCulture);
    }

    /// <inheritdoc />
    public async Task RenameAsync(Guid playlistId, Guid userId, string name, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        await _playlists
            .UpdatePlaylist(new PlaylistUpdateRequest { Id = playlistId, UserId = userId, Name = name })
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    /// <remarks>
    /// The server's own word for it is that the playlist is public, and its visibility
    /// rule reads that first: a public playlist is visible to every user, and only a
    /// playlist that is not public falls through to the list of named shares. So one
    /// value answers this and nothing here walks a share list.
    ///
    /// A playlist this user does not own answers false, because the read that finds it
    /// is asked as them.
    /// </remarks>
    public bool IsOpenToEveryone(Guid playlistId, Guid ownerUserId) =>
        _playlists.GetPlaylistForUser(playlistId, ownerUserId)?.OpenAccess == true;

    /// <inheritdoc />
    /// <remarks>
    /// One field on the update the rename already uses. The users of the playlist are
    /// not named in this request and are therefore not changed, which is the half that
    /// matters: this makes a playlist readable by everybody and gives nobody permission
    /// to edit it.
    /// </remarks>
    public async Task OpenToEveryoneAsync(Guid playlistId, Guid ownerUserId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        await _playlists
            .UpdatePlaylist(new PlaylistUpdateRequest { Id = playlistId, UserId = ownerUserId, Public = true })
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    /// <remarks>
    /// THIS LINE CANNOT HONOUR A POSITION AND IT APPENDS INSTEAD: the 10.11 playlist
    /// interface takes no insert position, so a position handed to this implementation
    /// is discarded and the items go on the end. What that costs for ordering is #19's,
    /// and it is stated here rather than left for a caller to discover from a list that
    /// came back in another order than the one it asked for.
    /// </remarks>
    public async Task AddAsync(Guid playlistId, Guid userId, IReadOnlyCollection<Guid> itemIds, int? position, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        await _playlists
            .AddItemToPlaylistAsync(playlistId, itemIds, userId)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task RemoveAsync(Guid playlistId, IReadOnlyCollection<string> entryIds, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        await _playlists
            .RemoveItemFromPlaylistAsync(playlistId.ToString("N", CultureInfo.InvariantCulture), entryIds)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    /// <remarks>
    /// THE LOOK-UP IS ASKED AS THE OWNER, which is the same question every other read
    /// on this seam asks, so a playlist that is not this owner's is not found and is
    /// not deleted. That is what makes the false answer safe: this cannot reach past
    /// the one list it was handed.
    ///
    /// The deletion is the server's own, with the same options its library controller
    /// uses for an item a user asked to delete:
    ///
    ///     git show v10.11.11:Jellyfin.Api/Controllers/LibraryController.cs | sed -n '385,388p'
    ///         _libraryManager.DeleteItem(
    ///             item,
    ///             new DeleteOptions { DeleteFileLocation = true },
    ///             true);
    ///
    /// The file location is a playlist's own directory under the server's data, which
    /// is the file the playlist IS rather than any media it points at; leaving it would
    /// put a directory on the server for a playlist that is gone. Nothing here reaches
    /// a media file, because a playlist holds links and not media.
    ///
    /// It is synchronous on both supported lines and is awaited nowhere, so the task
    /// this returns is already complete when it is handed back.
    /// </remarks>
    public Task<bool> DeleteAsync(Guid playlistId, Guid ownerUserId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var playlist = _playlists.GetPlaylistForUser(playlistId, ownerUserId);

        if (playlist is null)
        {
            return Task.FromResult(false);
        }

        _library.DeleteItem(playlist, new DeleteOptions { DeleteFileLocation = true }, true);

        return Task.FromResult(true);
    }
}
