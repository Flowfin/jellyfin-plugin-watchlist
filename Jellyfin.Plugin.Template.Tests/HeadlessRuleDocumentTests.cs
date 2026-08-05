using System;
using System.IO;
using System.Linq;
using Xunit;

namespace Jellyfin.Plugin.Template.Tests;

/// <summary>
/// The headless rule lives in HEADLESS.md next to these sources, and the issues on
/// this board point at that file instead of repeating it. A pointer to a file that
/// is gone is worse than no pointer, so the document is embedded in this assembly
/// and read back here.
/// </summary>
public class HeadlessRuleDocumentTests
{
    private const string ResourceName = "Jellyfin.Plugin.Template.Tests.HEADLESS.md";

    /// <summary>
    /// Deleting or renaming the document, or dropping its EmbeddedResource entry,
    /// leaves every pointer at it dangling. This test is what notices.
    /// </summary>
    [Fact]
    public void TheHeadlessRuleDocumentShipsWithTheSuite()
    {
        var names = typeof(HeadlessRuleDocumentTests).Assembly.GetManifestResourceNames();

        Assert.True(
            names.Contains(ResourceName, StringComparer.Ordinal),
            $"No embedded resource is named {ResourceName}. The assembly carries: {string.Join(", ", names)}");
    }

    /// <summary>
    /// The document is only useful if it still names what it refuses. An empty or
    /// gutted file would pass the test above and tell a reader nothing.
    /// </summary>
    [Fact]
    public void TheHeadlessRuleDocumentStillNamesWhatItRefuses()
    {
        using var stream = typeof(HeadlessRuleDocumentTests).Assembly.GetManifestResourceStream(ResourceName);
        Assert.NotNull(stream);

        using var reader = new StreamReader(stream);
        var text = reader.ReadToEnd();

        foreach (var subject in new[] { "display", "elevation", "trust store", "clock", "network", "server" })
        {
            Assert.Contains(subject, text, StringComparison.OrdinalIgnoreCase);
        }
    }
}
