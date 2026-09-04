using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using Xunit;
using Xunit.Abstractions;

namespace Jellyfin.Plugin.Watchlist.Tests;

/// <summary>
/// The guard over what this repository's documents paste under their commands.
///
/// Every document here carries commands with their output written underneath, and
/// that is how a claim in a document is checked on this board. Nothing re-ran them
/// until this, so a paste went stale the moment the code under it moved and every
/// check stayed green. This re-runs the ones it can and reds the run when what a
/// command prints is not what stands under it.
///
/// What it can and cannot reach is in DOCUMENT-PASTES.md next to this file. Read
/// that before treating a green run as "every paste in the tree agrees": the
/// population is bounded, and a paste outside it is unjudged rather than passed.
/// </summary>
public class DocumentPasteTests
{
    private const string TreePrefix = "tree/";
    private const string PluginSourcePrefix = "pluginsource/";
    private const string TestSourcePrefix = "testsource/";
    private const string FixturePrefix = "fixture/";
    private const string ResourcePrefix = "Jellyfin.Plugin.Watchlist.Tests.";
    private const string RegisterResource = ResourcePrefix + "DOCUMENT-PASTE-EXCEPTIONS.txt";
    private const string BoundResource = ResourcePrefix + "DOCUMENT-PASTES.md";

    /// <summary>
    /// The resources this project embeds under a name of their own, and the path in
    /// the repository each one came from. They are listed because they have to be:
    /// a file may be embedded once, those entries were already there for other
    /// tests, and a second include under a tree name is a build error. An entry
    /// here that names a resource the build no longer produces is refused by
    /// <see cref="EveryNamedResourceIsInTheAssembly"/> rather than silently leaving
    /// a hole in the tree.
    /// </summary>
    private static readonly (string Resource, string Path)[] Aliases =
    [
        ("build.yaml", "build.yaml"),
        ("CHANGELOG.md", "CHANGELOG.md"),
        ("settings.md", "docs/settings.md"),
        ("api.md", "docs/api.md"),
        ("watchlist-export.sample.json", "docs/samples/watchlist-export.sample.json"),
        ("project/Jellyfin.Plugin.Watchlist.csproj", "Jellyfin.Plugin.Watchlist/Jellyfin.Plugin.Watchlist.csproj"),
        ("project/Jellyfin.Plugin.Watchlist.Tests.csproj", "Jellyfin.Plugin.Watchlist.Tests/Jellyfin.Plugin.Watchlist.Tests.csproj"),
        ("lock/Jellyfin.Plugin.Watchlist.json", "Jellyfin.Plugin.Watchlist/packages.lock.json"),
        ("lock/Jellyfin.Plugin.Watchlist.Tests.json", "Jellyfin.Plugin.Watchlist.Tests/packages.lock.json"),
        (ResourcePrefix + "HEADLESS.md", "Jellyfin.Plugin.Watchlist.Tests/HEADLESS.md"),
        (ResourcePrefix + "HeadlessRules.txt", "Jellyfin.Plugin.Watchlist.Tests/HeadlessRules.txt"),
        (ResourcePrefix + "HEADLESS-EXCEPTIONS.txt", "Jellyfin.Plugin.Watchlist.Tests/HEADLESS-EXCEPTIONS.txt"),
        (ResourcePrefix + "Invariants.txt", "Jellyfin.Plugin.Watchlist.Tests/Invariants.txt"),
        (ResourcePrefix + "INVARIANT-EXCEPTIONS.txt", "Jellyfin.Plugin.Watchlist.Tests/INVARIANT-EXCEPTIONS.txt"),
        (ResourcePrefix + "VISIBILITY-GATE-READERS.txt", "Jellyfin.Plugin.Watchlist.Tests/VISIBILITY-GATE-READERS.txt"),
        (BoundResource, "Jellyfin.Plugin.Watchlist.Tests/DOCUMENT-PASTES.md"),
        (RegisterResource, "Jellyfin.Plugin.Watchlist.Tests/DOCUMENT-PASTE-EXCEPTIONS.txt"),
    ];

    private readonly ITestOutputHelper _output;

    public DocumentPasteTests(ITestOutputHelper output)
    {
        _output = output;
    }

    private static Assembly Suite => typeof(DocumentPasteTests).Assembly;

