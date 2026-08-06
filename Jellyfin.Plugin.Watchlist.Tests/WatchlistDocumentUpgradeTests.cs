using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json.Nodes;
using Jellyfin.Plugin.Watchlist.Store;
using Xunit;

namespace Jellyfin.Plugin.Watchlist.Tests;

/// <summary>
/// How a document written under an older schema reaches the shape this plugin reads.
/// </summary>
/// <remarks>
/// The chain is driven with steps this file makes up, not with the steps the plugin
/// ships. A test that judged only today's chain would prove the state of the tree on
/// the day it ran; the rule under test is that the steps run, in order, from the
/// version the document declares, and that a gap is refused rather than stepped over.
/// The plugin's own chain is judged separately, and only for whether it reaches.
/// </remarks>
public sealed class WatchlistDocumentUpgradeTests : IDisposable
{
    private static readonly Guid AUser = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private const string Trail = "UpgradeTrail";

    private readonly TemporaryDirectory _sandbox = new("watchlist-upgrade");

    private string DataFolder => Path.Combine(_sandbox.FullPath, "plugin-data");

    /// <summary>
    /// Gets every version a stored document may declare and still be read.
    /// </summary>
    /// <returns>The versions, one per case.</returns>
    public static TheoryData<int> ReadableStoredVersions()
    {
        var versions = new TheoryData<int>();

        foreach (var version in ReadableVersions())
        {
            versions.Add(version);
        }

        return versions;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _sandbox.Dispose();
    }

    /// <summary>
    /// Every step between the two versions runs, and in order. Each step here writes
    /// its own letter, so the order is something the assertion can see rather than
    /// something it assumes.
    /// </summary>
    [Fact]
    public void EveryStepBetweenTheTwoVersionsRunsInOrder()
    {
        var upgraded = WatchlistDocumentUpgrades.Apply(AtVersion(0), 0, 3, ThreeMarkingSteps());

        Assert.Equal("abc", upgraded[Trail]!.GetValue<string>());
    }

    /// <summary>
    /// The near miss. The chain starts at the version the document declares, not at
    /// the oldest step there is, so a document already past the first step is not put
    /// through it a second time.
    /// </summary>
    [Fact]
    public void TheChainStartsAtTheVersionTheDocumentDeclares()
    {
        var upgraded = WatchlistDocumentUpgrades.Apply(AtVersion(1), 1, 3, ThreeMarkingSteps());

        Assert.Equal("bc", upgraded[Trail]!.GetValue<string>());
    }

    /// <summary>
    /// A document already at the target version is not stepped at all.
    /// </summary>
    [Fact]
    public void ADocumentAlreadyAtTheTargetVersionIsNotStepped()
    {
        var upgraded = WatchlistDocumentUpgrades.Apply(AtVersion(3), 3, 3, ThreeMarkingSteps());

        Assert.Null(upgraded[Trail]);
        Assert.Equal(3, upgraded[nameof(WatchlistDocument.SchemaVersion)]!.GetValue<int>());
    }

    /// <summary>
    /// The version number is stamped by the chain rather than by each step, so a step
    /// that changes no member still leaves the document declaring what it reached. A
    /// step author who forgets it cannot produce a document carrying an old number.
    /// </summary>
    [Fact]
    public void TheChainStampsTheVersionEvenWhenTheStepChangesNothing()
    {
        var steps = new Dictionary<int, Func<JsonObject, JsonObject>>
        {
            [4] = document => document,
            [5] = document => document,
        };

        var upgraded = WatchlistDocumentUpgrades.Apply(AtVersion(4), 4, 6, steps);

        Assert.Equal(6, upgraded[nameof(WatchlistDocument.SchemaVersion)]!.GetValue<int>());
    }

