using System;
using System.Collections.Generic;

namespace Jellyfin.Plugin.Watchlist.Export;

/// <summary>
/// One list in an export, of either kind.
/// </summary>
public sealed record ExportedList
{
    /// <summary>
    /// Gets which kind of list this is.
    /// </summary>
    public required ExportedListKind Kind { get; init; }

    /// <summary>
    /// Gets the user the list belongs to, or null where no single user owns it.
    /// </summary>
    /// <remarks>
    /// A private list always names its user. A shared list names one where the record
    /// it came from holds one, and null says the export carried no owner rather than
    /// that the list has none.
    /// </remarks>
    public required Guid? OwnerUserId { get; init; }

    /// <summary>
    /// Gets the identifier of the list itself, or null for a list that has none.
    /// </summary>
    /// <remarks>
    /// A private list is identified by its user and carries null here. Whether a
    /// server has one shared list or several is not settled, so this field exists to
    /// carry an identifier where one is recorded and stays null where none is.
    /// </remarks>
    public required Guid? ListId { get; init; }

    /// <summary>
    /// Gets the name the list was shown under, or null for a list that has no name of
    /// its own. It is a label for a reader and not an identity: two exports from two
    /// servers can carry the same name for different lists.
    /// </summary>
    public required string? Name { get; init; }

    /// <summary>
    /// Gets the entries, in the order the list held them.
    /// </summary>
    public required IReadOnlyList<ExportedEntry> Entries { get; init; }
}
