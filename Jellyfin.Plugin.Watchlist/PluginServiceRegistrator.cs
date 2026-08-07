using Jellyfin.Plugin.Watchlist.Api;
using Jellyfin.Plugin.Watchlist.Store;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Plugins;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Watchlist;

/// <summary>
/// What this plugin puts into the server's service collection, so that the controller
/// the server discovers can be constructed at all.
/// </summary>
/// <remarks>
/// Until this existed the plugin registered nothing, because nothing in it was ever
/// resolved: the store was reached only by the suite, which builds one itself. A
/// controller is different. The server finds it by scanning the plugin assembly and
/// then asks its own container for the constructor arguments, so a dependency that is
/// not registered here is an endpoint that fails when it is called rather than when
/// the plugin loads.
///
/// The store is registered through a factory rather than by type. Its folder is the
/// plugin's own data folder, which the server hands to the plugin when it constructs
/// it, so the path is not known when this method runs and is read when something first
/// asks for a store.
/// </remarks>
public class PluginServiceRegistrator : IPluginServiceRegistrator
{
    /// <inheritdoc />
    public void RegisterServices(IServiceCollection serviceCollection, IServerApplicationHost applicationHost)
    {
        serviceCollection.AddSingleton<IWatchlistItemDescriber, LibraryItemDescriber>();

        serviceCollection.AddSingleton(provider => new WatchlistDocumentStore(
            Plugin.Instance!.DataFolderPath,
            provider.GetRequiredService<ILogger<WatchlistDocumentStore>>()));
    }
}
