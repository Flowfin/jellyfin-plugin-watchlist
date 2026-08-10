using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using Jellyfin.Plugin.Watchlist.Configuration;
using Xunit;

namespace Jellyfin.Plugin.Watchlist.Tests;

/// <summary>
/// The default of every setting, read off a fresh configuration and compared with what
/// the settings document states for it.
/// </summary>
/// <remarks>
/// <para>
/// A default is the value that lands on every server where nobody opened the page, so
/// it is what most installations actually run on and the value least likely to be
/// noticed when it moves. <see cref="SettingsDocumentTests"/> already refuses a setting
/// with no section of its own; what it does not read is what that section says the
/// default is, so a default changed in the class leaves the document stating the old
/// one and both halves stay green.
/// </para>
/// <para>
/// This reads the two against each other. The document is where a reader meets the
/// number, so moving a default becomes an edit to a sentence somebody sees rather than
/// to a constant nobody opens, which is what the second condition on #49 asks for. It
/// also reaches the setting somebody adds tomorrow: the set comes off the class by
/// reflection rather than out of a list here.
/// </para>
/// </remarks>
public sealed class SettingDefaultsTests
{
    private const string DocumentResource = "settings.md";

    private const string StatedDefaultPrefix = "Default: ";

    private const string TheCap = "MaxEntriesPerUser";

    /// <summary>
    /// The settings a server can set, declared on this plugin's own configuration class
    /// rather than inherited from the server's base. The same set
    /// <see cref="SettingsDocumentTests"/> reads, for the same reason: the base carries
    /// what every plugin has, and the document is about what this one added.
    /// </summary>
    /// <returns>The settings, ordered by name.</returns>
    private static IReadOnlyList<PropertyInfo> Settings() => typeof(PluginConfiguration)
        .GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
        .Where(p => p.CanRead && p.CanWrite && p.GetIndexParameters().Length == 0)
        .OrderBy(p => p.Name, StringComparer.Ordinal)
        .ToList();

    /// <summary>
    /// A check that reads nothing passes everything. What the rules below run over is
    /// asserted here, so none of them can be green by being empty.
    /// </summary>
    [Fact]
    public void TheConfigurationClassCarriesSettingsWithDefaultsToRead()
    {
        Assert.NotEmpty(Settings());
        Assert.NotEmpty(Document());
        Assert.Contains(TheCap, Settings().Select(s => s.Name), StringComparer.Ordinal);
    }

    /// <summary>
    /// Every setting's section states a default. A setting that ships without one is a
    /// value a reader can only find by installing the plugin and opening the page.
    /// </summary>
    [Fact]
    public void EverySettingStatesItsDefaultInTheDocument()
    {
        var silent = SettingsWithNoStatedDefault(Document());

        Assert.True(
            silent.Count == 0,
            "These settings have a section in docs/settings.md that states no default: "
                + string.Join(", ", silent));
    }

    /// <summary>
    /// The rule this file exists for. What the document states is what a fresh
    /// configuration holds, for every setting.
    /// </summary>
    [Fact]
    public void EveryStatedDefaultIsWhatAFreshConfigurationHolds()
    {
        var disagreements = Disagreements(Document());

        Assert.True(
            disagreements.Count == 0,
            "docs/settings.md and the configuration class disagree about a default: "
                + string.Join("; ", disagreements));
    }

    /// <summary>
    /// The near miss, and the mistake it is drawn from: a default moved in the class,
    /// with the assertion that names it in <see cref="WatchlistCapTests"/> moved along
    /// with it, and the document left stating the number it stated before.
    /// </summary>
    /// <remarks>
    /// The mutation runs the other way round, on the document rather than on the class,
    /// because a test cannot rewrite the constant it was compiled against. What it
    /// produces is the same disagreement from the same side: one value in the class,
    /// another in the document, and nothing else changed.
    /// </remarks>
    [Fact]
    public void ADocumentStatingAValueTheClassDoesNotHoldIsRefused()
    {
        var drifted = WithTheStatedDefaultOf(TheCap, Changed);

        Assert.NotEqual(Document(), drifted);

        var disagreements = Disagreements(drifted);

        Assert.Single(disagreements);
        Assert.Contains(TheCap, disagreements[0], StringComparison.Ordinal);
    }

