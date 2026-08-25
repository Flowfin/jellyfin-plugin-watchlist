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
        var undocumented = Undocumented(Document());

        Assert.True(
            undocumented.Count == 0,
            "These settings ship with no section in docs/settings.md: "
                + string.Join(", ", undocumented));
    }

    /// <summary>
    /// The near miss, and the reason this test is worth having: a setting the class
    /// carries and the document does not describe. The mutation runs on the document
    /// rather than on the class, because a test cannot add a property to the type it
    /// was compiled against, and it produces the same state from the same side: a name
    /// in the class with no heading anywhere in the file.
    /// </summary>
    /// <remarks>
    /// This was an assertion that the document did not contain the string
    /// <c>### ReconciliationIntervalHours</c>, on the reasoning that the name was one
    /// nobody had written. #32 wrote it. A near miss built from a name somebody is
    /// going to write stops being a near miss the day they write it, and the repair is
    /// a mutation of the committed document rather than a second unwritten name that
    /// would go the same way.
    /// </remarks>
    [Fact]
    public void ASettingWithNoSectionIsRefused()
    {
        var name = SettingNames()[0];
        var dropped = Document().Replace("### " + name, "### Something else", StringComparison.Ordinal);

        Assert.Equal(new[] { name }, Undocumented(dropped));
    }

    /// <summary>
    /// The one-change neighbour of the mutation above is the document as it is
    /// committed, and it trips nothing. Without this the test above would prove the
    /// mutation is unusual rather than that the rule reads it.
    /// </summary>
    [Fact]
    public void TheCommittedDocumentDescribesEverySetting()
    {
        Assert.Empty(Undocumented(Document()));
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

    /// <summary>
    /// The settings the given document gives no heading of its own.
    /// </summary>
    /// <param name="document">The settings document to read.</param>
    /// <returns>The names, ordered as the settings are.</returns>
    private static IReadOnlyList<string> Undocumented(string document) => SettingNames()
        .Where(name => !document.Contains("### " + name, StringComparison.Ordinal))
        .ToList();

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
