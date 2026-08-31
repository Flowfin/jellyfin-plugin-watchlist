using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Data.Events.Users;
using Jellyfin.Plugin.Watchlist.Api;
using Jellyfin.Plugin.Watchlist.Configuration;
using Jellyfin.Plugin.Watchlist.Export;
using Jellyfin.Plugin.Watchlist.Library;
using Jellyfin.Plugin.Watchlist.Projection;
using Jellyfin.Plugin.Watchlist.Store;
using Jellyfin.Plugin.Watchlist.Users;
using Jellyfin.Plugin.Watchlist.Watched;
using MediaBrowser.Controller.Events;
using MediaBrowser.Model.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Jellyfin.Plugin.Watchlist.Tests;

/// <summary>
/// The registration hook, executed rather than argued.
/// </summary>
/// <remarks>
/// <para>
/// This file was excluded from the coverage floor on the reason that the hook "is
/// called with the server's service collection and its application host, and running
/// it means having both". Neither half holds. <c>IServiceCollection</c> is satisfied
/// by <see cref="ServiceCollection"/>, which this suite already carries, and the
/// application host parameter is never read in the body, so nothing has to be built
/// to stand in for it.
/// </para>
/// <para>
/// The two factories are the part a call alone does not reach: registering a lambda
/// does not run it. Both read <c>Plugin.Instance</c>, and the plugin type is
/// constructible from a test since #34, so the provider is built and the two services
/// are resolved here rather than left as lines nothing executes.
/// </para>
/// </remarks>
[Collection(PluginInstanceCollection.Name)]
public class PluginServiceRegistratorTests
{
    /// <summary>
    /// What the server's container is given. The count is asserted alongside the
    /// service types so that a registration added tomorrow is a failing test rather
    /// than a silent addition to what the server resolves.
    /// </summary>
    [Fact]
    public void TheHookRegistersTheServicesTheControllersAreBuiltFrom()
    {
        var services = new ServiceCollection();

        new PluginServiceRegistrator().RegisterServices(services, null!);

        Assert.Equal(20, services.Count);
        Assert.Contains(services, d => d.ServiceType == typeof(IWatchlistItemDescriber));
        Assert.Contains(services, d => d.ServiceType == typeof(IProviderIdSource));
        Assert.Contains(services, d => d.ServiceType == typeof(IProviderIdIndex));
        Assert.Contains(services, d => d.ServiceType == typeof(TimeProvider));
        Assert.Contains(services, d => d.ServiceType == typeof(PluginConfiguration));
        Assert.Contains(services, d => d.ServiceType == typeof(Func<PluginConfiguration>));
        Assert.Contains(services, d => d.ServiceType == typeof(WatchlistDocumentStore));
        Assert.Contains(services, d => d.ServiceType == typeof(ISeriesEpisodes));
        Assert.Contains(services, d => d.ServiceType == typeof(ISeriesCompletion));
        Assert.Contains(services, d => d.ServiceType == typeof(WatchedRemovalHandler));
        Assert.Contains(services, d => d.ServiceType == typeof(WatchlistProjector));
        Assert.Contains(services, d => d.ServiceType == typeof(WatchlistReconciler));
        Assert.Contains(services, d => d.ServiceType == typeof(IPlaylistGateway));
        Assert.Contains(services, d => d.ServiceType == typeof(WatchlistProjectionPass));
        Assert.Contains(services, d => d.ServiceType == typeof(IScheduledTask));
        Assert.Contains(services, d => d.ServiceType == typeof(IHostedService));
        Assert.Contains(services, d => d.ServiceType == typeof(LibraryRemovalHandler));
        Assert.Contains(services, d => d.ServiceType == typeof(DeletedUserHandler));
        Assert.Contains(services, d => d.ServiceType == typeof(IEventConsumer<UserDeletedEventArgs>));
    }

