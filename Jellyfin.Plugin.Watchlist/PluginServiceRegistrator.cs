using System;
using Jellyfin.Data.Events.Users;
using Jellyfin.Plugin.Watchlist.Api;
using Jellyfin.Plugin.Watchlist.Export;
using Jellyfin.Plugin.Watchlist.Projection;
using Jellyfin.Plugin.Watchlist.Store;
using Jellyfin.Plugin.Watchlist.Users;
using Jellyfin.Plugin.Watchlist.Watched;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Events;
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

        // What a series holds, for one user, which is what the single row a show
        // projects as is chosen out of. Registered under its interface for the same
        // reason the describer is: everything above it is then a function of what it
        // answers, and the suite drives the whole rule with no library present.
        serviceCollection.AddSingleton<ISeriesEpisodes, LibrarySeriesEpisodes>();

        // The two provider questions, both answered by the one class that knows a
        // library can be searched. It is registered by type under each interface
        // rather than once behind a factory: it holds nothing but the library the
        // server hands it, so two instances of it cannot disagree, and a factory
        // here would be a line only a server can execute.
        serviceCollection.AddSingleton<IProviderIdSource, LibraryProviderIds>();
        serviceCollection.AddSingleton<IProviderIdIndex, LibraryProviderIds>();

        // The system clock, as a dependency rather than a call inside the controller.
        // An entry carries the instant it was added, and the suite is not allowed to
        // read the machine's clock to check it, so the one place that reads a real
        // clock is this line.
        serviceCollection.AddSingleton(TimeProvider.System);

        // The plugin's configuration, resolved when something asks rather than once.
        // The server replaces the object when the page is saved, so a singleton here
        // would hand every later request the cap that was set when the server started.
        serviceCollection.AddScoped(_ => Plugin.Instance!.Configuration);

        serviceCollection.AddSingleton(provider => new WatchlistDocumentStore(
            Plugin.Instance!.DataFolderPath,
            provider.GetRequiredService<ILogger<WatchlistDocumentStore>>()));

        // The watched rule and what it listens to. The completion answer is the only
        // half of it that asks the server anything, and it is registered under its
        // interface so nothing above it names a library.
        serviceCollection.AddSingleton<ISeriesCompletion, LibrarySeriesCompletion>();

        // The configuration reaches the handler as a question rather than as a value.
        // The handler lives as long as the server does and the server replaces the
        // configuration object whenever the page is saved, so a value captured here
        // would be the one that was in force at start-up.
        serviceCollection.AddSingleton(provider => new WatchedRemovalHandler(
            provider.GetRequiredService<WatchlistDocumentStore>(),
            () => Plugin.Instance!.Configuration,
            provider.GetRequiredService<ISeriesCompletion>(),
            provider.GetRequiredService<ILogger<WatchedRemovalHandler>>()));

        serviceCollection.AddHostedService<UserDataWatchedSubscription>();

        // What happens to a deleted user's list, and how the server says so. The
        // deletion arrives through the event manager rather than as an event on the
        // user manager, and the server resolves the consumers of a type out of its
        // own container when it publishes, so this line is the whole of the
        // attachment.
        serviceCollection.AddSingleton<DeletedUserHandler>();
        serviceCollection.AddSingleton<IEventConsumer<UserDeletedEventArgs>, UserDeletedSubscription>();
    }
}
