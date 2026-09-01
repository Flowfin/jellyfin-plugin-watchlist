using System;
using System.Collections.Generic;
using System.Linq;
using Jellyfin.Plugin.Watchlist.Api;
using Jellyfin.Plugin.Watchlist.Configuration;
using Jellyfin.Plugin.Watchlist.Store;

namespace Jellyfin.Plugin.Watchlist.Projection;

/// <summary>
/// One user's own list as a projection target: their playlist, under the configured
/// name, remembered in their own document.
/// </summary>
/// <remarks>
/// <para>
/// The owner and the document are the same user here, and that is what makes this the
/// private list rather than a rule the projector carries. The shared list is a target
/// whose owner and whose record are different things, and it is written beside this
/// one rather than by widening it.
/// </para>
/// <para>
/// The document is read once, when the target is made, so one pass sees one state of
/// it. The write goes straight back to the store, under the same gate as every other
/// writer, so a playlist recorded while an entry is being added loses neither.
/// </para>
/// <para>
/// The configuration is taken whole rather than as the two values read out of it. Which
/// of its names belongs to this list is a fact about this list, and a caller handing the
/// name in is a caller that can hand in the shared list's by mistake.
/// </para>
/// </remarks>
public sealed class UserProjectionTarget : IProjectionTarget
{
    private readonly WatchlistDocumentStore _store;
    private readonly PluginConfiguration _configuration;
    private readonly IWatchlistItemDescriber _describer;
    private readonly ISeriesEpisodes _episodes;
    private readonly TimeProvider _clock;

    private UserProjectionTarget(
        WatchlistDocumentStore store,
        PluginConfiguration configuration,
        IWatchlistItemDescriber describer,
        ISeriesEpisodes episodes,
        TimeProvider clock,
        Guid userId,
        bool isRecordAvailable,
        WatchlistProjectionState? remembered,
        IReadOnlyList<Guid> wanted)
    {
        _store = store;
        _configuration = configuration;
        _describer = describer;
        _episodes = episodes;
        _clock = clock;
        OwnerUserId = userId;
        IsRecordAvailable = isRecordAvailable;
        Remembered = remembered;
        Wanted = wanted;
    }

    /// <inheritdoc />
    public Guid OwnerUserId { get; }

    /// <inheritdoc />
    public string ConfiguredName => _configuration.ProjectedListName;

    /// <inheritdoc />
    /// <remarks>
    /// <para>
    /// Two rules make this set out of the document, and both of them are this target's
    /// rather than the reconciler's.
    /// </para>
    /// <para>
    /// THE ITEM IS ASKED ABOUT FOR THIS USER, and an entry the describer says nothing
    /// about is left out. That is one answer for an item that has been deleted and for
    /// an item this user may not see - a rating they are above, a library they were
    /// never given - because the describer gives one answer for both on purpose. So an
    /// item a user cannot see is not merely hidden from their playlist, it is never
    /// added to it.
    /// </para>
    /// <para>
    /// A SHOW IS ONE ROW AND NOT EVERY EPISODE OF IT. A series is on the accepted set
    /// of the store and cannot be a playlist row: a server handed a folder adds its
    /// non-folder children. So a series entry is turned into a single episode before it
    /// reaches the set, and which episode that is, and why, is
    /// <see cref="SeriesRow"/>. A show the library holds no episode of for this user
    /// contributes nothing, which is the only case where a show on the list appears in
    /// no playlist.
    /// </para>
    /// </remarks>
    public IReadOnlyList<Guid> Wanted { get; }

    /// <inheritdoc />
    /// <remarks>
    /// Never. A private watchlist is one person's, and the playlist it is projected into
    /// is theirs; the plugin makes it for them and asks the server to show it to nobody
    /// else. The list that everybody sees is a list of its own.
    /// </remarks>
    public bool IsOpenToEveryone => false;

    /// <inheritdoc />
    public bool IsRecordAvailable { get; }

    /// <inheritdoc />
    public WatchlistProjectionState? Remembered { get; }

    /// <summary>
    /// Reads one user's document and presents it as a target for one pass.
    /// </summary>
    /// <param name="store">The store.</param>
    /// <param name="configuration">The server's settings, which carry this list's name
    /// and the bound it is added under.</param>
    /// <param name="describer">What a library item is, for this user.</param>
    /// <param name="episodes">What a series holds, for this user, which is what the one
    /// row a show projects as is chosen out of.</param>
    /// <param name="clock">The clock an adopted entry is stamped from.</param>
    /// <param name="userId">The user, who is both the owner of the playlist and the
    /// holder of the record.</param>
    /// <returns>The target.</returns>
    /// <exception cref="ArgumentNullException">Any argument is null.</exception>
    /// <remarks>
    /// A user with no document at all is a target with nothing remembered rather than
    /// an unavailable one: a read of a file that is not there is an empty list, and a
    /// user who has never used the plugin is exactly that.
    /// </remarks>
    public static UserProjectionTarget For(
        WatchlistDocumentStore store,
        PluginConfiguration configuration,
        IWatchlistItemDescriber describer,
        ISeriesEpisodes episodes,
        TimeProvider clock,
        Guid userId)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(describer);
        ArgumentNullException.ThrowIfNull(episodes);
        ArgumentNullException.ThrowIfNull(clock);

