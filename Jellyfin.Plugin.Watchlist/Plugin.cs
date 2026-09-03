using System;
using System.Collections.Generic;
using System.Globalization;
using Jellyfin.Plugin.Watchlist.Configuration;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Watchlist;

/// <summary>
/// The main plugin.
/// </summary>
public class Plugin : BasePlugin<PluginConfiguration>, IHasWebPages
{
    /// <summary>
    /// Initializes a new instance of the <see cref="Plugin"/> class.
    /// </summary>
    /// <param name="applicationPaths">Instance of the <see cref="IApplicationPaths"/> interface.</param>
    /// <param name="xmlSerializer">Instance of the <see cref="IXmlSerializer"/> interface.</param>
    /// <param name="logger">Instance of the <see cref="ILogger{TCategoryName}"/> interface.</param>
    /// <remarks>
    /// The server resolves these three from its own container rather than calling a
    /// fixed signature: <c>PluginManager.CreatePluginInstance</c> activates the type
    /// with <c>ActivatorUtilities.CreateInstance(_appHost.ServiceProvider, type)</c>,
    /// on both supported lines, and the provider it passes is the generic host's, which
    /// registers <see cref="ILogger{TCategoryName}"/>. An in-tree plugin on the 12.0
    /// line already takes this exact parameter. The commands behind all three readings
    /// are in the body of the change that added the parameter.
    /// </remarks>
    public Plugin(IApplicationPaths applicationPaths, IXmlSerializer xmlSerializer, ILogger<Plugin> logger)
        : base(applicationPaths, xmlSerializer)
    {
        ArgumentNullException.ThrowIfNull(logger);

        Instance = this;

        // The configuration file can be edited by hand, and nothing on that path
        // passes the page. Reading it here and putting the repaired object back
        // through the protected setter is the only seam the server offers: the
        // property that loads it is not virtual and the method behind it is private,
        // so there is no way to validate ON load, only to repair immediately after it.
        // The difference shows if anything reads the configuration before this
        // constructor has run, and nothing in this plugin does.
        Configuration = ConfigurationValidation.Repaired(Configuration, out var repairs);
        ConfigurationRepairsOnLoad = repairs;

        // One line for the whole read rather than one per setting. The repair is a
        // property of the file that was found, not of each value in it, and an
        // administrator meeting five lines in a server log reads them as five events.
        if (repairs.Count > 0)
        {
            logger.LogWarning(
                "The stored configuration held {Count} value(s) this plugin will not act on, and each was replaced by its default. {Repairs}",
                repairs.Count,
                string.Join(" ", repairs));
        }
    }

    /// <inheritdoc />
    public override string Name => "Watchlist";

    /// <inheritdoc />
    /// <remarks>
    /// The paragraph build.yaml declares under <c>description</c>, held to it by the
    /// suite for the reason the name is. A server that reconciles a loaded plugin
    /// against the manifest beside it assigns the instance's description over the
    /// manifest's and writes the file back, and the base answers the empty string, so
    /// a plugin declaring none shows a blank description on its own page after the
    /// first load whatever the catalogue said before it. The words are the same in
    /// both places; only where a line wraps differs, and the comparison folds that.
    /// </remarks>
    public override string Description =>
        "Adds a watchlist to Jellyfin. Each user gets a private list, held on the "
        + "server, so it is the same list on every device that user signs in from. "
        + "The list is projected into a playlist owned by that user, which is a "
        + "surface every stock client already renders, so nothing has to be patched, "
        + "forked or installed alongside it.";

    /// <summary>
    /// Gets the identifier this plugin is known by. A server keys the configuration
    /// it stores for a plugin and the update entry it offers on this value, so it is
    /// minted once and never changed. build.yaml declares the same value and a test
    /// refuses the pair when they drift apart.
    /// </summary>
    public static Guid PluginId { get; } = Guid.Parse("6e1631d7-aa49-494d-a23b-d5785853fc0a");

    /// <inheritdoc />
    public override Guid Id => PluginId;

    /// <summary>
    /// Gets the current plugin instance.
    /// </summary>
    public static Plugin? Instance { get; private set; }

    /// <summary>
    /// Gets what the stored configuration had to have replaced when it was read, one
    /// sentence per setting, empty where the file was fine.
    /// </summary>
    /// <remarks>
    /// The line #34 asks for is written by the constructor. This property is what a
    /// caller inside the process can read afterwards, and it is the value the suite
    /// asserts the line was composed from, so the two cannot say different things.
    /// </remarks>
    public IReadOnlyList<string> ConfigurationRepairsOnLoad { get; } = [];

    /// <summary>
    /// Refuses a save this plugin will not act on, before the server writes it.
    /// </summary>
    /// <param name="configuration">What the page posted.</param>
    /// <remarks>
    /// The base assigns the field and then writes the file, so refusing before calling
    /// it leaves both the value in memory and the file on disk exactly as they were,
    /// which is what #34's second condition asks for.
    ///
    /// What an administrator sees is narrower than the message. The server's endpoint
    /// calls this inside no try and answers 204 on every path that reaches it, so a
    /// plugin that refuses a save refuses it by throwing, and the sentence naming the
    /// setting goes to the server log rather than to the page. The page is where a
    /// field is named at a person: its controls carry the same bounds, so a browser
    /// refuses the value first and this is the line behind it for anything that
    /// reaches the endpoint another way.
    /// </remarks>
    public override void UpdateConfiguration(BasePluginConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        if (configuration is PluginConfiguration settings)
        {
            var refusals = ConfigurationValidation.Refusals(settings);

            if (refusals.Count > 0)
            {
                throw new ArgumentException(
                    "This configuration was not saved. " + string.Join(" ", refusals),
                    nameof(configuration));
            }
        }

        base.UpdateConfiguration(configuration);
    }

    /// <inheritdoc />
    public IEnumerable<PluginPageInfo> GetPages()
    {
        return
        [
            new PluginPageInfo
            {
                Name = Name,
                EmbeddedResourcePath = string.Format(CultureInfo.InvariantCulture, "{0}.Configuration.configPage.html", GetType().Namespace)
            }
        ];
    }
}