    /// <summary>
    /// The scheduled pass and the task the dashboard shows are both resolvable out of a
    /// container built the way the server builds one, and the task runs the pass.
    /// </summary>
    /// <remarks>
    /// The two lambdas inside those registrations are the part a call alone does not
    /// reach, for the reason this file gives at the top: registering a lambda does not
    /// run it, and both of these read <c>Plugin.Instance</c>. So the provider is built
    /// and the task is executed here rather than left as lines nothing runs.
    ///
    /// The playlist seam is resolved as well, because it is registered by type and its
    /// constructor takes the server's playlist manager: a registration that named a type
    /// the container cannot build is a dashboard entry that throws the first time an
    /// administrator presses it.
    /// </remarks>
    [Fact]
    public async Task TheScheduledTaskResolvesAndRunsThePass()
    {
        using var paths = new ServerPathsInATemporaryDirectory();
        var plugin = new Plugin(paths, new PluginConfigurationFile(), new RecordingPluginLogger());
        var services = new ServiceCollection();

        new PluginServiceRegistrator().RegisterServices(services, null!);
        services.AddSingleton<ILogger<WatchlistDocumentStore>>(new RecordingLogger());
        services.AddSingleton<ILogger<WatchlistProjectionPass>>(new RecordingPassLogger());
        services.AddSingleton<ILogger<WatchlistProjector>>(new RecordingProjectorLogger());
        services.AddSingleton<ILogger<WatchlistReconciler>>(new RecordingReconcilerLogger());
        services.AddSingleton<MediaBrowser.Controller.Library.ILibraryManager>(new ALibraryOf());
        services.AddSingleton<MediaBrowser.Controller.Library.IUserManager>(new AUserDirectoryOf());

        // The seam the registrator names is asserted as a registration rather than
        // resolved, and then replaced. Building the real adapter means building the
        // server's playlist manager, which is exactly the width the adapter exists to
        // keep out of everything above it; what the registration owes is that the
        // server's container is told which implementation to use, and that is a fact
        // about the descriptor.
        Assert.Equal(
            typeof(ServerPlaylistGateway),
            Assert.Single(services, d => d.ServiceType == typeof(IPlaylistGateway)).ImplementationType);

        services.AddSingleton<IPlaylistGateway>(new APlaylistServerOf());

        using var provider = services.BuildServiceProvider();

        Assert.NotNull(provider.GetRequiredService<WatchlistProjectionPass>());

        // ACTIVATED THE WAY THE SERVER ACTIVATES IT, which is not the way the
        // registration below is resolved. The server finds the type by scanning this
        // assembly and builds it against its own container, so a constructor parameter
        // that is not registered is a plugin the server lists as Malfunctioned with one
        // line in its startup log. That is what happened, and it happened with this file
        // green: the interoperability boot caught it and nothing here could, because
        // every test resolved the registration and the server does not.
        //
        // What this reaches is an unregistered parameter. Whether a parameter registered
        // in the wrong LIFETIME would fail on a server is not separated here: this
        // provider serves a scoped registration from its root, so a scoped delegate
        // passes this line.
        var task = ActivatorUtilities.CreateInstance<WatchlistReconciliationTask>(provider);

        Assert.Equal("WatchlistReconciliation", task.Key);
        Assert.Single(task.GetDefaultTriggers());

        Assert.IsType<WatchlistReconciliationTask>(Assert.Single(provider.GetServices<IScheduledTask>()));

        // Nobody has used the plugin on this server, so the run walks no user and makes
        // no playlist call at all. What this executes is the two lambdas above it.
        await task.ExecuteAsync(new NobodyIsWatching(), CancellationToken.None);

        Assert.Equal(PluginConfiguration.DefaultReconciliationIntervalHours, plugin.Configuration.ReconciliationIntervalHours);
    }

