using System;
using System.Collections.Generic;

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
    public const int CurrentSchemaVersion = 1;

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
}
