using System;
using System.Collections.Generic;
using System.Globalization;
using Jellyfin.Plugin.Watchlist.Configuration;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;

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
    public Plugin(IApplicationPaths applicationPaths, IXmlSerializer xmlSerializer)
        : base(applicationPaths, xmlSerializer)
    {
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
    }

    /// <inheritdoc />
    public override string Name => "Watchlist";

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
    /// #34 asks that a repaired value produce one log line rather than a throw on
    /// every pass. There is no line yet and this property is not one: writing it needs
    /// a logger, and the only way to get one here is a third constructor parameter the
    /// server would have to resolve when it creates this type, which nothing in this
    /// repository can exercise. The repair happens either way and what it did is
    /// readable rather than silent, which is the half that can be built without a
    /// server to try it against.
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
