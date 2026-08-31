using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Xunit;

namespace Jellyfin.Plugin.Watchlist.Tests;

/// <summary>
/// The visibility gate's guard. docs/unresolvable-entries.md says the rule for an entry
/// whose item does not resolve is written once, in
/// <see cref="Store.WatchlistVisibility.Resolvable"/>, and that a read of the list goes
/// through that one function. This is what reads that claim.
/// </summary>
/// <remarks>
/// <para>
/// It names the site rather than printing a count for somebody to read. A plugin source
/// that takes the entries off a stored document and is named by no line of
/// VISIBILITY-GATE-READERS.txt reds this run with its file and its line; so does a file
/// declared as handing every read to the gate that takes more reads than it makes calls,
/// or that parks a read in a local where the rest of the file can reach it past the
/// gate; and so does a declaration that covers no read at all.
/// </para>
/// <para>
/// WHY THIS RATHER THAN A NOTE. The projection became the third reader the document
/// names and did not go through the gate: it asked the describer itself, which is the
/// same predicate written a second time, and nothing here was red for it. A reader that
/// filters slightly differently returns a list that is merely a bit wrong, for one
/// user, on one route, and no test that does not already know to look would notice.
/// </para>
/// <para>
/// WHERE IT STOPS is in the register's own header, beside the declarations, because
/// that is the file somebody opens when this guard refuses them.
/// </para>
/// </remarks>
public class VisibilityGateTests
{
    private const string PluginSourcePrefix = "pluginsource/";
    private const string FixturePrefix = "fixture/";
    private const string RegisterResource = "Jellyfin.Plugin.Watchlist.Tests.VISIBILITY-GATE-READERS.txt";

    private static Assembly Suite => typeof(VisibilityGateTests).Assembly;

    /// <summary>
    /// The scan this guard exists for. Every read the plugin's sources take is covered
    /// by a declaration, every declaration covers a read, and every file that says it
    /// hands its reads to the gate does.
    /// </summary>
    [Fact]
    public void ThePluginsSourcesHoldNoUndeclaredOrUnaccountedReadOfAStoredDocument()
    {
        var (entries, malformed) = VisibilityGateScanner.ParseRegister(Resource(RegisterResource));

        Assert.True(
            malformed.Count == 0,
            "The reader register has lines that are not a file, a kind this guard knows and a reason:"
                + Environment.NewLine
                + string.Join(Environment.NewLine, malformed));

        var verdict = Judge(PluginSources(), entries);

        Assert.True(
            verdict.Undeclared.Count == 0,
            "These reads of a stored document's entries are declared nowhere:"
                + Environment.NewLine
                + string.Join(Environment.NewLine, verdict.Undeclared));

        Assert.True(
            verdict.Unaccounted.Count == 0,
            "These declarations do not describe what the file does:"
                + Environment.NewLine
                + string.Join(Environment.NewLine, verdict.Unaccounted));

        Assert.True(
            verdict.Stale.Count == 0,
            "These declared readers take no read and have to go:"
                + Environment.NewLine
                + string.Join(Environment.NewLine, verdict.Stale));
    }

    /// <summary>
    /// A scan that reads nothing passes everything, so the wildcard that feeds this one
    /// is checked rather than trusted. The two files named are on opposite sides of the
    /// register: one hands its reads to the gate and one is the gate itself, which takes
    /// no read at all.
    /// </summary>
    [Fact]
    public void TheScanReadsThePluginsSources()
    {
        var scanned = PluginSources().Select(s => s.Name).ToList();

        Assert.NotEmpty(scanned);
        Assert.Contains("Api/WatchlistController.cs", scanned, StringComparer.Ordinal);
        Assert.Contains("Store/WatchlistVisibility.cs", scanned, StringComparer.Ordinal);
    }

    /// <summary>
    /// The register is not empty and every file it names is one the scan reads. A
    /// declaration over a file that has been renamed would widen the rule for nothing
    /// and read as though it covered something.
    /// </summary>
    [Fact]
    public void EveryDeclaredReaderIsAFileTheScanReads()
    {
        var (entries, _) = VisibilityGateScanner.ParseRegister(Resource(RegisterResource));
        var scanned = PluginSources().Select(s => s.Name).ToList();

        Assert.NotEmpty(entries);
        Assert.All(entries, e => Assert.Contains(e.File, scanned, StringComparer.Ordinal));
    }