    /// <summary>
    /// The second near miss: a section that describes its setting and never says what
    /// the value is when nobody touches it. That is the shape a new setting arrives in,
    /// because the description is the half somebody remembers to write.
    /// </summary>
    [Fact]
    public void ASectionThatStatesNoDefaultIsRefused()
    {
        var silent = SettingsWithNoStatedDefault(WithTheStatedDefaultOf(TheCap, _ => null));

        Assert.Equal(new[] { TheCap }, silent);
    }

    /// <summary>
    /// The one-change neighbour of both mutations above is the document as it is
    /// committed, and it trips neither rule. Without this the two tests above would
    /// prove the mutations are unusual rather than that the comparison reads them.
    /// </summary>
    [Fact]
    public void TheCommittedDocumentTripsNeitherRule()
    {
        Assert.Empty(SettingsWithNoStatedDefault(Document()));
        Assert.Empty(Disagreements(Document()));
    }

    /// <summary>
    /// A stated value that is not the one it was, of the same shape, so the mutation
    /// stays a disagreement rather than becoming a parse failure when the real default
    /// moves.
    /// </summary>
    /// <param name="stated">What the document states today.</param>
    /// <returns>Something else.</returns>
    private static string Changed(string stated) => stated + "1";

    /// <summary>
    /// Settings whose section carries no line stating a default.
    /// </summary>
    /// <param name="document">The settings document to read.</param>
    /// <returns>The names, ordered as the settings are.</returns>
    private static IReadOnlyList<string> SettingsWithNoStatedDefault(string document)
    {
        var sections = Sections(document);

        return Settings()
            .Where(setting => !sections.TryGetValue(setting.Name, out var section)
                || StatedDefault(section) is null)
            .Select(setting => setting.Name)
            .ToList();
    }

    /// <summary>
    /// Every setting whose stated default is not the value a fresh configuration holds,
    /// with both values in the sentence so a failure says which way round it is.
    /// </summary>
    /// <param name="document">The settings document to read.</param>
    /// <returns>One sentence per disagreement.</returns>
    /// <remarks>
    /// A setting whose section states nothing at all is left to the rule above rather
    /// than reported twice. Two failures naming one cause read as two causes.
    /// </remarks>
    private static IReadOnlyList<string> Disagreements(string document)
    {
        var sections = Sections(document);
        var fresh = new PluginConfiguration();
        var disagreements = new List<string>();

        foreach (var setting in Settings())
        {
            if (!sections.TryGetValue(setting.Name, out var section))
            {
                continue;
            }

            var stated = StatedDefault(section);
            if (stated is null)
            {
                continue;
            }

            var held = AsStated(setting, setting.GetValue(fresh));
            if (!string.Equals(stated, held, StringComparison.Ordinal))
            {
                disagreements.Add(
                    $"{setting.Name}: the class holds {held} and the document states {stated}");
            }
        }

        return disagreements;
    }

    /// <summary>
    /// The document split by its setting headings, keyed by the name each heading
    /// carries. A section runs to the next heading of any level, or to the end.
    /// </summary>
    /// <param name="document">The settings document to read.</param>
    /// <returns>The body of each setting's section, by setting name.</returns>
    private static IReadOnlyDictionary<string, IReadOnlyList<string>> Sections(string document)
    {
        var sections = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
        var current = string.Empty;
        var body = new List<string>();

        foreach (var line in Lines(document))
        {
            if (!line.StartsWith("#", StringComparison.Ordinal))
            {
                body.Add(line);
                continue;
            }

            if (current.Length > 0)
            {
                sections[current] = body;
            }

            current = line.StartsWith("### ", StringComparison.Ordinal) ? line[4..].Trim() : string.Empty;
            body = new List<string>();
        }

        if (current.Length > 0)
        {
            sections[current] = body;
        }

        return sections;
    }

