using System;
using Jellyfin.Data.Enums;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;

namespace Jellyfin.Plugin.Watchlist.Watched;

/// <summary>
/// The completion answer that asks the server's library.
/// </summary>
/// <remarks>
/// Two counts rather than a walk over the episodes. The library already answers a
/// filtered count, so asking it how many episodes the series holds for this user and
/// how many of those are unplayed is one question each and neither builds a list this
/// plugin then throws away.
/// </remarks>
public sealed class LibrarySeriesCompletion : ISeriesCompletion
{
    private readonly ILibraryManager _library;
    private readonly IUserManager _users;

    /// <summary>
    /// Initializes a new instance of the <see cref="LibrarySeriesCompletion"/> class.
    /// </summary>
    /// <param name="library">The server's library.</param>
    /// <param name="users">The server's users.</param>
    public LibrarySeriesCompletion(ILibraryManager library, IUserManager users)
    {
        _library = library;
        _users = users;
    }

    /// <inheritdoc />
    /// <remarks>
    /// A series holding no episodes this user can see answers false rather than true.
    /// Vacuously every episode of it is played, and acting on that would take a series
    /// off a list the moment its files went missing, which is the opposite of what the
    /// rule is for.
    /// </remarks>
    public bool EveryEpisodeIsPlayed(Guid seriesId, Guid userId)
    {
        var user = _users.GetUserById(userId);

        if (user is null)
        {
            return false;
        }

        if (_library.GetCount(EpisodesOf(user, seriesId, null)) == 0)
        {
            return false;
        }

        return _library.GetCount(EpisodesOf(user, seriesId, false)) == 0;
    }

    /// <summary>
    /// The episodes of one series as this user sees them, optionally narrowed to the
    /// ones they have or have not played.
    /// </summary>
    /// <param name="user">The user the count is taken for.</param>
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
