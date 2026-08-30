using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Watchlist.Store;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Watchlist.Projection;

/// <summary>
/// Makes sure one target has exactly one playlist, and that the plugin remembers
/// which playlist that is.
/// </summary>
/// <remarks>
/// <para>
/// This is the half of the projection that decides WHICH playlist a list belongs in.
/// What goes inside it is the reconciler on #19, and the two are separate because the
/// question this answers is asked once per target per pass and the other is asked of
/// every entry.
/// </para>
/// <para>
/// IT CREATES ON DEMAND AND NEVER FOR A POPULATION. There is no route through this
/// that walks the users of a server, and a caller hands it one target at a time, so a
/// server where nobody has used the plugin has no playlists and does no work. That is
/// a property of the shape rather than of a flag: nothing here can be given a set.
/// </para>
/// <para>
/// The identifier is the identity and the name never is. A server appends a digit to a
/// colliding playlist directory, so a projection that found its playlist by name would
/// meet two lists with one name and adopt whichever came first. What is remembered is
/// the identifier, and every later operation names it.
/// </para>
/// <para>
/// ADOPTION HAPPENS ON A FIRST PASS AND NOWHERE ELSE. Where a target has nothing
/// remembered and its owner has exactly one playlist carrying the configured name, that
/// playlist is taken over rather than a second one created beside it. A target whose
/// remembered playlist has been deleted is not a first pass and does not adopt: it is a
/// list this plugin already managed, and what happened to it is a different question
/// from what a person's hand-made list is.
/// </para>
/// <para>
/// Nothing here reaches a server playlist type. Every call goes through
/// <see cref="IPlaylistGateway"/>, which is the seam the two supported server lines
/// differ behind, and a guard over the plugin's own sources refuses such a type
/// anywhere but the one implementation of it.
/// </para>
/// </remarks>
public sealed class WatchlistProjector
{
    private readonly IPlaylistGateway _playlists;
    private readonly ILogger<WatchlistProjector> _logger;
    private readonly ConcurrentDictionary<string, byte> _said = new(StringComparer.Ordinal);

    /// <summary>
    /// Initializes a new instance of the <see cref="WatchlistProjector"/> class.
    /// </summary>
    /// <param name="playlists">The seam every playlist operation goes through.</param>
    /// <param name="logger">The logger.</param>
    public WatchlistProjector(IPlaylistGateway playlists, ILogger<WatchlistProjector> logger)
    {
        _playlists = playlists;
        _logger = logger;
    }

    /// <summary>
    /// Makes sure this target has a playlist, creating one only where it has none that
    /// the server still holds.
    /// </summary>
    /// <param name="target">The target.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>Which playlist the target is projected into, and how that came about.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="target"/> is null.</exception>
    /// <remarks>
    /// A second call over an unchanged target creates nothing. What makes that true is
    /// the remembered identifier being asked of the server rather than trusted: a
    /// playlist the user deleted is gone, and a target that kept pointing at it would
    /// have a list nobody can see and a reconciler writing into nothing.
    /// </remarks>
    public async Task<ProjectionResult> EnsurePlaylistAsync(IProjectionTarget target, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(target);

        if (!target.IsRecordAvailable)
        {
            // The store has already said why it will not read the document. Creating a
            // playlist here would make one this plugin could never write the identity
            // of, and the next pass would make another beside it.
            return ProjectionResult.RefusedRecordUnavailable();
        }

        var remembered = target.Remembered;
        var owned = _playlists.PlaylistsOf(target.OwnerUserId);
        var onTheServer = remembered is null ? null : Holding(owned, remembered.PlaylistId);

        if (remembered is not null && onTheServer is not null)
        {
            ReportACollision(target, owned, remembered.PlaylistId);

            return await KeepTheNameInStepAsync(target, remembered, onTheServer, cancellationToken)
                .ConfigureAwait(false);
        }

        if (remembered is null)
        {
            var adopted = AdoptableFrom(target, owned);

            if (adopted is not null)
            {
                return TakeOver(target, adopted);
            }
        }

        var name = target.ConfiguredName;
        var playlistId = await _playlists
            .CreateAsync(target.OwnerUserId, name, cancellationToken)
            .ConfigureAwait(false);

        var projection = new WatchlistProjectionState
        {
            PlaylistId = playlistId,
            LastNameWritten = name,
        };

        if (!target.Remember(projection))
        {
            // The document was readable when this pass started and is not now. The
            // playlist exists and nothing records it, which is the one outcome worth
            // a loud line: a later pass makes a second one and only this line says
            // where the first came from.
            _logger.LogError(
                "Created playlist {PlaylistId} for user {UserId} and could not write it into their document, so it is not recorded anywhere",
                playlistId,
                target.OwnerUserId);

            return ProjectionResult.RefusedRecordUnavailable();
        }

        _logger.LogInformation(
            "Projecting the list of user {UserId} into playlist {PlaylistId}",
            target.OwnerUserId,
            playlistId);

        return ProjectionResult.Created(projection);
    }

