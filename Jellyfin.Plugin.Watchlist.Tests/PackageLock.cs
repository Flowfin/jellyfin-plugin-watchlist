using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Jellyfin.Plugin.Watchlist.Tests;

/// <summary>
/// A project file and the lock file beside it, as the test assembly carries them, and
/// the comparison between the two.
/// </summary>
/// <remarks>
/// <para>
/// The restore refuses a graph that has moved away from its lock file, with NU1004,
/// and a restore with no lock file to bind is refused by a target in
/// Directory.Build.props. Those two are the enforcement, and neither of them reads
/// what is inside the file.
/// </para>
/// <para>
/// This is the reading that does. A lock file can exist, satisfy the restore of the
/// day it was written, and still not describe the project beside it: a hand edit, a
/// merge that took one side of a conflict, or a reference removed while the lock kept
/// its entry. Nothing here reaches the file system or the network. Both texts arrive
/// as embedded resources, which is what lets the same function judge the real pair and
/// a deliberately broken one.
/// </para>
/// </remarks>
internal static class PackageLock
{
    /// <summary>
    /// A package reference as this tree writes them. The identifier comes first and the
    /// version second on every one of them, and a reference written the other way round
    /// would not be matched, which is a bound rather than a rule: the disagreement it
    /// would produce is a missing entry rather than a silent pass.
    /// </summary>
    private static readonly Regex PackageReferenceEntry = new(
        @"<PackageReference\s+Include=""(?<id>[^""]+)""\s+Version=""(?<version>[^""]+)""",
        RegexOptions.CultureInvariant);

    /// <summary>
    /// The single target framework a project declares.
    /// </summary>
    private static readonly Regex TargetFrameworkEntry = new(
        @"<TargetFramework>(?<tfm>[^<]+)</TargetFramework>",
        RegexOptions.CultureInvariant);

    /// <summary>
    /// Gets the plugin project and its lock file.
    /// </summary>
    public static PackageLockPair Plugin { get; } = new(
        "Jellyfin.Plugin.Watchlist",
        ReadEmbedded("project/Jellyfin.Plugin.Watchlist.csproj"),
        ReadEmbedded("lock/Jellyfin.Plugin.Watchlist.json"));

    /// <summary>
    /// Gets the test project and its lock file.
    /// </summary>
    /// <remarks>
    /// The issue asks for a lock file for every project including this one, so this one
    /// is judged by the same comparison rather than trusted for being nearby.
    /// </remarks>
    public static PackageLockPair Tests { get; } = new(
        "Jellyfin.Plugin.Watchlist.Tests",
        ReadEmbedded("project/Jellyfin.Plugin.Watchlist.Tests.csproj"),
        ReadEmbedded("lock/Jellyfin.Plugin.Watchlist.Tests.json"));

    /// <summary>
    /// Gets both pairs, which is every project in the solution.
    /// </summary>
    public static IReadOnlyList<PackageLockPair> All { get; } = new[] { Plugin, Tests };

    /// <summary>
    /// Says every way in which a lock file fails to describe the project beside it. An
    /// empty result is agreement.
    /// </summary>
    /// <param name="projectText">The project file.</param>
    /// <param name="lockText">The lock file beside it.</param>
    /// <returns>One line per disagreement, empty when there is none.</returns>
    public static IReadOnlyList<string> Disagreements(string projectText, string lockText)
    {
        var disagreements = new List<string>();

        var framework = TargetFrameworkEntry.Match(projectText);
        if (!framework.Success)
        {
            return new[] { "The project declares no single target framework, so there is nothing to compare a lock file against." };
        }

        var tfm = framework.Groups["tfm"].Value;

        using var document = JsonDocument.Parse(lockText);
        var root = document.RootElement;

        if (!root.TryGetProperty("version", out var formatVersion) || formatVersion.GetInt32() != 1)
        {
            disagreements.Add("The lock file is not format version 1, which is the only format this comparison reads.");
            return disagreements;
        }

        if (!root.TryGetProperty("dependencies", out var dependencies)
            || !dependencies.TryGetProperty(tfm, out var forFramework))
        {
            disagreements.Add(
                "The lock file describes no dependencies for " + tfm + ", which is the framework the project targets.");
            return disagreements;
        }

        var locked = new Dictionary<string, LockedEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in forFramework.EnumerateObject())
        {
            var type = entry.Value.TryGetProperty("type", out var kind) ? kind.GetString() : null;
            var requested = entry.Value.TryGetProperty("requested", out var range) ? range.GetString() : null;
            var hash = entry.Value.TryGetProperty("contentHash", out var content) ? content.GetString() : null;

            locked[entry.Name] = new LockedEntry(type, requested, hash);

            // A project reference is recorded here too, as type Project, and it carries
            // no hash because there is no package to hash: it is built from this tree
            // rather than downloaded. Every other entry is a package, and a package
            // entry with no hash names a version and pins no bytes.
            if (string.IsNullOrEmpty(hash) && !string.Equals(type, "Project", StringComparison.Ordinal))
            {
                disagreements.Add(
                    "The lock file entry for " + entry.Name
                    + " carries no content hash, so it names a version rather than pinning bytes.");
            }
        }

