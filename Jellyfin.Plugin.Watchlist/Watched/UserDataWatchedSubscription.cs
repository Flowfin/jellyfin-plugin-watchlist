using System;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Watchlist.Store;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using Microsoft.Extensions.Hosting;

namespace Jellyfin.Plugin.Watchlist.Watched;

/// <summary>
/// The one thing in this plugin that listens to the server, and the only place that
/// knows a played item arrives as a library item.
/// </summary>
/// <remarks>
/// <para>
/// It subscribes when the server starts it and unsubscribes when the server stops it.
/// A subscription that outlives the plugin is a handler still running against a store
/// nobody is using, so the second half is written with the first rather than after it.
/// </para>
/// <para>
/// It translates and nothing more. Every decision is taken by
/// <see cref="WatchedRemovalHandler"/> out of <see cref="WatchedItem"/>, so what the
/// rule sees is the same whether the server raised the event or a test did.
/// </para>
/// </remarks>
public sealed class UserDataWatchedSubscription : IHostedService
{
    private readonly IUserDataManager _userData;
    private readonly WatchedRemovalHandler _handler;

    /// <summary>
    /// Initializes a new instance of the <see cref="UserDataWatchedSubscription"/> class.
    /// </summary>
    /// <param name="userData">The server's user data manager.</param>
    /// <param name="handler">What decides which entries leave.</param>
    public UserDataWatchedSubscription(IUserDataManager userData, WatchedRemovalHandler handler)
    {
        _userData = userData;
        _handler = handler;
    }

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        _userData.UserDataSaved += OnUserDataSaved;

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken)
    {
        _userData.UserDataSaved -= OnUserDataSaved;

        return Task.CompletedTask;
    }

    /// <summary>
    /// What this plugin makes of one saved user data record.
    /// </summary>
    /// <param name="args">The event the server raised.</param>
    /// <returns>The played item, or null where the event is not one this plugin acts on.</returns>
    /// <remarks>
    /// Three ways to be nothing. The save was not a play, so marking an item unplayed
    /// reaches here and stops, which is what keeps an entry from coming back. The event
    /// carries no item. And the item is of a kind a watchlist does not hold, which is
    /// everything outside the accepted set rather than everything this file names.
    /// </remarks>
    internal static WatchedItem? PlayedItemIn(UserDataSaveEventArgs args)
    {
        ArgumentNullException.ThrowIfNull(args);

        if (args.UserData?.Played != true || args.Item is null)
        {
            return null;
        }

        var kind = args.Item switch
        {
            Movie => WatchlistItemKind.Movie,
            Series => WatchlistItemKind.Series,
            Episode => WatchlistItemKind.Episode,
            _ => WatchlistItemKind.Other,
        };

        if (kind == WatchlistItemKind.Other)
        {
            return null;
        }

        return new WatchedItem
        {
            ItemId = args.Item.Id,
            Kind = kind,
            SeriesId = args.Item is Episode episode ? episode.SeriesId : null,
        };
    }

    private void OnUserDataSaved(object? sender, UserDataSaveEventArgs args)
    {
        var played = PlayedItemIn(args);

        if (played is null)
        {
            return;
        }

        _handler.Handle(args.UserId, played);
    }
}
