using System;
using Xunit;

namespace Jellyfin.Plugin.Watchlist.Tests;

/// <summary>
/// The changelog and the manifest key the packaged notes are written into. A release
/// carries notes, and notes that say something other than what the repository says
/// changed are worse than none, because a user acts on them. The notes are held in
/// one file for that reason: the publish route takes them from CHANGELOG.md at
/// package time, so a paragraph copied into build.yaml is one nothing ships and
/// nothing keeps current.
/// </summary>
public class ChangelogTests
{
    /// <summary>
    /// The real manifest. This is the one that reds when somebody writes release notes
    /// into build.yaml instead of into the file they ship from.
    /// </summary>
    [Fact]
    public void TheManifestCarriesNoSecondCopyOfTheNotes()
    {
        Assert.True(
            Changelog.NotesAreWrittenOnce(BuildManifest.Text, Changelog.Text),
            "build.yaml's changelog block is not a pointer at CHANGELOG.md. It reads: "
                + Changelog.PackagedEntry(BuildManifest.Text));
    }

    /// <summary>
    /// The near miss for the first half: a section pasted into the manifest, which is
    /// what the tree carried while the two copies were held together and is what
    /// somebody preparing a release by hand would reach for. It takes the newest
    /// section rather than the one for the declared version, so a manifest whose
    /// version has no section reds the test below and only that one.
    /// </summary>
    [Fact]
    public void NotesCopiedIntoTheManifestAreRefused()
    {
        var sections = Changelog.Entries(Changelog.Text);
        Assert.NotEmpty(sections);

        var copied = Changelog.WithPackagedEntry(BuildManifest.Text, sections[0]);

        Assert.NotEqual(BuildManifest.Text, copied);
        Assert.False(Changelog.NotesAreWrittenOnce(copied, Changelog.Text));
    }

    /// <summary>
    /// The near miss for the second half: a block that is neither the notes nor a
    /// pointer at them. It is the shape an edit leaves behind when somebody empties the
    /// key rather than removing it, and it reads as a value somebody chose.
    /// </summary>
    [Fact]
    public void AManifestBlockThatNamesNoSourceIsRefused()
    {
        var vague = Changelog.WithPackagedEntry(BuildManifest.Text, "Release notes.");

        Assert.NotEqual(BuildManifest.Text, vague);
        Assert.False(Changelog.NotesAreWrittenOnce(vague, Changelog.Text));
    }

    /// <summary>
    /// The version the manifest declares has a section. This is what a version bump
    /// meets, and it is the half of the old pair comparison that survived removing the
    /// copy: the publish route refuses a version with no section, and this refuses it
    /// on every pull request rather than on the one push that cannot be repeated.
    /// </summary>
    [Fact]
    public void TheDeclaredVersionHasASection()
    {
        var declared = BuildManifest.ReadVersion(BuildManifest.Text);

        Assert.NotNull(Changelog.EntryFor(Changelog.Text, declared));
    }

    /// <summary>
    /// The near miss for that one: a version bumped in the manifest and the changelog
    /// left alone. It is the mistake a release makes, and it is one character in one
    /// file.
    /// </summary>
    [Fact]
    public void AVersionBumpedWithoutAnEntryIsRefused()
    {
        var bumped = BuildManifest.WithVersion(BuildManifest.Text, "0.2.0.0");

        Assert.Null(Changelog.EntryFor(Changelog.Text, BuildManifest.ReadVersion(bumped)));
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
