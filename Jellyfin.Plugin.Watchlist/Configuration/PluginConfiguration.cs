using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.Watchlist.Configuration;

/// <summary>
/// Plugin configuration.
/// </summary>
/// <remarks>
/// Deliberately empty. Every setting a plugin declares is written into the
/// configuration document the server stores for it, so the template's four
/// demonstration settings would have outlived the demonstration. This plugin's
/// own settings arrive with M5.
/// </remarks>
public class PluginConfiguration : BasePluginConfiguration
{
}
