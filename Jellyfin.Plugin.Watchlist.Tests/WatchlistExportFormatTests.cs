using System;
using System.Collections.Generic;
using System.IO;
using Jellyfin.Plugin.Watchlist.Export;
using Jellyfin.Plugin.Watchlist.Store;
using Xunit;

namespace Jellyfin.Plugin.Watchlist.Tests;

/// <summary>
/// The export format is offered to a reader who is not this plugin and who cannot be
/// asked whether a change suits them. So the sample in docs/samples is written by this
/// suite from a store it fills, and compared byte for byte. A change to the format
/// that nobody meant shows up as a red run and a changed file, rather than as a
/// surprise for somebody who wrote a reader against it.
/// </summary>
public class WatchlistExportFormatTests
{
    private const string SampleResource = "watchlist-export.sample.json";

    /// <summary>
    /// The committed file ends with a newline, which is this tree's rule for a text
    /// file and not part of the format. The comparison adds one to what the writer
    /// produced rather than trimming one off the file, so a sample that lost its last
    /// line would still be caught.
    /// </summary>
    private const string FinalNewline = "\n";

    /// <summary>
    /// Every identifier and instant below is fixed. A sample that carried a fresh
    /// identifier or the machine clock could not be committed and compared.
    /// </summary>
    private static readonly Guid Alice = new("2f4a1e4c-0f9a-4a3f-8b21-000000000001");

    private static readonly Guid TheMovie = new("2f4a1e4c-0f9a-4a3f-8b21-000000000010");
    private static readonly Guid TheSeries = new("2f4a1e4c-0f9a-4a3f-8b21-000000000011");
    private static readonly Guid TheDeletedItem = new("2f4a1e4c-0f9a-4a3f-8b21-000000000012");
    private static readonly Guid TheSharedList = new("2f4a1e4c-0f9a-4a3f-8b21-000000000020");
    private static readonly Guid TheSharedEntry = new("2f4a1e4c-0f9a-4a3f-8b21-000000000021");