    /// <summary>
    /// The deletion consumer is registered under the interface the server resolves it
    /// by, because that registration is the whole of the attachment.
    /// </summary>
    /// <remarks>
    /// The server publishes an event by asking its own container for every
    /// <c>IEventConsumer</c> of that type, so a class registered under its own name
    /// only is a handler the server never calls, and nothing else in the plugin would
    /// notice.
    /// </remarks>
    [Fact]
    public void TheDeletionConsumerIsRegisteredUnderTheInterfaceTheServerResolves()
    {
        var services = new ServiceCollection();

        new PluginServiceRegistrator().RegisterServices(services, null!);

        var consumers = services.Where(d => d.ServiceType == typeof(IEventConsumer<UserDeletedEventArgs>)).ToList();

        Assert.Single(consumers);
        Assert.Equal(typeof(UserDeletedSubscription), consumers[0].ImplementationType);
    }

    /// <summary>
    /// The subscription is registered as something the server starts and stops rather
    /// than as a service somebody has to remember to resolve. Nothing else in this
    /// plugin is hosted, so the one hosted registration is asserted by its
    /// implementation as well as by its count.
    /// </summary>
    [Fact]
    public void TheWatchedSubscriptionIsRegisteredAsSomethingTheServerRuns()
    {
        var services = new ServiceCollection();

        new PluginServiceRegistrator().RegisterServices(services, null!);

        var hosted = services
            .Where(d => d.ServiceType == typeof(IHostedService))
            .Select(d => d.ImplementationType)
            .ToList();

        // Two, and both are subscriptions to something the server raises. The set is
        // asserted rather than the count, so a third hosted registration is a failing
        // test naming what arrived rather than a number somebody edits.
        Assert.Equal(
            new[] { typeof(UserDataWatchedSubscription), typeof(LibraryRemovalSubscription) },
            hosted);
    }

    /// <summary>
    /// The handler's factory is a lambda, so registering it runs nothing. This resolves
    /// it, which is what says the three things it is built from are reachable from the
    /// server's container rather than only from a test that hands them over.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The library and the user directory are the server's own registrations rather
    /// than this plugin's, so they are put into the collection here exactly as the
    /// server would have them there already.
    /// </para>
    /// <para>
    /// The handler is then driven once, because the configuration inside the factory is
    /// a second lambda and resolving the handler does not run it. What that line
    /// decides is which configuration object the handler reads, and it is the line that
    /// makes the setting the administrator saved the one in force rather than the one
    /// the server started with.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheHandlerFactoryBuildsSomethingTheServersContainerCanResolve()
    {
        using var paths = new ServerPathsInATemporaryDirectory();
        var plugin = new Plugin(paths, new PluginConfigurationFile(), new RecordingPluginLogger());
        var services = new ServiceCollection();

        new PluginServiceRegistrator().RegisterServices(services, null!);
        services.AddSingleton<ILogger<WatchlistDocumentStore>>(new RecordingLogger());
        services.AddSingleton<ILogger<WatchedRemovalHandler>>(new RecordingWatchedLogger());
        services.AddSingleton<MediaBrowser.Controller.Library.ILibraryManager>(new ALibraryOf());
        services.AddSingleton<MediaBrowser.Controller.Library.IUserManager>(new AUserDirectoryOf());

        using var provider = services.BuildServiceProvider();

        var handler = provider.GetRequiredService<WatchedRemovalHandler>();
        var store = provider.GetRequiredService<WatchlistDocumentStore>();

        Assert.IsType<LibrarySeriesCompletion>(provider.GetRequiredService<ISeriesCompletion>());

        var viewer = Guid.Parse("55555555-5555-5555-5555-555555555555");
        var film = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");
        var played = new WatchedItem { ItemId = film, Kind = WatchlistItemKind.Movie };

        store.Add(
            viewer,
            new WatchlistEntry
            {
                ItemId = film,
                Kind = WatchlistItemKind.Movie,
                AddedAt = new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero),
                Source = WatchlistEntrySource.Api,
            },
            maxEntriesPerUser: 10);

        plugin.UpdateConfiguration(new PluginConfiguration { RemoveWhenWatched = false });
        handler.Handle(viewer, played);

