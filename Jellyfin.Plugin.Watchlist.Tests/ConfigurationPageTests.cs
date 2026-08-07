using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Jellyfin.Plugin.Watchlist.Configuration;
using MediaBrowser.Model.Plugins;
using Xunit;

namespace Jellyfin.Plugin.Watchlist.Tests;

/// <summary>
/// The configuration page against the things it has to agree with. It is a static
/// file the server hands to a browser, so no compiler reads a word of it: the plugin
/// identifier in it, the settings it names and the hosts it reaches are three copies
/// of facts held elsewhere, and every one of them can drift without breaking a build.
/// </summary>
/// <remarks>
/// The template shipped the first of those already broken. Its page carried the
/// template's own identifier, so the page loaded no configuration and saved none, and
/// what an administrator saw was a page whose fields were empty and whose save button
/// appeared to work. That failure has no symptom a build or a suite would find, which
/// is why it is read here.
/// </remarks>
public class ConfigurationPageTests
{
    /// <summary>
    /// The value every unedited copy of the upstream template ships with, refused by
    /// name on the page the same way it is refused on the plugin class.
    /// </summary>
    private const string TemplateIdentifier = "eb5d7894-8eef-4b36-aa6f-5d124e828ce1";

    private static readonly Regex DeclaredIdentifierPattern = new(
        @"pluginUniqueId\s*:\s*'([^']*)'",
        RegexOptions.CultureInvariant);

    private static readonly Regex TouchedSettingPattern = new(
        @"\bconfig\s*\.\s*([A-Za-z_][A-Za-z0-9_]*)",
        RegexOptions.CultureInvariant);