    /// <summary>
    /// Brings the label of a playlist this plugin already has into step with the
    /// configured name, where the label is still the one this plugin wrote.
    /// </summary>
    /// <param name="target">The target.</param>
    /// <param name="remembered">What the record holds.</param>
    /// <param name="onTheServer">The playlist as the server holds it now.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The result of the pass.</returns>
    /// <remarks>
    /// <para>
    /// THE COMPARISON IS THE RULE AND AN INTENTION IS NOT. This plugin cannot ask a
    /// server whether a person typed a name, so the only honest question is whether the
    /// label is still the last one it set. Where it is not, the user named that playlist
    /// and this plugin never writes its name again.
    /// </para>
    /// <para>
    /// The record is written before the server is asked to rename. Both orders leave the
    /// same wrong state if the second half fails - a label and a record that disagree,
    /// which the next pass reads as a user-named list - and this one fails without having
    /// touched anything the user can see.
    /// </para>
    /// <para>
    /// A label that already reads as the configured name is left alone and the record is
    /// brought onto it. That is the user who renamed their playlist to exactly what the
    /// setting later became: indistinguishable from one who never renamed it, so this
    /// plugin manages the name again from then on. It is the rule getting a case wrong in
    /// the harmless direction, and it is cheaper to say so than to carry a value that
    /// tries to detect it.
    /// </para>
    /// </remarks>
    private async Task<ProjectionResult> KeepTheNameInStepAsync(
        IProjectionTarget target,
        WatchlistProjectionState remembered,
        ProjectedPlaylist onTheServer,
        CancellationToken cancellationToken)
    {
        var wanted = target.ConfiguredName;

        if (string.Equals(onTheServer.Name, remembered.LastNameWritten, StringComparison.Ordinal))
        {
            return await RenamedToAsync(target, remembered, wanted, cancellationToken).ConfigureAwait(false);
        }

        if (string.Equals(onTheServer.Name, wanted, StringComparison.Ordinal))
        {
            // The label already reads as the setting, so nothing is renamed and the
            // record is what moves. Without this the next setting change would find a
            // label this plugin does not recognise and leave it alone for ever.
            return Recorded(target, remembered with { LastNameWritten = wanted });
        }

        if (NotSaidYet("named-by-the-user", remembered.PlaylistId))
        {
            _logger.LogInformation(
                "Playlist {PlaylistId} of user {UserId} no longer carries the name this plugin wrote, so the name is the user's and this plugin will not write it again. Its contents are still reconciled",
                remembered.PlaylistId,
                target.OwnerUserId);
        }

        return ProjectionResult.AlreadyProjected(remembered);
    }

