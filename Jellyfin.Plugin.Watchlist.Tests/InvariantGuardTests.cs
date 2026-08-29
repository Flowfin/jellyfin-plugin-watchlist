using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Xunit;

namespace Jellyfin.Plugin.Watchlist.Tests;

/// <summary>
/// The invariant guard. It reads the plugin's own source files out of the test
/// assembly and refuses a call that breaks an invariant this plugin actually has,
/// so a second place that owns a file path reds this run rather than being found
/// by whoever next goes looking for where a document is written.
///
/// It is the same scanner the headless guard uses, pointed at a different set of
/// sources with a different table. Adopting a token-level lint as a suite guard
/// rather than as a workflow of its own is the decision on #60, and the reason is
/// that this shape already exists here, already runs in a required context, and
/// already runs the same way on a contributor's machine.
/// </summary>
public class InvariantGuardTests
{
    private const string PluginSourcePrefix = "pluginsource/";
    private const string FixturePrefix = "fixture/";
    private const string RuleTableResource = "Jellyfin.Plugin.Watchlist.Tests.Invariants.txt";
    private const string RegisterResource = "Jellyfin.Plugin.Watchlist.Tests.INVARIANT-EXCEPTIONS.txt";

    private static Assembly Suite => typeof(InvariantGuardTests).Assembly;

    /// <summary>
    /// The scan this guard exists for. Every embedded plugin source is read against
    /// every invariant, and what the register does not cover fails the run.
    /// </summary>
    [Fact]
    public void ThePluginsOwnSourcesCarryNoUndeclaredDeparture()
    {
        var (undeclared, _) = RealScan();

        Assert.True(
            undeclared.Count == 0,
            "An invariant is broken in the plugin's own sources:"
                + Environment.NewLine
                + string.Join(Environment.NewLine, undeclared));
    }

    /// <summary>
    /// A scan that reads nothing passes everything, so the wildcard that feeds this
    /// one is checked rather than trusted. The two files named are on opposite sides
    /// of the invariant: one is the declared departure and one has never touched a
    /// path.
    /// </summary>
    [Fact]
    public void TheScanReadsThePluginsSources()
    {
        var scanned = SourceEntries().Select(e => e.Name).ToList();

        Assert.NotEmpty(scanned);
        Assert.Contains("Store/WatchlistDocumentStore.cs", scanned, StringComparer.Ordinal);
        Assert.Contains("Plugin.cs", scanned, StringComparer.Ordinal);
    }

    /// <summary>
    /// The near-miss. An exporter that already produces the text writing it where the
    /// caller asked. It is one short method, it compiles, it works, and it puts a
    /// second owner of a file path into a plugin whose whole storage story is that
    /// there is one.
    /// </summary>
    [Fact]
    public void TheGuardRefusesTheNearMiss()
    {
        var findings = ScanFixture("NearMissStoreFilesystem.txt");

        Assert.NotEmpty(findings);
        Assert.All(findings, f => Assert.Equal("store-filesystem", f.RuleId));
    }

    /// <summary>
    /// The same step with the path and the write handed back to the thing that owns
    /// them. If this went red too, the test above would prove only that the fixture
    /// is unusual rather than that the guard reads the call.
    /// </summary>
    [Fact]
    public void TheOneChangeNeighbourOfTheNearMissPasses()
    {
        var findings = ScanFixture("NearMissStoreFilesystemRepaired.txt");

        Assert.True(
            findings.Count == 0,
            "The repaired fixture should trip nothing, and it tripped:"
                + Environment.NewLine
                + string.Join(Environment.NewLine, findings));
    }

    /// <summary>
    /// The near-miss for the other invariant. An endpoint saying why it refused,
    /// written the way somebody writes it while helping a client author who is
    /// getting a 404 and cannot see which of the two 404s it was. Every line of it
    /// compiles, and the one that names the item hands a caller the title of
    /// something they were never allowed to know exists.
    /// </summary>
    [Fact]
    public void TheGuardRefusesARefusalThatCarriesABody()
    {
        var findings = ScanFixture("NearMissApiRefusalBody.txt");

        Assert.NotEmpty(findings);
        Assert.All(findings, f => Assert.Equal("api-refusal-body", f.RuleId));
    }

    /// <summary>
    /// The same endpoint with every refusal handing back its code and nothing else.
    /// Without this the test above would prove that the fixture is unusual rather
    /// than that the guard reads the argument.
    /// </summary>
    [Fact]
    public void TheOneChangeNeighbourOfTheRefusalNearMissPasses()
    {
        var findings = ScanFixture("NearMissApiRefusalBodyRepaired.txt");

        Assert.True(
            findings.Count == 0,
            "The repaired fixture should trip nothing, and it tripped:"
                + Environment.NewLine
                + string.Join(Environment.NewLine, findings));
    }

    /// <summary>
    /// The near-miss for the third invariant. A describer asking somewhere else what a
    /// title looks like, written the way somebody writes it while making a client's row
    /// look better, with the server's own client factory taken through the constructor
    /// so the call is one line. What it sends is what a person means to watch.
    /// </summary>
    [Fact]
    public void TheGuardRefusesAnOutboundCall()
    {
        var findings = ScanFixture("NearMissPluginNetwork.txt");

        Assert.NotEmpty(findings);
        Assert.All(findings, f => Assert.Equal("plugin-network", f.RuleId));
    }