        var referenced = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (Match reference in PackageReferenceEntry.Matches(projectText))
        {
            var id = reference.Groups["id"].Value;
            var version = reference.Groups["version"].Value;
            referenced.Add(id);

            if (!locked.TryGetValue(id, out var entry))
            {
                disagreements.Add(
                    "The project references " + id + " " + version + " and the lock file has no entry for it.");
                continue;
            }

            if (!string.Equals(entry.Type, "Direct", StringComparison.Ordinal))
            {
                disagreements.Add(
                    "The project references " + id + " directly and the lock file records it as "
                    + (entry.Type ?? "nothing") + ".");
            }

            var expected = string.Create(CultureInfo.InvariantCulture, $"[{version}, )");
            if (!string.Equals(entry.Requested, expected, StringComparison.Ordinal))
            {
                disagreements.Add(
                    "The project references " + id + " " + version + " and the lock file was written for "
                    + (entry.Requested ?? "no stated range") + ".");
            }
        }

        foreach (var direct in locked
            .Where(e => string.Equals(e.Value.Type, "Direct", StringComparison.Ordinal))
            .Select(e => e.Key)
            .Where(id => !referenced.Contains(id))
            .OrderBy(id => id, StringComparer.Ordinal))
        {
            disagreements.Add(
                "The lock file records " + direct + " as a direct reference and the project no longer has one.");
        }

        return disagreements;
    }

    /// <summary>
    /// Returns the project with a different version on one package reference, leaving
    /// every other byte alone. This is the near miss the comparison exists for: somebody
    /// moves a version and the lock file stays where it was.
    /// </summary>
    /// <param name="projectText">The project file to rewrite.</param>
    /// <param name="id">The package whose version moves.</param>
    /// <param name="replacement">The version to put on it.</param>
    /// <returns>The rewritten project file.</returns>
    public static string WithReferenceVersion(string projectText, string id, string replacement) =>
        PackageReferenceEntry.Replace(
            projectText,
            m => string.Equals(m.Groups["id"].Value, id, StringComparison.OrdinalIgnoreCase)
                ? m.Value.Replace(
                    @"Version=""" + m.Groups["version"].Value + @"""",
                    @"Version=""" + replacement + @"""",
                    StringComparison.Ordinal)
                : m.Value);

    /// <summary>
    /// Returns the lock file with one entry's content hash emptied, leaving every other
    /// byte alone. A lock file that names versions and pins no bytes reads exactly like
    /// one that does.
    /// </summary>
    /// <param name="lockText">The lock file to rewrite.</param>
    /// <param name="id">The entry whose hash is emptied.</param>
    /// <returns>The rewritten lock file.</returns>
    public static string WithoutContentHash(string lockText, string id)
    {
        using var document = JsonDocument.Parse(lockText);
        var entry = document.RootElement
            .GetProperty("dependencies")
            .EnumerateObject().First()
            .Value.GetProperty(id);

        var hash = entry.GetProperty("contentHash").GetString();

        return lockText.Replace(
            @"""contentHash"": """ + hash + @"""",
            @"""contentHash"": """"",
            StringComparison.Ordinal);
    }

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

    private readonly record struct LockedEntry(string? Type, string? Requested, string? ContentHash);
}

/// <summary>
/// One project and the lock file beside it.
/// </summary>
/// <param name="Name">The project's name, used in test output.</param>
/// <param name="ProjectText">The project file.</param>
/// <param name="LockText">The lock file.</param>
internal readonly record struct PackageLockPair(string Name, string ProjectText, string LockText);
