using System.Linq;
using Xunit;

namespace Jellyfin.Plugin.Watchlist.Tests;

/// <summary>
/// The dependency graph is pinned, and these are the readings that say the pin is
/// still describing this tree.
/// </summary>
/// <remarks>
/// <para>
/// Two builds of one commit resolving different packages is the failure the pin exists
/// against, and its whole symptom is that nothing looks wrong. The restore refuses the
/// moved graph, with NU1004, and a restore with no lock file to bind is refused by a
/// target in Directory.Build.props, because locked mode binds a file that exists and a
/// missing one is otherwise written afresh.
/// </para>
/// <para>
/// Those two refusals are the enforcement and neither of them reads what is inside the
/// file. These tests do: they read the committed lock files out of the test assembly
/// and say each one still describes the project beside it.
/// </para>
/// </remarks>
public class PackageLockTests
{
    /// <summary>
    /// Every project in the solution carries a lock file that describes it. This is the
    /// assertion the rest of the file exists to give weight to.
    /// </summary>
    /// <param name="index">Which project, by position in the solution.</param>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void EveryProjectHasALockFileThatDescribesIt(int index)
    {
        var pair = PackageLock.All[index];

        Assert.Equal(
            System.Array.Empty<string>(),
            PackageLock.Disagreements(pair.ProjectText, pair.LockText).ToArray());
    }

    /// <summary>
    /// Both projects are judged, and not one of them twice. A theory that read the same
    /// pair under two indexes would pass exactly as this one does.
    /// </summary>
    [Fact]
    public void TheJudgedProjectsAreTheSolutionsTwoAndAreDistinct()
    {
        Assert.Equal(2, PackageLock.All.Count);
        Assert.Equal("Jellyfin.Plugin.Watchlist", PackageLock.All[0].Name);
        Assert.Equal("Jellyfin.Plugin.Watchlist.Tests", PackageLock.All[1].Name);
        Assert.NotEqual(PackageLock.All[0].LockText, PackageLock.All[1].LockText);
    }

    /// <summary>
    /// The near miss the pin is for. A version moves in the project and the lock file
    /// stays where it was, which is the one-character edit somebody actually makes, and
    /// it is the same state the restore reports as NU1004.
    /// </summary>
    [Fact]
    public void AVersionThatMovedWithoutItsLockFileIsRefused()
    {
        var moved = PackageLock.WithReferenceVersion(
            PackageLock.Plugin.ProjectText,
            "Jellyfin.Controller",
            "10.9.12");

        var disagreements = PackageLock.Disagreements(moved, PackageLock.Plugin.LockText);

        Assert.Contains(
            disagreements,
            d => d.Contains("Jellyfin.Controller", System.StringComparison.Ordinal)
                && d.Contains("10.9.12", System.StringComparison.Ordinal));
    }

    /// <summary>
    /// The other direction. A reference is removed from the project and the lock file
    /// keeps its direct entry, so the lock describes a graph that is no longer this one.
    /// </summary>
    [Fact]
    public void AReferenceRemovedFromTheProjectWithoutItsLockFileIsRefused()
    {
        var withoutModel = PackageLock.Plugin.ProjectText.Replace(
            @"<PackageReference Include=""Jellyfin.Model"" Version=""10.9.11"">",
            @"<PackageReference Include=""Removed.Placeholder"" Version=""10.9.11"">",
            System.StringComparison.Ordinal);

        var disagreements = PackageLock.Disagreements(withoutModel, PackageLock.Plugin.LockText);

        Assert.Contains(
            disagreements,
            d => d.Contains("Jellyfin.Model", System.StringComparison.Ordinal)
                && d.Contains("no longer", System.StringComparison.Ordinal));
    }

    /// <summary>
    /// A lock file entry with no content hash names a version and pins no bytes, which
    /// reads exactly like one that does. The comparison refuses it, because the reason
    /// the file is committed is the hash and not the number.
    /// </summary>
    [Fact]
    public void AnEntryThatPinsNoBytesIsRefused()
    {
        var unpinned = PackageLock.WithoutContentHash(PackageLock.Plugin.LockText, "Jellyfin.Controller");

        var disagreements = PackageLock.Disagreements(PackageLock.Plugin.ProjectText, unpinned);

        Assert.Contains(
            disagreements,
            d => d.Contains("Jellyfin.Controller", System.StringComparison.Ordinal)
                && d.Contains("content hash", System.StringComparison.Ordinal));
    }

    /// <summary>
    /// A lock file written for another framework is not this project's lock file, and
    /// the comparison says so rather than finding no entries and calling that agreement.
    /// </summary>
    [Fact]
    public void ALockFileForAnotherFrameworkIsRefused()
    {
        var elsewhere = PackageLock.Plugin.LockText.Replace(
            @"""net9.0""",
            @"""net8.0""",
            System.StringComparison.Ordinal);

        var disagreements = PackageLock.Disagreements(PackageLock.Plugin.ProjectText, elsewhere);

        Assert.Contains(disagreements, d => d.Contains("net9.0", System.StringComparison.Ordinal));
    }

    /// <summary>
    /// And the mutations above are mutations of the real thing rather than of a fixture
    /// that resembles it, so each of them starts from a pair the comparison accepts.
    /// Without this, a mutation that broke the input some other way would produce the
    /// same red and prove nothing.
    /// </summary>
    [Fact]
    public void ThePairEveryNearMissStartsFromIsAcceptedUnmutated()
    {
        Assert.Empty(PackageLock.Disagreements(PackageLock.Plugin.ProjectText, PackageLock.Plugin.LockText));
    }
}
