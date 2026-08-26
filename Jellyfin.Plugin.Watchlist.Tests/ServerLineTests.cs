using System;
using Xunit;

namespace Jellyfin.Plugin.Watchlist.Tests;

/// <summary>
/// The ABI the package declares against the package set it was compiled from. A server
/// reads the declared value as a floor and has no upper bound to check it against, so an
/// artifact that names a line it was not built for is offered to a server that will load
/// it and then meet a method that is not there.
/// </summary>
public class ServerLineTests
{
    /// <summary>
    /// The real pair. This is the one that reds when a package version moves and the
    /// manifest stays where it was, or the other way round.
    /// </summary>
    [Fact]
    public void TheDeclaredAbiNamesTheLineThePackageSetWasBuiltFrom()
    {
        var disagreements = ServerLine.Disagreements(BuildManifest.Text, PackageLock.Plugin.ProjectText);

        Assert.True(
            disagreements.Count == 0,
            string.Join(Environment.NewLine, disagreements));
    }

    /// <summary>
    /// The near miss: the package set moves to another line and the manifest keeps the
    /// ABI it had. Two values in one file changed, none in the other.
    /// </summary>
    /// <remarks>
    /// The move it probes is the next one this tree will make, from the line it builds
    /// against today to the one the second half of #4 adds. It is a version inside a
    /// fixture string and never a reference anything restores, so it names the line and
    /// not a package: which 12.0 version this repository would pin is decided on #1
    /// rather than here, and answer 5 there, taken 2026-08-24, is none until a stable
    /// 12.0 server release exists.
    /// </remarks>
    [Fact]
    public void APackageSetMovedWithoutTheAbiIsRefused()
    {
        var moved = PackageLock.Plugin.ProjectText
            .Replace("Version=\"10.11.11\"", "Version=\"12.0.0\"", StringComparison.Ordinal);

        var disagreements = ServerLine.Disagreements(BuildManifest.Text, moved);

        Assert.Equal(2, disagreements.Count);
        Assert.All(disagreements, d => Assert.Contains("12.0", d, StringComparison.Ordinal));
    }

    /// <summary>
    /// The same fixture with the one value corrected. The pair that moved together is
    /// what the check has to accept, or it refuses the repair as well as the mistake.
    /// </summary>
    [Fact]
    public void APackageSetAndAnAbiThatMovedTogetherAreAccepted()
    {
        var moved = PackageLock.Plugin.ProjectText
            .Replace("Version=\"10.11.11\"", "Version=\"12.0.0\"", StringComparison.Ordinal);
        var manifest = BuildManifest.WithTargetAbi(BuildManifest.Text, "12.0.0.0");

        Assert.Empty(ServerLine.Disagreements(manifest, moved));
    }

    /// <summary>
    /// One reference bumped and the other left behind, which is what a hand edit to a
    /// project file looks like when it is interrupted. The artifact then compiles against
    /// two lines and can only declare one of them.
    /// </summary>
    [Fact]
    public void APackageSetThatNamesTwoLinesIsRefused()
    {
        var half = PackageLock.Plugin.ProjectText
            .Replace(
                "<PackageReference Include=\"Jellyfin.Controller\" Version=\"10.11.11\"",
                "<PackageReference Include=\"Jellyfin.Controller\" Version=\"12.0.0\"",
                StringComparison.Ordinal);

        var disagreements = ServerLine.Disagreements(BuildManifest.Text, half);

        Assert.Contains(disagreements, d => d.Contains("more than one line", StringComparison.Ordinal));
    }

    /// <summary>
    /// A manifest that has lost the key is refused rather than compared. A comparison
    /// against a value that is not there is the shape that turns a guard off while every
    /// run stays green, and the publish route's own check on this key reads its shape at
    /// a moment that happens once per release rather than once per change.
    /// </summary>
    [Fact]
    public void AManifestWithNoDeclaredAbiIsRefused()
    {
        var gutted = BuildManifest.WithoutTargetAbi(BuildManifest.Text);

        Assert.Throws<InvalidOperationException>(
            () => ServerLine.Disagreements(gutted, PackageLock.Plugin.ProjectText));
    }

    /// <summary>
    /// A project with no server package at all. The reference this comparison reads is
    /// the one a bad merge drops, and dropping it must not be the way the comparison
    /// stops applying.
    /// </summary>
    [Fact]
    public void AProjectWithNoServerPackageIsRefused()
    {
        var stripped = PackageLock.Plugin.ProjectText
            .Replace("Include=\"Jellyfin.", "Include=\"Removed.", StringComparison.Ordinal);

        Assert.Contains(
            ServerLine.Disagreements(BuildManifest.Text, stripped),
            d => d.Contains("references no server package", StringComparison.Ordinal));
    }

    /// <summary>
    /// The line is the first two positions and the build position is not part of it.
    /// Stated on its own because the rule reads two files whose third position never
    /// matches: an ABI is written A.B.0.0 and a package is pinned at the newest build of
    /// its line.
    /// </summary>
    [Theory]
    [InlineData("10.9.0.0", "10.9")]
    [InlineData("10.9.11", "10.9")]
    [InlineData("10.11.11", "10.11")]
    [InlineData("12.0.0.0", "12.0")]
    [InlineData("12.0.0-rc4", "12.0")]
    public void ALineIsTheFirstTwoPositions(string version, string expected)
    {
        Assert.Equal(expected, ServerLine.Of(version));
    }

    /// <summary>
    /// A value that names no line is refused rather than read as one. The publish route
    /// checks the shape of this key at release time; this says what the comparison does
    /// with a value that has never reached that check.
    /// </summary>
    [Fact]
    public void AValueThatNamesNoLineIsRefused()
    {
        Assert.Throws<FormatException>(() => ServerLine.Of("stable"));
    }
}
