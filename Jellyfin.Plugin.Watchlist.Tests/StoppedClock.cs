using System;

namespace Jellyfin.Plugin.Watchlist.Tests;

/// <summary>
/// A clock that answers one instant, for as long as the test wants it to.
/// </summary>
/// <remarks>
/// The suite is not allowed to read the machine's clock, and this is why the plugin
/// takes one rather than calling one. A test that stamped an entry from the real clock
/// could only assert that the stamp was near something, which is a test that fails on
/// a slow machine and at a date boundary and passes everywhere else.
///
/// It moves only when a test moves it, so two entries added in one test carry the same
/// instant unless the test says otherwise, which is what makes an assertion about
/// ordering an assertion about the code rather than about how fast it ran.
/// </remarks>
internal sealed class StoppedClock : TimeProvider
{
    private DateTimeOffset _now;

    /// <summary>
    /// Initializes a new instance of the <see cref="StoppedClock"/> class.
    /// </summary>
    /// <param name="now">The instant it answers with until it is moved.</param>
    public StoppedClock(DateTimeOffset now)
    {
        _now = now;
    }

    /// <inheritdoc />
    public override DateTimeOffset GetUtcNow() => _now;

    /// <summary>
    /// Moves it forward.
    /// </summary>
    /// <param name="by">How far.</param>
    public void Advance(TimeSpan by) => _now += by;
}
