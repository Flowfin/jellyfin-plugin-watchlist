using System;
using System.Collections.Generic;
using Jellyfin.Plugin.Watchlist.Api;
using Jellyfin.Plugin.Watchlist.Store;

namespace Jellyfin.Plugin.Watchlist.Tests;

/// <summary>
/// A describer that answers from a table and says nothing about anything else, which
/// is what lets the projection be driven with no library present.
/// </summary>
/// <remarks>
/// A row that is not in the table produces the same answer as an item the user may not
/// see, which is the interface's own rule rather than a shortcut here: an absence and a
/// refusal are one answer so that a caller cannot tell them apart.
/// </remarks>
internal sealed class ADescriberOf : IWatchlistItemDescriber
{
    private readonly Dictionary<(Guid ItemId, Guid UserId), WatchlistItemDescription> _table = [];

    /// <summary>
    /// Initializes a new instance of the <see cref="ADescriberOf"/> class.
    /// </summary>
    /// <param name="rows">What it answers, per item and user.</param>
    public ADescriberOf(params (Guid ItemId, Guid UserId, WatchlistItemKind Kind)[] rows)
    {
        foreach (var row in rows)
        {
            _table[(row.ItemId, row.UserId)] = new WatchlistItemDescription
            {
                Name = "not read by the projection",
                Kind = row.Kind,
            };
        }
    }

    /// <summary>
    /// Takes one row out, which is what a library removal looks like from above this
    /// interface: the item stops resolving for that user and nothing says why.
    /// </summary>
    /// <param name="itemId">The item.</param>
    /// <param name="userId">The user it stops resolving for.</param>
    public void NoLongerSees(Guid itemId, Guid userId) => _table.Remove((itemId, userId));

    /// <summary>
    /// Puts one back, which is a rescan that found the same media again under the same
    /// identifier.
    /// </summary>
    /// <param name="itemId">The item.</param>
    /// <param name="userId">The user it resolves for again.</param>
    /// <param name="kind">What the library says it is.</param>
    public void SeesAgain(Guid itemId, Guid userId, WatchlistItemKind kind) =>
        _table[(itemId, userId)] = new WatchlistItemDescription
        {
            Name = "not read by the projection",
            Kind = kind,
        };

    /// <inheritdoc />
    public WatchlistItemDescription? Describe(Guid itemId, Guid userId) =>
        _table.TryGetValue((itemId, userId), out var description) ? description : null;
}
