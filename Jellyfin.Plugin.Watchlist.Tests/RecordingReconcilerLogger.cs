using System;
using System.Collections.Generic;
using System.Text;
using Jellyfin.Plugin.Watchlist.Projection;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Watchlist.Tests;

/// <summary>
/// A logger for the reconciler that keeps what it was told, so a test can read the one
/// line a rebuild owes.
/// </summary>
/// <remarks>
/// The same shape as the other recording loggers here, and a class of its own for the
/// reason the second of them gives: each is typed to the thing it listens to, and one
/// generic logger would put every change to it into tests that have nothing to do with
/// the subject being changed.
/// </remarks>
internal sealed class RecordingReconcilerLogger : ILogger<WatchlistReconciler>
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