    /// <summary>
    /// What a section states as the default: the text after the first line beginning
    /// with the prefix, up to the end of the sentence that line opens with.
    /// </summary>
    /// <param name="section">The lines of one setting's section.</param>
    /// <returns>The stated value, or null where the section states none.</returns>
    private static string? StatedDefault(IReadOnlyList<string> section)
    {
        var line = section.FirstOrDefault(l => l.StartsWith(StatedDefaultPrefix, StringComparison.Ordinal));
        if (line is null)
        {
            return null;
        }

        var stated = line[StatedDefaultPrefix.Length..];
        var sentence = stated.IndexOf(".", StringComparison.Ordinal);
        return (sentence < 0 ? stated : stated[..sentence]).Trim();
    }

    /// <summary>
    /// The value a fresh configuration holds, written the way the document states it.
    /// </summary>
    /// <param name="setting">The setting being read, named in the refusal below.</param>
    /// <param name="value">What that setting holds on a fresh configuration.</param>
    /// <returns>The value as the document would state it.</returns>
    /// <remarks>
    /// A value this does not know how to write is refused rather than skipped, for the
    /// reason the round trip in <see cref="ConfigurationSerialisationTests"/> refuses a
    /// type it cannot vary: a setting that looks covered by a comparison which never
    /// made it is worse than one nobody compared. A default whose written form carries a
    /// full stop, a decimal fraction among them, needs this and the sentence rule above
    /// changed together, and meeting that when the setting is added is the point.
    /// </remarks>
    private static string AsStated(PropertyInfo setting, object? value)
    {
        var type = Nullable.GetUnderlyingType(setting.PropertyType) ?? setting.PropertyType;

        if (value is not null)
        {
            if (type.IsEnum)
            {
                return Enum.GetName(type, value) ?? string.Empty;
            }

            if (type == typeof(bool))
            {
                return (bool)value ? "true" : "false";
            }

            if (type == typeof(Guid))
            {
                return ((Guid)value).ToString("D", CultureInfo.InvariantCulture);
            }

            if (type == typeof(int) || type == typeof(long) || type == typeof(short))
            {
                return Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;
            }

            if (type == typeof(string) && ((string)value).Length > 0)
            {
                return (string)value;
            }
        }

        throw new InvalidOperationException(
            $"{nameof(PluginConfiguration)}.{setting.Name} defaults to a {setting.PropertyType} value this suite has no way to write as docs/settings.md states it. "
            + "Teach AsStated that value, or say in the change why the default cannot be stated in the document.");
    }

    /// <summary>
    /// The committed document with one setting's stated default rewritten, and nothing
    /// else touched. The rewrite is confined to that setting's own section, so a second
    /// setting stating the same value is not carried along with it.
    /// </summary>
    /// <param name="setting">The setting whose section is rewritten.</param>
    /// <param name="rewrite">What to state instead, or null to take the line out.</param>
    /// <returns>The mutated document.</returns>
    private static string WithTheStatedDefaultOf(string setting, Func<string, string?> rewrite)
    {
        var lines = Lines(Document()).ToList();

        var heading = lines.FindIndex(l => string.Equals(l, "### " + setting, StringComparison.Ordinal));
        Assert.True(heading >= 0, "docs/settings.md has no section for " + setting + " to mutate.");

        var next = lines.FindIndex(heading + 1, l => l.StartsWith("#", StringComparison.Ordinal));
        var end = next < 0 ? lines.Count : next;

        var stated = lines.FindIndex(
            heading,
            end - heading,
            l => l.StartsWith(StatedDefaultPrefix, StringComparison.Ordinal));
        Assert.True(stated >= 0, "The section for " + setting + " states no default to mutate.");

        var line = lines[stated];
        var rest = line[StatedDefaultPrefix.Length..];
        var sentence = rest.IndexOf(".", StringComparison.Ordinal);
        var value = (sentence < 0 ? rest : rest[..sentence]).Trim();

        var replacement = rewrite(value);
        if (replacement is null)
        {
            lines.RemoveAt(stated);
        }
        else
        {
            lines[stated] = StatedDefaultPrefix + replacement + (sentence < 0 ? string.Empty : rest[sentence..]);
        }

        return string.Join("\n", lines);
    }

    private static IReadOnlyList<string> Lines(string document) => document
        .Replace("\r\n", "\n", StringComparison.Ordinal)
        .Split('\n');

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
