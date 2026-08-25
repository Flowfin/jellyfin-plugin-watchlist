using System;
using System.Globalization;
using System.IO;
using System.Linq;
using Jellyfin.Plugin.Watchlist.Configuration;
using MediaBrowser.Model.Plugins;
using Xunit;

namespace Jellyfin.Plugin.Watchlist.Tests;

/// <summary>
/// The two paths #34 is about, exercised through the plugin type rather than through
/// the validator underneath it.
/// </summary>
/// <remarks>
/// <para>
/// Every other test of the rules calls <see cref="ConfigurationValidation"/> directly.
/// That proves what a legal value is and proves nothing about the wiring: a plugin
/// whose constructor never calls the repair, or whose save override never calls the
/// refusal, passes all of them. The wiring was the half #34 recorded as proven by
/// nothing.
/// </para>
/// <para>
/// It is reachable because the server's base class asks <c>IApplicationPaths</c> where
/// to write and asks <c>IXmlSerializer</c> how, and both are interfaces. Nothing here
/// starts a server, reads a display, needs elevation or writes outside its own
/// temporary directory, which is the headless rule in HEADLESS.md.
/// </para>
/// </remarks>
[Collection(PluginInstanceCollection.Name)]
public class PluginConfigurationWiringTests
{
    /// <summary>
    /// A legal configuration is written, so the refusal below is a refusal and not a
    /// save path that never worked.
    /// </summary>
    [Fact]
    public void ASaveThisPluginWillActOnIsStored()
    {
        using var paths = new ServerPathsInATemporaryDirectory();
        var plugin = Construct(paths, out _);

        plugin.UpdateConfiguration(new PluginConfiguration { ProjectedListName = "Films to watch" });

        Assert.Equal("Films to watch", plugin.Configuration.ProjectedListName);
        Assert.Equal("Films to watch", ReadFromDisk(paths).ProjectedListName);
    }

