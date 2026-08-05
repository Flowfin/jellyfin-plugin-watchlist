using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using Jellyfin.Plugin.Watchlist.Store;
using Xunit;

namespace Jellyfin.Plugin.Watchlist.Tests;

/// <summary>
/// The stored shape, pinned against a sample document committed beside the suite. A
/// stored document outlives the code that wrote it, so a field rename has to show up
/// here as a failing test rather than on somebody's server as a file that no longer
/// reads.
/// </summary>
public class WatchlistDocumentFormatTests
{
    private const string SampleResource = "fixture/watchlist-document-v1.json";

    /// <summary>
    /// Gets the document the committed sample describes, built in code.
    /// </summary>
    private static WatchlistDocument Sample => new()
    {
        SchemaVersion = 1,
        UserId = Guid.Parse("11111111-1111-1111-1111-111111111111"),
        Entries =
        [
            new WatchlistEntry
            {
                ItemId = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001"),
                Kind = WatchlistItemKind.Movie,
                AddedAt = new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero),
                Source = WatchlistEntrySource.Api,
            },
            new WatchlistEntry
            {
                ItemId = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000002"),
                Kind = WatchlistItemKind.Series,
                AddedAt = new DateTimeOffset(2026, 1, 2, 3, 4, 6, TimeSpan.Zero),
                Source = WatchlistEntrySource.PlaylistEdit,
            },
            new WatchlistEntry
            {
                ItemId = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000003"),
                Kind = WatchlistItemKind.Episode,
                AddedAt = new DateTimeOffset(2026, 1, 2, 3, 4, 7, TimeSpan.Zero),
                Source = WatchlistEntrySource.Import,
            },
        ],
    };

    /// <summary>
    /// The whole shape in one assertion: rename a member, reorder an enum, change a
    /// type or drop a field and this stops matching.
    /// </summary>
    [Fact]
    public void TheFormatWritesExactlyTheCommittedSample()
    {
        Assert.Equal(
            NormaliseLineEndings(CommittedSample()),
            NormaliseLineEndings(WatchlistDocumentFormat.Write(Sample)));
    }

    /// <summary>
    /// And it reads back. Writing and reading are different paths through the library
    /// underneath, so matching text is not on its own a round trip.
    /// </summary>
    [Fact]
    public void TheCommittedSampleReadsBackWithEveryFieldIntact()
    {
        var document = WatchlistDocumentFormat.Read(CommittedSample());

        // Entry by entry rather than document by document: the record's generated
        // equality compares the entry list by reference, so a whole-document assertion
        // would fail on two equal lists and prove nothing about the fields.
        Assert.Equal(Sample.Entries, document.Entries);
        Assert.Equal(1, document.SchemaVersion);
        Assert.Equal(Guid.Parse("11111111-1111-1111-1111-111111111111"), document.UserId);
        Assert.Equal(3, document.Entries.Count);
        Assert.Equal(WatchlistItemKind.Series, document.Entries[1].Kind);
        Assert.Equal(WatchlistEntrySource.Import, document.Entries[2].Source);
        Assert.Equal(TimeSpan.Zero, document.Entries[0].AddedAt.Offset);
    }

    /// <summary>
    /// A document carries the schema version, the user and the entries, and nothing
    /// that could be derived from the library when the list is read. This is the
    /// assertion that refuses a title, an image or a path being added later: the set of
    /// member names is fixed rather than checked against the three known bad ones.
    /// It reads what the format writes rather than the committed sample, so a field
    /// added to the type is caught here and not only by the sample no longer matching.
    /// </summary>
    [Fact]
    public void ADocumentCarriesTheseMembersAndNoOthers()
    {
        using var parsed = JsonDocument.Parse(WatchlistDocumentFormat.Write(Sample));

        Assert.Equal(
            new[] { "AddedAt", "Entries", "ItemId", "Kind", "SchemaVersion", "Source", "UserId" },
            MemberNames(parsed.RootElement)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToArray());
    }

    /// <summary>
    /// The near miss the sample exists for. One member renamed, every other byte the
    /// same, and the read has to refuse it rather than hand back a document with a
    /// defaulted field in it.
    /// </summary>
    /// <param name="member">The member name in the sample.</param>
    /// <param name="renamed">What to rename it to.</param>
    [Theory]
    [InlineData("SchemaVersion", "SchemaVersionNumber")]
    [InlineData("UserId", "UserGuid")]
    [InlineData("ItemId", "ItemGuid")]
    [InlineData("AddedAt", "Added")]
    [InlineData("Kind", "ItemKind")]
    [InlineData("Source", "Origin")]
    public void ARenamedMemberIsRefusedRatherThanDefaulted(string member, string renamed)
    {
        var mutated = CommittedSample().Replace(
            "\"" + member + "\":",
            "\"" + renamed + "\":",
            StringComparison.Ordinal);

        Assert.NotEqual(CommittedSample(), mutated);
        Assert.Throws<JsonException>(() => WatchlistDocumentFormat.Read(mutated));
    }

    /// <summary>
    /// Reads the sample out of the test assembly, so the test reads the file this tree
    /// committed and never one that happens to sit beside the test host.
    /// </summary>
    /// <returns>The sample document text.</returns>
    private static string CommittedSample()
    {
        var assembly = Assembly.GetExecutingAssembly();
        using var stream = assembly.GetManifestResourceStream(SampleResource)
            ?? throw new InvalidOperationException(
                SampleResource + " is not embedded in the test assembly. The assembly carries: "
                + string.Join(", ", assembly.GetManifestResourceNames()));

        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    /// <summary>
    /// The repository declares no line-ending attribute, so a checkout on Windows hands
    /// the sample back with carriage returns while the format writes line feeds. The
    /// comparison is about the shape, so the ending is normalised on both sides and
    /// nothing else is.
    /// </summary>
    /// <param name="text">The text to normalise.</param>
    /// <returns>The text with line feeds only.</returns>
    private static string NormaliseLineEndings(string text) =>
        text.Replace("\r\n", "\n", StringComparison.Ordinal);

    private static IEnumerable<string> MemberNames(JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var member in element.EnumerateObject())
                {
                    yield return member.Name;

                    foreach (var nested in MemberNames(member.Value))
                    {
                        yield return nested;
                    }
                }

                break;

            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    foreach (var nested in MemberNames(item))
                    {
                        yield return nested;
                    }
                }

                break;

            default:
                break;
        }
    }
}
