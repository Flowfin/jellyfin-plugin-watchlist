using System;
using System.Reflection;
using Jellyfin.Plugin.Watchlist;
using Xunit;

namespace Jellyfin.Plugin.Watchlist.Tests;

/// <summary>
/// The version a server reads off the package and the version it reads off the
/// assembly are two separate readings of one number, and a plugin reporting one
/// version to the dashboard and another in its package is a support conversation
/// nobody can win.
/// </summary>
/// <remarks>
/// The number is written in build.yaml and Directory.Build.props derives the three
/// assembly properties from it, so these tests are what says the derivation actually
/// happened in the build that produced this assembly, rather than that two files
/// happen to carry matching text.
/// </remarks>
public class PluginVersionTests
{
    /// <summary>
    /// The value every unedited copy of the upstream template ships with, which is
    /// what this manifest carried before the version was chosen.
    /// </summary>
    private const string TemplateVersion = "1.0.0.0";

    private static Assembly PluginAssembly => typeof(Plugin).Assembly;

    /// <summary>
    /// The assembly version is the one the manifest declares. This is the assertion
    /// the rest of the file exists to give weight to.
    /// </summary>
    [Fact]
    public void TheAssemblyVersionIsTheOneTheManifestDeclares()
    {
        Assert.Equal(BuildManifest.ReadVersion(BuildManifest.Text), PluginAssembly.GetName().Version);
    }

    /// <summary>
    /// And so is the file version, which is the one a person reads off the file's
    /// properties rather than out of the assembly.
    /// </summary>
    [Fact]
    public void TheFileVersionIsTheOneTheManifestDeclares()
    {
        var declared = PluginAssembly
            .GetCustomAttribute<AssemblyFileVersionAttribute>()!
            .Version;

        Assert.Equal(BuildManifest.ReadVersion(BuildManifest.Text), Version.Parse(declared));
    }

    /// <summary>
    /// And so is the package version, which is what a build stamps as the informational
    /// version and what the packaging step names the artifact after.
    /// </summary>
    [Fact]
    public void ThePackageVersionIsTheOneTheManifestDeclares()
    {
        var informational = PluginAssembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()!
            .InformationalVersion;

        // A build appends the source revision after a plus sign when it has one, and
        // that suffix is not part of the version the package declares.
        var withoutRevision = informational.Split('+', 2)[0];

        Assert.Equal(BuildManifest.ReadVersion(BuildManifest.Text), Version.Parse(withoutRevision));
    }

    /// <summary>
    /// The near miss. The manifest moves and the assembly does not, which is what
    /// happens when somebody edits build.yaml and reads a stale build. The comparison
    /// has to refuse it.
    /// </summary>
    [Fact]
    public void TheComparisonRefusesAManifestWhoseVersionMovedAlone()
    {
        var moved = BuildManifest.WithVersion(BuildManifest.Text, "9.9.9.9");

        Assert.Equal(new Version(9, 9, 9, 9), BuildManifest.ReadVersion(moved));
        Assert.NotEqual(BuildManifest.ReadVersion(moved), PluginAssembly.GetName().Version);
    }

    /// <summary>
    /// A version a server cannot parse is refused here, because it is not refused
    /// there. The server replaces an unparsable manifest version with its own minimum,
    /// so the plugin would install under a number nobody wrote:
    ///
    ///     git show v10.11.11:Emby.Server.Implementations/Plugins/PluginManager.cs | sed -n '697,700p'
    ///     if (!Version.TryParse(manifest.Version, out version))
    ///     {
    ///         manifest.Version = _minimumVersion.ToString();
    ///     }
    /// </summary>
    /// <param name="unparsable">A version string a server would not parse.</param>
    [Theory]
    [InlineData("1.0.0.0-rc1")]
    [InlineData("v1.0.0.0")]
    [InlineData("1.0.0.0.0")]
    [InlineData("")]
    public void AVersionAServerCannotParseIsRefused(string unparsable)
    {
        var manifest = BuildManifest.WithVersion(BuildManifest.Text, unparsable);

        Assert.Throws<InvalidOperationException>(() => BuildManifest.ReadVersion(manifest));
    }

    /// <summary>
    /// The version is not the one the upstream template ships unchanged in every copy
    /// of itself. Agreeing on that value would satisfy every assertion above and still
    /// be a number nobody chose.
    /// </summary>
    [Fact]
    public void TheVersionIsNotTheOneEveryTemplateCopyShipsWith()
    {
        Assert.NotEqual(Version.Parse(TemplateVersion), BuildManifest.ReadVersion(BuildManifest.Text));
    }

    /// <summary>
    /// A manifest declaring no version at all is refused rather than read as zero, so
    /// a deleted line cannot pass as a version.
    /// </summary>
    [Fact]
    public void AManifestWithNoVersionEntryIsRefused()
    {
        var withoutVersion = BuildManifest.WithVersion(BuildManifest.Text, "removed").Replace(
            "version: \"removed\"",
            "# no version here",
            StringComparison.Ordinal);

        Assert.Throws<InvalidOperationException>(() => BuildManifest.ReadVersion(withoutVersion));
    }
}
