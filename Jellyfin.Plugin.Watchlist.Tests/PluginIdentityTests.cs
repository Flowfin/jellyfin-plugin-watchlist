using System;
using System.Runtime.CompilerServices;
using Xunit;

namespace Jellyfin.Plugin.Watchlist.Tests;

/// <summary>
/// The plugin identifier lives in two files a server reads separately: the class, which
/// decides where the configuration stored for the plugin goes, and build.yaml, which
/// decides which catalogue entry an update comes from. Nothing at build time compares
/// them, so they can drift apart in one commit and the damage only shows on an
/// installed server.
/// </summary>
public class PluginIdentityTests
{
    /// <summary>
    /// The value every unedited copy of the upstream template ships with. Shipping it
    /// means colliding with every other copy that never changed it.
    /// </summary>
    private const string TemplateIdentifier = "eb5d7894-8eef-4b36-aa6f-5d124e828ce1";

    /// <summary>
    /// The pair as this tree carries it. This is the assertion the other three exist to
    /// give weight to.
    /// </summary>
    [Fact]
    public void TheManifestAndThePluginClassDeclareTheSameIdentifier()
    {
        Assert.True(
            BuildManifest.Agrees(BuildManifest.Text, DeclaredIdentifier()),
            $"build.yaml declares {BuildManifest.ReadGuid(BuildManifest.Text)} and the plugin class declares {DeclaredIdentifier()}.");
    }

    /// <summary>
    /// Agreeing on the template's own value would satisfy the test above and still be
    /// the defect the issue names, so it is refused by name on both sides.
    /// </summary>
    [Fact]
    public void TheIdentifierIsNotTheOneEveryTemplateCopyShipsWith()
    {
        var template = Guid.Parse(TemplateIdentifier);

        Assert.NotEqual(template, DeclaredIdentifier());
        Assert.NotEqual(template, BuildManifest.ReadGuid(BuildManifest.Text));
    }

    /// <summary>
    /// The manifest moves and the class does not. One hex digit apart, which is the
    /// mistake a hand edit actually makes, and the comparison has to refuse it.
    /// </summary>
    [Fact]
    public void TheComparisonRefusesAManifestWhoseIdentifierMovedAlone()
    {
        var moved = OneDigitApart(DeclaredIdentifier());
        var manifest = BuildManifest.WithGuid(BuildManifest.Text, moved);

        Assert.Equal(moved, BuildManifest.ReadGuid(manifest));
        Assert.False(BuildManifest.Agrees(manifest, DeclaredIdentifier()));
    }

    /// <summary>
    /// The class moves and the manifest does not. The same near miss from the other
    /// side, because a comparison that only watches one of the two is not a comparison.
    /// </summary>
    [Fact]
    public void TheComparisonRefusesAPluginIdentifierThatMovedAlone()
    {
        var moved = OneDigitApart(DeclaredIdentifier());

        Assert.False(BuildManifest.Agrees(BuildManifest.Text, moved));
    }

    /// <summary>
    /// Reads Id off the plugin type the way a server would, rather than off a constant
    /// standing beside it. BasePlugin's constructor wants an application paths instance
    /// and a serializer and writes to disk on the way through; the override under test is
    /// a computed expression over no instance state, so an uninitialised instance answers
    /// it with the value a loaded plugin reports.
    /// </summary>
    /// <returns>The identifier the plugin class declares.</returns>
    private static Guid DeclaredIdentifier()
    {
        var pluginType = Assert.Single(PluginUnderTest.DiscoverableTypes);
        var property = pluginType.GetProperty("Id");

        Assert.NotNull(property);

        return (Guid)property.GetValue(RuntimeHelpers.GetUninitializedObject(pluginType))!;
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
