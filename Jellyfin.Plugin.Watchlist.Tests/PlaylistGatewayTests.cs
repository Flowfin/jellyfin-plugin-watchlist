using System;
using Jellyfin.Plugin.Watchlist.Projection;
using Xunit;

namespace Jellyfin.Plugin.Watchlist.Tests;

/// <summary>
/// The two shapes the seam speaks in, and the one decision inside them.
/// </summary>
/// <remarks>
/// These do not exercise the server's playlist manager and cannot: the implementation
/// over it is excluded from the coverage floor with the reason in the test project
/// file. What is pinned here is the part that is this plugin's own, and it is the part
/// a reconciler gets wrong: a row's own identifier is not the item it points at, so a
/// list holding one film twice has two rows to take one of.
/// </remarks>
public class PlaylistGatewayTests
{
    [Fact]
    public void ARowCarriesItsOwnIdentifierBesideTheItemItPointsAt()
    {
        var item = Guid.NewGuid();
        var first = new ProjectedPlaylistEntry { EntryId = "a", ItemId = item };
        var second = new ProjectedPlaylistEntry { EntryId = "b", ItemId = item };

        Assert.Equal(first.ItemId, second.ItemId);
        Assert.NotEqual(first.EntryId, second.EntryId, StringComparer.Ordinal);
        Assert.NotEqual(first, second);
    }

    [Fact]
    public void AListIsItsIdentifierAndTheNameAnAdoptionMatchesOn()
    {
        var playlistId = Guid.NewGuid();
        var list = new ProjectedPlaylist { PlaylistId = playlistId, Name = "Watchlist" };

        var renamed = list with { Name = "Watch later" };

        Assert.Equal(playlistId, renamed.PlaylistId);
        Assert.Equal("Watch later", renamed.Name);
        Assert.NotEqual(list, renamed);
    }
}
