using System;
using System.Collections.Generic;
using Jellyfin.Plugin.Watchlist.Projection;
using Jellyfin.Plugin.Watchlist.Store;

namespace Jellyfin.Plugin.Watchlist.Tests;

/// <summary>
/// A projection target that is handed its answers, so the reconciler can be driven
/// over a target that is not one user's own document.
/// </summary>
/// <remarks>
/// <para>
/// THIS IS NOT THE SHARED TARGET AND DOES NOT STAND IN FOR IT. The shared list's own
/// target is issue #84 and is not in this tree; what this carries is the SHAPE that
/// makes a target something other than a private list - an owner who is not the holder
/// of the record, and a wanted set the target decided rather than the reconciler. A
/// pass over one of these and a pass over a <see cref="UserProjectionTarget"/> from the
/// same wanted set going the same way is what the suite can say today, and it is less
/// than a pass over #84's target.
/// </para>
/// <para>
/// Everything the reconciler could branch on is a value here rather than a rule, which
/// is the point: a difference calculation that answered differently for this target
/// than for a user's own would show up as two different call sequences from one wanted
/// set.
/// </para>
/// </remarks>
internal sealed class AListProjectedInto : IProjectionTarget
{
    /// <summary>
    /// Initializes a new instance of the <see cref="AListProjectedInto"/> class.
    /// </summary>
    /// <param name="ownerUserId">Whose playlist it is, which on a list that is not one
    /// person's is not the person whose document holds the entries.</param>
    /// <param name="wanted">What the playlist should hold, in order.</param>
    public AListProjectedInto(Guid ownerUserId, params Guid[] wanted)
    {
        OwnerUserId = ownerUserId;
        Wanted = wanted;
    }

    /// <inheritdoc />
    public Guid OwnerUserId { get; }

    /// <inheritdoc />
    public string ConfiguredName => "A list several people can see";

    /// <inheritdoc />
    public IReadOnlyList<Guid> Wanted { get; }

    /// <inheritdoc />
    public bool IsRecordAvailable => true;

    public bool IsOpenToEveryone => false;

    public IProjectionTarget Reread() => this;

    /// <inheritdoc />
    public WatchlistProjectionState? Remembered => null;

    /// <inheritdoc />
    public bool Remember(WatchlistProjectionState projection) => true;

    /// <inheritdoc />
    public int Adopt(IReadOnlyList<Guid> itemIds) => 0;

    public PlaylistEditsTaken TakeEdits(IReadOnlyList<Guid> rows) =>
        new() { Added = 0, Removed = 0 };
}