    /// <summary>
    /// The check this exists for. Every paste in the population is re-run and
    /// compared with the lines under it, and a disagreement no register entry
    /// covers reds the run naming the file, the line and both readings.
    /// </summary>
    [Fact]
    public void EveryPastedCommandStillPrintsWhatStandsUnderIt()
    {
        var (undeclared, _, _, _) = RealScan();

        Assert.True(
            undeclared.Count == 0,
            "These pasted commands no longer print what stands under them:"
                + Environment.NewLine
                + string.Join(Environment.NewLine, undeclared));
    }

    /// <summary>
    /// A scan that reads no document passes everything, and so does one that reads
    /// the documents and judges none of them. Both are the way this guard goes
    /// quiet, so both are asserted against rather than hoped about.
    /// </summary>
    [Fact]
    public void TheScanReadsTheDocumentsAndJudgesPastesInThem()
    {
        var documents = Population(Tree()).Select(d => d.Path).ToList();

        Assert.Contains("README.md", documents, StringComparer.Ordinal);
        Assert.Contains("CONTRIBUTING.md", documents, StringComparer.Ordinal);
        Assert.Contains("SECURITY.md", documents, StringComparer.Ordinal);
        Assert.Contains("docs/personal-data.md", documents, StringComparer.Ordinal);
        Assert.Contains("docs/parity.md", documents, StringComparer.Ordinal);

        var (_, _, judged, _) = RealScan();

        Assert.True(judged.Count > 20, $"Only {judged.Count} pastes were judged, which is fewer than this tree holds.");
    }

    /// <summary>
    /// The accounting, printed rather than implied. A run that covered less than the
    /// whole population must not read as one that covered it and found nothing, so
    /// every paste this check did not judge is named here with the reason.
    ///
    /// It is printed by the test host rather than asserted: read it with
    /// `dotnet test --logger "console;verbosity=detailed"`. What IS asserted is that
    /// nothing lands in the unjudged set without a reason.
    /// </summary>
    [Fact]
    public void TheRunSaysWhatItDidNotJudge()
    {
        var (_, _, judged, unjudged) = RealScan();

        _output.WriteLine(string.Format(
            CultureInfo.InvariantCulture,
            "pastes judged: {0}  not judged: {1}",
            judged.Count,
            unjudged.Count));

        foreach (var skipped in unjudged)
        {
            _output.WriteLine(skipped.ToString());
        }

        Assert.All(unjudged, skipped => Assert.False(string.IsNullOrWhiteSpace(skipped.Reason)));
    }

    /// <summary>
    /// The near-miss. A document whose paste is right in every character but one:
    /// the line number under the command is one off, which is exactly what a change
    /// that moves a line leaves behind, and it is the shape four of the five sites
    /// on issue #233 had.
    /// </summary>
    [Fact]
    public void TheGuardRefusesTheNearMiss()
    {
        var mismatches = ScanFixture("NearMissDocumentPaste.txt");

        var mismatch = Assert.Single(mismatches);
        Assert.Equal(3, mismatch.Paste.Line);
        Assert.Contains("2:the second line", mismatch.Printed, StringComparer.Ordinal);
    }

    /// <summary>
    /// The same document with that one character put right. Without this the test
    /// above would prove the fixture unusual rather than the paste read.
    /// </summary>
    [Fact]
    public void TheOneChangeNeighbourOfTheNearMissPasses()
    {
        var mismatches = ScanFixture("NearMissDocumentPasteRepaired.txt");

        Assert.True(
            mismatches.Count == 0,
            "The repaired document should disagree with nothing, and it disagreed:"
                + Environment.NewLine
                + string.Join(Environment.NewLine, mismatches));
    }

    /// <summary>
    /// The second near-miss, and it is the one the first cannot reach. A claim
    /// rather than a number: the paste says an absence and the command prints a
    /// line, which is what `docs/personal-data.md` did while it told a reader no
    /// endpoint hands an export out.
    /// </summary>
    [Fact]
    public void TheGuardRefusesAPasteThatClaimsAnAbsence()
    {
        var mismatches = ScanFixture("NearMissDocumentPasteAbsence.txt");

        var mismatch = Assert.Single(mismatches);
        Assert.Equal(new[] { "exit=1" }, mismatch.Paste.Output);
        Assert.Contains("exit=0", mismatch.Printed, StringComparer.Ordinal);
    }

