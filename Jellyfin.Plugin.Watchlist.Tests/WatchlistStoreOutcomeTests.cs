using System;
using System.IO;
using System.Reflection;
using System.Text.Json;
using Jellyfin.Plugin.Watchlist.Store;
using Xunit;

namespace Jellyfin.Plugin.Watchlist.Tests;

/// <summary>
/// What an add and a remove report when the ordinary case does not apply: a list this
/// plugin could not read, and an item that was not on the list to begin with.
/// </summary>
/// <remarks>
/// These are the paths a caller reaches on somebody's bad day, and they are the ones
/// with the worst failure mode. An add that treats an unreadable document as an empty
/// list writes over it, and that is a downgrade dropping every entry a user had. A
/// remove that reports success against a document it never read tells a client to
/// take a title off a list that still holds it.
/// </remarks>
public sealed class WatchlistStoreOutcomeTests : IDisposable
{
    private static readonly Guid AUser = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private static readonly Guid AnItem = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");

    private readonly TemporaryDirectory _sandbox = new("watchlist-outcome");

    private string DataFolder => Path.Combine(_sandbox.FullPath, "plugin-data");

    /// <inheritdoc />
    public void Dispose()
    {
        _sandbox.Dispose();
    }

    /// <summary>
    /// A document declaring no schema version at all is refused rather than read as
    /// though it declared the current one. A hand edit that deletes the line, or a
    /// file that is JSON but is not one of these documents, must not be adopted.
    /// </summary>
    [Fact]
    public void ADocumentDeclaringNoSchemaVersionIsRefusedRatherThanAssumedCurrent()
    {
        var store = new WatchlistDocumentStore(DataFolder, new RecordingLogger());
        Place("{ \"UserId\": \"11111111-1111-1111-1111-111111111111\", \"Entries\": [] }");

        Assert.Throws<JsonException>(() => store.Read(AUser));
    }

    /// <summary>
    /// And so is a document whose schema version is there but is not a number.
    /// </summary>
    [Fact]
    public void ADocumentWhoseSchemaVersionIsNotANumberIsRefused()
    {
        var store = new WatchlistDocumentStore(DataFolder, new RecordingLogger());
        Place("{ \"SchemaVersion\": \"one\", \"UserId\": \"11111111-1111-1111-1111-111111111111\", \"Entries\": [] }");

        Assert.Throws<JsonException>(() => store.Read(AUser));
    }

    /// <summary>
    /// And text that is JSON but is not an object at all.
    /// </summary>
    [Fact]
    public void ADocumentThatIsNotAJsonObjectIsRefused()
    {
        var store = new WatchlistDocumentStore(DataFolder, new RecordingLogger());
        Place("[]");

        Assert.Throws<JsonException>(() => store.Read(AUser));
    }

    /// <summary>
    /// Adding to a list this plugin could not read is refused, and it writes nothing.
    /// Treating the refusal as an empty list would replace the file with a list of
    /// one, which is every entry the user had, gone.
    /// </summary>
    [Fact]
    public void AddingToAListThatCouldNotBeReadIsRefusedAndWritesNothing()
    {
        var store = new WatchlistDocumentStore(DataFolder, new RecordingLogger());
        var path = PlaceUnreadable();
        var before = File.ReadAllBytes(path);

        var result = store.Add(AUser, AnEntry(), maxEntriesPerUser: 10);

        Assert.Equal(WatchlistAddOutcome.RefusedListUnavailable, result.Outcome);
        Assert.Equal(before, File.ReadAllBytes(path));
        Assert.Equal(new[] { Path.GetFileName(path) }, FileNamesInTheDataFolder());
    }

    /// <summary>
    /// Removing from a list this plugin could not read is refused in the same way, and
    /// says the list was not available rather than that the item was not there.
    /// </summary>
    [Fact]
    public void RemovingFromAListThatCouldNotBeReadIsRefusedAndWritesNothing()
    {
        var store = new WatchlistDocumentStore(DataFolder, new RecordingLogger());
        var path = PlaceUnreadable();
        var before = File.ReadAllBytes(path);

        var result = store.Remove(AUser, AnItem);

        Assert.False(result.ListWasAvailable);
        Assert.False(result.Removed);
        Assert.Equal(before, File.ReadAllBytes(path));
        Assert.Equal(new[] { Path.GetFileName(path) }, FileNamesInTheDataFolder());
    }

