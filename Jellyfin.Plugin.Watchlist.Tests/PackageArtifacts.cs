using System;
using System.Collections.Generic;
using System.Linq;

namespace Jellyfin.Plugin.Watchlist.Tests;

/// <summary>
/// The files a package says it is built out of against the assembly the build actually
/// produces, and the comparison between the two.
/// </summary>
/// <remarks>
/// <para>
/// The packaging tool takes the named files out of the build output and puts them in the
/// archive. A name that no build wrote is not a smaller package: it is a packaging step
/// that finds nothing where it was told to look, at the moment a tag is spent, and the
/// only two ways that ends are a run that dies there and a run that ships an archive
/// without the plugin in it.
/// </para>
/// <para>
/// The release route derives the assembly name from MSBuild and compares it with what the
/// archive turned out to contain. It never compares it with what the manifest asked for,
/// so the two strings that have to agree before there is an archive at all are compared
/// with each other nowhere.
/// </para>
/// <para>
/// The assembly name arrives from the compiled assembly rather than from the project file,
/// so an AssemblyName set in the project, in a shared property file or on the command line
/// all reach this comparison the same way. Nothing here reaches the file system or the
/// network.
/// </para>
/// </remarks>
internal static class PackageArtifacts
{
    /// <summary>
    /// The file the plugin build produces, as the compiled assembly names itself.
    /// </summary>
    public static string BuiltAssemblyFile =>
        PluginUnderTest.Assembly.GetName().Name + ".dll";

    /// <summary>
    /// Says every way in which what a manifest asks to be packaged and what the build
    /// produces fail to name one file. An empty result is agreement.
    /// </summary>
    /// <param name="manifestText">The packaging manifest.</param>
    /// <param name="builtAssemblyFile">The file the build produces.</param>
    /// <returns>One line per disagreement, empty when there is none.</returns>
    public static IReadOnlyList<string> Disagreements(string manifestText, string builtAssemblyFile)
    {
        ArgumentNullException.ThrowIfNull(builtAssemblyFile);

        var disagreements = new List<string>();
        var declared = BuildManifest.ReadArtifacts(manifestText);

        if (declared.Count == 0)
        {
            // An empty sequence and a missing key are different states and only one of
            // them throws. This one is reported, because a manifest that opens the key and
            // lists nothing under it is a package with nothing in it rather than a manifest
            // the tool cannot read.
            disagreements.Add(
                "The manifest lists no artifact, so the package would be built out of nothing. The build produces "
                + builtAssemblyFile
                + ".");
            return disagreements;
        }

        if (!declared.Contains(builtAssemblyFile, StringComparer.Ordinal))
        {
            disagreements.Add(
                "The manifest asks for "
                + string.Join(", ", declared)
                + " and the build produces "
                + builtAssemblyFile
                + ". The packaging step takes the named files out of the build output.");
        }

        var unaccounted = declared
            .Where(a => !string.Equals(a, builtAssemblyFile, StringComparison.Ordinal))
            .ToArray();

        if (unaccounted.Length > 0)
        {
            // One project, one assembly, one line. A second name here is a file this
            // repository does not build, and the release route's own inventory step
            // refuses an assembly in the archive that no package accounts for. This is the
            // same refusal one step earlier, where the name is still a string somebody
            // typed.
            disagreements.Add(
                "The manifest asks for "
                + string.Join(", ", unaccounted)
                + ", which this repository does not build. One artifact is one assembly, and a second server line gets a manifest of its own.");
        }

        return disagreements;
    }
}