    /// <summary>
    /// A missing step is refused rather than stepped over. Skipping it would produce a
    /// document carrying one shape and declaring another, and nothing after that can
    /// tell it from a document that was really brought forward.
    /// </summary>
    [Fact]
    public void AMissingStepIsRefusedRatherThanSteppedOver()
    {
        var steps = new Dictionary<int, Func<JsonObject, JsonObject>>
        {
            [0] = Marking("a"),
            [2] = Marking("c"),
        };

        var refusal = Assert.Throws<InvalidOperationException>(
            () => WatchlistDocumentUpgrades.Apply(AtVersion(0), 0, 3, steps));

        Assert.Contains("version 1", refusal.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// A step that returns nothing is refused rather than carried on from. Continuing
    /// with the document the step was handed would silently make it a step that did
    /// nothing, and the version would be stamped over the old shape anyway.
    /// </summary>
    [Fact]
    public void AStepThatReturnsNothingIsRefused()
    {
        var steps = new Dictionary<int, Func<JsonObject, JsonObject>>
        {
            [0] = _ => null!,
        };

        var refusal = Assert.Throws<InvalidOperationException>(
            () => WatchlistDocumentUpgrades.Apply(AtVersion(0), 0, 1, steps));

        Assert.Contains("returned nothing", refusal.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// A chain reaches only when every version in between has a step.
    /// </summary>
    /// <param name="from">The version a document declares.</param>
    /// <param name="to">The version it has to reach.</param>
    /// <param name="reaches">Whether it should reach.</param>
    [Theory]
    [InlineData(0, 3, true)]
    [InlineData(1, 3, true)]
    [InlineData(3, 3, true)]
    [InlineData(0, 4, false)]
    [InlineData(4, 3, false)]
    public void AChainReachesOnlyWhenEveryVersionInBetweenHasAStep(int from, int to, bool reaches)
    {
        Assert.Equal(reaches, WatchlistDocumentUpgrades.Covers(from, to, ThreeMarkingSteps()));
    }

    /// <summary>
    /// A gap in the middle is a gap, even though both ends have a step.
    /// </summary>
    [Fact]
    public void AGapInTheMiddleOfAChainDoesNotReach()
    {
        var steps = new Dictionary<int, Func<JsonObject, JsonObject>>
        {
            [0] = Marking("a"),
            [2] = Marking("c"),
        };

        Assert.False(WatchlistDocumentUpgrades.Covers(0, 3, steps));
    }

    /// <summary>
    /// The chain this plugin ships reaches the version it writes from the oldest
    /// version it claims to read.
    /// </summary>
    [Fact]
    public void TheShippedChainReachesTheCurrentVersionFromTheOldestReadableOne()
    {
        Assert.True(WatchlistDocumentUpgrades.CanBringForward(
            WatchlistDocumentUpgrades.OldestReadableSchemaVersion));
    }

    /// <summary>
    /// And the near miss that says the assertion above is not true of everything. One
    /// version further than the plugin writes is not reachable, so raising
    /// <see cref="WatchlistDocument.CurrentSchemaVersion"/> without adding the step
    /// that goes with it turns the assertion above red.
    /// </summary>
    [Fact]
    public void TheShippedChainDoesNotReachOneVersionBeyondTheCurrentOne()
    {
        Assert.False(WatchlistDocumentUpgrades.Covers(
            WatchlistDocumentUpgrades.OldestReadableSchemaVersion,
            WatchlistDocument.CurrentSchemaVersion + 1,
            WatchlistDocumentUpgrades.Steps));
    }

    /// <summary>
    /// A document below the oldest readable version cannot reach the current one, so
    /// the store has something to refuse rather than a number it silently accepts.
    /// </summary>
    [Fact]
    public void ADocumentBelowTheOldestReadableVersionCannotBeBroughtForward()
    {
        Assert.False(WatchlistDocumentUpgrades.CanBringForward(
            WatchlistDocumentUpgrades.OldestReadableSchemaVersion - 1));
    }

    /// <summary>
    /// A committed fixture exists for every version a document on a server may
    /// declare, so an upgrade that breaks an old document fails here rather than on
    /// somebody's server. A new version with no fixture turns this red.
    /// </summary>
    [Fact]
    public void AFixtureExistsForEveryReadableStoredVersion()
    {
        var embedded = Assembly.GetExecutingAssembly().GetManifestResourceNames();

        var missing = ReadableVersions()
            .Select(FixtureNameFor)
            .Where(name => !embedded.Contains("fixture/" + name, StringComparer.Ordinal))
            .ToArray();

        Assert.Empty(missing);
    }

    /// <summary>
    /// And each of those fixtures is read, arrives at the current version, and keeps
    /// every entry it carried.
    /// </summary>
    /// <param name="storedVersion">The version the fixture declares.</param>
    [Theory]
    [MemberData(nameof(ReadableStoredVersions))]
    public void EveryReadableStoredVersionIsReadAndKeepsItsEntries(int storedVersion)
    {
        var store = new WatchlistDocumentStore(DataFolder, new RecordingLogger());
        Place(FixtureNameFor(storedVersion));

        var result = store.Read(AUser);

        Assert.True(result.IsAvailable);
        Assert.Equal(WatchlistDocument.CurrentSchemaVersion, result.Document!.SchemaVersion);
        Assert.Equal(3, result.Document.Entries.Count);
        Assert.Equal(
            Guid.Parse("aaaaaaaa-0000-0000-0000-000000000002"),
            result.Document.Entries[1].ItemId);
        Assert.Equal(WatchlistItemKind.Series, result.Document.Entries[1].Kind);
        Assert.Equal(WatchlistEntrySource.PlaylistEdit, result.Document.Entries[1].Source);
    }

    /// <summary>
    /// A document declaring a version the chain does not start from is refused, and
    /// the file is left exactly as it was. Reading it as current would leave an old
    /// shape wearing a new label.
    /// </summary>
    [Fact]
    public void ADocumentFromBeforeTheChainIsRefusedAndTheFileIsUnchanged()
    {
        var store = new WatchlistDocumentStore(DataFolder, new RecordingLogger());
        var path = Place("watchlist-document-from-before-the-chain.json");
        var before = File.ReadAllBytes(path);

        var result = store.Read(AUser);

        Assert.False(result.IsAvailable);
        Assert.Null(result.Document);
        Assert.Equal(-1, result.StoredSchemaVersion);
        Assert.Equal(before, File.ReadAllBytes(path));
    }

    /// <summary>
    /// And it says so once, naming the file and both versions, because that refusal is
    /// the only thing an administrator sees when a user's list goes blank.
    /// </summary>
    [Fact]
    public void TheRefusalIsReportedOnceWithTheFileAndBothVersions()
    {
        var log = new RecordingLogger();
        var store = new WatchlistDocumentStore(DataFolder, log);
        var path = Place("watchlist-document-from-before-the-chain.json");

        store.Read(AUser);

        var line = Assert.Single(log.Lines);
        Assert.Contains(path, line, StringComparison.Ordinal);
        Assert.Contains("version -1", line, StringComparison.Ordinal);
        Assert.Contains(
            "version " + WatchlistDocument.CurrentSchemaVersion.ToString(CultureInfo.InvariantCulture),
            line,
            StringComparison.Ordinal);
        Assert.StartsWith("Error", line, StringComparison.Ordinal);
    }

    /// <summary>
    /// The fixture that is refused differs from the readable one in the version number
    /// and in nothing else, so the refusal above is about the version and not about
    /// anything else in the file.
    /// </summary>
    [Fact]
    public void TheRefusedFixtureDiffersFromTheCurrentOneOnlyInTheVersionNumber()
    {
        Assert.Equal(
            Fixture(FixtureNameFor(WatchlistDocument.CurrentSchemaVersion)),
            Fixture("watchlist-document-from-before-the-chain.json").Replace(
                "\"SchemaVersion\": -1",
                "\"SchemaVersion\": " + WatchlistDocument.CurrentSchemaVersion.ToString(CultureInfo.InvariantCulture),
                StringComparison.Ordinal));
    }

    /// <summary>
    /// The upgraded form reaches the disk only when something else changes the
    /// document. A read leaves the old number in the file; the write that follows an
    /// add carries the new one.
    /// </summary>
    [Fact]
    public void TheUpgradedFormIsWrittenOnlyWhenSomethingElseChangesTheDocument()
    {
        var store = new WatchlistDocumentStore(DataFolder, new RecordingLogger());
        var path = Place("watchlist-document-v0.json");

        store.Read(AUser);

        Assert.Contains("\"SchemaVersion\": 0", File.ReadAllText(path), StringComparison.Ordinal);

        store.Add(AUser, AnEntry(), maxEntriesPerUser: 10);

        Assert.Contains(
            "\"SchemaVersion\": " + WatchlistDocument.CurrentSchemaVersion.ToString(CultureInfo.InvariantCulture),
            File.ReadAllText(path),
            StringComparison.Ordinal);
        Assert.Equal(4, store.Read(AUser).Document!.Entries.Count);
    }

    /// <summary>
    /// Every version a stored document may declare and still be read, derived from the
    /// two constants rather than listed, so the set moves when they do.
    /// </summary>
    /// <returns>The versions, oldest first.</returns>
    private static IEnumerable<int> ReadableVersions() => Enumerable.Range(
        WatchlistDocumentUpgrades.OldestReadableSchemaVersion,
        WatchlistDocument.CurrentSchemaVersion - WatchlistDocumentUpgrades.OldestReadableSchemaVersion + 1);

    private static string FixtureNameFor(int storedVersion) => string.Format(
        CultureInfo.InvariantCulture,
        "watchlist-document-v{0}.json",
        storedVersion);

    private static WatchlistEntry AnEntry() => new()
    {
        ItemId = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000004"),
        Kind = WatchlistItemKind.Movie,
        AddedAt = new DateTimeOffset(2026, 1, 2, 3, 4, 8, TimeSpan.Zero),
        Source = WatchlistEntrySource.Api,
    };

    private static JsonObject AtVersion(int schemaVersion) =>
        new() { [nameof(WatchlistDocument.SchemaVersion)] = schemaVersion };

    private static Dictionary<int, Func<JsonObject, JsonObject>> ThreeMarkingSteps() => new()
    {
        [0] = Marking("a"),
        [1] = Marking("b"),
        [2] = Marking("c"),
    };

    /// <summary>
    /// A step that writes one letter into the document, so what ran and in what order
    /// is readable off the result.
    /// </summary>
    /// <param name="mark">The letter this step leaves.</param>
    /// <returns>The step.</returns>
    private static Func<JsonObject, JsonObject> Marking(string mark) => document =>
    {
        document[Trail] = (document[Trail]?.GetValue<string>() ?? string.Empty) + mark;

        return document;
    };

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
