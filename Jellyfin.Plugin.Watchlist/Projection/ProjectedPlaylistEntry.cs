using System;

namespace Jellyfin.Plugin.Watchlist.Projection;

/// <summary>
/// One row of a playlist, as this plugin reads it back.
/// </summary>
/// <remarks>
/// Two identifiers rather than one, because a playlist row is not the item it points
/// at. The server gives every row its own entry identifier and that is what a removal
/// names; the item identifier is what the store holds. Collapsing them would make a
/// list holding one film twice impossible to take one copy out of.
///
/// The entry identifier is a string because the server's own removal takes one, and
/// nothing here parses it. What it looks like is the server's business.
/// </remarks>
public sealed record ProjectedPlaylistEntry
{
    /// <summary>
    /// Gets the identifier of this row, which is what a removal names.
    /// </summary>
    public required string EntryId { get; init; }

    /// <summary>
    /// Gets the library item the row points at.
    /// </summary>
    public required Guid ItemId { get; init; }
}
