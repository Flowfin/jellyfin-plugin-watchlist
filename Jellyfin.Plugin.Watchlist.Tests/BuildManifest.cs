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
    /// A top-level quoted version entry, anchored per line for the same reason as the
    /// guid entry above: a version written inside the description or under another key
    /// is not the manifest's own.
    /// </summary>
    private static readonly Regex VersionEntry = new(
        @"^version:[ \t]*""(?<version>[^""]*)""[ \t]*\r?$",
        RegexOptions.Multiline | RegexOptions.CultureInvariant);

    /// <summary>
    /// A top-level quoted name entry, anchored per line for the same reason as the two
    /// above: the word appears inside the description and under other keys, and neither
    /// of those is the name the package is published under.
    /// </summary>
    private static readonly Regex NameEntry = new(
        @"^name:[ \t]*""(?<name>[^""]*)""[ \t]*\r?$",
        RegexOptions.Multiline | RegexOptions.CultureInvariant);

    /// <summary>
    /// A top-level quoted targetAbi entry, anchored per line for the same reason as the
    /// three above. The word appears in the comments this manifest carries, and a
    /// comment is not what a server reads.
    /// </summary>
    private static readonly Regex TargetAbiEntry = new(
        @"^targetAbi:[ \t]*""(?<abi>[^""]*)""[ \t]*\r?$",
        RegexOptions.Multiline | RegexOptions.CultureInvariant);

    /// <summary>
    /// A top-level quoted framework entry, anchored per line for the same reason as the
    /// four above. The word appears in the prose this manifest carries around the key,
    /// and prose is not what the packaging tool reads.
    /// </summary>
    private static readonly Regex FrameworkEntry = new(
        @"^framework:[ \t]*""(?<framework>[^""]*)""[ \t]*\r?$",
        RegexOptions.Multiline | RegexOptions.CultureInvariant);

    /// <summary>
    /// Gets the manifest text embedded at build time from the build.yaml beside the solution.
    /// </summary>
    public static string Text { get; } = ReadEmbeddedManifest();

    /// <summary>
    /// Reads the version a manifest declares.
    /// </summary>
    /// <param name="manifestText">The manifest to read.</param>
    /// <returns>The declared version.</returns>
    /// <remarks>
    /// Parsed rather than compared as text, because that is what the server does with
    /// it, and because a version that will not parse is not refused there: it is
    /// replaced with the server's own minimum, so the plugin installs under a number
    /// nobody wrote.
    /// </remarks>
    public static Version ReadVersion(string manifestText)
    {
        var match = VersionEntry.Match(manifestText);
        if (!match.Success)
        {
            throw new InvalidOperationException("The manifest declares no top-level quoted version entry.");
        }

        var declared = match.Groups["version"].Value;

        if (!Version.TryParse(declared, out var version))
        {
            throw new InvalidOperationException(
                "The manifest declares the version " + declared + ", which is not a version a server can parse.");
        }

        return version;
    }

    /// <summary>
    /// Returns the manifest with a different version in it, leaving every other byte
    /// alone. Used to build the near miss: one manifest, one changed version.
    /// </summary>
    /// <param name="manifestText">The manifest to rewrite.</param>
    /// <param name="replacement">The version to put in it.</param>
    /// <returns>The rewritten manifest.</returns>
    public static string WithVersion(string manifestText, string replacement) =>
        VersionEntry.Replace(
            manifestText,
            m => m.Value.Replace(m.Groups["version"].Value, replacement, StringComparison.Ordinal),
            1);

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

    /// <summary>
    /// Reads the name a manifest declares.
    /// </summary>
    /// <param name="manifestText">The manifest to read.</param>
    /// <returns>The declared name.</returns>
    public static string ReadName(string manifestText)
    {
        var match = NameEntry.Match(manifestText);
        if (!match.Success)
        {
            throw new InvalidOperationException("The manifest declares no top-level quoted name entry.");
        }

        return match.Groups["name"].Value;
    }

    /// <summary>
    /// Answers whether a manifest and a plugin class call the plugin the same thing.
    /// </summary>
    /// <param name="manifestText">The manifest to read.</param>
    /// <param name="pluginName">The name the plugin class declares.</param>
    /// <returns>True when the two agree.</returns>
    public static bool NamesAgree(string manifestText, string pluginName) =>
        string.Equals(ReadName(manifestText), pluginName, StringComparison.Ordinal);

    /// <summary>
    /// Returns the manifest with a different name in it, leaving every other byte alone.
    /// Used to build the near miss: one manifest, one changed name.
    /// </summary>
    /// <param name="manifestText">The manifest to rewrite.</param>
    /// <param name="replacement">The name to put in it.</param>
    /// <returns>The rewritten manifest.</returns>
    public static string WithName(string manifestText, string replacement) =>
        NameEntry.Replace(
            manifestText,
            m => m.Value.Replace(m.Groups["name"].Value, replacement, StringComparison.Ordinal),
            1);

    /// <summary>
    /// Reads the ABI a manifest declares.
    /// </summary>
    /// <param name="manifestText">The manifest to read.</param>
    /// <returns>The declared ABI, as text.</returns>
    /// <remarks>
    /// Returned as text rather than parsed, because what this value is compared against
    /// is a package version, and both are read for the server line they name rather than
    /// for their fourth position. A missing entry throws instead of answering, because a
    /// key dropped from the manifest would otherwise leave every comparison against it
    /// quietly true.
    /// </remarks>
    public static string ReadTargetAbi(string manifestText)
    {
        var match = TargetAbiEntry.Match(manifestText);
        if (!match.Success)
        {
            throw new InvalidOperationException("The manifest declares no top-level quoted targetAbi entry.");
        }

        return match.Groups["abi"].Value;
    }

    /// <summary>
    /// Returns the manifest with a different ABI in it, leaving every other byte alone.
    /// Used to build the near miss: one manifest, one changed ABI.
    /// </summary>
    /// <param name="manifestText">The manifest to rewrite.</param>
    /// <param name="replacement">The ABI to put in it.</param>
    /// <returns>The rewritten manifest.</returns>
    public static string WithTargetAbi(string manifestText, string replacement) =>
        TargetAbiEntry.Replace(
            manifestText,
            m => m.Value.Replace(m.Groups["abi"].Value, replacement, StringComparison.Ordinal),
            1);

    /// <summary>
    /// Returns the manifest with no targetAbi entry at all. Used to prove the reading
    /// refuses a manifest that has lost the value rather than passing everything
    /// compared against it.
    /// </summary>
    /// <param name="manifestText">The manifest to rewrite.</param>
    /// <returns>The rewritten manifest.</returns>
    public static string WithoutTargetAbi(string manifestText) =>
        TargetAbiEntry.Replace(manifestText, string.Empty, 1);

    /// <summary>
    /// Reads the target framework a manifest declares.
    /// </summary>
    /// <param name="manifestText">The manifest to read.</param>
    /// <returns>The declared framework moniker, as text.</returns>
    /// <remarks>
    /// Returned as text and compared as text, because the packaging tool joins this value
    /// into the path it takes the built assembly from rather than parsing it. A missing
    /// entry throws instead of answering, because a key dropped from the manifest would
    /// otherwise leave every comparison against it quietly true.
    /// </remarks>
    public static string ReadFramework(string manifestText)
    {
        var match = FrameworkEntry.Match(manifestText);
        if (!match.Success)
        {
            throw new InvalidOperationException("The manifest declares no top-level quoted framework entry.");
        }

        return match.Groups["framework"].Value;
    }

    /// <summary>
    /// Returns the manifest with a different framework in it, leaving every other byte
    /// alone. Used to build the near miss: one manifest, one changed framework.
    /// </summary>
    /// <param name="manifestText">The manifest to rewrite.</param>
    /// <param name="replacement">The framework to put in it.</param>
    /// <returns>The rewritten manifest.</returns>
    public static string WithFramework(string manifestText, string replacement) =>
        FrameworkEntry.Replace(
            manifestText,
            m => m.Value.Replace(m.Groups["framework"].Value, replacement, StringComparison.Ordinal),
            1);

    /// <summary>
    /// Returns the manifest with no framework entry at all. Used to prove the reading
    /// refuses a manifest that has lost the value rather than passing everything compared
    /// against it.
    /// </summary>
    /// <param name="manifestText">The manifest to rewrite.</param>
    /// <returns>The rewritten manifest.</returns>
    public static string WithoutFramework(string manifestText) =>
        FrameworkEntry.Replace(manifestText, string.Empty, 1);

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
