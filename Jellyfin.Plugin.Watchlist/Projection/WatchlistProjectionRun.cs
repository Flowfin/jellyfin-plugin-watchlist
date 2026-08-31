using System;

namespace Jellyfin.Plugin.Watchlist.Projection;

/// <summary>
/// What one pass over every projection did, in counts.
/// </summary>
/// <remarks>
/// Counts and never titles. A scheduled run logs on a server whose log an
/// administrator reads, and a title is what somebody put on a list of their own; the
/// number of entries a pass wrote says everything an operator needs and nothing about
/// what anybody is watching. That is a privacy property of this plugin rather than a
/// preference about logging, and it is why the summary is a record of numbers rather
/// than a string assembled at the call site.
/// </remarks>
public sealed record WatchlistProjectionRun
{
    /// <summary>
    /// Gets how many users the pass looked at.
    /// </summary>
    public required int Users { get; init; }

    /// <summary>
    /// Gets how many playlists it made.
    /// </summary>
    public required int Created { get; init; }

    /// <summary>
    /// Gets how many playlist write calls it made in total, creations included.
    /// </summary>
    /// <remarks>
    /// This is the number the third condition of the pass is judged by: a run over a
    /// server whose projections are already correct is a run whose writes are zero, and
    /// a summary that reported only users would say the same thing whether it had
    /// written nothing or rebuilt every list on the server.
    /// </remarks>
    public required int Writes { get; init; }

    /// <summary>
    /// Gets how many users the pass could not reconcile, because their record could not
    /// be read or written.
    /// </summary>
    /// <remarks>
    /// Reported rather than thrown. One unreadable document is one user's list, and a
    /// pass that stopped on it would leave every user after them unreconciled until
    /// somebody noticed.
    /// </remarks>
    public required int Skipped { get; init; }
}
