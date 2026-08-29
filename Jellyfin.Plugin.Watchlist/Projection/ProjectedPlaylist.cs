using System;

namespace Jellyfin.Plugin.Watchlist.Projection;

/// <summary>
/// A playlist this plugin can see, named and identified and nothing else.
/// </summary>
/// <remarks>
/// This carries what the projection decides with: which list to adopt rather than
/// create, and which one to rename when the setting moves. It deliberately does not
/// carry the rows. Reading a list's contents is a second call, because the reconciler
/// asks for a name match over every list a user has and then asks for the contents of
/// the one it matched, and a shape that carried both would make the cheap question
/// cost the expensive one.
/// </remarks>
public sealed record ProjectedPlaylist
{
    /// <summary>
    /// Gets the playlist's identifier.
    /// </summary>
    public required Guid PlaylistId { get; init; }

    /// <summary>
    /// Gets the name the playlist carries, which is what a user sees and what an
    /// adoption matches on.
    /// </summary>
    public required string Name { get; init; }
}
