using System.Collections.Generic;
using System.Collections.Immutable;
using Jellyfin.Plugin.Watchlist.Store;

namespace Jellyfin.Plugin.Watchlist.Api;

/// <summary>
/// What may go on a watchlist, and it is a short list on purpose.
/// </summary>
/// <remarks>
/// A watchlist is a list of things somebody intends to watch. A film, a whole show,
/// and one episode are all answers to that; a music track, a photograph, a book and a
/// collection are not, and a library holds all of them under identifiers that look
/// exactly the same from an endpoint.
///
/// So the rule is a set of what is accepted rather than a set of what is refused.
/// Written the other way round, a kind the server adds next year arrives on the list
/// by default and nobody decides anything, which is how a watchlist becomes a
/// bookmark folder one release at a time.
///
/// <see cref="WatchlistItemKind.Unknown"/> is outside the set for the same reason it
/// exists: it is what a describer that answered nothing leaves behind, and recording
/// an entry whose kind nothing decided is worse than refusing the add.
/// </remarks>
public static class AcceptedWatchlistItemKinds
{
    /// <summary>
    /// Gets the kinds an add is accepted for. Everything else is refused, and the
    /// refusal names this set.
    /// </summary>
    public static IReadOnlySet<WatchlistItemKind> All { get; } = ImmutableHashSet.Create(
        WatchlistItemKind.Movie,
        WatchlistItemKind.Series,
        WatchlistItemKind.Episode);

    /// <summary>
    /// Whether an item of this kind may go on a list.
    /// </summary>
    /// <param name="kind">The kind the library answered with.</param>
    /// <returns>True when an add of it is accepted.</returns>
    public static bool Accepts(WatchlistItemKind kind) => All.Contains(kind);
}