    /// <summary>
    /// The refusal, one character from a value that is taken. A trailing space is the
    /// mistake somebody actually makes, and trimming it silently is what this plugin
    /// refuses to do.
    /// </summary>
    [Fact]
    public void ASaveThisPluginWillNotActOnIsRefusedAndNamesTheSetting()
    {
        using var paths = new ServerPathsInATemporaryDirectory();
        var plugin = Construct(paths, out _);
        plugin.UpdateConfiguration(new PluginConfiguration { ProjectedListName = "Films to watch" });

        var refused = Assert.Throws<ArgumentException>(
            () => plugin.UpdateConfiguration(new PluginConfiguration { ProjectedListName = "Films to watch " }));

        Assert.Contains(nameof(PluginConfiguration.ProjectedListName), refused.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// The second condition of #34 in the words it uses: the stored configuration is
    /// left unchanged. Asserted on the bytes on disk rather than on the object, because
    /// the object is what a later read would repair anyway.
    /// </summary>
    [Fact]
    public void ARefusedSaveLeavesTheStoredConfigurationWhereItWas()
    {
        using var paths = new ServerPathsInATemporaryDirectory();
        var plugin = Construct(paths, out _);
        plugin.UpdateConfiguration(new PluginConfiguration { MaxEntriesPerUser = 12 });
        var before = File.ReadAllBytes(paths.PluginConfigurationFilePath);

        Assert.Throws<ArgumentException>(
            () => plugin.UpdateConfiguration(new PluginConfiguration { MaxEntriesPerUser = -1 }));

        Assert.Equal(before, File.ReadAllBytes(paths.PluginConfigurationFilePath));
        Assert.Equal(12, plugin.Configuration.MaxEntriesPerUser);
        Assert.Equal(12, ReadFromDisk(paths).MaxEntriesPerUser);
    }

    /// <summary>
    /// The bound itself, from the other side. One below the smallest cap is refused and
    /// the smallest cap is taken, so the test pins the edge rather than the direction.
    /// </summary>
    [Fact]
    public void TheSmallestLegalCapIsTakenAndTheValueBelowItIsRefused()
    {
        using var paths = new ServerPathsInATemporaryDirectory();
        var plugin = Construct(paths, out _);

        plugin.UpdateConfiguration(new PluginConfiguration { MaxEntriesPerUser = SettingLimits.SmallestEntryCap });
        Assert.Equal(SettingLimits.SmallestEntryCap, plugin.Configuration.MaxEntriesPerUser);

        Assert.Throws<ArgumentException>(() => plugin.UpdateConfiguration(
            new PluginConfiguration { MaxEntriesPerUser = SettingLimits.SmallestEntryCap - 1 }));
    }

    /// <summary>
    /// A configuration that is not this plugin's own is not judged by the rules here.
    /// The server hands the override a <see cref="BasePluginConfiguration"/>, and this
    /// plugin has no rule for a type that is not its own, so the value goes on to the
    /// base and fails there rather than being refused for a reason nobody wrote.
    /// </summary>
    [Fact]
    public void AConfigurationOfAnotherTypeIsNotJudgedHere()
    {
        using var paths = new ServerPathsInATemporaryDirectory();
        var plugin = Construct(paths, out _);

        Assert.Throws<InvalidCastException>(() => plugin.UpdateConfiguration(new BasePluginConfiguration()));
    }

    /// <summary>
    /// Null reaches the guard rather than the base.
    /// </summary>
    [Fact]
    public void ASaveOfNothingIsRefused()
    {
        using var paths = new ServerPathsInATemporaryDirectory();
        var plugin = Construct(paths, out _);

        Assert.Throws<ArgumentNullException>(() => plugin.UpdateConfiguration(null!));
    }

    /// <summary>
    /// The load path. A file edited by hand carries a value no page would have posted,
    /// and the plugin comes up on the default rather than throwing on every pass.
    /// </summary>
    [Fact]
    public void AHandEditedFileIsRepairedOnLoadRatherThanThrowing()
    {
        using var paths = new ServerPathsInATemporaryDirectory();
        WriteByHand(paths, "<MaxEntriesPerUser>-4</MaxEntriesPerUser>");

        var plugin = Construct(paths, out _);

        Assert.Equal(PluginConfiguration.DefaultMaxEntriesPerUser, plugin.Configuration.MaxEntriesPerUser);
        Assert.Contains(
            nameof(PluginConfiguration.MaxEntriesPerUser),
            Assert.Single(plugin.ConfigurationRepairsOnLoad),
            StringComparison.Ordinal);
    }

    /// <summary>
    /// The third condition of #34: one log line, not one per setting. Three settings
    /// are wrong in the file and the line is one.
    /// </summary>
    [Fact]
    public void ARepairedLoadWritesOneLineNamingWhatWasReplaced()
    {
        using var paths = new ServerPathsInATemporaryDirectory();
        WriteByHand(
            paths,
            "<MaxEntriesPerUser>-4</MaxEntriesPerUser>",
            "<ReconciliationIntervalHours>0</ReconciliationIntervalHours>",
            "<ProjectedListName> untrimmed </ProjectedListName>");

        var plugin = Construct(paths, out var log);

        var line = Assert.Single(log.Lines);
        Assert.StartsWith("Warning ", line, StringComparison.Ordinal);
        Assert.Equal(3, plugin.ConfigurationRepairsOnLoad.Count);
        Assert.All(
            plugin.ConfigurationRepairsOnLoad,
            repair => Assert.Contains(repair, line, StringComparison.Ordinal));
    }

    /// <summary>
    /// The other direction, which is what a line written unconditionally would pass. A
    /// file every rule accepts writes nothing at all, so the line means a repair
    /// happened rather than that a load happened.
    /// </summary>
    [Fact]
    public void AFileThisPluginAcceptsWritesNoLine()
    {
        using var paths = new ServerPathsInATemporaryDirectory();
        WriteByHand(paths, "<MaxEntriesPerUser>12</MaxEntriesPerUser>");

        var plugin = Construct(paths, out var log);

        Assert.Empty(log.Lines);
        Assert.Empty(plugin.ConfigurationRepairsOnLoad);
        Assert.Equal(12, plugin.Configuration.MaxEntriesPerUser);
    }

    /// <summary>
    /// No file at all is the first start on a fresh install. The base writes the
    /// defaults, and a default is a value every rule accepts, so nothing is repaired
    /// and nothing is logged.
    /// </summary>
    [Fact]
    public void AFirstStartWithNoFileRepairsNothing()
    {
        using var paths = new ServerPathsInATemporaryDirectory();

        var plugin = Construct(paths, out var log);

        Assert.Empty(log.Lines);
        Assert.Empty(plugin.ConfigurationRepairsOnLoad);
        Assert.Equal(PluginConfiguration.DefaultProjectedListName, plugin.Configuration.ProjectedListName);
    }

    /// <summary>
    /// The logger is required rather than optional. The server resolves it, and a
    /// constructor that quietly accepted nothing would turn a container that stopped
    /// providing one into a plugin that stops logging without saying so.
    /// </summary>
    [Fact]
    public void TheConstructorRefusesToBeBuiltWithoutALogger()
    {
        using var paths = new ServerPathsInATemporaryDirectory();

        Assert.Throws<ArgumentNullException>(
            () => new Plugin(paths, new PluginConfigurationFile(), null!));
    }

    /// <summary>
    /// What the rest of the type holds, reached from the same constructed instance so
    /// that no line of the file is left with nothing running it.
    /// </summary>
    [Fact]
    public void TheConstructedPluginCarriesItsNameIdentifierAndPage()
    {
        using var paths = new ServerPathsInATemporaryDirectory();
        var plugin = Construct(paths, out _);

        Assert.Equal("Watchlist", plugin.Name);
        Assert.Equal(Plugin.PluginId, plugin.Id);

        var page = Assert.Single(plugin.GetPages());
        Assert.Equal("Watchlist", page.Name);
        Assert.EndsWith(".Configuration.configPage.html", page.EmbeddedResourcePath, StringComparison.Ordinal);
    }

    private static Plugin Construct(ServerPathsInATemporaryDirectory paths, out RecordingPluginLogger log)
    {
        log = new RecordingPluginLogger();
        return new Plugin(paths, new PluginConfigurationFile(), log);
    }

    private static PluginConfiguration ReadFromDisk(ServerPathsInATemporaryDirectory paths) =>
        (PluginConfiguration)new PluginConfigurationFile()
            .DeserializeFromFile(typeof(PluginConfiguration), paths.PluginConfigurationFilePath);

    /// <summary>
    /// Writes a configuration file the way a person with a text editor would: the
    /// elements they meant to change and nothing else, leaving the serialiser to
    /// default the rest.
    /// </summary>
    private static void WriteByHand(ServerPathsInATemporaryDirectory paths, params string[] elements)
    {
        var body = string.Join(Environment.NewLine, elements.Select(element => "  " + element));

        File.WriteAllText(
            paths.PluginConfigurationFilePath,
            string.Format(
                CultureInfo.InvariantCulture,
                "<?xml version=\"1.0\" encoding=\"utf-8\"?>{0}<PluginConfiguration>{0}{1}{0}</PluginConfiguration>{0}",
                Environment.NewLine,
                body));
    }
}
