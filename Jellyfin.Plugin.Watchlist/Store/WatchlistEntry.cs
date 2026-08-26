using System;
using System.Text.Json.Serialization;

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

    /// <summary>
    /// Gets the user who put the entry on the list, or null where the list the entry
    /// sits on records no such thing.
    /// </summary>
    /// <remarks>
    /// A shared list is written by more than one person, so an entry on it says who
    /// put it there and every reader of the list sees that. A private list has one
    /// writer and the answer would be the user whose document it is, which is already
    /// in the document, so nothing sets this on one.
    ///
    /// Not required, and suppressed when it is null, so a private entry has no such
    /// member on disk at all rather than an explicit null in every document this
    /// plugin writes. That is what lets one entry type serve both lists: an entry
    /// moved from a private list to the shared one gains the member and one moved the
    /// other way loses it, and neither direction needs a converter or a second shape.
    /// </remarks>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Guid? AddedBy { get; init; }
}
