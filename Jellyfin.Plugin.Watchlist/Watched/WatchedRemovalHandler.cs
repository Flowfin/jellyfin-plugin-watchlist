using System;
using Jellyfin.Plugin.Watchlist.Configuration;
using Jellyfin.Plugin.Watchlist.Store;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Watchlist.Watched;

/// <summary>
/// Takes a played item and one user, and leaves that user's list holding what they
/// have not watched yet.
/// </summary>
/// <remarks>
/// <para>
/// It acts for the one user the event named and for nobody else. There is no path
/// through it that reads or writes another user's document, which is what stops a
/// person's viewing from changing somebody else's list.
/// </para>
/// <para>
/// The configuration is asked for rather than held, because the server replaces the
/// object when an administrator saves the page and this lives for as long as the
/// server does. A handler holding one would answer with the setting that was in force
/// when the server started.
/// </para>
/// </remarks>
public sealed class WatchedRemovalHandler
{
    private readonly WatchlistDocumentStore _store;
    private readonly Func<PluginConfiguration> _configuration;
    private readonly ISeriesCompletion _series;
    private readonly ILogger<WatchedRemovalHandler> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="WatchedRemovalHandler"/> class.
    /// </summary>
    /// <param name="store">The store.</param>
    /// <param name="configuration">Where the server-wide settings are read from.</param>
    /// <param name="series">The answer about a series being finished.</param>
    /// <param name="logger">The logger.</param>
    public WatchedRemovalHandler(
        WatchlistDocumentStore store,
        Func<PluginConfiguration> configuration,
        ISeriesCompletion series,
        ILogger<WatchedRemovalHandler> logger)
    {
        _store = store;
        _configuration = configuration;
        _series = series;
        _logger = logger;
    }

    /// <summary>
    /// Acts on one played item for one user.
    /// </summary>
    /// <param name="userId">The user the event named.</param>
    /// <param name="played">What was played.</param>
    /// <exception cref="ArgumentNullException"><paramref name="played"/> is null.</exception>
    public void Handle(Guid userId, WatchedItem played)
    {
        ArgumentNullException.ThrowIfNull(played);

        var read = _store.Read(userId);

        if (read.Document is null)
        {
            // The list is unavailable, so this plugin does not know what is on it.
            // Removing nothing is the only safe answer; the store has already said
            // why it will not read the document.
            return;
        }

        if (!EffectiveSettings.RemoveWhenWatched(_configuration(), read.Document.Preferences))
        {
            return;
        }

        var retired = WatchedRemoval.EntriesRetiredBy(read.Document.Entries, played, userId, _series);

        if (retired.Count == 0)
        {
            return;
        }

        foreach (var itemId in retired)
        {
            _store.Remove(userId, itemId);
        }

        _logger.LogInformation(
            "Watched removal took {Count} entries off the list of user {UserId} after item {ItemId} was played",
            retired.Count,
            userId,
            played.ItemId);
    }
}