    /// <summary>
    /// A declared departure suppresses the site it names, and only that one.
    /// </summary>
    [Fact]
    public void ADeclaredExceptionSuppressesTheSiteItNames()
    {
        var document = Resource(FixturePrefix + "NearMissDocumentPaste.txt");
        var pastes = DocumentPasteScanner.Find("NearMissDocumentPaste.txt", document);
        var mismatches = Judge(pastes, Tree()).Mismatches;

        var (entries, malformed) = DocumentPasteScanner.ParseExceptions(
            Resource(FixturePrefix + "CoveringPasteRegister.txt"));
        Assert.Empty(malformed);

        var (undeclared, stale) = DocumentPasteScanner.Apply(mismatches, pastes, entries);

        Assert.Empty(undeclared);
        Assert.Empty(stale);
    }

    /// <summary>
    /// A departure that outlived the paste it covered. The register has to fail on
    /// it, or an entry written once quietly widens what this guard allows forever.
    /// </summary>
    [Fact]
    public void AnExceptionThatNamesNoPasteIsRefused()
    {
        var document = Resource(FixturePrefix + "NearMissDocumentPaste.txt");
        var pastes = DocumentPasteScanner.Find("NearMissDocumentPaste.txt", document);
        var mismatches = Judge(pastes, Tree()).Mismatches;

        var (entries, _) = DocumentPasteScanner.ParseExceptions(
            Resource(FixturePrefix + "StalePasteRegister.txt"));

        var (_, stale) = DocumentPasteScanner.Apply(mismatches, pastes, entries);

        var entry = Assert.Single(stale);
        Assert.Equal("docs/a-page-that-was-deleted.md", entry.Document);
    }

    /// <summary>
    /// An entry with an empty reason, one missing the field altogether, and one
    /// whose site is not a line number. All three are dispensations nobody can act
    /// on, and all three are reported rather than accepted as entries.
    /// </summary>
    [Fact]
    public void AnExceptionThatCannotBeReadIsRefused()
    {
        var (entries, malformed) = DocumentPasteScanner.ParseExceptions(
            Resource(FixturePrefix + "UnreadablePasteRegister.txt"));

        Assert.Empty(entries);
        Assert.Equal(3, malformed.Count);
    }

    /// <summary>
    /// The register that ships. An entry in it that names no paste in the population
    /// reds this run, which is the fixture above applied to the real tree.
    /// </summary>
    [Fact]
    public void NoDeclaredExceptionInTheShippedRegisterIsStale()
    {
        var (_, stale, _, _) = RealScan();

        Assert.True(
            stale.Count == 0,
            "These declared departures name no paste in the population and have to go:"
                + Environment.NewLine
                + string.Join(Environment.NewLine, stale));
    }

    /// <summary>
    /// The tree is a declared file set, and a name in the alias list that the build
    /// stopped producing would take a file out of it silently. A paste over that
    /// file would then be unjudged for a reason nobody chose.
    /// </summary>
    [Fact]
    public void EveryNamedResourceIsInTheAssembly()
    {
        var present = Suite.GetManifestResourceNames();

        foreach (var (resource, _) in Aliases)
        {
            Assert.Contains(resource, present, StringComparer.Ordinal);
        }
    }

    /// <summary>
    /// The bound is a document a reader meets, so it has to be in the tree rather
    /// than in somebody's memory of what the check does.
    /// </summary>
    [Fact]
    public void TheBoundIsWrittenDownBesideTheCheck()
    {
        var bound = Resource(BoundResource);

        Assert.Contains("## What it does not reach", bound, StringComparison.Ordinal);
        Assert.Contains("## What a paste has to look like", bound, StringComparison.Ordinal);
    }

    private static (
        IReadOnlyList<PasteMismatch> Undeclared,
        IReadOnlyList<PasteException> Stale,
        IReadOnlyList<DocumentPaste> Judged,
        IReadOnlyList<UnjudgedPaste> Unjudged) RealScan()
    {
        var tree = Tree();
        var pastes = Population(tree)
            .SelectMany(d => DocumentPasteScanner.Find(d.Path, d.Text))
            .ToList();

        var (mismatches, judged, unjudged) = Judge(pastes, tree);

        var (entries, malformed) = DocumentPasteScanner.ParseExceptions(Resource(RegisterResource));

        Assert.True(
            malformed.Count == 0,
            "The exception register has entries that are not a document, a line and a reason:"
                + Environment.NewLine
                + string.Join(Environment.NewLine, malformed));

        var (undeclared, stale) = DocumentPasteScanner.Apply(mismatches, pastes, entries);

        return (undeclared, stale, judged, unjudged);
    }