    /// <summary>
    /// Moves the record onto a name and then asks the server for it, doing neither where
    /// the name has not moved.
    /// </summary>
    /// <param name="target">The target.</param>
    /// <param name="remembered">What the record holds.</param>
    /// <param name="wanted">The configured name.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The result of the pass.</returns>
    private async Task<ProjectionResult> RenamedToAsync(
        IProjectionTarget target,
        WatchlistProjectionState remembered,
        string wanted,
        CancellationToken cancellationToken)
    {
        if (string.Equals(remembered.LastNameWritten, wanted, StringComparison.Ordinal))
        {
            return ProjectionResult.AlreadyProjected(remembered);
        }

        var renamed = remembered with { LastNameWritten = wanted };

        if (!target.Remember(renamed))
        {
            return ProjectionResult.RefusedRecordUnavailable();
        }

        await _playlists
            .RenameAsync(remembered.PlaylistId, target.OwnerUserId, wanted, cancellationToken)
            .ConfigureAwait(false);

        _logger.LogInformation(
            "Renamed playlist {PlaylistId} of user {UserId} to the configured name",
            remembered.PlaylistId,
            target.OwnerUserId);

        return ProjectionResult.Renamed(renamed);
    }

    /// <summary>
    /// Writes a record that carries no change to the server with it.
    /// </summary>
    /// <param name="target">The target.</param>
    /// <param name="projection">What the record should hold.</param>
    /// <returns>The result of the pass.</returns>
    private static ProjectionResult Recorded(IProjectionTarget target, WatchlistProjectionState projection) =>
        target.Remember(projection)
            ? ProjectionResult.AlreadyProjected(projection)
            : ProjectionResult.RefusedRecordUnavailable();

    /// <summary>
    /// Adopts a playlist a person made by hand: records it, and takes its rows onto the
    /// list.
    /// </summary>
    /// <param name="target">The target.</param>
    /// <param name="adopted">The playlist being adopted.</param>
    /// <returns>The result of the pass.</returns>
    /// <remarks>
    /// The record is written before the rows are read, for the reason the rename uses:
    /// a failure after the rows had been taken would leave the entries on the list and
    /// nothing pointing at the playlist they came from, and the next pass would take
    /// them again.
    ///
    /// The configured name is what is recorded as last written, and that is what makes
    /// an adopted list behave like one this plugin created: it matched that name at the
    /// moment it was adopted, so a later setting change renames it under #35's rule
    /// rather than being read as a name the user chose.
    /// </remarks>
    private ProjectionResult TakeOver(IProjectionTarget target, ProjectedPlaylist adopted)
    {
        var projection = new WatchlistProjectionState
        {
            PlaylistId = adopted.PlaylistId,
            LastNameWritten = target.ConfiguredName,
        };

        if (!target.Remember(projection))
        {
            return ProjectionResult.RefusedRecordUnavailable();
        }

        var rows = _playlists.EntriesOf(adopted.PlaylistId, target.OwnerUserId);
        var itemIds = new List<Guid>(rows.Count);

        foreach (var row in rows)
        {
            itemIds.Add(row.ItemId);
        }

        var taken = target.Adopt(itemIds);

        _logger.LogInformation(
            "Adopted playlist {PlaylistId} of user {UserId} as their projected list and took {Taken} of its {Offered} rows onto the list",
            adopted.PlaylistId,
            target.OwnerUserId,
            taken,
            itemIds.Count);

        return ProjectionResult.Adopted(projection);
    }

    /// <summary>
    /// The one playlist of this user carrying the configured name, or null where there
    /// is none and where there is more than one.
    /// </summary>
    /// <param name="target">The target.</param>
    /// <param name="owned">The playlists this user has.</param>
    /// <returns>The playlist to adopt, or null.</returns>
    /// <remarks>
    /// MORE THAN ONE MATCH ADOPTS NOTHING. Two lists with one name is a state the server
    /// produces on its own, and there is nothing in either of them that says which one
    /// the person meant. Guessing gets it right half the time and the half it gets wrong
    /// is somebody's list being managed without their asking, so this plugin makes its
    /// own instead and says why.
    /// </remarks>
    private ProjectedPlaylist? AdoptableFrom(IProjectionTarget target, IReadOnlyList<ProjectedPlaylist> owned)
    {
        var carryingTheName = owned
            .Where(playlist => string.Equals(playlist.Name, target.ConfiguredName, StringComparison.Ordinal))
            .ToList();

        if (carryingTheName.Count <= 1)
        {
            return carryingTheName.FirstOrDefault();
        }

        _logger.LogInformation(
            "User {UserId} has {Matches} playlists carrying the configured list name, so none is adopted and a new one is created instead",
            target.OwnerUserId,
            carryingTheName.Count);

        return null;
    }

