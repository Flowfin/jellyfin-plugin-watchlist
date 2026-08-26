using System;
using System.Linq;
using Jellyfin.Plugin.Watchlist.Api;
using Jellyfin.Plugin.Watchlist.Configuration;
using Jellyfin.Plugin.Watchlist.Export;
using Jellyfin.Plugin.Watchlist.Store;
using Microsoft.Extensions.DependencyInjection;
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

        Assert.Equal(6, services.Count);
        Assert.Contains(services, d => d.ServiceType == typeof(IWatchlistItemDescriber));
        Assert.Contains(services, d => d.ServiceType == typeof(IProviderIdSource));
        Assert.Contains(services, d => d.ServiceType == typeof(IProviderIdIndex));
        Assert.Contains(services, d => d.ServiceType == typeof(TimeProvider));
        Assert.Contains(services, d => d.ServiceType == typeof(PluginConfiguration));
        Assert.Contains(services, d => d.ServiceType == typeof(WatchlistDocumentStore));
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
}
