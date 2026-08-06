using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Jellyfin.Plugin.Watchlist.Configuration;
using Xunit;

namespace Jellyfin.Plugin.Watchlist.Tests;

/// <summary>
/// The settings document against the configuration class. A setting that ships
/// without a description is not a documentation problem, it is a support
/// conversation, and the only way that stays true is if the document is read
/// against the class by something that fails.
/// </summary>
public class SettingsDocumentTests
{
    private const string DocumentResource = "settings.md";

    /// <summary>
    /// The properties a server can set. Declared on this plugin's own configuration
    /// class rather than inherited from the server's base, because the base carries
    /// what every plugin has and this document is about what this one added.
    /// </summary>
    private static IReadOnlyList<string> SettingNames() => typeof(PluginConfiguration)
        .GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
        .Where(p => p.CanRead && p.CanWrite)
        .Select(p => p.Name)
        .OrderBy(n => n, StringComparer.Ordinal)
        .ToList();

    /// <summary>
    /// A check that reads nothing passes everything, so the set it reads is checked
    /// rather than trusted. If this ever goes empty the test below stops meaning
    /// anything and would still be green.
    /// </summary>
    [Fact]
    public void TheConfigurationClassCarriesSettingsToDescribe()
    {
        Assert.NotEmpty(SettingNames());
    }

    /// <summary>
    /// The rule. Every setting has a heading of its own in the document.
    /// </summary>
    [Fact]
    public void EverySettingHasASectionInTheDocument()
    {
        var document = Document();

        var undocumented = SettingNames()
            .Where(name => !document.Contains("### " + name, StringComparison.Ordinal))
            .ToList();

        Assert.True(
            undocumented.Count == 0,
            "These settings ship with no section in docs/settings.md: "
                + string.Join(", ", undocumented));
    }

    /// <summary>
    /// The near miss, and the reason this test is worth having: a setting added to the
    /// class and not to the document. The name is one nobody has written, so a pass
    /// here would mean the check reads a list rather than the class.
    /// </summary>
    [Fact]
    public void ASettingWithNoSectionIsRefused()
    {
        var document = Document();

        Assert.DoesNotContain("### ReconciliationIntervalHours", document, StringComparison.Ordinal);
    }

    /// <summary>
    /// The document does not describe a setting that is not there. An entry left
    /// behind by a removal reads as a setting somebody can look for and not find,
    /// which is the same defect pointing the other way.
    /// </summary>
    [Fact]
    public void EverySectionInTheDocumentNamesASetting()
    {
        var names = SettingNames();

        var described = Document()
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Split('\n')
            .Where(line => line.StartsWith("### ", StringComparison.Ordinal))
            .Select(line => line[4..].Trim())
            .ToList();

        var orphaned = described
            .Where(heading => !names.Contains(heading, StringComparer.Ordinal))
            .ToList();

        Assert.True(
            orphaned.Count == 0,
            "docs/settings.md describes these, and the configuration class has no such setting: "
                + string.Join(", ", orphaned));
    }

    private static string Document()
    {
        var assembly = Assembly.GetExecutingAssembly();
        using var stream = assembly.GetManifestResourceStream(DocumentResource)
            ?? throw new InvalidOperationException(
                DocumentResource + " is not embedded in the test assembly. The assembly carries: "
                + string.Join(", ", assembly.GetManifestResourceNames()));

        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
