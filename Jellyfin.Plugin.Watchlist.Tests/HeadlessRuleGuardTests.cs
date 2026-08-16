using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Xunit;

namespace Jellyfin.Plugin.Watchlist.Tests;

/// <summary>
/// The guard. It reads the suite's own source files out of the test assembly and
/// refuses the calls HEADLESS.md names, so a test that needs a display, elevation,
/// a trust store, the network, the machine clock or a wait bought off it reds this
/// run instead of running fine on the machine it was written on.
///
/// The sources are embedded by a wildcard in the project file rather than listed
/// anywhere, so a file added tomorrow is covered the moment it is added.
/// </summary>
public class HeadlessRuleGuardTests
{
    private const string SourcePrefix = "testsource/";
    private const string FixturePrefix = "fixture/";
    private const string RuleTableResource = "Jellyfin.Plugin.Watchlist.Tests.HeadlessRules.txt";
    private const string RegisterResource = "Jellyfin.Plugin.Watchlist.Tests.HEADLESS-EXCEPTIONS.txt";

    private static Assembly Suite => typeof(HeadlessRuleGuardTests).Assembly;

    /// <summary>
    /// The scan the suite exists for. Every embedded source is read against every
    /// rule, and what the register does not cover fails the run.
    /// </summary>
    [Fact]
    public void TheSuitesOwnSourcesCarryNoUndeclaredDeparture()
    {
        var (undeclared, _) = RealScan();

        Assert.True(
            undeclared.Count == 0,
            "The headless rule is broken in the suite's own sources:"
                + Environment.NewLine
                + string.Join(Environment.NewLine, undeclared));
    }

    /// <summary>
    /// A scan that reads nothing passes everything. This is what stops the guard
    /// from going quiet when the wildcard that feeds it stops matching.
    /// </summary>
    [Fact]
    public void TheScanReadsTheSuitesOwnSourcesIncludingThisFile()
    {
        var scanned = SourceNames();

        Assert.NotEmpty(scanned);
        Assert.Contains("HeadlessRuleGuardTests.cs", scanned, StringComparer.Ordinal);
        Assert.Contains("PluginDiscoveryTests.cs", scanned, StringComparer.Ordinal);
    }

    /// <summary>
    /// The near-miss. A store test that owns its temporary directory, reaches no
    /// network and reads the clock on exactly one line to build the value it then
    /// asserts on. Nothing about it looks wrong until it runs on a machine in
    /// another time zone.
    /// </summary>
    [Fact]
    public void TheGuardRefusesTheNearMiss()
    {
        var findings = ScanFixture("NearMissMachineClock.txt");

        var finding = Assert.Single(findings);
        Assert.Equal("machine-clock", finding.RuleId);
    }

    /// <summary>
    /// The same fixture with that one call replaced by an injected clock. If this
    /// went red too, the test above would prove only that the fixture is unusual
    /// rather than that the guard reads the call.
    /// </summary>
    [Fact]
    public void TheOneChangeNeighbourOfTheNearMissPasses()
    {
        var findings = ScanFixture("NearMissMachineClockRepaired.txt");

        Assert.True(
            findings.Count == 0,
            "The repaired fixture should trip nothing, and it tripped:"
                + Environment.NewLine
                + string.Join(Environment.NewLine, findings));
    }

    /// <summary>
    /// The second near-miss. A storm test that owns its directory, reaches no
    /// network and reads no clock, and buys the wait for a debounce off the
    /// machine on one line. It passes on the machine it was written on and it is
    /// the first thing to fail when a build agent is busy.
    /// </summary>
    [Fact]
    public void TheGuardRefusesTheWaitingNearMiss()
    {
        var findings = ScanFixture("NearMissWallClockWait.txt");

        var finding = Assert.Single(findings);
        Assert.Equal("wall-clock-wait", finding.RuleId);
    }

    /// <summary>
    /// The same fixture with that one line replaced by moving an injected clock.
    /// Without this the test above would prove the fixture unusual rather than the
    /// rule readable.
    /// </summary>
    [Fact]
    public void TheOneChangeNeighbourOfTheWaitingNearMissPasses()
    {
        var findings = ScanFixture("NearMissWallClockWaitRepaired.txt");

        Assert.True(
            findings.Count == 0,
            "The repaired fixture should trip nothing, and it tripped:"
                + Environment.NewLine
                + string.Join(Environment.NewLine, findings));
    }

