using System;
using System.Collections.Generic;
using System.Text;
using Jellyfin.Plugin.Watchlist.Users;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Watchlist.Tests;

/// <summary>
/// A logger for the deleted-user handler that keeps what it was told, so a test can
/// read the one line a removal owes and see that a user with no list wrote none.
/// </summary>
/// <remarks>
/// The same shape as <see cref="RecordingLogger"/> and <see cref="RecordingWatchedLogger"/>,
/// and a fourth class rather than a generic one for the reason the second of those
/// gives: the first is typed to the store and constructed in many places, and widening
/// it would put this change into tests that have nothing to do with a deleted user.
/// </remarks>
internal sealed class RecordingDeletedUserLogger : ILogger<DeletedUserHandler>
{
    private readonly List<string> _lines = [];

    /// <summary>
    /// Gets what was logged, in order, each line prefixed with its level.
    /// </summary>
    public IReadOnlyList<string> Lines => _lines;

    /// <inheritdoc />
    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull => null;

    /// <inheritdoc />
    public bool IsEnabled(LogLevel logLevel) => true;

    /// <inheritdoc />
    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        ArgumentNullException.ThrowIfNull(formatter);

        _lines.Add(new StringBuilder()
            .Append(logLevel)
            .Append(' ')
            .Append(formatter(state, exception))
            .ToString());
    }
}
