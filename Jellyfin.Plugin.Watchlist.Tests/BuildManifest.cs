using System;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;

namespace Jellyfin.Plugin.Watchlist.Tests;

/// <summary>
/// The packaging manifest, as the test assembly carries it, and the one value read
/// out of it. The comparison itself lives here as a named function so the test that
/// asserts the real pair and the tests that feed it a mutated pair exercise the same
/// code rather than two similar ones.
/// </summary>
internal static class BuildManifest
{
    /// <summary>
    /// A top-level quoted guid entry. Anchored per line, so a guid appearing inside a
    /// description or nested under another key is not mistaken for the manifest's own.
    /// </summary>
    private static readonly Regex GuidEntry = new(
        @"^guid:[ \t]*""(?<guid>[^""]*)""[ \t]*\r?$",
        RegexOptions.Multiline | RegexOptions.CultureInvariant);

    /// <summary>
    /// Gets the manifest text embedded at build time from the build.yaml beside the solution.
    /// </summary>
    public static string Text { get; } = ReadEmbeddedManifest();

    /// <summary>
    /// Reads the identifier a manifest declares.
    /// </summary>
    /// <param name="manifestText">The manifest to read.</param>
    /// <returns>The declared identifier.</returns>
    public static Guid ReadGuid(string manifestText)
    {
        var match = GuidEntry.Match(manifestText);
        if (!match.Success)
        {
            throw new InvalidOperationException("The manifest declares no top-level quoted guid entry.");
        }

        return Guid.Parse(match.Groups["guid"].Value);
    }

    /// <summary>
    /// Answers whether a manifest and a plugin identifier name the same plugin. This is
    /// the whole rule: a server keys stored configuration on the value in the class and
    /// keys the catalogue entry on the value in the manifest, so the two disagreeing is
    /// an install that updates nothing and a configuration that is never found again.
    /// </summary>
    /// <param name="manifestText">The manifest to read.</param>
    /// <param name="pluginId">The identifier the plugin class declares.</param>
    /// <returns>True when the two agree.</returns>
    public static bool Agrees(string manifestText, Guid pluginId) => ReadGuid(manifestText) == pluginId;

    /// <summary>
    /// Returns the manifest with a different identifier in it, leaving every other byte
    /// alone. Used to build the near miss: one manifest, one changed identifier.
    /// </summary>
    /// <param name="manifestText">The manifest to rewrite.</param>
    /// <param name="replacement">The identifier to put in it.</param>
    /// <returns>The rewritten manifest.</returns>
    public static string WithGuid(string manifestText, Guid replacement) =>
        GuidEntry.Replace(
            manifestText,
            m => m.Value.Replace(m.Groups["guid"].Value, replacement.ToString(), StringComparison.Ordinal),
            1);

    private static string ReadEmbeddedManifest()
    {
        var assembly = Assembly.GetExecutingAssembly();
        using var stream = assembly.GetManifestResourceStream("build.yaml")
            ?? throw new InvalidOperationException(
                "build.yaml is not embedded in the test assembly. The assembly carries: "
                + string.Join(", ", assembly.GetManifestResourceNames()));

        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