    /// <summary>
    /// The near miss for both of those. The same two calls against a list that reads
    /// normally do what they say, so the refusals above are about the document being
    /// unreadable and not about the calls themselves.
    /// </summary>
    [Fact]
    public void TheSameTwoCallsAgainstAReadableListSucceed()
    {
        var store = new WatchlistDocumentStore(DataFolder, new RecordingLogger());

        var added = store.Add(AUser, AnEntry(), maxEntriesPerUser: 10);
        var removed = store.Remove(AUser, AnItem);

        Assert.Equal(WatchlistAddOutcome.Added, added.Outcome);
        Assert.True(removed.ListWasAvailable);
        Assert.True(removed.Removed);
    }

    /// <summary>
    /// Removing an item that is not on the list changes nothing and says so, with the
    /// list reported as available. "It was not there" and "I could not look" are
    /// different answers and the caller decides differently on each.
    /// </summary>
    [Fact]
    public void RemovingAnItemThatIsNotOnTheListChangesNothingAndSaysTheListWasReadable()
    {
        var store = new WatchlistDocumentStore(DataFolder, new RecordingLogger());
        store.Add(AUser, AnEntry(), maxEntriesPerUser: 10);
        var path = store.PathFor(AUser);
        var before = File.ReadAllBytes(path);

        var result = store.Remove(AUser, Guid.Parse("aaaaaaaa-0000-0000-0000-00000000ffff"));

        Assert.True(result.ListWasAvailable);
        Assert.False(result.Removed);
        Assert.Equal(1, result.EntryCount);
        Assert.Equal(before, File.ReadAllBytes(path));
    }

    /// <summary>
    /// Each outcome describes itself in a sentence an operator can read, and the
    /// unavailable one names no numbers, because the numbers it would name are the
    /// zeroes it was given rather than anything about the user's list.
    /// </summary>
    [Fact]
    public void EachAddOutcomeDescribesItself()
    {
        var store = new WatchlistDocumentStore(DataFolder, new RecordingLogger());

        var added = store.Add(AUser, AnEntry(), maxEntriesPerUser: 10).Describe();

        Assert.Contains("Added.", added, StringComparison.Ordinal);
        Assert.Contains("1", added, StringComparison.Ordinal);
        Assert.Contains("10", added, StringComparison.Ordinal);

        PlaceUnreadable();
        var unavailable = store.Add(AUser, AnEntry(), maxEntriesPerUser: 10).Describe();

        Assert.Contains("could not be read", unavailable, StringComparison.Ordinal);
        Assert.DoesNotContain("0", unavailable, StringComparison.Ordinal);
    }

    private static WatchlistEntry AnEntry() => new()
    {
        ItemId = AnItem,
        Kind = WatchlistItemKind.Movie,
        AddedAt = new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero),
        Source = WatchlistEntrySource.Api,
    };

    private string[] FileNamesInTheDataFolder() =>
        Array.ConvertAll(Directory.GetFiles(DataFolder), Path.GetFileName)!;

    /// <summary>
    /// Puts a document this plugin refuses to read where this user's document goes.
    /// The committed fixture from the future is used rather than a broken file, so
    /// what makes it unreadable is the rule under test in M2 and not a parse error.
    /// </summary>
    /// <returns>The path it was written to.</returns>
    private string PlaceUnreadable()
    {
        var assembly = Assembly.GetExecutingAssembly();
        const string Resource = "fixture/watchlist-document-from-the-future.json";
        using var stream = assembly.GetManifestResourceStream(Resource)
            ?? throw new InvalidOperationException(
                Resource + " is not embedded in the test assembly. The assembly carries: "
                + string.Join(", ", assembly.GetManifestResourceNames()));

        using var reader = new StreamReader(stream);

        return Place(reader.ReadToEnd());
    }

    private string Place(string text)
    {
        Directory.CreateDirectory(DataFolder);

        var path = new WatchlistDocumentStore(DataFolder).PathFor(AUser);
        File.WriteAllText(path, text);

        return path;
    }
}
