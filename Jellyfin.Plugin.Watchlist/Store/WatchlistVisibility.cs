using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Jellyfin.Plugin.Watchlist.Store;

/// <summary>
/// The one gate every read path goes through, and the one place the rule for an entry
/// whose item no longer resolves is written.
/// </summary>
/// <remarks>
/// THE RULE: an entry whose item does not resolve is skipped on read and left in the
/// document. It is never dropped, and never dropped by one caller and kept by
/// another.
///
/// Skipping rather than dropping, because the two ways an item stops resolving look
/// identical from here and only one of them is permanent. A user deleting a film and
/// a library rebuild after a detached drive both make an identifier stop resolving,
/// and dropping the entry turns the second one into data loss a user cannot undo. The
/// entry costs a few dozen bytes to keep and the identifier is what a later
/// reattachment would need.
///
/// One place rather than one per caller, because "skipped on read" is only true if
/// every read does it. The API, the projection and the scheduled reconciliation are
/// three readers, and three copies of this rule are three chances to keep two of them
/// in step and lose the third. docs/unresolvable-entries.md argues the choice.
/// </remarks>
public static class WatchlistVisibility
{
    /// <summary>
    /// The entries a caller may show, which is those whose items still resolve.
    /// </summary>
    /// <param name="entries">Everything the document holds.</param>
    /// <param name="resolver">What decides whether an item is still there.</param>
    /// <param name="userId">Whose list this is, for the one line below.</param>
    /// <param name="logger">Where the count goes, or null for no report.</param>
    /// <returns>The entries that resolve, in the order the document holds them.</returns>
    /// <remarks>
    /// One line per pass, and only when something was skipped, so a server with no
    /// deleted media says nothing. The line carries a count and a user, and there is no
    /// title in it to leak because the document holds no title to begin with.
    /// </remarks>
    public static IReadOnlyList<WatchlistEntry> Resolvable(
        IReadOnlyList<WatchlistEntry> entries,
        IWatchlistItemResolver resolver,
        Guid userId,
        ILogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentNullException.ThrowIfNull(resolver);

        var resolvable = entries.Where(entry => resolver.Exists(entry.ItemId)).ToArray();
        var skipped = entries.Count - resolvable.Length;

        if (skipped > 0)
        {
            (logger ?? NullLogger.Instance).LogInformation(
                "Skipped {SkippedCount} watchlist entries for user {UserId} because their items no longer resolve. Nothing was removed from the stored list.",
                skipped,
                userId);
        }

        return resolvable;
    }
}
