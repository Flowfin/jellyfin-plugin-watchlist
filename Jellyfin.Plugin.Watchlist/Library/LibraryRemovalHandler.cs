using System;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Watchlist.Projection;

namespace Jellyfin.Plugin.Watchlist.Library;

/// <summary>
/// What a library removal does to the projected playlists: one reconciliation pass,
/// however many items went.
/// </summary>
/// <remarks>
/// <para>
/// NOTHING IS WRITTEN TO A DOCUMENT HERE, AND THAT IS THE M2 RULE RATHER THAN A GAP. An
/// entry whose item no longer resolves is skipped on read and left in the document,
/// which docs/unresolvable-entries.md argues and names the one function carrying it. So
/// a removal is not a change to anybody's list; it is a change to what their list
/// resolves to, and the only thing that has to move is the playlist showing it.
/// This file takes no read of a stored list and reaches that function through nothing,
/// which is why it is outside the register of readers rather than declared in it.
/// </para>
/// <para>
/// THAT IS WHY THIS EXISTS AT ALL. Without it a removed item stays in every projected
/// playlist until the scheduled pass comes round, which is up to the configured interval
/// with the item still offered to a user who can no longer play it.
/// </para>
/// <para>
/// A SCAN RAISES THIS THOUSANDS OF TIMES AND IT COSTS TWO PASSES. A removal does not say
/// whose list it was on, so there is nothing per-item to accumulate: what a removal
/// establishes is that a pass is DUE. While one is running, further removals set the flag
/// rather than starting anything, and one more pass runs when it finishes. So a bulk
/// removal costs at most two passes rather than one per item, and the second one exists
/// because an item removed while a pass was walking may have been missed by it.
/// </para>
/// <para>
/// The bound is a property of the shape rather than of a number, and there is no clock in
/// it. A debounce would need one, and a rule that waits for a quiet period is a rule that
/// behaves differently on a slow disk; this one behaves the same everywhere and is driven
/// by the suite with no waiting at all.
/// </para>
/// </remarks>
public sealed class LibraryRemovalHandler
{
    private readonly WatchlistProjectionPass _pass;
    private readonly object _gate = new();

    private Task _running = Task.CompletedTask;
    private bool _due;

    /// <summary>
    /// Initializes a new instance of the <see cref="LibraryRemovalHandler"/> class.
    /// </summary>
    /// <param name="pass">The pass a removal asks for.</param>
    /// <exception cref="ArgumentNullException"><paramref name="pass"/> is null.</exception>
    public LibraryRemovalHandler(WatchlistProjectionPass pass)
    {
        ArgumentNullException.ThrowIfNull(pass);

        _pass = pass;
    }

    /// <summary>
    /// Gets what the pass started by the last removal is doing, so the suite can wait for
    /// it rather than sleeping.
    /// </summary>
    /// <remarks>
    /// The server raises its events on its own thread and does not wait for a plugin, so
    /// nothing on the server route reads this. It is here because the alternative in a
    /// test is a wait on the clock, which the headless rule refuses and which would be
    /// slow when it worked and flaky when it did not.
    /// </remarks>
    public Task InFlight
    {
        get
        {
            lock (_gate)
            {
                return _running;
            }
        }
    }

    /// <summary>
    /// Says that something left the library.
    /// </summary>
    /// <remarks>
    /// It returns as soon as the pass is under way or noted, because the caller is a
    /// server event handler: a scan that waited for a plugin on every item it removed
    /// would be a scan this plugin had slowed down.
    /// </remarks>
    public void SomethingWasRemoved()
    {
        lock (_gate)
        {
            if (!_running.IsCompleted)
            {
                // A pass is walking the users right now. It may already have passed the
                // user this removal matters to, so one more run is owed - but exactly
                // one, however many more removals arrive before it starts.
                _due = true;

                return;
            }

            _running = RunUntilNothingIsDueAsync();
        }
    }

    /// <summary>
    /// Runs a pass, and another if a removal arrived while it was running, until none
    /// has.
    /// </summary>
    /// <returns>The task.</returns>
    private async Task RunUntilNothingIsDueAsync()
    {
        // Off the caller's thread before anything is done, so a server raising the event
        // is not made to wait for a walk over every user on it.
        await Task.Yield();

        do
        {
            lock (_gate)
            {
                _due = false;
            }

            await _pass.RunAsync(null, CancellationToken.None).ConfigureAwait(false);
        }
        while (StillDue());
    }

    /// <summary>
    /// Whether a removal arrived while the last pass was running.
    /// </summary>
    /// <returns>True where one more pass is owed.</returns>
    private bool StillDue()
    {
        lock (_gate)
        {
            return _due;
        }
    }
}
