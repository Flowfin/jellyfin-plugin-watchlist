using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.Watchlist.Store;

/// <summary>
/// One user's whole list, as it is written to and read from disk.
/// </summary>
public sealed record WatchlistDocument
{
    /// <summary>
    /// The version this plugin writes. A document carrying a higher number was
    /// written by a newer plugin than the one reading it.
    /// </summary>
    public const int CurrentSchemaVersion = 4;

    /// <summary>
    /// Gets the schema version the document was written with.
    /// </summary>
    public required int SchemaVersion { get; init; }

    /// <summary>
    /// Gets the user the list belongs to. Held in the document as well as in its file
    /// name, so a document that is moved or restored under the wrong name can be
    /// recognised rather than silently adopted.
    /// </summary>
    public required Guid UserId { get; init; }

    /// <summary>
    /// Gets the entries, in the order they were written.
    /// </summary>
    public required IReadOnlyList<WatchlistEntry> Entries { get; init; }

    /// <summary>
    /// Gets this user's own answers to the settings that are theirs rather than the
    /// server's, or null where they have answered none.
    /// </summary>
    /// <remarks>
    /// Not required, and suppressed when it is null, so a user who never set anything
    /// has no block on disk at all rather than an explicit null in every document this
    /// plugin writes. The member is optional in the other direction as well: a version
    /// 2 document that carries no block reads as a user who answered nothing, which is
    /// exactly what every document upgraded from version 1 is.
    /// </remarks>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public WatchlistUserPreferences? Preferences { get; init; }

    /// <summary>
    /// Gets what this plugin remembers about the playlist this list is projected
    /// into, or null where nothing has been projected for this user yet.
    /// </summary>
    /// <remarks>
    /// Null means no playlist has ever been made for this user, which is the state
    /// every user is in until they add something. The projection is on demand: a
    /// server with a thousand users who have never touched the plugin holds a
    /// thousand documents that do not exist and no playlists at all.
    ///
    /// Suppressed when it is null for the same reason the preferences block is, so a
    /// user with no projection carries no such member on disk and their document is
    /// byte for byte the shape it had before this member existed.
    /// </remarks>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public WatchlistProjectionState? Projection { get; init; }
}
