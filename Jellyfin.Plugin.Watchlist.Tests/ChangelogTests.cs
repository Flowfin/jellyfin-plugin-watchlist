using System;
using Xunit;

namespace Jellyfin.Plugin.Watchlist.Tests;

/// <summary>
/// The changelog and the manifest's packaged copy of it. A release carries notes,
/// and notes that say something other than what the repository says changed are
/// worse than none, because a user acts on them.
/// </summary>
public class ChangelogTests
{
    /// <summary>
    /// The real pair. This is the one that reds when somebody edits either file and
    /// not the other.
    /// </summary>
    [Fact]
    public void TheManifestPackagesTheNewestChangelogEntry()
    {
        Assert.True(
            Changelog.Agrees(BuildManifest.Text, Changelog.Text),
            "build.yaml and CHANGELOG.md disagree. The manifest packages: "
                + Changelog.PackagedEntry(BuildManifest.Text)
                + " and the changelog section for the declared version holds: "
                + (Changelog.EntryFor(Changelog.Text, BuildManifest.ReadVersion(BuildManifest.Text)) ?? "no section at all"));
    }

    /// <summary>
    /// The version the manifest declares has a section. Stated on its own as well as
    /// inside the rule above, because "the texts differ" and "there is no section" are
    /// different mistakes and the second one is the one a version bump makes.
    /// </summary>
    [Fact]
    public void TheDeclaredVersionHasASection()
    {
        var declared = BuildManifest.ReadVersion(BuildManifest.Text);

        Assert.NotNull(Changelog.EntryFor(Changelog.Text, declared));
    }

    /// <summary>
    /// The near miss for the second half of the rule: a version bumped in the manifest
    /// and the changelog left alone. It is the mistake a release makes, and it is one
    /// character in one file.
    /// </summary>
    [Fact]
    public void AVersionBumpedWithoutAnEntryIsRefused()
    {
        var bumped = BuildManifest.WithVersion(BuildManifest.Text, "0.2.0.0");

        Assert.False(Changelog.Agrees(bumped, Changelog.Text));
        Assert.Null(Changelog.EntryFor(Changelog.Text, new Version(0, 2, 0, 0)));
    }

    /// <summary>
    /// The near miss for the first half: the section is there and the packaged text is
    /// not what it says. One word changed in the manifest, which is what a hand edit
    /// to release notes looks like.
    /// </summary>
    [Fact]
    public void PackagedTextThatIsNotTheEntryIsRefused()
    {
        var edited = BuildManifest.Text.Replace(
            "nothing to install",
            "nothing much to install",
            StringComparison.Ordinal);

        Assert.NotEqual(BuildManifest.Text, edited);
        Assert.False(Changelog.Agrees(edited, Changelog.Text));
    }

    /// <summary>
    /// The one-change neighbour of both near misses. The pair as it ships agrees, so
    /// the two above prove the rule bites rather than that the comparison is broken.
    /// </summary>
    [Fact]
    public void TheShippedPairAgrees()
    {
        Assert.True(Changelog.Agrees(BuildManifest.Text, Changelog.Text));
    }

    /// <summary>
    /// The changelog is read as sections rather than as text, so a file that has
    /// stopped being one is a failure here rather than a silently empty comparison.
    /// </summary>
    [Fact]
    public void TheChangelogCarriesAtLeastOneSection()
    {
        Assert.NotEmpty(Changelog.Versions(Changelog.Text));
    }
}