    /// <summary>
    /// Everything on the page that names something to fetch: the attributes a browser
    /// resolves into a request, and the one style form that does the same from inside
    /// a rule.
    /// </summary>
    private static readonly Regex ReferencePattern = new(
        @"(?:src|href|action|poster|srcset|data-src)\s*=\s*[""']([^""']*)[""']|url\(\s*[""']?([^""')]*)",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    /// <summary>
    /// The page is served out of the plugin assembly under the name the plugin
    /// declares to the server, so a moved file, a renamed namespace or a dropped
    /// EmbeddedResource entry is a page the dashboard cannot open. Reading it through
    /// the declared name rather than off disk is what makes the rest of this class a
    /// reading of what ships.
    /// </summary>
    [Fact]
    public void TheDeclaredPageIsEmbeddedUnderTheNameThePluginDeclares()
    {
        var declared = DeclaredPage();

        Assert.Equal("Watchlist", declared.Name);
        Assert.NotNull(EmbeddedPageOrNull(declared.EmbeddedResourcePath));
    }

    /// <summary>
    /// The pair this class exists for. The identifier written into the page is the one
    /// the plugin class reports, which is the one the server keys stored configuration
    /// on.
    /// </summary>
    [Fact]
    public void ThePageAndThePluginClassDeclareTheSameIdentifier()
    {
        Assert.Equal(PluginIdentifier(), IdentifierOn(Page));
    }

    /// <summary>
    /// Agreeing on the template's value would satisfy the test above on a page nobody
    /// edited, so it is refused by name.
    /// </summary>
    [Fact]
    public void ThePageDoesNotCarryTheIdentifierEveryTemplateCopyShipsWith()
    {
        Assert.NotEqual(Guid.Parse(TemplateIdentifier), IdentifierOn(Page));
    }

    /// <summary>
    /// The near miss. One hex digit is what a hand edit gets wrong, and a page one
    /// digit out behaves exactly like a page that is right until somebody saves.
    /// </summary>
    [Fact]
    public void TheComparisonRefusesAPageWhoseIdentifierMovedByOneDigit()
    {
        var moved = OneDigitApart(PluginIdentifier());
        var page = Page.Replace(
            PluginIdentifier().ToString(),
            moved.ToString(),
            StringComparison.Ordinal);

        Assert.Equal(moved, IdentifierOn(page));
        Assert.NotEqual(PluginIdentifier(), IdentifierOn(page));
    }

    /// <summary>
    /// The rule the issue asks for: the page shows this plugin's settings and nothing
    /// else. Read in both directions at once, because a setting the page never touches
    /// is a setting no administrator can change, and a setting the page touches that
    /// the class does not have is a control that silently does nothing.
    /// </summary>
    [Fact]
    public void ThePageTouchesExactlyTheSettingsTheConfigurationDeclares()
    {
        Assert.Equal(SettingNames(), TouchedSettings(Page));
    }

    /// <summary>
    /// A check that reads nothing passes everything. If the configuration class ever
    /// goes empty the test above is green over two empty sets.
    /// </summary>
    [Fact]
    public void TheConfigurationClassCarriesSettingsForThePageToShow()
    {
        Assert.NotEmpty(SettingNames());
    }

    /// <summary>
    /// The near miss on the settings, from the side that has no symptom: a control
    /// bound to a name the class does not carry. The page still renders, the field
    /// still accepts a number, and the save writes a property the server drops.
    /// </summary>
    [Fact]
    public void TheComparisonRefusesAPageBoundToASettingTheConfigurationDoesNotHave()
    {
        var name = Assert.Single(SettingNames());
        var page = Page.Replace("config." + name, "config." + name + "s", StringComparison.Ordinal);

        Assert.NotEqual(SettingNames(), TouchedSettings(page));
    }

    /// <summary>
    /// The same near miss from the other side: a setting that exists and that the page
    /// never offers. This is the shape a settings issue produces when it lands the
    /// property and forgets the control.
    /// </summary>
    [Fact]
    public void TheComparisonRefusesASettingThePageNeverTouches()
    {
        var name = Assert.Single(SettingNames());
        var page = Page.Replace("config." + name, "config", StringComparison.Ordinal);

        Assert.Empty(TouchedSettings(page));
        Assert.NotEqual(SettingNames(), TouchedSettings(page));
    }

    /// <summary>
    /// Nothing on the page is fetched from another host. A media server is often on a
    /// network with no route out, and a page that needs one renders as a broken frame
    /// with no explanation of what is missing.
    /// </summary>
    [Fact]
    public void ThePageFetchesNothingFromAnotherHost()
    {
        Assert.Empty(ForeignReferences(Page));
    }

    /// <summary>
    /// The near miss, and the one somebody actually writes: a web font pulled in to
    /// make the page look like the rest of the dashboard.
    /// </summary>
    [Fact]
    public void TheScanFindsAStylesheetFetchedFromAnotherHost()
    {
        var page = Page.Replace(
            "</head>",
            "    <link rel=\"stylesheet\" href=\"https://fonts.example.invalid/css?family=Inter\">\n</head>",
            StringComparison.Ordinal);

        Assert.Single(ForeignReferences(page));
    }

    /// <summary>
    /// The same thing hidden one level down, inside a style rule rather than in an
    /// attribute a reader would notice.
    /// </summary>
    [Fact]
    public void TheScanFindsAnImageFetchedByAStyleRule()
    {
        var page = Page.Replace(
            "</form>",
            "</form>\n<div style=\"background: url(https://images.example.invalid/hero.png)\"></div>",
            StringComparison.Ordinal);

        Assert.Single(ForeignReferences(page));
    }

    /// <summary>
    /// The other direction, so the scan is not a scan that refuses every reference. A
    /// page may point at things the server itself serves, and refusing those would
    /// make the guard something people work around.
    /// </summary>
    [Fact]
    public void TheScanPassesAReferenceThatStaysOnThisServer()
    {
        var page = Page.Replace(
            "</form>",
            "</form>\n<img src=\"assets/watchlist.png\" alt=\"\">\n<a href=\"#WatchlistConfigPage\">top</a>",
            StringComparison.Ordinal);

        Assert.Empty(ForeignReferences(page));
    }

    /// <summary>
    /// Gets the page as the server would serve it.
    /// </summary>
    private static string Page => EmbeddedPageOrNull(DeclaredPage().EmbeddedResourcePath)
        ?? throw new InvalidOperationException(
            DeclaredPage().EmbeddedResourcePath
            + " is not embedded in the plugin assembly. It carries: "
            + string.Join(", ", PluginUnderTest.Assembly.GetManifestResourceNames()));

    /// <summary>
    /// The page entry the plugin hands the server, read off the plugin type the way a
    /// server reads it. BasePlugin's constructor wants an application paths instance
    /// and a serializer and writes to disk on the way through; the override under test
    /// reads no instance state, so an uninitialised instance answers it with what a
    /// loaded plugin reports.
    /// </summary>
    /// <returns>The one page this plugin declares.</returns>
    private static PluginPageInfo DeclaredPage()
    {
        var pluginType = Assert.Single(PluginUnderTest.DiscoverableTypes);
        var method = pluginType.GetMethod("GetPages");

        Assert.NotNull(method);

        var pages = (IEnumerable<PluginPageInfo>)method
            .Invoke(RuntimeHelpers.GetUninitializedObject(pluginType), null)!;

        return Assert.Single(pages);
    }

    private static string? EmbeddedPageOrNull(string resourceName)
    {
        using var stream = PluginUnderTest.Assembly.GetManifestResourceStream(resourceName);

        if (stream is null)
        {
            return null;
        }

        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private static Guid PluginIdentifier()
    {
        var pluginType = Assert.Single(PluginUnderTest.DiscoverableTypes);
        var property = pluginType.GetProperty("Id");

        Assert.NotNull(property);

        return (Guid)property.GetValue(RuntimeHelpers.GetUninitializedObject(pluginType))!;
    }

    private static Guid IdentifierOn(string page)
    {
        var match = DeclaredIdentifierPattern.Match(page);

        Assert.True(match.Success, "The page declares no pluginUniqueId for the configuration API to be called with.");

        return Guid.Parse(match.Groups[1].Value);
    }

    /// <summary>
    /// The properties a server can set, declared on this plugin's own configuration
    /// class rather than inherited from the server's base. The same set the settings
    /// document is read against, so the class, the document and the page are three
    /// readings of one list.
    /// </summary>
    /// <returns>The setting names, ordered.</returns>
    private static IReadOnlyList<string> SettingNames() => typeof(PluginConfiguration)
        .GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
        .Where(p => p.CanRead && p.CanWrite)
        .Select(p => p.Name)
        .OrderBy(n => n, StringComparer.Ordinal)
        .ToList();

    private static IReadOnlyList<string> TouchedSettings(string page) => TouchedSettingPattern
        .Matches(page)
        .Select(m => m.Groups[1].Value)
        .Distinct(StringComparer.Ordinal)
        .OrderBy(n => n, StringComparer.Ordinal)
        .ToList();

    private static IReadOnlyList<string> ForeignReferences(string page) => ReferencePattern
        .Matches(page)
        .Select(m => (m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value).Trim())
        .Where(IsFetchedFromAnotherHost)
        .ToList();

    /// <summary>
    /// Whether a browser resolving this reference would leave the server it loaded the
    /// page from. A scheme it can dial is one, and so is the protocol-relative form,
    /// which is the spelling that looks like a path. A data reference carries its own
    /// bytes and reaches nothing.
    /// </summary>
    /// <param name="reference">The reference as it appears on the page.</param>
    /// <returns>True when resolving it needs another host.</returns>
    private static bool IsFetchedFromAnotherHost(string reference)
    {
        if (reference.StartsWith("//", StringComparison.Ordinal))
        {
            return true;
        }

        var colon = reference.IndexOf(':');

        if (colon <= 0)
        {
            return false;
        }

        var scheme = reference[..colon];

        if (!scheme.All(c => char.IsAsciiLetterOrDigit(c) || c == '+' || c == '-' || c == '.'))
        {
            return false;
        }

        return !string.Equals(scheme, "data", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The smallest change anybody makes by hand: one hex digit of the last group.
    /// </summary>
    /// <param name="identifier">The identifier to move.</param>
    /// <returns>An identifier one character away from it.</returns>
    private static Guid OneDigitApart(Guid identifier)
    {
        var text = identifier.ToString();
        var replacement = text[^1] == '0' ? "1" : "0";

        return Guid.Parse(string.Concat(text.AsSpan(0, text.Length - 1), replacement));
    }
}
