using System;
using System.Collections.Generic;
using System.Linq;
using Jellyfin.Data.Enums;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;

namespace Jellyfin.Plugin.Watchlist.Projection;

/// <summary>
/// The episodes of a series, asked of the server's library.
/// </summary>
/// <remarks>
/// <para>
/// Two queries and no walk over user data. The library answers a filtered list for one
/// user, so the visibility question is settled by the query rather than by this plugin
/// filtering afterwards, and asking a second time for the unplayed ones settles the
/// played question the same way. That is two round trips per series on a list rather
/// than one per episode, and it is the shape
/// <see cref="Watched.LibrarySeriesCompletion"/> already uses for the neighbouring
/// question.
/// </para>
/// <para>
/// It decides nothing. Which of the episodes becomes the playlist row is
/// <see cref="SeriesRow"/>, which is a function of what this returns and is where every
/// test of that rule lives. This file is small enough to read instead of test for the
/// same reason <see cref="Api.LibraryItemDescriber"/> is: reaching a line of it means
/// constructing a library manager and a user manager.
/// </para>
/// <para>
/// A user the server does not know and a series it cannot answer for are both an empty
/// list, which the interface declares and the rule above turns into no playlist row.
/// </para>
/// </remarks>
public sealed class LibrarySeriesEpisodes : ISeriesEpisodes
{
    private readonly ILibraryManager _library;
    private readonly IUserManager _users;

    /// <summary>
    /// Initializes a new instance of the <see cref="LibrarySeriesEpisodes"/> class.
    /// </summary>
    /// <param name="library">The server's library.</param>
    /// <param name="users">The server's users.</param>
    public LibrarySeriesEpisodes(ILibraryManager library, IUserManager users)
    {
        _library = library;
        _users = users;
    }

    /// <inheritdoc />
    public IReadOnlyList<SeriesEpisode> Of(Guid seriesId, Guid userId)
    {
        var user = _users.GetUserById(userId);

        if (user is null)
        {
            return [];
        }

        var unplayed = _library
            .GetItemList(EpisodesOf(user, seriesId, isPlayed: false))
            .Select(item => item.Id)
            .ToHashSet();

        return _library
            .GetItemList(EpisodesOf(user, seriesId, isPlayed: null))
            .Select(item => new SeriesEpisode
            {
                ItemId = item.Id,
                SeasonNumber = item.ParentIndexNumber,
                EpisodeNumber = item.IndexNumber,
                IsPlayed = !unplayed.Contains(item.Id),
            })
            .ToList();
    }

    /// <summary>
    /// The episodes of one series as this user sees them, optionally narrowed to the
    /// ones they have or have not played.
    /// </summary>
    /// <param name="user">The user the question is asked for.</param>
    /// <param name="seriesId">The series.</param>
    /// <param name="isPlayed">The played state to narrow to, or null for all of them.</param>
    /// <returns>The query.</returns>
    private static InternalItemsQuery EpisodesOf(Jellyfin.Database.Implementations.Entities.User user, Guid seriesId, bool? isPlayed) =>
        new(user)
        {
            AncestorIds = [seriesId],
            IncludeItemTypes = [BaseItemKind.Episode],
            IsPlayed = isPlayed,
            Recursive = true,
        };
}
