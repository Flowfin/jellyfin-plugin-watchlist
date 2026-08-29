using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Watchlist.Projection;

namespace Jellyfin.Plugin.Watchlist.Tests;

/// <summary>
/// A server's playlists, held in memory, behind the seam the projection speaks
/// through.
/// </summary>
/// <remarks>
/// <para>
/// This is what lets the projection be exercised with no server present. It is the
/// interface on #82 and not a mock of the server's own type: nothing here names a
/// server playlist type, and a guard over the plugin's sources refuses one outside the
/// single adapter anyway.
/// </para>
/// <para>
/// It counts what it was asked to do as well as answering, because most of what a
/// projection owes is about the calls it did NOT make. A pass that creates a second
/// playlist and a pass that creates none look the same from the outside unless
/// something counts.
/// </para>
/// </remarks>
internal sealed class APlaylistServerOf : IPlaylistGateway
{
    private readonly Dictionary<Guid, List<ProjectedPlaylist>> _byOwner = [];
    private readonly Dictionary<Guid, List<ProjectedPlaylistEntry>> _rows = [];
    private readonly List<string> _calls = [];

    private int _minted;

    /// <summary>
    /// Gets every call this was asked to make, in order.
    /// </summary>
    public IReadOnlyList<string> Calls => _calls;

    /// <summary>
    /// Gets how many playlists were created through this.
    /// </summary>
    public int Creations => _calls.Count(call => call.StartsWith("create ", StringComparison.Ordinal));

    /// <summary>
    /// Puts a playlist on the server without going through a creation, so a test can
    /// start from a server that already holds one.
    /// </summary>
    /// <param name="ownerUserId">Whose playlist it is.</param>
    /// <param name="playlistId">The playlist.</param>
    /// <param name="name">What it is called.</param>
    public void AlreadyHolds(Guid ownerUserId, Guid playlistId, string name)
    {
        Owned(ownerUserId).Add(new ProjectedPlaylist { PlaylistId = playlistId, Name = name });
    }

    /// <summary>
    /// Puts rows on a playlist without going through an add, so a test can start from a
    /// list somebody made by hand.
    /// </summary>
    /// <param name="playlistId">The playlist.</param>
    /// <param name="itemIds">The library items its rows point at, in order.</param>
    public void Rows(Guid playlistId, params Guid[] itemIds)
    {
        var rows = new List<ProjectedPlaylistEntry>();

        for (var at = 0; at < itemIds.Length; at++)
        {
            rows.Add(new ProjectedPlaylistEntry
            {
                EntryId = string.Format(CultureInfo.InvariantCulture, "row-{0}", at),
                ItemId = itemIds[at],
            });
        }

        _rows[playlistId] = rows;
    }

    /// <summary>
    /// Takes a playlist off the server the way a user deleting it from a client would,
    /// leaving whatever remembered it pointing at nothing.
    /// </summary>
    /// <param name="ownerUserId">Whose playlist it was.</param>
    /// <param name="playlistId">The playlist.</param>
    public void NoLongerHolds(Guid ownerUserId, Guid playlistId)
    {
        Owned(ownerUserId).RemoveAll(playlist => playlist.PlaylistId == playlistId);
    }

    /// <inheritdoc />
    public IReadOnlyList<ProjectedPlaylist> PlaylistsOf(Guid userId)
    {
        Record("list", userId);

        return Owned(userId);
    }

    /// <inheritdoc />
    public IReadOnlyList<ProjectedPlaylistEntry> EntriesOf(Guid playlistId, Guid userId)
    {
        Record("entries", playlistId);

        return _rows.TryGetValue(playlistId, out var rows) ? rows : [];
    }

    /// <inheritdoc />
    public Task<Guid> CreateAsync(Guid userId, string name, CancellationToken cancellationToken)
    {
        _minted++;
        var playlistId = Guid.Parse(string.Format(
            CultureInfo.InvariantCulture,
            "bbbbbbbb-0000-0000-0000-{0:D12}",
            _minted));

        _calls.Add(string.Format(CultureInfo.InvariantCulture, "create {0} {1} {2}", userId, playlistId, name));
        Owned(userId).Add(new ProjectedPlaylist { PlaylistId = playlistId, Name = name });

        return Task.FromResult(playlistId);
    }

    /// <inheritdoc />
    public Task RenameAsync(Guid playlistId, Guid userId, string name, CancellationToken cancellationToken)
    {
        _calls.Add(string.Format(CultureInfo.InvariantCulture, "rename {0} {1}", playlistId, name));

        var owned = Owned(userId);
        var at = owned.FindIndex(playlist => playlist.PlaylistId == playlistId);

        if (at >= 0)
        {
            owned[at] = owned[at] with { Name = name };
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task AddAsync(Guid playlistId, Guid userId, IReadOnlyCollection<Guid> itemIds, int? position, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(itemIds);

        _calls.Add(string.Format(CultureInfo.InvariantCulture, "add {0} {1}", playlistId, itemIds.Count));

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task RemoveAsync(Guid playlistId, IReadOnlyCollection<string> entryIds, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(entryIds);

        _calls.Add(string.Format(CultureInfo.InvariantCulture, "remove {0} {1}", playlistId, entryIds.Count));

        return Task.CompletedTask;
    }

    private void Record(string verb, Guid subject)
    {
        _calls.Add(string.Format(CultureInfo.InvariantCulture, "{0} {1}", verb, subject));
    }

    private List<ProjectedPlaylist> Owned(Guid ownerUserId)
    {
        if (!_byOwner.TryGetValue(ownerUserId, out var owned))
        {
            owned = [];
            _byOwner[ownerUserId] = owned;
        }

        return owned;
    }
}
