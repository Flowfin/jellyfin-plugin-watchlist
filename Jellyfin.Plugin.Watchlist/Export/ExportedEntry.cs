using System;
using System.Collections.Generic;

namespace Jellyfin.Plugin.Watchlist.Export;

/// <summary>
/// One entry as it leaves this plugin.
/// </summary>
/// <remarks>
/// The store holds the server's own identifier for an item and nothing else, which is
/// enough for the server that wrote it and worthless anywhere else: that identifier is
/// assigned by the library and does not survive a rebuild, let alone a move to another
/// server. So an exported entry names its item twice, once the way this server knows
/// it and once the way the rest of the world does.
/// </remarks>
public sealed record ExportedEntry
{
    /// <summary>
    /// Gets the identifier the server this export came from used. A reader on another
    /// server may not resolve it and must not fail when it cannot.
    /// </summary>
    public required Guid ItemId { get; init; }

    /// <summary>
    /// Gets what kind of thing the entry points at, as it was recorded when the entry
    /// was made.
    /// </summary>
    public required Store.WatchlistItemKind Kind { get; init; }

    /// <summary>
    /// Gets the instant the entry was added, in UTC.
    /// </summary>
    public required DateTimeOffset AddedAt { get; init; }

    /// <summary>
    /// Gets the identifiers a reader can look the item up by, keyed by the provider
    /// name the server uses. Empty where the item could not be read at the moment the
    /// export ran, which is what happens to an entry whose media has been deleted.
    /// </summary>
    public required IReadOnlyDictionary<string, string> ProviderIds { get; init; }
}