        Assert.Single(store.Read(viewer).Document!.Entries);

        plugin.UpdateConfiguration(new PluginConfiguration { RemoveWhenWatched = true });
        handler.Handle(viewer, played);

        Assert.Empty(store.Read(viewer).Document!.Entries);
    }

    /// <summary>
    /// Both provider questions are answered by the one class that knows a library can
    /// be searched, so an export and an import on one server read the same library.
    /// The implementation type is asserted rather than an instance, because resolving
    /// either one needs the server's library manager.
    /// </summary>
    [Fact]
    public void BothProviderQuestionsAreAnsweredByTheOneLibraryAdapter()
    {
        var services = new ServiceCollection();

        new PluginServiceRegistrator().RegisterServices(services, null!);

        Assert.All(
            services.Where(d => d.ServiceType == typeof(IProviderIdSource)
                || d.ServiceType == typeof(IProviderIdIndex)),
            d => Assert.Equal(typeof(LibraryProviderIds), d.ImplementationType));
    }

    /// <summary>
    /// The clock is the one the plugin declares rather than an instance built here,
    /// because the entry that carries an instant is checked against it.
    /// </summary>
    [Fact]
    public void TheClockIsRegisteredAsTheSystemOne()
    {
        var services = new ServiceCollection();

        new PluginServiceRegistrator().RegisterServices(services, null!);

        using var provider = services.BuildServiceProvider();

        Assert.Same(TimeProvider.System, provider.GetRequiredService<TimeProvider>());
    }

    /// <summary>
    /// The configuration is resolved per scope rather than once, which is the reason
    /// it is registered through a factory at all: the server replaces the object when
    /// the page is saved. Two scopes are taken across one save so that a registration
    /// changed to a singleton fails here instead of handing every later request the
    /// value the server started with.
    /// </summary>
    [Fact]
    public void TheConfigurationFactoryAnswersWithWhatThePluginHoldsNow()
    {
        using var paths = new ServerPathsInATemporaryDirectory();
        var plugin = new Plugin(paths, new PluginConfigurationFile(), new RecordingPluginLogger());
        var services = new ServiceCollection();

        new PluginServiceRegistrator().RegisterServices(services, null!);

        using var provider = services.BuildServiceProvider();

        using (var scope = provider.CreateScope())
        {
            Assert.Same(plugin.Configuration, scope.ServiceProvider.GetRequiredService<PluginConfiguration>());
        }

        plugin.UpdateConfiguration(new PluginConfiguration { MaxEntriesPerUser = 37 });

        using (var scope = provider.CreateScope())
        {
            Assert.Equal(37, scope.ServiceProvider.GetRequiredService<PluginConfiguration>().MaxEntriesPerUser);
        }
    }

    /// <summary>
    /// The store's folder is not known when the hook runs, so the factory reads it when
    /// something first asks. The assertion is on the folder the plugin declares, which
    /// is what makes this a test of the factory rather than of the store's constructor.
    /// </summary>
    [Fact]
    public void TheStoreFactoryReadsThePluginsOwnDataFolder()
    {
        using var paths = new ServerPathsInATemporaryDirectory();
        var plugin = new Plugin(paths, new PluginConfigurationFile(), new RecordingPluginLogger());
        var services = new ServiceCollection();

        new PluginServiceRegistrator().RegisterServices(services, null!);
        services.AddSingleton<ILogger<WatchlistDocumentStore>>(new RecordingLogger());

        using var provider = services.BuildServiceProvider();

        var store = provider.GetRequiredService<WatchlistDocumentStore>();

        Assert.Equal(new WatchlistDocumentStore(plugin.DataFolderPath).DataFolderPath, store.DataFolderPath);
    }

    /// <summary>
    /// A progress sink that keeps nothing, for the one run here that has no user to
    /// report on.
    /// </summary>
    private sealed class NobodyIsWatching : IProgress<double>
    {
        public void Report(double value)
        {
        }
    }
}

