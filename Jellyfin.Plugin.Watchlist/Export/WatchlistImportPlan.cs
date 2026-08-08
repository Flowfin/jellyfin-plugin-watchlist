using System.Collections.Generic;
using System.Linq;

namespace Jellyfin.Plugin.Watchlist.Export;

/// <summary>
/// What an export comes out as when it is read against this server, before anything is
/// written anywhere.
/// </summary>
/// <remarks>
/// Reading and writing are separate on purpose. This is the whole answer to "what would
/// an import of this file do here", and producing it touches no document, so it can be
/// shown to somebody before they commit to it and it can be tested without a store.
/// </remarks>
public sealed record WatchlistImportPlan
{
    /// <summary>
    /// Gets one result per entry of the list that was read, in the order the list held
    /// them.
    /// </summary>
    public required IReadOnlyList<ImportedEntryMatch> Entries { get; init; }

    /// <summary>
    /// Gets how many entries came out as an item on this server.
    /// </summary>
    public int MatchedCount => Entries.Count(entry => entry.Match != WatchlistImportMatch.Unmatched);

    /// <summary>
    /// Gets how many entries nothing here answered to. It is derived rather than
    /// recorded, so it cannot disagree with the entries above it.
    /// </summary>
    public int UnmatchedCount => Entries.Count(entry => entry.Match == WatchlistImportMatch.Unmatched);
}