    /// <summary>
    /// The near miss. The scheduled pass of #24, written the way it gets written: read
    /// the document, ask the describer about each entry, keep what answers. Every line
    /// of it compiles, it does what its issue asks, and it is the rule about
    /// unresolvable entries written a fourth time. Declared nowhere, it is refused.
    /// </summary>
    [Fact]
    public void TheGuardRefusesAFourthReaderNoDeclarationCovers()
    {
        var (entries, _) = VisibilityGateScanner.ParseRegister(Resource(RegisterResource));

        var verdict = Judge([Fixture("NearMissVisibilityGate.txt")], entries);

        Assert.NotEmpty(verdict.Undeclared);
    }

    /// <summary>
    /// The same pass declared as one that hands its reads to the gate. It does not, and
    /// counting says so, which is the arm that catches a second read added inside a file
    /// that is already allowed to read.
    /// </summary>
    [Fact]
    public void TheGuardRefusesAGatedReaderThatDoesNotReachTheGate()
    {
        var fixture = Fixture("NearMissVisibilityGate.txt");

        var verdict = Judge([fixture], [Declared(fixture.Name, ReaderKind.Gated)]);

        Assert.Empty(verdict.Undeclared);
        Assert.Empty(verdict.Stale);
        Assert.NotEmpty(verdict.Unaccounted);
    }

    /// <summary>
    /// The one-change neighbour: the same pass with the entries handed to the gate and
    /// the describer left to answer the gate's question instead of its own. Without this
    /// the two tests above would prove the fixture unusual rather than that the guard
    /// reads the call.
    /// </summary>
    [Fact]
    public void TheOneChangeNeighbourOfTheFourthReaderPasses()
    {
        var fixture = Fixture("NearMissVisibilityGateRepaired.txt");

        var verdict = Judge([fixture], [Declared(fixture.Name, ReaderKind.Gated)]);

        Assert.Empty(verdict.Undeclared);
        Assert.Empty(verdict.Stale);
        Assert.True(
            verdict.Unaccounted.Count == 0,
            "The repaired fixture should account for every read, and it did not:"
                + Environment.NewLine
                + string.Join(Environment.NewLine, verdict.Unaccounted));
    }

    /// <summary>
    /// A file that reaches the gate and is declared as one whose subject is the document
    /// rather than the list. That declaration is wrong rather than generous: a file that
    /// gates half its reads is one nobody has read properly, and the register is where
    /// somebody would go looking for the answer it gives.
    /// </summary>
    [Fact]
    public void TheGuardRefusesAnOutsideDeclarationOverAFileThatReachesTheGate()
    {
        var fixture = Fixture("NearMissVisibilityGateRepaired.txt");

        var verdict = Judge([fixture], [Declared(fixture.Name, ReaderKind.Outside)]);

        Assert.NotEmpty(verdict.Unaccounted);
    }

    /// <summary>
    /// A declaration that covers no read is stale and goes, so the register cannot
    /// outlive the code it was written for.
    /// </summary>
    [Fact]
    public void TheGuardRefusesADeclarationThatCoversNoRead()
    {
        var fixture = Fixture("NearMissVisibilityGateRepaired.txt");

        var verdict = Judge(
            [fixture],
            [Declared(fixture.Name, ReaderKind.Gated), Declared("Nowhere/NoSuchReader.cs", ReaderKind.Outside)]);

        Assert.Single(verdict.Stale);
    }

    /// <summary>
    /// A register line naming a kind this guard does not know is reported rather than
    /// dropped. A declaration nobody can read would otherwise leave the file it names
    /// undeclared while looking as though it had been decided.
    /// </summary>
    [Fact]
    public void ARegisterLineNamingAnUnknownKindIsReportedRatherThanDropped()
    {
        var (entries, malformed) = VisibilityGateScanner.ParseRegister(
            "Api/WatchlistController.cs | mostly | it reads a bit and gates a bit");

        Assert.Empty(entries);
        Assert.Single(malformed);
    }

