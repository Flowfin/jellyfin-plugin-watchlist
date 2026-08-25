using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Jellyfin.Plugin.Watchlist.Store;
using Xunit;

namespace Jellyfin.Plugin.Watchlist.Tests;

/// <summary>
/// What the store does with a document whose schema version is not the one it writes.
/// </summary>
/// <remarks>
/// Downgrading a plugin is one click, and the failure mode of guessing at a shape a
/// newer version wrote is a rewritten file with entries dropped. The three fixtures
/// here differ from each other in the version number and in nothing else, so what a
/// test proves is about the version and not about the rest of the document.
/// </remarks>
public sealed class WatchlistDocumentVersionTests : IDisposable
{
    private static readonly Guid AUser = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private readonly TemporaryDirectory _sandbox = new("watchlist-version");

    private string DataFolder => Path.Combine(_sandbox.FullPath, "plugin-data");

    /// <inheritdoc />
    public void Dispose()
    {
        _sandbox.Dispose();
    }

    /// <summary>
    /// The fixtures are one document at four versions. If they ever differ in anything
    /// else, every other test in this file is proving something other than what it
    /// says.
    /// </summary>
    /// <remarks>
    /// Version 2 added the per-user preferences block and these fixtures still differ
    /// in the number alone, which is the point rather than an oversight: the block is
    /// written only for a user who answered something, so the version 2 shape of a
    /// user who answered nothing is byte for byte the version 1 shape.
    /// </remarks>
    [Fact]
    public void TheFixturesDifferOnlyInTheVersionNumber()
    {
        var current = Fixture("watchlist-document-v2.json");

        Assert.Equal(current, Fixture("watchlist-document-from-the-future.json").Replace("\"SchemaVersion\": 3", "\"SchemaVersion\": 2", StringComparison.Ordinal));
        Assert.Equal(current, Fixture("watchlist-document-v1.json").Replace("\"SchemaVersion\": 1", "\"SchemaVersion\": 2", StringComparison.Ordinal));
        Assert.Equal(current, Fixture("watchlist-document-v0.json").Replace("\"SchemaVersion\": 0", "\"SchemaVersion\": 2", StringComparison.Ordinal));
    }

    /// <summary>
    /// A document from the future is refused. It is not parsed, the list reads as
    /// unavailable rather than as empty, and the file is not touched.
    /// </summary>
    [Fact]
    public void ADocumentFromTheFutureIsRefusedAndTheFileIsUnchanged()
    {
        var log = new RecordingLogger();
        var store = new WatchlistDocumentStore(DataFolder, log);
        var path = Place("watchlist-document-from-the-future.json");
        var before = File.ReadAllBytes(path);

        var result = store.Read(AUser);

        Assert.False(result.IsAvailable);
        Assert.Null(result.Document);
        Assert.Equal(3, result.StoredSchemaVersion);
        Assert.Equal(before, File.ReadAllBytes(path));
    }

    /// <summary>
    /// And it says so once, naming the file and both versions, because an
    /// administrator looking at a user whose list went blank has nothing else to go on.
    /// </summary>
    [Fact]
    public void TheRefusalIsReportedOnceWithTheFileAndBothVersions()
    {
        var log = new RecordingLogger();
        var store = new WatchlistDocumentStore(DataFolder, log);
        var path = Place("watchlist-document-from-the-future.json");

        store.Read(AUser);

        var line = Assert.Single(log.Lines);
        Assert.Contains(path, line, StringComparison.Ordinal);
        Assert.Contains("version 3", line, StringComparison.Ordinal);
        Assert.Contains("version 2", line, StringComparison.Ordinal);
        Assert.StartsWith("Error", line, StringComparison.Ordinal);
    }

    /// <summary>
    /// The near miss. The same document at the version this plugin writes is read
    /// normally, so the refusal above is about the version number and not about
    /// anything else in the file.
    /// </summary>
    [Fact]
    public void TheSameDocumentAtTheCurrentVersionIsRead()
    {
        var store = new WatchlistDocumentStore(DataFolder, new RecordingLogger());
        Place("watchlist-document-v2.json");

        var result = store.Read(AUser);

        Assert.True(result.IsAvailable);
        Assert.Equal(3, result.Document!.Entries.Count);
        Assert.Equal(2, result.Document.SchemaVersion);
    }

    /// <summary>
    /// A document from an older version is brought up to the current one in memory,
    /// with its entries intact.
    /// </summary>
    [Fact]
    public void AnOlderDocumentIsBroughtForwardInMemoryWithItsEntriesIntact()
    {
        var store = new WatchlistDocumentStore(DataFolder, new RecordingLogger());
        Place("watchlist-document-v0.json");

        var result = store.Read(AUser);

        Assert.True(result.IsAvailable);
        Assert.Equal(WatchlistDocument.CurrentSchemaVersion, result.Document!.SchemaVersion);
        Assert.Equal(3, result.Document.Entries.Count);
        Assert.Equal(Guid.Parse("aaaaaaaa-0000-0000-0000-000000000002"), result.Document.Entries[1].ItemId);
        Assert.Equal(WatchlistItemKind.Series, result.Document.Entries[1].Kind);
    }

    /// <summary>
    /// In memory and nowhere else. A read alone never rewrites the tree, so opening a
    /// list does not turn every older document on a server into a written file, and a
    /// downgrade after an upgrade does not find its documents already moved.
    /// </summary>
    /// <param name="fixture">The document to place.</param>
    [Theory]
    [InlineData("watchlist-document-v0.json")]
    [InlineData("watchlist-document-v1.json")]
    [InlineData("watchlist-document-v2.json")]
    [InlineData("watchlist-document-from-the-future.json")]
    public void AReadNeverRewritesTheFile(string fixture)
    {
        var store = new WatchlistDocumentStore(DataFolder, new RecordingLogger());
        var path = Place(fixture);
        var before = File.ReadAllBytes(path);

        store.Read(AUser);
        store.Read(AUser);

        Assert.Equal(before, File.ReadAllBytes(path));
        Assert.Equal(new[] { Path.GetFileName(path) }, Directory.GetFiles(DataFolder).Select(Path.GetFileName).ToArray());
    }

    /// <summary>
    /// Puts a fixture on disk where the store expects this user's document.
    /// </summary>
    /// <param name="fixture">The fixture file name.</param>
    /// <returns>The path it was written to.</returns>
    private string Place(string fixture)
    {
        Directory.CreateDirectory(DataFolder);

        var path = new WatchlistDocumentStore(DataFolder).PathFor(AUser);
        File.WriteAllText(path, Fixture(fixture));

        return path;
    }

    private static string Fixture(string name)
    {
        var assembly = Assembly.GetExecutingAssembly();
        var resource = "fixture/" + name;
        using var stream = assembly.GetManifestResourceStream(resource)
            ?? throw new InvalidOperationException(
                resource + " is not embedded in the test assembly. The assembly carries: "
                + string.Join(", ", assembly.GetManifestResourceNames()));

        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
