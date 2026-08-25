using System;
using System.Collections.Generic;
using System.Text;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Watchlist.Tests;

/// <summary>
/// A logger for the plugin type that keeps what it was told, so a test can count the
/// lines a load wrote and read the one line a repair owes.
/// </summary>
/// <remarks>
/// This is the same shape as <see cref="RecordingLogger"/> and is a second class
/// rather than a generic one because that one is typed to the store and is constructed
/// in twenty-one places across four files. Widening it would put this change into
/// tests that have nothing to do with the configuration wiring, which is the second
/// topic a change is not supposed to carry.
/// </remarks>
public sealed class RecordingPluginLogger : ILogger<Plugin>
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
