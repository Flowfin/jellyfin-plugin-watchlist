using System;
using System.Globalization;
using System.Linq;
using MediaBrowser.Model.Plugins;
using Xunit;

namespace Jellyfin.Plugin.Watchlist.Tests;

/// <summary>
/// What the server needs to be true of the assembly before any of the plugin's own
/// behaviour matters. Each test reads the assembly by reflection rather than by
/// name, so the rules survive a rename of the namespace or the type.
/// </summary>
public class PluginDiscoveryTests
{
    /// <summary>
    /// A server scans the assembly and instantiates what it finds. Two discoverable
    /// types means two plugins registered from one file; none means the assembly
    /// loads and nothing happens, which is the failure that looks like a working
    /// install.
    /// </summary>
    [Fact]
    public void AssemblyDeclaresExactlyOnePluginTypeAServerCanDiscover()
    {
        var discoverable = PluginUnderTest.DiscoverableTypes
            .Select(t => t.FullName ?? t.Name)
            .ToList();

        Assert.Single(discoverable);
    }

    /// <summary>
    /// The plugin ships a configuration surface, so the type has to advertise pages.
    /// Losing the interface in a refactor leaves the settings unreachable while
    /// everything else still works.
    /// </summary>
    [Fact]
    public void ThePluginTypeAdvertisesConfigurationPages()
    {
        var plugin = Assert.Single(PluginUnderTest.DiscoverableTypes);

        Assert.True(
            typeof(IHasWebPages).IsAssignableFrom(plugin),
            $"{plugin.FullName} does not implement {nameof(IHasWebPages)}, so the server offers no configuration page for it.");
    }

    /// <summary>
    /// The page is handed to the server as a manifest resource name built from the
    /// plugin type's namespace. Nothing at build time checks that the name resolves,
    /// so a moved file or a dropped EmbeddedResource entry is only visible when an
    /// administrator opens the settings on a running server and gets nothing.
    /// </summary>
    [Fact]
    public void TheConfigurationPageResourceResolvesUnderThePluginNamespace()
    {
        var plugin = Assert.Single(PluginUnderTest.DiscoverableTypes);
        var expected = string.Format(
            CultureInfo.InvariantCulture,
            "{0}.Configuration.configPage.html",
            plugin.Namespace);

        var embedded = PluginUnderTest.Assembly.GetManifestResourceNames();

        Assert.True(
            embedded.Contains(expected, StringComparer.Ordinal),
            $"No embedded resource is named {expected}. The assembly carries: {string.Join(", ", embedded)}");
    }
}
