using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Jellyfin.Plugin.Watchlist.Store;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Jellyfin.Plugin.Watchlist.Tests;

/// <summary>
/// What a read does with an entry whose item is no longer in the library. Media gets
/// deleted and libraries get rebuilt, and the two look identical from inside the
/// store.
/// </summary>
public sealed class WatchlistVisibilityTests : IDisposable
{
    private static readonly Guid AUser = Guid.Parse("55555555-5555-5555-5555-555555555555");

    private readonly TemporaryDirectory _sandbox = new("watchlist-visibility");

    private string DataFolder => Path.Combine(_sandbox.FullPath, "plugin-data");

    /// <inheritdoc />
    public void Dispose()
    {
        _sandbox.Dispose();
    }

    /// <summary>
    /// Skipped, in the order the document holds them, and nothing else touched.
    /// </summary>
    [Fact]
    public void AnEntryWhoseItemDoesNotResolveIsSkipped()
    {
        var entries = Entries(1, 2, 3, 4, 5);
        var resolver = new ResolverFor(Item(1), Item(3), Item(5));

        var visible = WatchlistVisibility.Resolvable(entries, resolver, AUser);

        Assert.Equal([Item(1), Item(3), Item(5)], visible.Select(entry => entry.ItemId));
    }

    /// <summary>
    /// And left in the document. This is the half that makes the rule a decision rather
    /// than a convenience: the stored file is byte for byte what it was, so a library
    /// that comes back brings the list back with it.
    /// </summary>
    [Fact]
    public void TheSkippedEntryStaysInTheStoredDocument()
    {
        var store = new WatchlistDocumentStore(DataFolder);
        store.Write(new WatchlistDocument
        {
            SchemaVersion = WatchlistDocument.CurrentSchemaVersion,
            UserId = AUser,
            Entries = Entries(1, 2, 3),
        });

        var path = store.PathFor(AUser);
        var before = File.ReadAllBytes(path);

        var visible = WatchlistVisibility.Resolvable(
            store.Read(AUser).Document!.Entries,
            new ResolverFor(Item(2)),
            AUser);

        Assert.Single(visible);
        Assert.Equal(before, File.ReadAllBytes(path));
        Assert.Equal(3, store.Read(AUser).Document!.Entries.Count);
    }

    /// <summary>
    /// One line per pass, naming how many were affected.
    /// </summary>
    [Fact]
    public void OnePassReportsOneLineWithTheCount()
    {
        var log = new RecordingLogger();

        WatchlistVisibility.Resolvable(Entries(1, 2, 3, 4), new ResolverFor(Item(2)), AUser, log);

        var line = Assert.Single(log.Lines);
        Assert.Contains("Skipped 3 watchlist entries", line, StringComparison.Ordinal);
        Assert.Contains(AUser.ToString(), line, StringComparison.Ordinal);
        Assert.Contains("Nothing was removed", line, StringComparison.Ordinal);
    }

    /// <summary>
    /// And no line names a title. Nothing in the document holds one, so the line
    /// carries a count and a user and nothing that came out of an entry except the fact
    /// that it did not resolve.
    /// </summary>
    [Fact]
    public void NoLineNamesAnythingFromTheEntriesThemselves()
    {
        var log = new RecordingLogger();
        var entries = Entries(1, 2, 3);

        WatchlistVisibility.Resolvable(entries, new ResolverFor(), AUser, log);

        var line = Assert.Single(log.Lines);
        foreach (var entry in entries)
        {
            Assert.DoesNotContain(entry.ItemId.ToString(), line, StringComparison.Ordinal);
            Assert.DoesNotContain(entry.AddedAt.ToString("O"), line, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// A server with nothing deleted says nothing. A line on every read would bury the
    /// one that matters.
    /// </summary>
    [Fact]
    public void APassThatSkipsNothingReportsNothing()
    {
        var log = new RecordingLogger();

        var visible = WatchlistVisibility.Resolvable(Entries(1, 2), new ResolverFor(Item(1), Item(2)), AUser, log);

        Assert.Equal(2, visible.Count);
        Assert.Empty(log.Lines);
    }

    /// <summary>
    /// A list where nothing resolves reads as empty rather than as unavailable. The
    /// user has entries and none of them can be shown, which is a different thing from
    /// a document this plugin refused to read, and #100 is where that distinction
    /// lives.
    /// </summary>
    [Fact]
    public void AListWhereNothingResolvesIsEmptyRatherThanUnavailable()
    {
        var store = new WatchlistDocumentStore(DataFolder);
        store.Write(new WatchlistDocument
        {
            SchemaVersion = WatchlistDocument.CurrentSchemaVersion,
            UserId = AUser,
            Entries = Entries(1, 2),
        });

        var read = store.Read(AUser);
        var visible = WatchlistVisibility.Resolvable(read.Document!.Entries, new ResolverFor(), AUser);

        Assert.True(read.IsAvailable);
        Assert.Empty(visible);
        Assert.Equal(2, read.Document.Entries.Count);
    }

    private static IReadOnlyList<WatchlistEntry> Entries(params int[] numbers) => numbers
        .Select(n => new WatchlistEntry
        {
            ItemId = Item(n),
            Kind = WatchlistItemKind.Movie,
            AddedAt = new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero).AddSeconds(n),
            Source = WatchlistEntrySource.Api,
        })
        .ToArray();

    private static Guid Item(int n) => Guid.Parse($"cccccccc-0000-0000-0000-{n:D12}");

    /// <summary>
    /// A resolver that answers from a set, which is the whole library a test needs.
    /// </summary>
    private sealed class ResolverFor : IWatchlistItemResolver
    {
        private readonly HashSet<Guid> _present;

        public ResolverFor(params Guid[] present) => _present = [.. present];

        public bool Exists(Guid itemId) => _present.Contains(itemId);
    }

    private sealed class RecordingLogger : ILogger
    {
        private readonly List<string> _lines = [];

        public IReadOnlyList<string> Lines => _lines;

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            ArgumentNullException.ThrowIfNull(formatter);

            _lines.Add(formatter(state, exception));
        }
    }
}
