namespace Jellyfin.Plugin.Watchlist.Projection;

/// <summary>
/// What one pass took out of a playlist and put back into the list behind it.
/// </summary>
/// <remarks>
/// Two counts and no titles, for the same reason every other count in this projection
/// carries none: what a run says on a server's log is how much moved, and what moved is
/// what somebody meant to watch.
/// </remarks>
public sealed record PlaylistEditsTaken
{
    /// <summary>
    /// Gets how many entries were added to the list because somebody put them in the
    /// playlist.
    /// </summary>
    public required int Added { get; init; }

    /// <summary>
    /// Gets how many entries left the list because somebody took them out of the
    /// playlist.
    /// </summary>
    public required int Removed { get; init; }
}
