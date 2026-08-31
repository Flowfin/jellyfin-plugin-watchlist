namespace Jellyfin.Plugin.Watchlist.Projection;

/// <summary>
/// What one reconciliation pass did to one playlist.
/// </summary>
/// <remarks>
/// It counts rows rather than calls on purpose. A caller wants to know whether the
/// playlist moved and by how much; how many calls that took is the gateway's business
/// and the suite's, and a count of calls here would be a number that changes when the
/// batching changes without anything a user sees changing.
///
/// <see cref="Rebuilt"/> is separate from the two counts because it is the expensive
/// outcome. A pass that removed four rows and added four is ordinary; a pass that
/// removed every row and wrote the whole list back is the order being unreachable any
/// other way, and a run of them is worth noticing.
/// </remarks>
public sealed record ReconciliationResult
{
    /// <summary>
    /// Gets how many rows were added.
    /// </summary>
    public required int Added { get; init; }

    /// <summary>
    /// Gets how many rows were removed.
    /// </summary>
    public required int Removed { get; init; }

    /// <summary>
    /// Gets a value indicating whether the playlist was emptied and written again
    /// because the order could not be reached any other way.
    /// </summary>
    public required bool Rebuilt { get; init; }
}