    /// <summary>
    /// The near miss the counting arm cannot see. The same scheduled pass with the read
    /// given a name first, the name handed to the gate, and the name used again for the
    /// answer. One read and one call, so the counts agree; what comes back is the
    /// ungated collection.
    /// </summary>
    /// <remarks>
    /// This fixture exists because the counting arm was written first and this shape
    /// walked through it. It was found by making a bypass of exactly this shape in the
    /// controller and watching the suite stay green.
    /// </remarks>
    [Fact]
    public void TheGuardRefusesAGatedReaderThatParksTheReadInALocal()
    {
        var fixture = Fixture("NearMissVisibilityGateHoisted.txt");

        var verdict = Judge([fixture], [Declared(fixture.Name, ReaderKind.Gated)]);

        Assert.Empty(verdict.Undeclared);
        Assert.NotEmpty(verdict.Unaccounted);
    }

    /// <summary>
    /// The counting arm alone says nothing about the fixture above, which is why the arm
    /// beside it exists. Without this the test above would read as though counting had
    /// caught it.
    /// </summary>
    [Fact]
    public void TheCountingArmAloneDoesNotSeeTheParkedRead()
    {
        var (name, source) = Fixture("NearMissVisibilityGateHoisted.txt");

        Assert.Equal(
            VisibilityGateScanner.Reads(name, source).Count,
            VisibilityGateScanner.GateCalls(source));
    }

    /// <summary>
    /// What the guard fails on: reads no declaration covers, declarations no read
    /// matches, and declarations the file contradicts.
    /// </summary>
    /// <param name="sources">The sources to judge, each under its reported name.</param>
    /// <param name="register">The declarations to judge them against.</param>
    /// <returns>The three sets a red run prints.</returns>
    private static (IReadOnlyList<EntryRead> Undeclared, IReadOnlyList<DeclaredReader> Stale, IReadOnlyList<string> Unaccounted) Judge(
        IReadOnlyList<(string Name, string Source)> sources,
        IReadOnlyList<DeclaredReader> register)
    {
        var undeclared = new List<EntryRead>();
        var unaccounted = new List<string>();
        var covered = new HashSet<string>(StringComparer.Ordinal);

        foreach (var (name, source) in sources)
        {
            var reads = VisibilityGateScanner.Reads(name, source);

            if (reads.Count == 0)
            {
                continue;
            }

            var declaration = register.FirstOrDefault(e => string.Equals(e.File, name, StringComparison.Ordinal));

            if (declaration is null)
            {
                undeclared.AddRange(reads);
                continue;
            }

            covered.Add(name);

            var calls = VisibilityGateScanner.GateCalls(source);

            if (declaration.Kind == ReaderKind.Gated && reads.Count > calls)
            {
                unaccounted.Add(
                    $"{name} is declared as handing every read to the gate, and it takes {reads.Count} reads against {calls} calls.");
            }

            if (declaration.Kind == ReaderKind.Gated)
            {
                foreach (var parked in VisibilityGateScanner.Parked(name, source))
                {
                    unaccounted.Add(
                        $"{parked} and gives it a name, so what the gate returns is not the only thing the rest of the file can reach.");
                }
            }

            if (declaration.Kind == ReaderKind.Outside && calls > 0)
            {
                unaccounted.Add(
                    $"{name} is declared as reading the document rather than the list, and it calls the gate {calls} times.");
            }
        }

        var stale = register.Where(e => !covered.Contains(e.File)).ToList();

        return (undeclared, stale, unaccounted);
    }

    private static DeclaredReader Declared(string file, ReaderKind kind) =>
        new(file, kind, "the reason a fixture carries is this sentence");

    /// <summary>
    /// The embedded plugin sources, each under the name the scan reports it by. The name
    /// keeps its directory, because two files with one name in two folders are two
    /// files, and the separator is normalised because the build writes the one the
    /// machine uses and a register line has to read the same on all three.
    /// </summary>
    /// <returns>The sources, in name order.</returns>
    private static IReadOnlyList<(string Name, string Source)> PluginSources() => Suite
        .GetManifestResourceNames()
        .Where(n => n.StartsWith(PluginSourcePrefix, StringComparison.Ordinal))
        .Select(n => (Name: n[PluginSourcePrefix.Length..].Replace('\\', '/'), Source: Resource(n)))
        .OrderBy(s => s.Name, StringComparer.Ordinal)
        .ToList();

    private static (string Name, string Source) Fixture(string fixture) =>
        (fixture[..^".txt".Length], Resource(FixturePrefix + fixture));

    private static string Resource(string name)
    {
        using var stream = Suite.GetManifestResourceStream(name)
            ?? throw new InvalidOperationException($"No embedded resource is named {name}.");
        using var reader = new System.IO.StreamReader(stream);

        return reader.ReadToEnd();
    }
}