    private static (
        IReadOnlyList<PasteMismatch> Mismatches,
        IReadOnlyList<DocumentPaste> Judged,
        IReadOnlyList<UnjudgedPaste> Unjudged) Judge(
        IReadOnlyList<DocumentPaste> pastes,
        IReadOnlyDictionary<string, string> tree)
    {
        var paths = tree.Keys.OrderBy(p => p, StringComparer.Ordinal).ToList();
        var mismatches = new List<PasteMismatch>();
        var judged = new List<DocumentPaste>();
        var unjudged = new List<UnjudgedPaste>();

        foreach (var paste in pastes)
        {
            if (paste.Output.Any(Elided))
            {
                unjudged.Add(new UnjudgedPaste(paste, "the pasted output is deliberately elided"));
                continue;
            }

            var plan = PastedGrep.Plan(paste.Command, paths);
            if (plan.Stages is null)
            {
                unjudged.Add(new UnjudgedPaste(paste, plan.Refusal!));
                continue;
            }

            judged.Add(paste);
            var printed = PastedGrep.Run(plan, tree);

            if (!printed.SequenceEqual(paste.Output, StringComparer.Ordinal))
            {
                mismatches.Add(new PasteMismatch(paste, printed));
            }
        }

        return (mismatches, judged, unjudged);
    }

    private static bool Elided(string line) =>
        string.Equals(line, "...", StringComparison.Ordinal)
        || string.Equals(line, "…", StringComparison.Ordinal);

    /// <summary>
    /// The population, declared here and nowhere else: the two root documents and
    /// every markdown page under docs/. A markdown file anywhere else in the tree -
    /// the changelog, the notices, the documents beside the suite - is not judged,
    /// because those are not the pages this rule is about.
    /// </summary>
    private static IReadOnlyList<(string Path, string Text)> Population(IReadOnlyDictionary<string, string> tree) => tree
        .Where(entry => IsPopulation(entry.Key))
        .Select(entry => (entry.Key, entry.Value))
        .OrderBy(entry => entry.Key, StringComparer.Ordinal)
        .ToList();

    private static bool IsPopulation(string path) =>
        string.Equals(path, "README.md", StringComparison.Ordinal)
        || string.Equals(path, "CONTRIBUTING.md", StringComparison.Ordinal)
        || string.Equals(path, "SECURITY.md", StringComparison.Ordinal)
        || (path.StartsWith("docs/", StringComparison.Ordinal) && path.EndsWith(".md", StringComparison.Ordinal));

    private static IReadOnlyList<PasteMismatch> ScanFixture(string fixture)
    {
        var pastes = DocumentPasteScanner.Find(fixture, Resource(FixturePrefix + fixture));

        return Judge(pastes, Tree()).Mismatches;
    }

    private static IReadOnlyDictionary<string, string> Tree()
    {
        var tree = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var name in Suite.GetManifestResourceNames())
        {
            var path = PathOf(name);
            if (path is not null)
            {
                tree[path] = Resource(name);
            }
        }

        foreach (var (resource, path) in Aliases)
        {
            tree[path] = Resource(resource);
        }

        return tree;
    }

    private static string? PathOf(string resource)
    {
        if (resource.StartsWith(TreePrefix, StringComparison.Ordinal))
        {
            return Normalise(resource[TreePrefix.Length..]);
        }

        if (resource.StartsWith(PluginSourcePrefix, StringComparison.Ordinal))
        {
            return "Jellyfin.Plugin.Watchlist/" + Normalise(resource[PluginSourcePrefix.Length..]);
        }

        if (resource.StartsWith(TestSourcePrefix, StringComparison.Ordinal))
        {
            return "Jellyfin.Plugin.Watchlist.Tests/" + Normalise(resource[TestSourcePrefix.Length..]);
        }

        if (resource.StartsWith(FixturePrefix, StringComparison.Ordinal))
        {
            return "Jellyfin.Plugin.Watchlist.Tests/Fixtures/" + Normalise(resource[FixturePrefix.Length..]);
        }

        return null;
    }

    private static string Normalise(string path) => path.Replace('\\', '/');

    private static string Resource(string name)
    {
        using var stream = Suite.GetManifestResourceStream(name)
            ?? throw new InvalidOperationException($"No embedded resource is named {name}.");
        using var reader = new StreamReader(stream);

        return reader.ReadToEnd();
    }
}