    /// <summary>
    /// The same describer with the artwork taken off the item the library already
    /// handed over. Without this the test above would prove the fixture is unusual
    /// rather than that the guard reads the call.
    /// </summary>
    [Fact]
    public void TheOneChangeNeighbourOfTheOutboundCallPasses()
    {
        var findings = ScanFixture("NearMissPluginNetworkRepaired.txt");

        Assert.True(
            findings.Count == 0,
            "The repaired fixture should trip nothing, and it tripped:"
                + Environment.NewLine
                + string.Join(Environment.NewLine, findings));
    }

    /// <summary>
    /// The near-miss for the fourth invariant. A projector taking the gateway for
    /// everything except one flag, reaching past it for the server's own creation
    /// request because that is where the flag is and widening the seam is a
    /// conversation. Every line of it compiles, and it puts the one type that differs
    /// between the two server lines back into the one file that must not know.
    /// </summary>
    [Fact]
    public void TheGuardRefusesAServerPlaylistTypeOutsideTheAdapter()
    {
        var findings = ScanFixture("NearMissPlaylistNamespace.txt");

        Assert.NotEmpty(findings);
        Assert.All(findings, f => Assert.Equal("playlist-namespace", f.RuleId));
    }

    /// <summary>
    /// The same projector with the flag asked for through the seam. Without this the
    /// test above would prove the fixture unusual rather than that the guard reads the
    /// reference.
    /// </summary>
    [Fact]
    public void TheOneChangeNeighbourOfTheServerPlaylistTypePasses()
    {
        var findings = ScanFixture("NearMissPlaylistNamespaceRepaired.txt");

        Assert.True(
            findings.Count == 0,
            "The repaired fixture should trip nothing, and it tripped:"
                + Environment.NewLine
                + string.Join(Environment.NewLine, findings));
    }

    /// <summary>
    /// The adapter is a real file the scan reads, so the register entry that covers it
    /// is covering something. A guard whose one departure named a file that had been
    /// renamed would go quiet in exactly the direction nobody looks.
    /// </summary>
    [Fact]
    public void TheAdapterTheRegisterNamesIsInTheScannedSet()
    {
        var scanned = SourceEntries().Select(e => e.Name).ToList();

        Assert.Contains("Projection/ServerPlaylistGateway.cs", scanned, StringComparer.Ordinal);
        Assert.Contains("Projection/IPlaylistGateway.cs", scanned, StringComparer.Ordinal);
    }

    /// <summary>
    /// The register that ships. An entry in it that covers nothing reds this run, so
    /// the day the store stops touching the file system the entry has to go rather
    /// than sit there widening the rule for a file that no longer needs it.
    /// </summary>
    [Fact]
    public void NoDeclaredExceptionInTheShippedRegisterIsStale()
    {
        var (_, stale) = RealScan();

        Assert.True(
            stale.Count == 0,
            "These declared departures match nothing in the tree and have to go:"
                + Environment.NewLine
                + string.Join(Environment.NewLine, stale));
    }

    /// <summary>
    /// The table is not empty and the id the register names is one the table
    /// carries. A register entry naming an id no rule uses would suppress nothing
    /// and read as though it did.
    /// </summary>
    [Fact]
    public void EveryDeclaredExceptionNamesARuleTheTableCarries()
    {
        var ids = HeadlessRuleScanner
            .ParseRules(Resource(RuleTableResource))
            .Select(r => r.Id)
            .Distinct(StringComparer.Ordinal)
            .ToList();
        var (entries, malformed) = HeadlessRuleScanner.ParseExceptions(Resource(RegisterResource));

        Assert.NotEmpty(ids);
        Assert.Empty(malformed);
        Assert.NotEmpty(entries);
        Assert.All(entries, e => Assert.Contains(e.RuleId, ids, StringComparer.Ordinal));
    }

    private static (IReadOnlyList<HeadlessFinding> Undeclared, IReadOnlyList<HeadlessException> Stale) RealScan()
    {
        var rules = HeadlessRuleScanner.ParseRules(Resource(RuleTableResource));
        var (entries, malformed) = HeadlessRuleScanner.ParseExceptions(Resource(RegisterResource));

        Assert.True(
            malformed.Count == 0,
            "The invariant register has entries that are not three readable fields:"
                + Environment.NewLine
                + string.Join(Environment.NewLine, malformed));

        var findings = SourceEntries()
            .SelectMany(e => HeadlessRuleScanner.Scan(e.Name, Resource(e.Resource), rules))
            .ToList();

        return HeadlessRuleScanner.Apply(findings, entries);
    }

    private static IReadOnlyList<HeadlessFinding> ScanFixture(string fixture)
    {
        var rules = HeadlessRuleScanner.ParseRules(Resource(RuleTableResource));
        var name = fixture[..^".txt".Length];

        return HeadlessRuleScanner.Scan(name, Resource(FixturePrefix + fixture), rules);
    }

    /// <summary>
    /// The embedded plugin sources, each as the resource it is stored under and the
    /// name the scan reports it by. The reported name keeps its directory, because
    /// two files with one name in two folders are two files, and the separator is
    /// normalised because the build writes the one the machine uses and a register
    /// entry has to read the same on all three.
    /// </summary>
    private static IReadOnlyList<(string Resource, string Name)> SourceEntries() => Suite
        .GetManifestResourceNames()
        .Where(n => n.StartsWith(PluginSourcePrefix, StringComparison.Ordinal))
        .Select(n => (Resource: n, Name: n[PluginSourcePrefix.Length..].Replace('\\', '/')))
        .OrderBy(e => e.Name, StringComparer.Ordinal)
        .ToList();

    private static string Resource(string name)
    {
        using var stream = Suite.GetManifestResourceStream(name)
            ?? throw new InvalidOperationException($"No embedded resource is named {name}.");
        using var reader = new System.IO.StreamReader(stream);

        return reader.ReadToEnd();
    }
}
