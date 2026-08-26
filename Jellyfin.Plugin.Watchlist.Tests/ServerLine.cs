using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace Jellyfin.Plugin.Watchlist.Tests;

/// <summary>
/// The server line a package declares and the server line it was compiled against, and
/// the comparison between the two.
/// </summary>
/// <remarks>
/// <para>
/// A server reads the declared ABI as a floor and nothing else, on both supported lines,
/// so an artifact built against one line and declaring another is offered to a server it
/// cannot run on. Nothing on the server side refuses that, which is why the refusal lives
/// here.
/// </para>
/// <para>
/// Both inputs arrive as embedded resources, the manifest and the project file, so the
/// same function judges this tree's real pair and a deliberately broken one. Nothing here
/// reaches the file system or the network.
/// </para>
/// </remarks>
internal static class ServerLine
{
    /// <summary>
    /// A package reference, in the shape this tree writes them: the identifier first and
    /// the version second. The same bound as the reference reader beside it, in
    /// PackageLock: a reference written the other way round is not matched, and what that
    /// produces is a missing entry rather than a silent pass.
    /// </summary>
    private static readonly Regex PackageReferenceEntry = new(
        @"<PackageReference\s+Include=""(?<id>[^""]+)""\s+Version=""(?<version>[^""]+)""",
        RegexOptions.CultureInvariant);

    /// <summary>
    /// Reads the line a version names: its first two positions, which is what a server
    /// release is called. The build position is not part of it, because 10.11.11 and
    /// 10.11.3 are the same line and a package set is pinned at whichever of them was
    /// newest on the day.
    /// </summary>
    /// <param name="version">A version string, from a manifest or from a package reference.</param>
    /// <returns>The line it names, as text.</returns>
    /// <exception cref="FormatException">The string names no line.</exception>
    public static string Of(string version)
    {
        ArgumentNullException.ThrowIfNull(version);

        // A prerelease suffix belongs to the package and not to the line. 10.11.0-rc1 is
        // built against the 10.11 line exactly as 10.11.0 is. Whether this repository
        // pins such a package is decided on #1 rather than here, and answer 5 there,
        // taken 2026-08-24, is that it does not: the 12.0 artifact waits for a stable
        // 12.0 server release instead of being built against a candidate.
        var numeric = version.Split('-', '+')[0];
        var positions = numeric.Split('.');

        if (positions.Length < 2
            || !int.TryParse(positions[0], NumberStyles.None, CultureInfo.InvariantCulture, out var major)
            || !int.TryParse(positions[1], NumberStyles.None, CultureInfo.InvariantCulture, out var minor))
        {
            throw new FormatException(
                "The value " + version + " names no server line. Two leading numeric positions are what a line is.");
        }

        return string.Create(CultureInfo.InvariantCulture, $"{major}.{minor}");
    }

    /// <summary>
    /// The lines the server packages a project references name. One entry per reference,
    /// so a set that names two lines is visible as two entries rather than as one answer.
    /// </summary>
    /// <param name="projectText">The project file to read.</param>
    /// <returns>The package identifier and the line it names, one per server package.</returns>
    public static IReadOnlyList<(string Package, string Line)> OfPackageSet(string projectText)
    {
        ArgumentNullException.ThrowIfNull(projectText);

        return PackageReferenceEntry.Matches(projectText)
            .Where(m => m.Groups["id"].Value.StartsWith("Jellyfin.", StringComparison.Ordinal))
            .Select(m => (m.Groups["id"].Value, Of(m.Groups["version"].Value)))
            .ToArray();
    }

    /// <summary>
    /// Says every way in which what a manifest declares and what a project compiles
    /// against fail to name one server line. An empty result is agreement.
    /// </summary>
    /// <param name="manifestText">The packaging manifest.</param>
    /// <param name="projectText">The project file whose package set the artifact is built from.</param>
    /// <returns>One line per disagreement, empty when there is none.</returns>
    public static IReadOnlyList<string> Disagreements(string manifestText, string projectText)
    {
        var disagreements = new List<string>();

        var packages = OfPackageSet(projectText);
        if (packages.Count == 0)
        {
            // A project that references no server package builds against no line, and a
            // manifest declaring one is then a claim about nothing. Reported rather than
            // read as agreement, because a reference removed by a bad merge is exactly
            // how this comparison would otherwise be turned off.
            disagreements.Add(
                "The project references no server package, so there is no line for the declared ABI to agree with.");
            return disagreements;
        }

        var declared = Of(BuildManifest.ReadTargetAbi(manifestText));

        foreach (var (package, line) in packages)
        {
            if (!string.Equals(line, declared, StringComparison.Ordinal))
            {
                disagreements.Add(
                    "The manifest declares the "
                    + declared
                    + " line and "
                    + package
                    + " is the "
                    + line
                    + " line. A server offers the plugin on the strength of the declared value alone.");
            }
        }

        var lines = packages.Select(p => p.Line).Distinct(StringComparer.Ordinal).ToArray();
        if (lines.Length > 1)
        {
            disagreements.Add(
                "The server packages name more than one line: "
                + string.Join(", ", lines.Order(StringComparer.Ordinal))
                + ". One artifact is built against one line.");
        }

        return disagreements;
    }
}