    private static readonly DateTimeOffset FirstAdd = new(2026, 3, 1, 9, 15, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset SecondAdd = new(2026, 3, 2, 20, 40, 30, TimeSpan.Zero);
    private static readonly DateTimeOffset ThirdAdd = new(2026, 3, 4, 6, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset SharedAdd = new(2026, 2, 14, 12, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// The sample committed in docs/samples is what this suite writes from a store it
    /// filled. If the two ever differ, one of them moved.
    /// </summary>
    [Fact]
    public void TheCommittedSampleIsWhatTheExporterWritesFromAFilledStore()
    {
        var written = WatchlistExportFormat.Write(BuildTheSampleExport());

        Assert.Equal(CommittedSample(), written + FinalNewline, ignoreLineEndingDifferences: false);
    }

    /// <summary>
    /// The sample is not just text this suite happens to agree with. It has to read
    /// back as the export it was written from.
    /// </summary>
    [Fact]
    public void TheCommittedSampleReadsBackAsTheExportItWasWrittenFrom()
    {
        var read = WatchlistExportFormat.Read(CommittedSample());

        Assert.NotNull(read);
        Assert.Equal(CommittedSample(), WatchlistExportFormat.Write(read) + FinalNewline, ignoreLineEndingDifferences: false);
    }

    /// <summary>
    /// Both kinds are in one file and each says which it is, so a reader that handles
    /// only one of them can skip the other rather than read a shared list as somebody's
    /// private one.
    /// </summary>
    [Fact]
    public void EachListSaysWhichKindItIsAndWhoOwnedIt()
    {
        var export = BuildTheSampleExport();

        var user = Assert.Single(export.Lists, l => l.Kind == ExportedListKind.Private);
        var shared = Assert.Single(export.Lists, l => l.Kind == ExportedListKind.Shared);

        Assert.Equal(Alice, user.OwnerUserId);
        Assert.Null(user.ListId);
        Assert.Equal(TheSharedList, shared.ListId);
    }

    /// <summary>
    /// The kind is written as a name. A number would mean that reordering an enum in
    /// this repository silently changed what every exported entry says to a reader
    /// somewhere else.
    /// </summary>
    [Fact]
    public void KindsAreWrittenAsNamesRatherThanNumbers()
    {
        var text = WatchlistExportFormat.Write(BuildTheSampleExport());

        Assert.Contains("\"Kind\": \"Private\"", text, StringComparison.Ordinal);
        Assert.Contains("\"Kind\": \"Series\"", text, StringComparison.Ordinal);
        Assert.DoesNotContain("\"Kind\": 1", text, StringComparison.Ordinal);
    }

    /// <summary>
    /// An entry the server can no longer read still leaves, carrying no provider
    /// identifier. Dropping it would make the export quietly shorter than the list it
    /// came from.
    /// </summary>
    [Fact]
    public void AnEntryThatCannotBeResolvedIsExportedWithNoProviderIdentifiers()
    {
        var export = BuildTheSampleExport();

        var user = Assert.Single(export.Lists, l => l.Kind == ExportedListKind.Private);
        var entry = Assert.Single(user.Entries, e => e.ItemId == TheDeletedItem);

        Assert.Empty(entry.ProviderIds);
        Assert.Equal(3, user.Entries.Count);
    }

    /// <summary>
    /// The promise says a later version may add fields. A reader here is the first
    /// thing that would break if it did not hold, so it is read rather than trusted.
    /// </summary>
    [Fact]
    public void AFieldALaterVersionAddedIsSkippedRatherThanRefused()
    {
        var withAnAddition = CommittedSample()
            .Replace("\"FormatVersion\": 1,", "\"FormatVersion\": 1,\n  \"SomethingAddedLater\": \"x\",", StringComparison.Ordinal);

        var read = WatchlistExportFormat.Read(withAnAddition);

        Assert.NotNull(read);
        Assert.Equal(CommittedSample(), WatchlistExportFormat.Write(read) + FinalNewline, ignoreLineEndingDifferences: false);
    }

    /// <summary>
    /// The document says a reader elsewhere should be forgiving about a kind it does
    /// not know, and that this plugin's own reader is not. The second half is a claim
    /// about code, so it is read here rather than asserted in prose.
    /// </summary>
    [Fact]
    public void ThisPluginsOwnReaderRefusesAKindItDoesNotKnow()
    {
        var withAKindFromLater = CommittedSample()
            .Replace("\"Kind\": \"Shared\"", "\"Kind\": \"SomethingAddedLater\"", StringComparison.Ordinal);

        Assert.ThrowsAny<System.Text.Json.JsonException>(() => WatchlistExportFormat.Read(withAKindFromLater));
    }

    /// <summary>
    /// The document says an export with no lists is a valid export of nothing, which is
    /// what a server with no lists produces. A reader that treated it as an error would
    /// report a fault on a server that has none.
    /// </summary>
    [Fact]
    public void AnExportCarryingNoListsIsRead()
    {
        var read = WatchlistExportFormat.Read("{\n  \"FormatVersion\": 1,\n  \"Lists\": []\n}");

        Assert.NotNull(read);
        Assert.Equal(WatchlistExport.CurrentFormatVersion, read.FormatVersion);
        Assert.Empty(read.Lists);
    }

    /// <summary>
    /// The near miss for the sentence above. The stored document refuses an unknown
    /// member, and if the export inherited that rule the test above would be proving a
    /// property the format does not have.
    /// </summary>
    [Fact]
    public void TheStoredDocumentStillRefusesAFieldItDoesNotKnow()
    {
        var document = "{\n  \"SchemaVersion\": 1,\n  \"UserId\": \"" + Alice + "\",\n  \"Entries\": [],\n  \"SomethingAddedLater\": \"x\"\n}";

        Assert.ThrowsAny<System.Text.Json.JsonException>(() => WatchlistDocumentFormat.Read(document));
    }

    /// <summary>
    /// Builds the export the sample holds. The private half comes out of a real store
    /// on disk, written through the same calls a server makes, so the sample is a
    /// reading of the store rather than a hand-written picture of it.
    ///
    /// The shared half is passed in as values. The record that holds a shared list is
    /// not built yet, so there is no store to read it from, and inventing one here
    /// would put a guess about that record into a file this repository offers as a
    /// promise.
    /// </summary>
    private static WatchlistExport BuildTheSampleExport()
    {
        using var directory = new TemporaryDirectory(nameof(WatchlistExportFormatTests));
        var store = new WatchlistDocumentStore(directory.FullPath);
        const int NoCapInTheWay = 100;

        store.Add(Alice, Entry(TheMovie, WatchlistItemKind.Movie, FirstAdd, WatchlistEntrySource.Api), NoCapInTheWay);
        store.Add(Alice, Entry(TheSeries, WatchlistItemKind.Series, SecondAdd, WatchlistEntrySource.PlaylistEdit), NoCapInTheWay);
        store.Add(Alice, Entry(TheDeletedItem, WatchlistItemKind.Movie, ThirdAdd, WatchlistEntrySource.Api), NoCapInTheWay);

        var read = store.Read(Alice);
        Assert.True(read.IsAvailable);

        var providers = new ProviderIdTable(new Dictionary<Guid, IReadOnlyDictionary<string, string>>
        {
            [TheMovie] = new Dictionary<string, string>(StringComparer.Ordinal) { ["Imdb"] = "tt0111161", ["Tmdb"] = "278" },
            [TheSeries] = new Dictionary<string, string>(StringComparer.Ordinal) { ["Tvdb"] = "81189", ["Tmdb"] = "1396" },
            [TheSharedEntry] = new Dictionary<string, string>(StringComparer.Ordinal) { ["Imdb"] = "tt0068646" },
        });

        return WatchlistExporter.Export(
        [
            WatchlistExporter.PrivateList(read.Document!, providers),
            WatchlistExporter.SharedList(
                TheSharedList,
                "Staff picks",
                null,
                [Entry(TheSharedEntry, WatchlistItemKind.Movie, SharedAdd, WatchlistEntrySource.Api)],
                providers),
        ]);
    }

    private static WatchlistEntry Entry(Guid itemId, WatchlistItemKind kind, DateTimeOffset addedAt, WatchlistEntrySource source) => new()
    {
        ItemId = itemId,
        Kind = kind,
        AddedAt = addedAt,
        Source = source,
    };

    /// <summary>
    /// The sample as the repository holds it. It is embedded from docs/samples by the
    /// project file rather than found on disk, so the file cannot be renamed or deleted
    /// without this run going red.
    ///
    /// The committed file ends with a newline, which is this tree's rule for a text
    /// file and not part of the format, so the comparison above adds one to what the
    /// writer produced rather than trimming one off the file. A checkout that
    /// normalised the line endings on the way in is read back as the writer writes
    /// them, which is why the replacement below is here and not a convenience.
    /// </summary>
    private static string CommittedSample()
    {
        var assembly = typeof(WatchlistExportFormatTests).Assembly;
        using var stream = assembly.GetManifestResourceStream(SampleResource)
            ?? throw new InvalidOperationException($"No embedded resource is named {SampleResource}.");
        using var reader = new StreamReader(stream);

        return reader.ReadToEnd().Replace("\r\n", "\n", StringComparison.Ordinal);
    }

    /// <summary>
    /// The provider identifiers, answered from a table. An item the table does not hold
    /// is an item the library could not read.
    /// </summary>
    private sealed class ProviderIdTable(IReadOnlyDictionary<Guid, IReadOnlyDictionary<string, string>> known) : IProviderIdSource
    {
        private static readonly IReadOnlyDictionary<string, string> Nothing =
            new Dictionary<string, string>(StringComparer.Ordinal);

        public IReadOnlyDictionary<string, string> ProviderIdsFor(Guid itemId) =>
            known.TryGetValue(itemId, out var ids) ? ids : Nothing;
    }
}
