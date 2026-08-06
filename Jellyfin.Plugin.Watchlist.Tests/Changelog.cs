using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;

namespace Jellyfin.Plugin.Watchlist.Tests;

/// <summary>
/// The changelog, as the test assembly carries it, and the comparison between it
/// and the manifest's packaged copy. The comparison lives here as a named function
/// so the test that asserts the real pair and the tests that feed it a mutated pair
/// exercise the same code rather than two similar ones, which is how BuildManifest
/// beside it is arranged and for the same reason.
/// </summary>
internal static class Changelog
{
    /// <summary>
    /// A section heading and everything under it up to the next heading. The version
    /// is whatever the heading starts with, so a heading may carry a date or a note
    /// after the number without the number becoming unreadable.
    /// </summary>
    private static readonly Regex Section = new(
        @"^##[ \t]+(?<version>[0-9][0-9.]*)[^\r\n]*\r?$(?<body>(?:\r?\n(?!##)[^\r\n]*)*)",
        RegexOptions.Multiline | RegexOptions.CultureInvariant);

    /// <summary>
    /// The manifest's folded changelog block: the key, then every indented line under
    /// it. A folded block is what the file already used, and folding is what makes the
    /// packaged text one paragraph rather than a column of short lines.
    /// </summary>
    private static readonly Regex ManifestBlock = new(
        @"^changelog:[ \t]*>[ \t]*\r?$(?<body>(?:\r?\n[ \t]+[^\r\n]*)*)",
        RegexOptions.Multiline | RegexOptions.CultureInvariant);

    /// <summary>
    /// Gets the changelog text embedded at build time from the CHANGELOG.md beside the solution.
    /// </summary>
    public static string Text { get; } = ReadEmbedded("CHANGELOG.md");

    /// <summary>
    /// The versions the changelog carries a section for, newest first, in the order
    /// they appear.
    /// </summary>
    /// <param name="changelogText">The changelog to read.</param>
    /// <returns>The declared versions.</returns>
    public static IReadOnlyList<Version> Versions(string changelogText) => Section
        .Matches(changelogText)
        .Select(m => Version.Parse(m.Groups["version"].Value))
        .ToList();

    /// <summary>
    /// The text of the section a version carries, folded to one paragraph.
    /// </summary>
    /// <param name="changelogText">The changelog to read.</param>
    /// <param name="version">The version to find.</param>
    /// <returns>The folded section text, or null when there is no section for it.</returns>
    public static string? EntryFor(string changelogText, Version version)
    {
        foreach (Match match in Section.Matches(changelogText))
        {
            if (Version.Parse(match.Groups["version"].Value) == version)
            {
                return Fold(match.Groups["body"].Value);
            }
        }

        return null;
    }

    /// <summary>
    /// The changelog text a manifest carries, folded the same way.
    /// </summary>
    /// <param name="manifestText">The manifest to read.</param>
    /// <returns>The folded packaged text.</returns>
    public static string PackagedEntry(string manifestText)
    {
        var match = ManifestBlock.Match(manifestText);
        if (!match.Success)
        {
            throw new InvalidOperationException("The manifest carries no folded changelog block.");
        }

        return Fold(match.Groups["body"].Value);
    }

    /// <summary>
    /// The whole rule. The version a manifest declares has a section in the changelog,
    /// and the text the manifest packages is that section. Either half failing is the
    /// same defect from a user's side: a release whose notes say something other than
    /// what the repository says changed.
    /// </summary>
    /// <param name="manifestText">The manifest to read.</param>
    /// <param name="changelogText">The changelog to read.</param>
    /// <returns>True when the two agree.</returns>
    public static bool Agrees(string manifestText, string changelogText)
    {
        var entry = EntryFor(changelogText, BuildManifest.ReadVersion(manifestText));

        return entry is not null && string.Equals(entry, PackagedEntry(manifestText), StringComparison.Ordinal);
    }

    /// <summary>
    /// Folds a block to one paragraph: every line trimmed, blank lines dropped, the
    /// rest joined by single spaces. Both sides of the comparison go through this, so
    /// the rule is about the words and not about where a line happens to wrap.
    /// </summary>
    /// <param name="block">The block to fold.</param>
    /// <returns>The folded text.</returns>
    private static string Fold(string block) => string.Join(
        ' ',
        block
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Split('\n')
            .Select(line => line.Trim())
            .Where(line => line.Length > 0));

    private static string ReadEmbedded(string name)
    {
        var assembly = Assembly.GetExecutingAssembly();
        using var stream = assembly.GetManifestResourceStream(name)
            ?? throw new InvalidOperationException(
                name + " is not embedded in the test assembly. The assembly carries: "
                + string.Join(", ", assembly.GetManifestResourceNames()));

        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
