using System;
using Xunit;

namespace Jellyfin.Plugin.Watchlist.Tests;

/// <summary>
/// The file the manifest asks to be packaged against the file the build produces. The
/// packaging tool takes the named files out of the build output, so a name no build wrote
/// is a packaging step that finds nothing, and the moment it finds nothing is the moment a
/// tag has already been spent.
/// </summary>
public class PackageArtifactsTests
{
    /// <summary>
    /// The real pair. This is the one that reds when the assembly is renamed and the
    /// manifest keeps the name it had, or the other way round.
    /// </summary>
    [Fact]
    public void TheDeclaredArtifactIsTheAssemblyTheBuildProduces()
    {
        var disagreements = PackageArtifacts.Disagreements(
            BuildManifest.Text,
            PackageArtifacts.BuiltAssemblyFile);

        Assert.True(
            disagreements.Count == 0,
            string.Join(Environment.NewLine, disagreements));
    }

    /// <summary>
    /// The near miss: the assembly is renamed and the manifest keeps the old file name.
    /// One value moved, in the file where an AssemblyName is set, and none in the other.
    /// </summary>
    [Fact]
    public void AnAssemblyRenamedWithoutTheManifestIsRefused()
    {
        var disagreements = PackageArtifacts.Disagreements(
            BuildManifest.Text,
            "Jellyfin.Plugin.Watchlists.dll");

        Assert.Contains(disagreements, d => d.Contains("Jellyfin.Plugin.Watchlists.dll", StringComparison.Ordinal));
    }

    /// <summary>
    /// The same mistake made in the manifest. One letter in a file name nobody reads back
    /// is the shape this comparison exists for, because the string is never compiled and
    /// nothing else reads it until a release.
    /// </summary>
    [Fact]
    public void AManifestNamingAFileNoBuildWritesIsRefused()
    {
        var mistyped = BuildManifest.WithArtifact(BuildManifest.Text, "Jellyfin.Plugin.Watchlist.Dll");

        var disagreements = PackageArtifacts.Disagreements(mistyped, PackageArtifacts.BuiltAssemblyFile);

        Assert.Contains(disagreements, d => d.Contains("Jellyfin.Plugin.Watchlist.Dll", StringComparison.Ordinal));
    }

    /// <summary>
    /// The pair that moved together, which the comparison has to accept or it refuses the
    /// repair as readily as the mistake.
    /// </summary>
    [Fact]
    public void AnAssemblyAndAManifestThatMovedTogetherAreAccepted()
    {
        var renamed = BuildManifest.WithArtifact(BuildManifest.Text, "Jellyfin.Plugin.Watchlists.dll");

        Assert.Empty(PackageArtifacts.Disagreements(renamed, "Jellyfin.Plugin.Watchlists.dll"));
    }

    /// <summary>
    /// A second file asked for beside the one this repository builds. It is accepted by the
    /// first half of the comparison, because the built assembly is still listed, and it is
    /// what the second half is for.
    /// </summary>
    [Fact]
    public void AFileThisRepositoryDoesNotBuildIsRefused()
    {
        var widened = BuildManifest.WithExtraArtifact(BuildManifest.Text, "Newtonsoft.Json.dll");

        var disagreements = PackageArtifacts.Disagreements(widened, PackageArtifacts.BuiltAssemblyFile);

        Assert.Contains(disagreements, d => d.Contains("Newtonsoft.Json.dll", StringComparison.Ordinal));
    }

    /// <summary>
    /// A manifest that has lost the key is refused rather than compared. The packaging tool
    /// reads this key without a default, and a comparison against a value that is not there
    /// passes everything.
    /// </summary>
    [Fact]
    public void AManifestWithNoArtifactsKeyIsRefused()
    {
        var gutted = BuildManifest.WithoutArtifacts(BuildManifest.Text);

        Assert.Throws<InvalidOperationException>(
            () => PackageArtifacts.Disagreements(gutted, PackageArtifacts.BuiltAssemblyFile));
    }

    /// <summary>
    /// A key that opens onto nothing. That is a different state from a missing key and it
    /// is reported rather than thrown: the manifest can be read, and what it says is that
    /// the package is built out of no files at all.
    /// </summary>
    [Fact]
    public void AManifestListingNoArtifactIsRefused()
    {
        // The entry is taken out by what the manifest declares rather than by what the
        // build produces, so this fixture still says what it is about on a tree where the
        // two have come apart.
        var emptied = BuildManifest.Text.Replace(
            "- \"" + BuildManifest.ReadArtifacts(BuildManifest.Text)[0] + "\"",
            string.Empty,
            StringComparison.Ordinal);

        Assert.Contains(
            PackageArtifacts.Disagreements(emptied, PackageArtifacts.BuiltAssemblyFile),
            d => d.Contains("lists no artifact", StringComparison.Ordinal));
    }

    /// <summary>
    /// The name is read off the compiled assembly rather than off a string in the suite, so
    /// an AssemblyName set anywhere reaches this comparison. Stated on its own because it
    /// is the property that makes the real pair above worth anything.
    /// </summary>
    [Fact]
    public void TheAssemblyNameComesFromTheAssembly()
    {
        Assert.Equal(
            PluginUnderTest.Assembly.GetName().Name + ".dll",
            PackageArtifacts.BuiltAssemblyFile);
    }
}
