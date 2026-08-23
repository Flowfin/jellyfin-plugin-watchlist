using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace Jellyfin.Plugin.Watchlist.Tests;

/// <summary>
/// The target framework a package declares against the one the project is compiled for,
/// and the comparison between the two.
/// </summary>
/// <remarks>
/// <para>
/// The packaging tool joins the declared value into the path it takes the built assembly
/// from. So a manifest naming a framework the project does not target points the packaging
/// step at a directory the build never wrote, and the two ways that ends are a run that
/// dies after the compile and a run that packages whatever was left in that directory by an
/// earlier build. Neither is a package anybody chose.
/// </para>
/// <para>
/// The release route already compares both values against a constant of its own, and it
/// does it at the moment a tag is spent. That is one reading per release. This is the same
/// disagreement read on every change, before the tag exists.
/// </para>
/// <para>
/// Both inputs arrive as embedded resources, the manifest and the project file, so the same
/// function judges this tree's real pair and a deliberately broken one. Nothing here reaches
/// the file system or the network.
/// </para>
/// </remarks>
internal static class PackageFramework
{
    /// <summary>
    /// The single target framework a project declares. The same bound as the reader in
    /// PackageLock beside it: a project that sets the value through a property or a
    /// TargetFrameworks list is not matched, and what that produces is a reported absence
    /// rather than a silent pass.
    /// </summary>
    private static readonly Regex TargetFrameworkEntry = new(
        @"<TargetFramework>(?<tfm>[^<]+)</TargetFramework>",
        RegexOptions.CultureInvariant);

    /// <summary>
    /// Reads the framework a project targets.
    /// </summary>
    /// <param name="projectText">The project file to read.</param>
    /// <returns>The declared moniker, or null when the project declares no single one.</returns>
    public static string? OfProject(string projectText)
    {
        ArgumentNullException.ThrowIfNull(projectText);

        var match = TargetFrameworkEntry.Match(projectText);
        return match.Success ? match.Groups["tfm"].Value : null;
    }

    /// <summary>
    /// Says every way in which what a manifest declares and what a project compiles for fail
    /// to name one framework. An empty result is agreement.
    /// </summary>
    /// <param name="manifestText">The packaging manifest.</param>
    /// <param name="projectText">The project file the artifact is built from.</param>
    /// <returns>One line per disagreement, empty when there is none.</returns>
    public static IReadOnlyList<string> Disagreements(string manifestText, string projectText)
    {
        var disagreements = new List<string>();

        var declared = BuildManifest.ReadFramework(manifestText);
        var targeted = OfProject(projectText);

        if (targeted is null)
        {
            // A project declaring no single target framework leaves nothing for the
            // manifest to agree with. Reported rather than read as agreement, because a
            // property moved into Directory.Build.props is exactly how this comparison
            // would otherwise be turned off without anybody editing it.
            disagreements.Add(
                "The project declares no single target framework, so there is nothing for the declared framework "
                + declared
                + " to agree with.");
            return disagreements;
        }

        if (!string.Equals(declared, targeted, StringComparison.Ordinal))
        {
            disagreements.Add(
                "The manifest declares the framework "
                + declared
                + " and the project is built for "
                + targeted
                + ". The packaging step takes the assembly out of the directory the declared value names.");
        }

        return disagreements;
    }
}
