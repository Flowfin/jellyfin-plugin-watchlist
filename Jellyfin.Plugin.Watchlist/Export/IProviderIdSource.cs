using System;
using System.Collections.Generic;

namespace Jellyfin.Plugin.Watchlist.Export;

/// <summary>
/// Answers what a library item is called outside this server.
/// </summary>
/// <remarks>
/// The store holds identifiers and nothing else, so it cannot answer this and must not
/// try. The implementation that matters asks the running server's library; the suite
/// passes one that answers from a table, which is what lets the export be built and
/// compared with no server present.
/// </remarks>
public interface IProviderIdSource
{
    /// <summary>
    /// The identifiers the item is known by, keyed by provider name.
    /// </summary>
    /// <param name="itemId">The item, as this server identifies it.</param>
    /// <returns>
    /// The identifiers, or an empty set for an item this server can no longer read.
    /// An entry whose media has been deleted still leaves in the export, carrying
    /// nothing a reader elsewhere can resolve it by, and saying so is better than
    /// dropping it silently.
    /// </returns>
    IReadOnlyDictionary<string, string> ProviderIdsFor(Guid itemId);
}
