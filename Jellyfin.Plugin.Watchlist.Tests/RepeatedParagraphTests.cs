using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Xunit;

namespace Jellyfin.Plugin.Watchlist.Tests;

/// <summary>
/// The guard over a paragraph written twice in a row in a tracked document.
///
/// `docs/client-verification.md` carried one paragraph twice, one immediately after
/// the other, from `a7a017f` until this landed, and every route in this tree was
/// green over it. Nothing here reads a document for its prose, so the four claims
/// that had gone stale in that file and in `README.md` before it were all caught by
/// somebody reading rather than by a run. Most of what those readings caught needs
/// judgement about meaning and no check can make it. This case needs none: two
/// adjacent blocks are byte-equal once whitespace is normalised, which is why it is
/// the half that can be refused.
///
/// What this does NOT read is in RepeatedParagraphScanner next to it, and the short
/// form is that a repeat with text between the copies is outside it on purpose.
/// </summary>
public class RepeatedParagraphTests
{
    private const string TreePrefix = "tree/";
    private const string SuitePrefix = "Jellyfin.Plugin.Watchlist.Tests.";
    private const string FixturePrefix = "fixture/";

    /// <summary>
    /// The two documents that reach this assembly under a name of their own rather
    /// than under a tree name, mapped back onto the path they came from.
    /// </summary>
    /// <remarks>
    /// They are embedded that way because other checks needed them first and a file
    /// may be an EmbeddedResource once. The population below is still derived - every
    /// embedded resource whose name ends in `.md` is read - and this table only
    /// decides what a finding in one of those two is CALLED, so a document embedded
    /// under a new name tomorrow is judged tomorrow and reported under that name.
    /// <see cref="EveryAliasedDocumentIsInTheAssembly"/> reds the run if either of
    /// these stops being a resource.
    /// </remarks>
    private static readonly (string Resource, string Path)[] Aliases =
    [
        ("settings.md", "docs/settings.md"),
        ("api.md", "docs/api.md"),
    ];

    private static Assembly Suite => typeof(RepeatedParagraphTests).Assembly;

    /// <summary>
    /// The scan this guard exists for.
    /// </summary>
    [Fact]
    public void TheTrackedDocumentsCarryNoBlockWrittenTwiceInARow()
    {
        var findings = DocumentEntries()
            .SelectMany(e => RepeatedParagraphScanner.Scan(e.Name, Resource(e.Resource)))
            .ToList();

        Assert.True(
            findings.Count == 0,
            "A block of a document is written twice in a row. Keep one copy:"
                + Environment.NewLine
                + string.Join(Environment.NewLine, findings));
    }

    /// <summary>
    /// A scan that reads nothing passes everything, so the population that feeds this
    /// one is checked rather than trusted. The four named are on the three routes a
    /// document reaches this assembly by: the root files, the docs wildcard, and the
    /// alias table above.
    /// </summary>
    [Fact]
    public void TheScanReadsTheTrackedDocuments()
    {
        var read = DocumentEntries().Select(e => e.Name).ToList();

        Assert.NotEmpty(read);
        Assert.Contains("README.md", read, StringComparer.Ordinal);
        Assert.Contains("CONTRIBUTING.md", read, StringComparer.Ordinal);
        Assert.Contains("docs/client-verification.md", read, StringComparer.Ordinal);
        Assert.Contains("docs/api.md", read, StringComparer.Ordinal);
    }

    /// <summary>
    /// The near miss, and it is the mistake that was actually made: a section rewritten
    /// with the paragraph carried into the new text while the old copy stayed where it
    /// was. Every line of it is text somebody meant to write, and the document reads
    /// correctly right up to the repeat.
    /// </summary>
    [Fact]
    public void TheGuardRefusesTheNearMiss()
    {
        var findings = ScanFixture("NearMissRepeatedParagraph.txt");

        var finding = Assert.Single(findings);
        Assert.Equal(11, finding.FirstLine);
        Assert.Equal(16, finding.SecondLine);
    }

