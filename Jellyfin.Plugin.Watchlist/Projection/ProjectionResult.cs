using Jellyfin.Plugin.Watchlist.Store;

namespace Jellyfin.Plugin.Watchlist.Projection;

/// <summary>
/// What making sure a target has a playlist did, and which playlist that is.
/// </summary>
/// <remarks>
/// The two answers are separate because a caller needs both and they come apart. A
/// reconciler wants the playlist and does not care which pass made it; a log line and
/// an adoption rule want to know whether this pass created one.
/// </remarks>
public sealed record ProjectionResult
{
    private ProjectionResult(ProjectionOutcome outcome, WatchlistProjectionState? projection)
    {
        Outcome = outcome;
        Projection = projection;
    }

    /// <summary>
    /// Gets what happened.
    /// </summary>
    public ProjectionOutcome Outcome { get; }

    /// <summary>
    /// Gets the playlist this target is projected into, or null where the pass
    /// produced none.
    /// </summary>
    public WatchlistProjectionState? Projection { get; }

    /// <summary>
    /// The target already had a playlist and it is still there.
    /// </summary>
    /// <param name="projection">What the record remembered.</param>
    /// <returns>The result.</returns>
    public static ProjectionResult AlreadyProjected(WatchlistProjectionState projection) =>
        new(ProjectionOutcome.AlreadyProjected, projection);

    /// <summary>
    /// A playlist was created for this target and written into its record.
    /// </summary>
    /// <param name="projection">The playlist and the name it was created under.</param>
    /// <returns>The result.</returns>
    public static ProjectionResult Created(WatchlistProjectionState projection) =>
        new(ProjectionOutcome.Created, projection);

    /// <summary>
    /// The record could not be read or could not be written. Nothing is projected, and
    /// a caller may not treat that as a target with an empty playlist.
    /// </summary>
    /// <returns>The result.</returns>
    public static ProjectionResult RefusedRecordUnavailable() =>
        new(ProjectionOutcome.RefusedRecordUnavailable, null);
}
