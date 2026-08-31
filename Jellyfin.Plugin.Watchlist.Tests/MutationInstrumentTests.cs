using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Xunit;

namespace Jellyfin.Plugin.Watchlist.Tests;

/// <summary>
/// The two things about the mutation instrument that have to stay true, and that
/// nothing else would notice going wrong.
/// </summary>
/// <remarks>
/// <para>
/// The instrument itself is not run from here and cannot be: it builds the plugin
/// several hundred times and re-runs this suite against each build, which is minutes
/// rather than the second this suite takes, and a suite that ran it would be a suite
/// nobody runs. What is judged here is its CONFIGURATION, which is two files in this
/// tree and is exactly the part that can drift without anybody meeting a failure.
/// </para>
/// <para>
/// MUTATION.md beside this file carries what the run found and what each surviving
/// mutant was judged to be.
/// </para>
/// </remarks>
public sealed class MutationInstrumentTests
{
    // Written with forward slashes and MATCHED with either, because the separator
    // inside the recursive part of a logical name is the one the build put there and
    // that is the host's: a backslash on Windows and a slash on Linux and macOS. A
    // name spelled one way is a test that passes on the machine it was written on and
    // reds the other two, which is how this was found.
    private const string WorkflowResource = "tree/.github/workflows/mutation.yaml";

    private const string ConfigResource = "Jellyfin.Plugin.Watchlist.Tests.stryker-config.json";

    private const string RegisterResource = "Jellyfin.Plugin.Watchlist.Tests.MUTATION.md";

    /// <summary>
    /// The instrument never becomes a pull request check.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The decision is in the workflow's own header and in docs/parity.md: a mutation
    /// score that blocks a merge is a number people learn to raise rather than a
    /// question about the suite, and the run costs minutes rather than seconds. A
    /// sentence in a header is not what keeps that true - adding two lines to the
    /// trigger block is a change somebody makes while adding a trigger they do want,
    /// and every check on this board would stay green.
    /// </para>
    /// <para>
    /// It reads the trigger block rather than the file, so a `pull_request` written
    /// inside a comment or in a step's own name is not a match. The block is the lines
    /// under `on:` up to the next declaration at the left margin, which is the shape
    /// every workflow in this tree is written in.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheMutationRunHasNoPullRequestTrigger()
    {
        var triggers = TriggerBlockOf(Text(WorkflowResource));

        Assert.NotEmpty(triggers);
        Assert.DoesNotContain(triggers, line => line.Contains("pull_request", StringComparison.Ordinal));
    }

    /// <summary>
    /// The scope is the three subjects the instrument was chosen for, and nothing else.
    /// </summary>
    /// <remarks>
    /// Both directions matter and they fail differently. A subject dropped from the
    /// scope is a subject nobody mutates any more, reported as a slightly better score
    /// rather than as a gap. A subject added is minutes of run time and a page of
    /// mutants in code the instrument was not chosen for, which is how a triage list
    /// stops being read.
    /// </remarks>
    [Fact]
    public void TheScopeIsTheThreeSubjectsAndNothingElse()
    {
        Assert.Equal(
            [
                "**/Projection/WatchlistReconciler.cs",
                "**/Store/*.cs",
                "**/Watched/LibrarySeriesCompletion.cs",
            ],
            ScopeIn(Text(ConfigResource)));
    }

    /// <summary>
    /// The register is in the assembly, which is what makes deleting or renaming it a
    /// red suite rather than a quiet loss.
    /// </summary>
    /// <remarks>
    /// The same reason HEADLESS.md is embedded: a document that carries the verdicts
    /// for a run's survivors is worth as much as the run, and nothing else in this tree
    /// would notice it going missing.
    /// </remarks>
    [Fact]
    public void TheRegisterOfSurvivingMutantsIsCarriedWithTheSuite()
    {
        Assert.Contains("The eleven that survived", Text(RegisterResource), StringComparison.Ordinal);
    }

    private static IReadOnlyList<string> TriggerBlockOf(string workflow)
    {
        var lines = workflow.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        var block = new List<string>();
        var inside = false;

        foreach (var line in lines)
        {
            if (inside && line.Length > 0 && !char.IsWhiteSpace(line[0]))
            {
                break;
            }

            if (inside && line.TrimStart().StartsWith('#'))
            {
                continue;
            }

            if (inside && line.Trim().Length > 0)
            {
                block.Add(line);
            }

            if (line.StartsWith("on:", StringComparison.Ordinal))
            {
                inside = true;
            }
        }

        return block;
    }

    private static IReadOnlyList<string> ScopeIn(string config)
    {
        using var document = System.Text.Json.JsonDocument.Parse(config);

        return document
            .RootElement
            .GetProperty("stryker-config")
            .GetProperty("mutate")
            .EnumerateArray()
            .Select(entry => entry.GetString()!)
            .OrderBy(entry => entry, StringComparer.Ordinal)
            .ToList();
    }

    private static string Text(string resource)
    {
        var assembly = typeof(MutationInstrumentTests).GetTypeInfo().Assembly;

        var name = assembly
            .GetManifestResourceNames()
            .FirstOrDefault(candidate => Normalise(candidate) == Normalise(resource))
            ?? throw new InvalidOperationException(
                resource + " is not embedded in the test assembly. The assembly carries: "
                + string.Join(", ", assembly.GetManifestResourceNames()));

        using var stream = assembly.GetManifestResourceStream(name)!;
        using var reader = new StreamReader(stream);

        return reader.ReadToEnd();
    }

    private static string Normalise(string resource) =>
        resource.Replace('\\', '/');
}
