using System;
using System.Collections.Generic;
using System.Text;
using Jellyfin.Plugin.Watchlist.Store;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Watchlist.Tests;

/// <summary>
/// A logger that keeps what it was told, so a test can read the one line a refusal
/// owes without a logging framework in the suite.
/// </summary>
public sealed class RecordingLogger : ILogger<WatchlistDocumentStore>
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