    /// <summary>
    /// The playlist the server holds under this identifier, or null where it holds none.
    /// </summary>
    /// <param name="owned">The playlists this user has.</param>
    /// <param name="playlistId">The playlist the record remembers.</param>
    /// <returns>The playlist, or null.</returns>
    private static ProjectedPlaylist? Holding(IReadOnlyList<ProjectedPlaylist> owned, Guid playlistId)
    {
        return owned.FirstOrDefault(playlist => playlist.PlaylistId == playlistId);
    }

    /// <summary>
    /// Says once, for each other playlist of this user that carries the configured name,
    /// that the identifier and not the name decides which one is the projection.
    /// </summary>
    /// <param name="target">The target.</param>
    /// <param name="owned">The playlists this user has.</param>
    /// <param name="playlistId">The playlist that IS the projection.</param>
    /// <remarks>
    /// <para>
    /// The server resolves such a collision on the directory rather than on the name, so
    /// two playlists with one label is a thing that happens and not a thing to guard
    /// against. This is said so an operator can see why two rows read alike.
    /// </para>
    /// <para>
    /// THIS LOOP STAYS A LOOP ON PURPOSE, and cs/linq/missed-where is dismissed at this
    /// site rather than fixed. The third condition in its test is not a test: NotSaidYet
    /// records that the observation is being made, so it answers false the second time it
    /// is asked about the same playlist. Moving the condition into a Where would put that
    /// mutation inside a predicate, where the number of times it runs is a property of
    /// how the sequence is consumed rather than of what this method does, and a later
    /// reader adding a Count or a second enumeration would silence a report without
    /// touching a line that mentions it. The two neighbouring sites in this file were
    /// rewritten because their conditions only read.
    /// </para>
    /// </remarks>
    private void ReportACollision(IProjectionTarget target, IReadOnlyList<ProjectedPlaylist> owned, Guid playlistId)
    {
        foreach (var playlist in owned)
        {
            if (playlist.PlaylistId != playlistId
                && string.Equals(playlist.Name, target.ConfiguredName, StringComparison.Ordinal)
                && NotSaidYet("name-collision", playlist.PlaylistId))
            {
                _logger.LogInformation(
                    "Playlist {PlaylistId} of user {UserId} carries the configured list name and is not the projected list, which is {ProjectedPlaylistId}. The identifier decides and nothing is renamed",
                    playlist.PlaylistId,
                    target.OwnerUserId,
                    playlistId);
            }
        }
    }

    /// <summary>
    /// Whether this observation about this playlist has not been made yet, and records
    /// that it is being made now.
    /// </summary>
    /// <param name="kind">Which observation this is, so two about one playlist are two.</param>
    /// <param name="playlistId">The playlist the observation is about.</param>
    /// <returns>True the first time, false afterwards.</returns>
    /// <remarks>
    /// ONCE PER PROCESS, AND A RESTART SAYS IT AGAIN. What is reported is a standing
    /// state of the server rather than an event, so a pass finding it again has nothing
    /// new to say and a line per user per pass is a log nobody reads. The alternative is
    /// a value in every user's document whose only purpose is to keep a log quiet, and
    /// that costs a schema version on every installed server. The bound is stated rather
    /// than hidden: this suppresses repetition, it does not promise a line was written
    /// exactly once in the life of a server.
    /// </remarks>
    private bool NotSaidYet(string kind, Guid playlistId) => _said.TryAdd(
        kind + " " + playlistId.ToString("N", CultureInfo.InvariantCulture),
        0);
}
