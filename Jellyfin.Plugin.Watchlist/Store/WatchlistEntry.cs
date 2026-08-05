using System;

namespace Jellyfin.Plugin.Watchlist.Store;

/// <summary>
/// One item on one user's list.
/// </summary>
/// <remarks>
/// Nothing here can be derived from the library at read time. A title, an image or
/// a path would be a second copy of something the server already owns, and it would
/// be wrong the moment the media is renamed or moved.
/// </remarks>
public sealed record WatchlistEntry
{
    /// <summary>
    /// Gets the library item this entry points at.
    /// </summary>
    public required Guid ItemId { get; init; }

    /// <summary>
    /// Gets what kind of item it is, as recorded when the entry was made.
    /// </summary>
    public required WatchlistItemKind Kind { get; init; }

    /// <summary>
    /// Gets the instant the entry was added, in UTC.
    /// </summary>
    public required DateTimeOffset AddedAt { get; init; }

    /// <summary>
    /// Gets how the entry arrived.
    /// </summary>
    public required WatchlistEntrySource Source { get; init; }
}
