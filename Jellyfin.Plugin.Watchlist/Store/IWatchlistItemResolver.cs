using System;

namespace Jellyfin.Plugin.Watchlist.Store;

/// <summary>
/// Answers whether a library item identifier still resolves to something.
/// </summary>
/// <remarks>
/// The store holds identifiers and nothing else, so it cannot answer this on its own
/// and must not try. The one implementation that matters asks the server's library;
/// the suite passes one that answers from a set, which is what lets the rule below be
/// tested with no server present.
/// </remarks>
public interface IWatchlistItemResolver
{
    /// <summary>
    /// Whether the item is still in the library.
    /// </summary>
    /// <param name="itemId">The item.</param>
    /// <returns>True when it resolves.</returns>
    bool Exists(Guid itemId);
}
