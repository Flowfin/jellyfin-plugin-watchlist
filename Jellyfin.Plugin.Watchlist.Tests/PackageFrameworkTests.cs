using System;
using Xunit;

namespace Jellyfin.Plugin.Watchlist.Tests;

/// <summary>
/// The framework the package declares against the framework the project is compiled for.
/// The packaging tool reads the declared value as the directory to take the assembly from,
/// so the two disagreeing is a release that packages nothing or packages the wrong thing,
/// and the only reading that catches it today happens once, at the moment a tag is spent.
/// </summary>
public class PackageFrameworkTests
{
    /// <summary>
    /// The real pair. This is the one that reds when the project moves framework and the
    /// manifest stays where it was, or the other way round.
    /// </summary>
    [Fact]
    public void TheDeclaredFrameworkIsTheOneTheProjectIsBuiltFor()
    {
        var disagreements = PackageFramework.Disagreements(BuildManifest.Text, PackageLock.Plugin.ProjectText);

        Assert.True(
            disagreements.Count == 0,
            string.Join(Environment.NewLine, disagreements));
    }

    /// <summary>
    /// The near miss: the project moves to the framework the next runtime needs and the
    /// manifest keeps the one it had. One value in one file changed, none in the other.
    /// </summary>
    /// <remarks>
    /// This is the mistake this tree has already made once, in the other direction: the
    /// manifest named net8.0 while both projects targeted net9.0, and the only thing that
    /// would have refused it was the release route.
    /// </remarks>
    [Fact]
    public void AProjectMovedWithoutTheManifestIsRefused()
    {
        var moved = PackageLock.Plugin.ProjectText
            .Replace("<TargetFramework>net9.0</TargetFramework>", "<TargetFramework>net10.0</TargetFramework>", StringComparison.Ordinal);

        var disagreements = PackageFramework.Disagreements(BuildManifest.Text, moved);

        Assert.Contains(disagreements, d => d.Contains("net10.0", StringComparison.Ordinal));
    }

    /// <summary>
    /// The same mistake made in the file the earlier one was made in. A manifest edited for
    /// a line the project has not moved to points the packaging step at a directory the
    /// build never wrote.
    /// </summary>
    [Fact]
    public void AManifestMovedWithoutTheProjectIsRefused()
    {
        var moved = BuildManifest.WithFramework(BuildManifest.Text, "net8.0");

        var disagreements = PackageFramework.Disagreements(moved, PackageLock.Plugin.ProjectText);

        Assert.Contains(disagreements, d => d.Contains("net8.0", StringComparison.Ordinal));
    }

    /// <summary>
    /// The pair that moved together, which the comparison has to accept or it refuses the
    /// repair as readily as the mistake.
    /// </summary>
    [Fact]
    public void AProjectAndAManifestThatMovedTogetherAreAccepted()
    {
        var moved = PackageLock.Plugin.ProjectText
            .Replace("<TargetFramework>net9.0</TargetFramework>", "<TargetFramework>net10.0</TargetFramework>", StringComparison.Ordinal);
        var manifest = BuildManifest.WithFramework(BuildManifest.Text, "net10.0");

        Assert.Empty(PackageFramework.Disagreements(manifest, moved));
    }

    /// <summary>
    /// A manifest that has lost the key is refused rather than compared. A comparison
    /// against a value that is not there is the shape that turns a guard off while every run
    /// stays green, and the packaging tool reads this key without a default and dies after
    /// the compile when it is absent.
    /// </summary>
    [Fact]
    public void AManifestWithNoDeclaredFrameworkIsRefused()
    {
        var gutted = BuildManifest.WithoutFramework(BuildManifest.Text);

        Assert.Throws<InvalidOperationException>(
            () => PackageFramework.Disagreements(gutted, PackageLock.Plugin.ProjectText));
    }

    /// <summary>
    /// A project whose framework has moved out of the file this reads. Moving the property
    /// into a shared one is an ordinary thing to do and it must not be the way the
    /// comparison stops applying.
    /// </summary>
    [Fact]
    public void AProjectWithNoSingleTargetFrameworkIsRefused()
    {
        var lifted = PackageLock.Plugin.ProjectText
            .Replace("<TargetFramework>net9.0</TargetFramework>", string.Empty, StringComparison.Ordinal);

        Assert.Contains(
            PackageFramework.Disagreements(BuildManifest.Text, lifted),
            d => d.Contains("no single target framework", StringComparison.Ordinal));
    }
}
