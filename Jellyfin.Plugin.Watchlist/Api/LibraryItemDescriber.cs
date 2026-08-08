using System;
using Jellyfin.Plugin.Watchlist.Store;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;

namespace Jellyfin.Plugin.Watchlist.Api;

/// <summary>
/// The describer that asks the server. It is the only thing in this plugin that knows
/// there is a library.
/// </summary>
/// <remarks>
/// Everything above it takes <see cref="IWatchlistItemDescriber"/>, so the endpoint,
/// the visibility rule and their tests never touch a server type. That is what lets
/// the suite drive the whole read with no server present, and it is why this file is
/// small enough to read instead of test: reaching a line of it means constructing a
/// library manager and a user manager, and the coverage floor names it for that reason
/// rather than because it is unimportant.
/// </remarks>
public sealed class LibraryItemDescriber : IWatchlistItemDescriber
{
    private readonly ILibraryManager _library;
    private readonly IUserManager _users;

    /// <summary>
    /// Initializes a new instance of the <see cref="LibraryItemDescriber"/> class.
    /// </summary>
    /// <param name="library">The server's library.</param>
    /// <param name="users">The server's users.</param>
    public LibraryItemDescriber(ILibraryManager library, IUserManager users)
    {
        _library = library;
        _users = users;
    }

    /// <inheritdoc />
    /// <remarks>
    /// Three ways to answer nothing and one answer for all of them. A user the server
    /// does not know, an item that is no longer in the library and an item this user is
    /// not allowed to see are told apart here and are indistinguishable to the caller,
    /// which is what stops the list being a way to ask what sits in somebody else's
    /// library.
    /// </remarks>
    public WatchlistItemDescription? Describe(Guid itemId, Guid userId)
    {
        var user = _users.GetUserById(userId);

        if (user is null)
        {
            return null;
        }

        var item = _library.GetItemById(itemId);

        if (item is null || !item.IsVisible(user))
        {
            return null;
        }

        var episode = item as Episode;

        return new WatchlistItemDescription
        {
            Name = item.Name ?? string.Empty,
            Kind = KindOf(item),
            ProductionYear = item.ProductionYear,
            SeriesName = episode?.SeriesName,
            SeasonNumber = episode?.ParentIndexNumber,
            EpisodeNumber = episode?.IndexNumber,
        };
    }

    /// <summary>
    /// What the library holds this as, in the plugin's own vocabulary. Everything the
    /// three named types do not cover is <see cref="WatchlistItemKind.Other"/> rather
    /// than a guess, and the add endpoint is where that answer is refused.
    /// </summary>
    /// <param name="item">The library item.</param>
    /// <returns>The kind.</returns>
    private static WatchlistItemKind KindOf(MediaBrowser.Controller.Entities.BaseItem item) => item switch
    {
        Movie => WatchlistItemKind.Movie,
        Series => WatchlistItemKind.Series,
        Episode => WatchlistItemKind.Episode,
        _ => WatchlistItemKind.Other,
    };
}
