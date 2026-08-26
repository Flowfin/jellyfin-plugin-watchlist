using System;
using Jellyfin.Plugin.Watchlist.Export;

namespace Jellyfin.Plugin.Watchlist.Api;

/// <summary>
/// What happened to one entry of an imported file.
/// </summary>
public sealed record WatchlistImportEntryReport
{
    /// <summary>
    /// Gets the identifier the file carried, which is the one the exporting server
    /// used. It is here so a caller can line this report up against the file it sent
    /// rather than against the list it now has.
    /// </summary>
    public required Guid ItemId { get; init; }

    /// <summary>
    /// Gets how the entry was lined up against this server, or that it was not.
    /// </summary>
    public required WatchlistImportMatch Match { get; init; }

    /// <summary>
    /// Gets the provider whose identifier decided the match, or null where the match
    /// came from the exporting server's own identifier or where there was none.
    /// </summary>
    public required string? Provider { get; init; }

    /// <summary>
    /// Gets the item on this server the entry came out as, or null where nothing here
    /// answered to it.
    /// </summary>
    public required Guid? MatchedItemId { get; init; }

    /// <summary>
    /// Gets what the import did with it.
    /// </summary>
    public required WatchlistImportOutcome Outcome { get; init; }
}
