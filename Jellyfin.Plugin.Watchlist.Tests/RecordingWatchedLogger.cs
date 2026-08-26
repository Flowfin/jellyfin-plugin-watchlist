using System;
using System.Collections.Generic;
using System.Text;
using Jellyfin.Plugin.Watchlist.Watched;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Watchlist.Tests;

/// <summary>
/// A logger for the watched handler that keeps what it was told, so a test can read
/// the one line a removal owes and see that nothing else wrote one.
/// </summary>
/// <remarks>
/// The same shape as <see cref="RecordingLogger"/> and <see cref="RecordingPluginLogger"/>,
/// and a third class rather than a generic one for the reason the second one gives:
/// the first is typed to the store and constructed in many places, and widening it
/// would put this change into tests that have nothing to do with the watched rule.
/// </remarks>
internal sealed class RecordingWatchedLogger : ILogger<WatchedRemovalHandler>
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