        var read = store.Read(userId);

        return new UserProjectionTarget(
            store,
            configuration,
            describer,
            episodes,
            clock,
            userId,
            read.IsAvailable,
            read.Document?.Projection,
            WantedFrom(read.Document?.Entries ?? [], describer, episodes, userId));
    }

    /// <summary>
    /// Turns one user's entries into the rows their playlist should hold, in order.
    /// </summary>
    /// <param name="entries">The entries as the document holds them.</param>
    /// <param name="describer">What a library item is, for this user.</param>
    /// <param name="episodes">What a series holds, for this user.</param>
    /// <param name="userId">The user the question is asked for.</param>
    /// <returns>The items to project, newest addition first.</returns>
    /// <remarks>
    /// THE ORDER IS DECIDED OVER THE ENTRIES AND THE ROW IS CHOSEN AFTERWARDS, so a
    /// show sits where the user put the show rather than where the episode it happens
    /// to point at today would fall. The duplicate check moved with it, onto the rows,
    /// because two entries can now ask for one row: a user holding a series and also
    /// the very episode it projects as names the same item twice, and the earlier of
    /// the two in the order is the one that stands.
    /// </remarks>
    private static List<Guid> WantedFrom(
        IReadOnlyList<WatchlistEntry> entries,
        IWatchlistItemDescriber describer,
        ISeriesEpisodes episodes,
        Guid userId)
    {
        var rows = new List<Guid>();
        var held = new HashSet<Guid>();

        var ordered = WatchlistVisibility
            .Resolvable(entries, new WhatThisUserMayBeTold(describer, userId), userId)
            .OrderByDescending(entry => entry.AddedAt)
            .ThenBy(entry => entry.ItemId);

        foreach (var row in ordered.Select(entry => RowFor(entry, episodes, userId)))
        {
            if (row is not null && held.Add(row.Value))
            {
                rows.Add(row.Value);
            }
        }

        return rows;
    }

    /// <summary>
    /// The one playlist row an entry becomes, or null where it becomes none.
    /// </summary>
    /// <param name="entry">The entry as the document holds it.</param>
    /// <param name="episodes">What a series holds, for this user.</param>
    /// <param name="userId">The user the question is asked for.</param>
    /// <returns>The library item to put in the playlist, or null.</returns>
    /// <remarks>
    /// A film and an episode are single items and are the row they name. A show is not,
    /// and <see cref="SeriesRow"/> is where the episode it appears as is chosen and
    /// where the reason for choosing that one is written.
    ///
    /// A show the library holds no episode of for this user is null here and no row.
    /// That is a show sitting on the list, served by the endpoints and absent from the
    /// playlist, in the one case where there is nothing to put there; it is this
    /// issue's own condition rather than the gap that stood here before, which left
    /// every show out whatever the library held.
    ///
    /// Anything else never reached the list: the accepted set the endpoints enforce
    /// holds these three kinds and nothing more, so an entry of another kind is one
    /// written by a plugin that is not this one.
    /// </remarks>
    private static Guid? RowFor(WatchlistEntry entry, ISeriesEpisodes episodes, Guid userId) =>
        entry.Kind switch
        {
            WatchlistItemKind.Movie or WatchlistItemKind.Episode => entry.ItemId,
            WatchlistItemKind.Series => SeriesRow.OneEpisodeOf(episodes.Of(entry.ItemId, userId)),
            _ => null,
        };

    /// <inheritdoc />
    public bool Remember(WatchlistProjectionState projection) =>
        _store.SetProjection(OwnerUserId, projection);

    /// <inheritdoc />
    public IProjectionTarget Reread() =>
        For(_store, _configuration, _describer, _episodes, _clock, OwnerUserId);

    /// <inheritdoc />
    /// <remarks>
    /// Every row goes through the same rules an add through the endpoint goes through:
    /// the library is asked what the item is FOR THIS USER, a kind outside the accepted
    /// set is left off, and the list's bound is honoured. A playlist a person made by
    /// hand can hold a music track, a photograph or an item they no longer have access
    /// to, and adoption is not a way past the rules that refuse those.
    ///
    /// An entry adopted this way is recorded as having arrived from a playlist edit,
    /// because that is what it is: a row somebody put on a playlist, taken into the
    /// store. The instant is the one the clock answers, not the playlist's own, which
    /// the server does not keep per row.
    /// </remarks>
    public int Adopt(IReadOnlyList<Guid> itemIds)
    {
        ArgumentNullException.ThrowIfNull(itemIds);

        var taken = 0;

        foreach (var itemId in itemIds)
        {
            var described = _describer.Describe(itemId, OwnerUserId);

            if (described is null || !AcceptedWatchlistItemKinds.Accepts(described.Kind))
            {
                continue;
            }

            var result = _store.Add(
                OwnerUserId,
                new WatchlistEntry
                {
                    ItemId = itemId,
                    Kind = described.Kind,
                    AddedAt = _clock.GetUtcNow(),
                    Source = WatchlistEntrySource.PlaylistEdit,
                },
                _configuration.MaxEntriesPerUser);

            if (result.Outcome == WatchlistAddOutcome.Added)
            {
                taken++;
            }
        }

        return taken;
    }

    /// <inheritdoc />
    /// <remarks>
    /// <para>
    /// The removal half runs first, and it goes through the store's own removal rather
    /// than through anything of its own, so an entry taken off from a client leaves by
    /// exactly the route an entry taken off through the endpoint leaves by.
    /// </para>
    /// <para>
    /// A REMOVAL THAT NAMES NO ENTRY CHANGES NOTHING, AND THAT IS THE SHOW CASE. A
    /// series projects as one episode, so the row in the playlist carries the episode's
    /// identifier and the list holds the show's. Somebody taking that row away is
    /// removing an episode this list has no entry for, the store finds nothing to
    /// remove, and the show stays. That is the answer rather than an oversight: what
    /// they put on the list was the show, taking an episode out of a playlist is not a
    /// statement about the show, and the next pass puts the row back - re-pointed at the
    /// same episode, because playing is what moves it and this was not that.
    /// </para>
    /// <para>
    /// The addition half is <see cref="Adopt"/>, which is the same route a first-pass
    /// adoption takes, so a row somebody added on a client goes through the accepted-kind
    /// rule and the list's bound exactly as one adopted from a playlist that was taken
    /// over does.
    /// </para>
    /// <para>
    /// EVERY ROW IS OFFERED TO IT, INCLUDING THE ONES THE PROJECTOR WROTE, and that is
    /// deliberate rather than sloppy. A row the projector wrote is already an entry, so
    /// the store refuses it as a duplicate and the count comes back as nothing. Filtering
    /// them out first was written and then taken away again: it changed no outcome any
    /// test could see, which makes it a second copy of the store's duplicate rule living
    /// where a reader would take it for a rule of its own.
    /// </para>
    /// </remarks>
    public PlaylistEditsTaken TakeEdits(IReadOnlyList<Guid> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);

        var projected = Remembered?.ProjectedItemIds ?? [];
        var held = rows.ToHashSet();
        var removed = 0;

        foreach (var itemId in projected)
        {
            if (!held.Contains(itemId) && _store.Remove(OwnerUserId, itemId).Removed)
            {
                removed++;
            }
        }

        return new PlaylistEditsTaken
        {
            Added = Adopt(rows),
            Removed = removed,
        };
    }

    /// <summary>
    /// The question the gate above is handed: whether this item resolves FOR THIS USER.
    /// </summary>
    /// <remarks>
    /// The rule for an entry whose item does not resolve - skipped on read, left in the
    /// document, never dropped by one caller and kept by another - is written once, in
    /// <see cref="WatchlistVisibility.Resolvable"/>, and docs/unresolvable-entries.md
    /// names the projection as one of its readers. What sits here is the resolver that
    /// gate is given and not a second copy of the rule: an item deleted from the library
    /// and an item this user may no longer see are one answer from the describer, so
    /// asking it is asking both questions at once and neither can be told from the other
    /// afterwards.
    ///
    /// It is a shape of its own rather than a lambda so the sentence above has somewhere
    /// to live. It caches nothing, because a target is made for one pass and each entry
    /// is asked about once inside it.
    /// </remarks>
    private sealed class WhatThisUserMayBeTold : IWatchlistItemResolver
    {
        private readonly IWatchlistItemDescriber _describer;
        private readonly Guid _userId;

        public WhatThisUserMayBeTold(IWatchlistItemDescriber describer, Guid userId)
        {
            _describer = describer;
            _userId = userId;
        }

        public bool Exists(Guid itemId) => _describer.Describe(itemId, _userId) is not null;
    }
}
