namespace Jellyfin.Plugin.Watchlist.Projection;

/// <summary>
/// What making sure a target has a playlist did.
/// </summary>
public enum ProjectionOutcome
{
    /// <summary>
    /// The target already had a playlist and it is still on the server. Nothing was
    /// created and nothing was written.
    /// </summary>
    AlreadyProjected = 0,

    /// <summary>
    /// A playlist was created for this target and its identity was written into the
    /// target's record.
    /// </summary>
    Created = 1,

    /// <summary>
    /// The record that remembers this target's playlist could not be read or could not
    /// be written, so nothing further was done with it.
    /// </summary>
    RefusedRecordUnavailable = 2,

    /// <summary>
    /// The target already had a playlist, its label was still the one this plugin
    /// wrote, and the configured name had moved, so the playlist was renamed.
    /// </summary>
    Renamed = 3,

    /// <summary>
    /// A playlist the owner already had, carrying the configured name, was taken over
    /// as this target's projected list and its rows were read onto the list.
    /// </summary>
    Adopted = 4,
}
