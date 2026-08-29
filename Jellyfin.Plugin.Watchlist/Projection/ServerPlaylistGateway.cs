using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
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

    /// <summary>
    /// Initializes a new instance of the <see cref="ServerPlaylistGateway"/> class.
    /// </summary>
    /// <param name="playlists">The server's playlists.</param>
    public ServerPlaylistGateway(IPlaylistManager playlists)
    {
        _playlists = playlists;
    }

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
}
