using System;

namespace Jellyfin.Plugin.Watchlist.Api;

/// <summary>
/// Describes a library item to one user, or says that it has nothing to tell them.
/// </summary>
/// <remarks>
/// One method rather than two, and this is the point of the shape. "The item is gone"
/// and "the item is there and this user may not see it" have to produce the same
/// answer from here, because a caller that could tell them apart could ask about
/// identifiers until it learned what sits in a library it has no access to. The
/// refusal and the absence are the same absence.
///
/// The one implementation that matters asks the server's library and its user
/// manager; the suite passes one that answers from a table, which is what lets the
/// endpoint be tested with no server present.
/// </remarks>
public interface IWatchlistItemDescriber
{
    /// <summary>
    /// What this user may be told about this item.
    /// </summary>
    /// <param name="itemId">The library item.</param>
    /// <param name="userId">The user asking.</param>
    /// <returns>The description, or null when the item does not resolve for them.</returns>
    WatchlistItemDescription? Describe(Guid itemId, Guid userId);
}