    /// <summary>
    /// A declared departure suppresses the finding it names, and only that one.
    /// </summary>
    [Fact]
    public void ADeclaredExceptionSuppressesTheFindingItNames()
    {
        var findings = ScanFixture("NearMissMachineClock.txt");
        var (entries, malformed) = HeadlessRuleScanner.ParseExceptions(Resource(FixturePrefix + "CoveringExceptionRegister.txt"));
        Assert.Empty(malformed);

        var (undeclared, stale) = HeadlessRuleScanner.Apply(findings, entries);

        Assert.Empty(undeclared);
        Assert.Empty(stale);
    }

    /// <summary>
    /// A departure that outlived the code it covered. The register has to fail on
    /// it, or an entry written once quietly widens what the guard allows forever.
    /// </summary>
    [Fact]
    public void AnExceptionThatMatchesNothingIsRefused()
    {
        var findings = ScanFixture("NearMissMachineClock.txt");
        var (entries, _) = HeadlessRuleScanner.ParseExceptions(Resource(FixturePrefix + "StaleExceptionRegister.txt"));

        var (_, stale) = HeadlessRuleScanner.Apply(findings, entries);

        var entry = Assert.Single(stale);
        Assert.Equal("SomeTestThatWasDeleted.cs", entry.File);
    }

    /// <summary>
    /// An entry with an empty reason, and one missing the field altogether. Both
    /// are dispensations nobody can read, and both are reported rather than
    /// silently accepted as entries.
    /// </summary>
    [Fact]
    public void AnExceptionWithNoReasonIsRefused()
    {
        var (entries, malformed) = HeadlessRuleScanner.ParseExceptions(Resource(FixturePrefix + "UnreasonedExceptionRegister.txt"));

        Assert.Empty(entries);
        Assert.Equal(2, malformed.Count);
    }

    /// <summary>
    /// The register that ships. An entry in it that covers nothing reds this run,
    /// which is the same rule as the fixture above applied to the real tree.
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
    /// The document names classes of refusal and the rule table has to carry all of
    /// them. A class dropped from the table leaves the document promising something
    /// no rule reads.
    /// </summary>
    [Fact]
    public void TheRuleTableCarriesEveryClassTheDocumentNames()
    {
        var ids = HeadlessRuleScanner
            .ParseRules(Resource(RuleTableResource))
            .Select(r => r.Id)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        foreach (var expected in new[]
        {
            "certificate-store",
            "elevation",
            "machine-wide-path",
            "browser-automation",
            "network",
            "machine-clock",
            "wall-clock-wait",
            "locale",
        })
        {
            Assert.Contains(expected, ids, StringComparer.Ordinal);
        }
    }

    private static (IReadOnlyList<HeadlessFinding> Undeclared, IReadOnlyList<HeadlessException> Stale) RealScan()
    {
        var rules = HeadlessRuleScanner.ParseRules(Resource(RuleTableResource));
        var (entries, malformed) = HeadlessRuleScanner.ParseExceptions(Resource(RegisterResource));

        Assert.True(
            malformed.Count == 0,
            "The exception register has entries that are not three readable fields:"
                + Environment.NewLine
                + string.Join(Environment.NewLine, malformed));

        var findings = SourceNames()
            .SelectMany(name => HeadlessRuleScanner.Scan(name, Resource(SourcePrefix + name), rules))
            .ToList();

        return HeadlessRuleScanner.Apply(findings, entries);
    }

    private static IReadOnlyList<HeadlessFinding> ScanFixture(string fixture)
    {
        var rules = HeadlessRuleScanner.ParseRules(Resource(RuleTableResource));
        var name = fixture[..^".txt".Length];

        return HeadlessRuleScanner.Scan(name, Resource(FixturePrefix + fixture), rules);
    }

    private static IReadOnlyList<string> SourceNames() => Suite
        .GetManifestResourceNames()
        .Where(n => n.StartsWith(SourcePrefix, StringComparison.Ordinal))
        .Select(n => n[SourcePrefix.Length..])
        .OrderBy(n => n, StringComparer.Ordinal)
        .ToList();

    private static string Resource(string name)
    {
        using var stream = Suite.GetManifestResourceStream(name)
            ?? throw new InvalidOperationException($"No embedded resource is named {name}.");
        using var reader = new StreamReader(stream);

        return reader.ReadToEnd();
    }
}
