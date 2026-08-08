using System;

namespace Jellyfin.Plugin.Watchlist.Export;

/// <summary>
/// What happened to one entry of an export when it was read against this server.
/// </summary>
/// <remarks>
/// One of these exists for every entry that went in, including the ones nothing here
/// answered to. An import that reported only what it matched would be shorter than the
/// list it came from, and a person comparing the two counts would have nothing to read
/// the difference off.
/// </remarks>
public sealed record ImportedEntryMatch
{
    /// <summary>
    /// Gets the entry as the export carried it, unchanged.
    /// </summary>
    public required ExportedEntry Entry { get; init; }

    /// <summary>
    /// Gets how the entry was lined up, or that it was not.
    /// </summary>
    public required WatchlistImportMatch Match { get; init; }

    /// <summary>
    /// Gets the item on this server the entry came out as, or null where nothing here
    /// answered to it.
    /// </summary>
    public required Guid? ItemId { get; init; }

    /// <summary>
    /// Gets the provider name whose identifier matched, or null where the match came
    /// from the exporting server's own identifier or where there was no match.
    /// </summary>
    /// <remarks>
    /// Kept because an entry can carry several provider identifiers and only one of
    /// them decided the answer. A person reading a surprising match wants to know
    /// which one it was, and a count of matched entries cannot tell them.
    /// </remarks>
    public required string? Provider { get; init; }
}
