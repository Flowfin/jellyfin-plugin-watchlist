using System;

namespace Jellyfin.Plugin.Watchlist.Store;

/// <summary>
/// What this plugin remembers about the playlist it projects a list into: which
/// playlist it is, and what this plugin last called it.
/// </summary>
/// <remarks>
/// <para>
/// The identifier is the identity and the name never is. A server appends a digit to
/// a colliding playlist directory, so two playlists with one displayed name is a
/// thing that happens, and a projection that found its list by name would adopt
/// whichever one it met first.
/// </para>
/// <para>
/// The second member is here rather than added later, and that is the whole reason
/// this is a record and not a bare identifier. A stored document refuses an unmapped
/// member, so a member added to this shape afterwards costs a schema version, an
/// upgrade step and a fixture on every installed server. Two rules already decided
/// elsewhere need this value: a configured name that moves renames a playlist only
/// where the current name is the one this plugin last wrote, and an adopted playlist
/// records the name it was adopted under so a later rename treats it like any other.
/// Both are one comparison against this member.
/// </para>
/// <para>
/// It is the name this plugin WROTE rather than the name the playlist carries now.
/// The two coming apart is exactly the case the rename rule is about: a user who
/// renamed their playlist by hand has a current name this plugin never wrote, and
/// that is how the plugin knows to leave the label alone.
/// </para>
/// </remarks>
public sealed record WatchlistProjectionState
{
    /// <summary>
    /// Gets the playlist this list is projected into.
    /// </summary>
    public required Guid PlaylistId { get; init; }

    /// <summary>
    /// Gets the name this plugin last wrote for that playlist.
    /// </summary>
    public required string LastNameWritten { get; init; }
}
