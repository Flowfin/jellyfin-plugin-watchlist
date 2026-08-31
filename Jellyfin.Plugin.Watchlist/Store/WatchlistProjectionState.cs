using System;
using System.Collections.Generic;

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

    /// <summary>
    /// Gets the library items this plugin last wrote into that playlist.
    /// </summary>
    /// <remarks>
    /// <para>
    /// THIS IS WHAT SEPARATES AN EDIT SOMEBODY MADE FROM A PROJECTION THAT HAS NOT
    /// HAPPENED YET, and without it the two are indistinguishable. A row in the playlist
    /// that this plugin did not put there is somebody adding on a client. A row this
    /// plugin DID put there and that is now gone is somebody removing on a client. An
    /// entry on the list that is in neither is one no pass has projected yet. The three
    /// look identical to a reader comparing only the list against the playlist, and one
    /// of them means delete.
    /// </para>
    /// <para>
    /// Empty is not the same as nothing having been written. It is what an upgraded
    /// document carries, and it means this plugin does not know what it last wrote, so
    /// every row it meets is read as somebody's addition. That is the safe direction:
    /// the reading that could be wrong adds to a list rather than taking from one.
    /// </para>
    /// </remarks>
    public required IReadOnlyList<Guid> ProjectedItemIds { get; init; }

    /// <summary>
    /// Gets when this plugin last wrote that set, or null where it does not know.
    /// </summary>
    /// <remarks>
    /// It is recorded because the rule above has one weakness and this is the only thing
    /// that could ever narrow it: an edit made while the server was down arrives looking
    /// exactly like one made through the plugin, and nothing on either side carries a
    /// time to compare. Nothing reads this today, and it is written rather than left out
    /// so the instant exists on disk when something can use it.
    ///
    /// Null on a document that was upgraded, beside the empty set above, for the same
    /// reason: not knowing is a state, and a zero instant would be a claim.
    /// </remarks>
    public required DateTimeOffset? WrittenAt { get; init; }
}
