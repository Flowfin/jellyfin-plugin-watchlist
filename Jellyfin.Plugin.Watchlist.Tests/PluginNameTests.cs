using System.Runtime.CompilerServices;
using Xunit;

namespace Jellyfin.Plugin.Watchlist.Tests;

/// <summary>
/// The name is written twice, in build.yaml and in the plugin class, and the two are
/// not independent on a running server.
/// </summary>
/// <remarks>
/// A server that reconciles a loaded plugin against the manifest beside it assigns the
/// class's name over the manifest's and writes the file back:
/// <code>
/// git show v10.11.11:Emby.Server.Implementations/Plugins/PluginManager.cs | sed -n '589p'
///                         manifest.Name = plugin.Instance.Name;
/// </code>
/// and the name in that file is the key the same manager groups a plugin's installed
/// versions by when it cleans old folders up, at line 763 of the same file. So two
/// spellings are not two labels: one of them silently replaces the other on disk, and
/// which one wins is not decided in this repository. The line is identical on the other
/// supported line, so this holds on both.
/// </remarks>
public class PluginNameTests
{
    /// <summary>
    /// The pair as this tree carries it. The near misses below are what give it weight.
    /// </summary>
    [Fact]
    public void TheManifestAndThePluginClassCallThePluginTheSameThing()
    {
        Assert.True(
            BuildManifest.NamesAgree(BuildManifest.Text, DeclaredName()),
            $"build.yaml calls it {BuildManifest.ReadName(BuildManifest.Text)} and the plugin class calls it {DeclaredName()}.");
    }

    /// <summary>
    /// The manifest moves and the class does not. Renaming the published thing and
    /// leaving the class alone is the shape a rename actually has, because the manifest
    /// is where somebody goes to change what a catalogue shows.
    /// </summary>
    [Fact]
    public void TheComparisonRefusesAManifestNameThatMovedAlone()
    {
        var manifest = BuildManifest.WithName(BuildManifest.Text, "Watchlists");

        Assert.Equal("Watchlists", BuildManifest.ReadName(manifest));
        Assert.False(BuildManifest.NamesAgree(manifest, DeclaredName()));
    }

    /// <summary>
    /// The class moves and the manifest does not. The same near miss from the other
    /// side, because a comparison that only watches one of the two is not a comparison.
    /// </summary>
    [Fact]
    public void TheComparisonRefusesAPluginNameThatMovedAlone()
    {
        Assert.False(BuildManifest.NamesAgree(BuildManifest.Text, DeclaredName() + "s"));
    }

    /// <summary>
    /// A difference in case only, which is the rename nobody notices in a diff. The two
    /// values are not interchangeable even so: the pass that groups a plugin's installed
    /// versions compares their names with <c>OrdinalIgnoreCase</c>, so it reads two
    /// spellings as one plugin, while the reconciliation above writes the class's
    /// spelling into the file byte for byte.
    /// </summary>
    [Fact]
    public void TheComparisonRefusesADifferenceOfCaseAlone()
    {
        var manifest = BuildManifest.WithName(BuildManifest.Text, DeclaredName().ToUpperInvariant());

        Assert.False(BuildManifest.NamesAgree(manifest, DeclaredName()));
    }

    /// <summary>
    /// Reads Name off the plugin type the way a server would, rather than off a constant
    /// standing beside it. BasePlugin's constructor wants an application paths instance
    /// and a serializer and writes to disk on the way through; the override under test is
    /// a computed expression over no instance state, so an uninitialised instance answers
    /// it with the value a loaded plugin reports.
    /// </summary>
    /// <returns>The name the plugin class declares.</returns>
    private static string DeclaredName()
    {
        var pluginType = Assert.Single(PluginUnderTest.DiscoverableTypes);
        var property = pluginType.GetProperty("Name");

        Assert.NotNull(property);

        return (string)property.GetValue(RuntimeHelpers.GetUninitializedObject(pluginType))!;
    }
}
