using System;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Watchlist.Store;
using MediaBrowser.Controller.Library;
using Microsoft.Extensions.Hosting;

namespace Jellyfin.Plugin.Watchlist.Library;

/// <summary>
/// The plugin's ear on the library: it hears an item leave and asks for a
/// reconciliation.
/// </summary>
/// <remarks>
/// <para>
/// It subscribes when the server starts it and unsubscribes when the server stops it. A
/// subscription that outlives the plugin is a handler still running against a store
/// nobody is using, so the second half is written with the first rather than after it,
/// which is the shape <see cref="Watched.UserDataWatchedSubscription"/> already carries.
/// </para>
/// <para>
/// ONE EVENT AND NOT THREE. The library raises an addition, an update and a removal, and
/// this plugin listens to the removal alone.
/// </para>
/// <para>
/// An ADDITION cannot put anything on anybody's list: an entry is a library identifier,
/// media removed and added again gets a new one, and the plugin has no way to know that a
/// new item is the old one. So an addition changes nothing this plugin holds, and a
/// handler for it would be a pass over every user for an event that cannot have moved
/// anything.
/// </para>
/// <para>
/// An UPDATE is the same case one step further. What a projection reads about an item is
/// whether it resolves for a user, and an item that is being updated still resolves.
/// </para>
/// <para>
/// It translates and nothing more. What a removal means is decided by
/// <see cref="LibraryRemovalHandler"/>, so the rule sees the same thing whether the
/// server raised the event or a test did.
/// </para>
/// </remarks>
public sealed class LibraryRemovalSubscription : IHostedService
{
    private readonly ILibraryManager _library;
    private readonly LibraryRemovalHandler _handler;

    /// <summary>
    /// Initializes a new instance of the <see cref="LibraryRemovalSubscription"/> class.
    /// </summary>
    /// <param name="library">The server's library.</param>
    /// <param name="handler">What a removal means.</param>
    /// <exception cref="ArgumentNullException">Any argument is null.</exception>
    public LibraryRemovalSubscription(ILibraryManager library, LibraryRemovalHandler handler)
    {
        ArgumentNullException.ThrowIfNull(library);
        ArgumentNullException.ThrowIfNull(handler);

        _library = library;
        _handler = handler;
    }

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        _library.ItemRemoved += OnItemRemoved;

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken)
    {
        _library.ItemRemoved -= OnItemRemoved;

        return Task.CompletedTask;
    }

    /// <summary>
    /// Whether a removal of this item could have changed what a watchlist resolves to.
    /// </summary>
    /// <param name="args">The event the server raised.</param>
    /// <returns>True where a pass is worth asking for.</returns>
    /// <remarks>
    /// A kind a list cannot hold is not worth a pass. A library scan removes music
    /// tracks, images and folders alongside the things this plugin holds, and each of
    /// those would otherwise ask for a walk over every user for an item no entry can
    /// point at. An event carrying no item is nothing at all.
    ///
    /// The kind is read off the item the event carries rather than asked of the library,
    /// because the item is being removed and the library may already have forgotten it.
    /// </remarks>
    internal static bool CouldBeOnAList(ItemChangeEventArgs args)
    {
        ArgumentNullException.ThrowIfNull(args);

        if (args.Item is null)
        {
            return false;
        }

        var kind = args.Item switch
        {
            MediaBrowser.Controller.Entities.Movies.Movie => WatchlistItemKind.Movie,
            MediaBrowser.Controller.Entities.TV.Series => WatchlistItemKind.Series,
            MediaBrowser.Controller.Entities.TV.Episode => WatchlistItemKind.Episode,
            _ => WatchlistItemKind.Other,
        };

        return Api.AcceptedWatchlistItemKinds.Accepts(kind);
    }

    private void OnItemRemoved(object? sender, ItemChangeEventArgs args)
    {
        if (!CouldBeOnAList(args))
        {
            return;
        }

        _handler.SomethingWasRemoved();
    }
}
