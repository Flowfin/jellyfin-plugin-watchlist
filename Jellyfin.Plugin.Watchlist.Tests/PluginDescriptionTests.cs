using System;
using System.Runtime.CompilerServices;
using Xunit;

namespace Jellyfin.Plugin.Watchlist.Tests;

/// <summary>
/// The description is written twice, in build.yaml and in the plugin class, and the
/// two are not independent on a running server.
/// </summary>
/// <remarks>
/// The same reconciliation that assigns the class's name over the manifest's assigns
/// its description too, and writes the file back:
/// <code>
/// git show v10.11.11:Emby.Server.Implementations/Plugins/PluginManager.cs | sed -n '590p'
///                         manifest.Description = plugin.Instance.Description;
/// </code>
/// The base class answers the empty string, so a plugin that declares no description
/// shows the catalogue's paragraph until its first load and nothing after it. The
/// catalogue reads the same paragraph out of the packaged metadata, so holding the
/// class to build.yaml is what makes the description one value everywhere a server
/// shows it. The line is identical on the other supported line, so this holds on both.
/// </remarks>
public class PluginDescriptionTests
{
    /// <summary>
    /// The pair as this tree carries it. The near misses below are what give it weight.
    /// </summary>
    [Fact]
    public void TheManifestAndThePluginClassDescribeThePluginTheSameWay()
    {
        Assert.True(
            BuildManifest.DescriptionsAgree(BuildManifest.Text, DeclaredDescription()),
            $"build.yaml says: {BuildManifest.ReadDescription(BuildManifest.Text)} and the plugin class says: {DeclaredDescription()}");
    }

    /// <summary>
    /// The manifest moves and the class does not, which is the shape an edit to the
    /// catalogue text actually has, because build.yaml is where somebody goes to change
    /// what a catalogue shows.
    /// </summary>
    [Fact]
    public void TheComparisonRefusesAManifestDescriptionThatMovedAlone()
    {
        var manifest = BuildManifest.WithDescription(BuildManifest.Text, "Adds a watchlist to Jellyfin.");

        Assert.Equal("Adds a watchlist to Jellyfin.", BuildManifest.ReadDescription(manifest));
        Assert.False(BuildManifest.DescriptionsAgree(manifest, DeclaredDescription()));
    }

    /// <summary>
    /// The class moves and the manifest does not. The same near miss from the other
    /// side, because a comparison that only watches one of the two is not a comparison.
    /// </summary>
    [Fact]
    public void TheComparisonRefusesAPluginDescriptionThatMovedAlone()
    {
        Assert.False(BuildManifest.DescriptionsAgree(BuildManifest.Text, DeclaredDescription() + " It is free."));
    }

    /// <summary>
    /// A difference of case only, which is the edit nobody notices in a diff, and which
    /// the reconciliation writes into the file byte for byte.
    /// </summary>
    [Fact]
    public void TheComparisonRefusesADifferenceOfCaseAlone()
    {
        var manifest = BuildManifest.WithDescription(BuildManifest.Text, DeclaredDescription().ToUpperInvariant());

        Assert.False(BuildManifest.DescriptionsAgree(manifest, DeclaredDescription()));
    }

    /// <summary>
    /// The one-change neighbour that has to pass: the same words wrapped at another
    /// width. build.yaml folds its description, so where a line breaks is a fact about
    /// the file and not about the paragraph, and a rule that refused a re-wrap would
    /// refuse every edit to the surrounding comments that moved a line.
    /// </summary>
    [Fact]
    public void TheComparisonReadsTheWordsAndNotWhereTheyWrap()
    {
        var oneWordPerLine = string.Join("\n", DeclaredDescription().Split(' '));
        var rewrapped = BuildManifest.WithDescription(BuildManifest.Text, oneWordPerLine);

        Assert.NotEqual(BuildManifest.Text, rewrapped);
        Assert.True(BuildManifest.DescriptionsAgree(rewrapped, DeclaredDescription()));
    }

    /// <summary>
    /// A manifest that has lost the block is refused rather than read as empty, because
    /// empty is what the base class answers and two absences would agree.
    /// </summary>
    [Fact]
    public void AManifestWithNoDescriptionBlockIsRefused()
    {
        var missing = BuildManifest.WithoutDescription(BuildManifest.Text);

        Assert.NotEqual(BuildManifest.Text, missing);
        Assert.Throws<InvalidOperationException>(() => BuildManifest.ReadDescription(missing));
    }

    /// <summary>
    /// Reads Description off the plugin type the way a server would, rather than off a
    /// constant standing beside it, for the reason PluginNameTests gives: the override
    /// is a computed expression over no instance state, so an uninitialised instance
    /// answers it with the value a loaded plugin reports.
    /// </summary>
    /// <returns>The description the plugin class declares.</returns>
    private static string DeclaredDescription()
    {
        var pluginType = Assert.Single(PluginUnderTest.DiscoverableTypes);
        var property = pluginType.GetProperty("Description");

        Assert.NotNull(property);

        return (string)property.GetValue(RuntimeHelpers.GetUninitializedObject(pluginType))!;
    }
}