    /// <summary>
    /// The same document with the stray copy taken out and nothing else changed.
    /// Without this the test above would prove that the fixture is unusual rather than
    /// that the scan reads the repeat.
    /// </summary>
    [Fact]
    public void TheOneChangeNeighbourOfTheNearMissPasses()
    {
        var findings = ScanFixture("NearMissRepeatedParagraphRepaired.txt");

        Assert.True(
            findings.Count == 0,
            "The repaired fixture should trip nothing, and it tripped:"
                + Environment.NewLine
                + string.Join(Environment.NewLine, findings));
    }

    /// <summary>
    /// A copy that was re-wrapped on the way in is still the same block. This is the
    /// half of the comparison that a byte-for-byte one would miss, and re-wrapping is
    /// what an editor does to a pasted paragraph without being asked.
    /// </summary>
    [Fact]
    public void ACopyWrappedDifferentlyIsStillRefused()
    {
        const string Document = """
            A page about one thing.

            The list is held on the server and
            shown by clients that were never changed.

            The list is held on the server
            and shown by clients
            that were never changed.
            """;

        var findings = RepeatedParagraphScanner.Scan("wrapped.md", Document);

        Assert.Single(findings);
    }

    /// <summary>
    /// The bound, executed rather than only written down. Two copies with a paragraph
    /// between them are not refused, because that is the shape `docs/api.md` and
    /// `docs/settings.md` repeat a clause per section in, and a rule reaching it would
    /// refuse how those two documents are built.
    /// </summary>
    [Fact]
    public void ARepeatWithSomethingBetweenTheCopiesIsNotRefused()
    {
        const string Document = """
            A page about one thing.

            Where it is stored: the plugin's configuration.

            A second setting, which is a different setting.

            Where it is stored: the plugin's configuration.
            """;

        var findings = RepeatedParagraphScanner.Scan("spaced.md", Document);

        Assert.Empty(findings);
    }

    /// <summary>
    /// The alias table names two resources, and an entry naming a resource the build
    /// no longer produces would rename a document to nothing while the population
    /// stayed the same size. It fails closed here rather than being found in a
    /// failure message that points at a path nobody can open.
    /// </summary>
    [Fact]
    public void EveryAliasedDocumentIsInTheAssembly()
    {
        var resources = Suite.GetManifestResourceNames();

        Assert.NotEmpty(Aliases);
        Assert.All(Aliases, a => Assert.Contains(a.Resource, resources, StringComparer.Ordinal));
    }

    private static IReadOnlyList<RepeatedBlock> ScanFixture(string fixture)
    {
        var name = fixture[..^".txt".Length];

        return RepeatedParagraphScanner.Scan(name, Resource(FixturePrefix + fixture));
    }

    /// <summary>
    /// Every Markdown document this assembly carries, as the resource it is stored
    /// under and the name a finding is reported by. The population is derived from the
    /// resource names rather than listed, so a document added to the tree tomorrow is
    /// judged the day it is added.
    /// </summary>
    private static IReadOnlyList<(string Resource, string Name)> DocumentEntries() => Suite
        .GetManifestResourceNames()
        .Where(n => n.EndsWith(".md", StringComparison.Ordinal))
        .Where(n => !n.StartsWith(FixturePrefix, StringComparison.Ordinal))
        .Select(n => (Resource: n, Name: ReportedName(n)))
        .OrderBy(e => e.Name, StringComparer.Ordinal)
        .ToList();

    private static string ReportedName(string resource)
    {
        foreach (var (aliased, path) in Aliases)
        {
            if (string.Equals(resource, aliased, StringComparison.Ordinal))
            {
                return path;
            }
        }

        if (resource.StartsWith(TreePrefix, StringComparison.Ordinal))
        {
            return resource[TreePrefix.Length..].Replace('\\', '/');
        }

        if (resource.StartsWith(SuitePrefix, StringComparison.Ordinal))
        {
            return "Jellyfin.Plugin.Watchlist.Tests/" + resource[SuitePrefix.Length..];
        }

        return resource;
    }

    private static string Resource(string name)
    {
        using var stream = Suite.GetManifestResourceStream(name)
            ?? throw new InvalidOperationException($"No embedded resource is named {name}.");
        using var reader = new System.IO.StreamReader(stream);

        return reader.ReadToEnd();
    }
}
