using System;
using System.Collections.Generic;
using System.Linq;
using Jellyfin.Plugin.Watchlist.Api;
using Jellyfin.Plugin.Watchlist.Configuration;
using Jellyfin.Plugin.Watchlist.Store;

namespace Jellyfin.Plugin.Watchlist.Projection;

/// <summary>
/// The one list the whole server can see, as a projection target: a playlist owned by
/// the administrator who made the list, readable by everybody, remembered in the shared
/// record.
/// </summary>
/// <remarks>
/// <para>
/// It is written beside <see cref="UserProjectionTarget"/> rather than by widening it,
/// which is what the interface was shaped for. The owner and the record are the same
/// user on a private list and different things here, what an entry carries is not the
/// same on both, and who may take one off differs - and none of those differences reach
/// the projector or the reconciler.
/// </para>
/// <para>
/// WHOSE EYES DECIDE WHAT GOES ON IT. A playlist holds the same rows for everybody who
/// can see it, so an item only some users may open is a case this plugin has to answer
/// rather than the server. The answer is the OWNER'S: an entry is projected when it
/// resolves for the administrator whose list it is, and a user who cannot open that item
/// sees the row and is refused when they play it, by the server, exactly as they would
/// be anywhere else.
/// </para>
/// <para>
/// The alternative - carrying only items EVERY user can see - was not taken, and the
/// reason is that it cannot be asked here. It is one question per item per user, its
/// answer changes when a user is added, and this plugin has no list of the server's
/// users to ask it of: the pass walks the documents the store holds, which is a
/// different population. It would also make one person's library permissions silently
/// empty a list an administrator curated for everybody.
/// </para>
/// <para>
/// SO THE COST IS DISCLOSED RATHER THAN AVOIDED. A shared list can show a user the name
/// of something they may not play. That is what a list the whole server can see is, it
/// is why the setting is off until an administrator turns it on, and docs/personal-data.md
/// says it where a reader of that page will meet it.
/// </para>
/// <para>
/// The record is read once, when the target is made, so one pass sees one state of it,
/// and the write goes straight back to the store under the same gate as every other
/// writer.
/// </para>
/// </remarks>
public sealed class SharedProjectionTarget : IProjectionTarget
{
    private readonly WatchlistDocumentStore _store;
    private readonly PluginConfiguration _configuration;
    private readonly IWatchlistItemDescriber _describer;
    private readonly ISeriesEpisodes _episodes;
    private readonly TimeProvider _clock;

    private SharedProjectionTarget(
        WatchlistDocumentStore store,
        PluginConfiguration configuration,
        IWatchlistItemDescriber describer,
        ISeriesEpisodes episodes,
        TimeProvider clock,
        Guid ownerUserId,
        bool isRecordAvailable,
        WatchlistProjectionState? remembered,
        IReadOnlyList<Guid> wanted)
    {
        _store = store;
        _configuration = configuration;
        _describer = describer;
        _episodes = episodes;
        _clock = clock;
        OwnerUserId = ownerUserId;
        IsRecordAvailable = isRecordAvailable;
        Remembered = remembered;
        Wanted = wanted;
    }

    /// <inheritdoc />
    /// <remarks>
    /// The administrator who made the list, taken from the record rather than from
    /// whoever is an administrator today. A server whose administrator account is
    /// replaced still has the list, and the playlist the server made belongs to the
    /// account it was made for.
    /// </remarks>
    public Guid OwnerUserId { get; }

    /// <inheritdoc />
    public string ConfiguredName => _configuration.SharedListName;

    /// <inheritdoc />
    public IReadOnlyList<Guid> Wanted { get; }

    /// <inheritdoc />
    /// <remarks>
    /// Always, and by open access rather than by a share per user. The server keeps both
    /// mechanisms and the choice is this: a share list is a second population to hold in
    /// step with the server's own, so a user created while the plugin was off would
    /// silently not see the list, and one deleted would leave a row behind. Open access
    /// is one value, it is what the server already means by everybody, and there is
    /// nothing to drift.
    ///
    /// What it costs is that the list is visible to every user the server has, including
    /// one an administrator might not have thought of. That is what a list the whole
    /// server can see is; the switch that turns it on is off by default for exactly that
    /// reason.
    /// </remarks>
    public bool IsOpenToEveryone => true;

    /// <inheritdoc />
    public bool IsRecordAvailable { get; }

    /// <inheritdoc />
    public WatchlistProjectionState? Remembered { get; }

    /// <summary>
    /// Reads the shared record and presents it as a target for one pass, or answers null
    /// where this server has no shared list to project.
    /// </summary>
    /// <param name="store">The store.</param>
    /// <param name="configuration">The server's settings, which carry this list's name
    /// and the bound it is added under.</param>
    /// <param name="describer">What a library item is; asked for the owner.</param>
    /// <param name="episodes">What a series holds; asked for the owner.</param>
    /// <param name="clock">The clock an adopted entry is stamped from.</param>
    /// <returns>The target, or null where there is nothing to project.</returns>
    /// <exception cref="ArgumentNullException">Any argument is null.</exception>
    /// <remarks>
    /// NULL IS THE ANSWER FOR A SERVER THAT HAS NO SHARED LIST, and it is what makes the
    /// condition about such a server costing no write true by construction rather than by
    /// a flag somebody has to remember. There is no target, so nothing is asked to make a
    /// playlist. Two cases produce it: the setting is off, and there is no record.
    ///
    /// A record this build cannot read is NOT null. It is a target whose record is
    /// unavailable, which the projector already refuses without making anything - the
    /// same answer a user whose document cannot be read gets, and a different one from a
    /// server that never had a list.
    /// </remarks>
    public static SharedProjectionTarget? For(
        WatchlistDocumentStore store,
        PluginConfiguration configuration,
        IWatchlistItemDescriber describer,
        ISeriesEpisodes episodes,
        TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(describer);
        ArgumentNullException.ThrowIfNull(episodes);
        ArgumentNullException.ThrowIfNull(clock);

        if (!configuration.SharedListEnabled)
        {
            return null;
        }

        var read = store.ReadShared();

        // THE TWO WAYS OF HAVING NO DOCUMENT ARE NOT THE SAME ANSWER, and asking whether
        // the read is AVAILABLE cannot tell them apart: a record that is not there and
        // one this build refuses to read are both unavailable. What separates them is
        // whether the file exists. Nothing there is a server with no shared list, which
        // is null; a file this build will not read is a target whose record is
        // unavailable, which the projector refuses without making a playlist and which
        // the run counts as stepped over rather than as nothing to do.
        if (!read.Exists)
        {
            return null;
        }

        return FromRead(store, configuration, describer, episodes, clock, read, Guid.Empty);
    }

    /// <inheritdoc />
    public bool Remember(WatchlistProjectionState projection) => _store.SetSharedProjection(projection);

    /// <inheritdoc />
    /// <remarks>
    /// It never answers null, and <see cref="For"/> does. The difference is what the two
    /// are asked: whether this server HAS a shared list to project, which is a question
    /// with an answer of no, and what the list says NOW, which is asked only where the
    /// first answer was yes. A record removed between the two reads comes back here as a
    /// target with nothing on it whose record could not be read, and the projector
    /// refuses that without making anything.
    /// </remarks>
    public IProjectionTarget Reread() =>
        FromRead(_store, _configuration, _describer, _episodes, _clock, _store.ReadShared(), OwnerUserId);

    /// <summary>
    /// One target out of one reading of the record.
    /// </summary>
    /// <param name="store">The store.</param>
    /// <param name="configuration">The server's settings.</param>
    /// <param name="describer">What a library item is, for the owner.</param>
    /// <param name="episodes">What a series holds, for the owner.</param>
    /// <param name="clock">The clock an adopted entry is stamped from.</param>
    /// <param name="read">What the store answered.</param>
    /// <param name="ownerWhereThereIsNoRecord">The owner to keep where the record could
    /// not be read, which is the one this target already had.</param>
    /// <returns>The target.</returns>
    /// <remarks>
    /// ONE PLACE READS THE ENTRIES AND IT IS THIS ONE, which is why both constructions
    /// come through here rather than each taking the entries off the record. A second
    /// site reading them is a second place the gate could be forgotten, and the register
    /// that judges this file counts the reads against the calls for exactly that reason.
    ///
    /// A record that is unavailable or absent makes a target with nothing on it whose
    /// record is unavailable, which the projector refuses without making anything. The
    /// absent case is only reachable from a re-read, because a caller asking whether there
    /// is a list at all is answered with null instead.
    /// </remarks>
    private static SharedProjectionTarget FromRead(
        WatchlistDocumentStore store,
        PluginConfiguration configuration,
        IWatchlistItemDescriber describer,
        ISeriesEpisodes episodes,
        TimeProvider clock,
        SharedWatchlistReadResult read,
        Guid ownerWhereThereIsNoRecord)
    {
        var owner = read.Document?.OwnerUserId ?? ownerWhereThereIsNoRecord;

        return new SharedProjectionTarget(
            store,
            configuration,
            describer,
            episodes,
            clock,
            owner,
            read.IsAvailable,
            read.Document?.Projection,
            WantedFrom(read.Document?.Entries ?? [], describer, episodes, owner));
    }

    /// <inheritdoc />
    /// <remarks>
    /// An entry adopted here is attributed to the OWNER, and that is a reading of who
    /// could have made the edit rather than a default. The plugin gives nobody permission
    /// to edit the shared playlist, so the only person who can put a row in it is the
    /// user it belongs to; attributing it to anybody else would be a guess, and leaving
    /// it unattributed would break answer 8 on #1, which is that every entry on this list
    /// carries and shows who put it there.
    ///
    /// Every row goes through the same rules an add through the endpoint goes through:
    /// the library is asked what the item is for the owner, a kind outside the accepted
    /// set is left off, and the list's bound is honoured.
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

            var result = _store.AddShared(
                new WatchlistEntry
                {
                    ItemId = itemId,
                    Kind = described.Kind,
                    AddedAt = _clock.GetUtcNow(),
                    Source = WatchlistEntrySource.PlaylistEdit,
                    AddedBy = OwnerUserId,
                },
                _configuration.MaxEntriesInSharedList);

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
    /// THE REMOVAL HALF IS NARROWER HERE THAN ON A PRIVATE LIST, DELIBERATELY. A row this
    /// plugin wrote and that is gone takes an entry off only where the OWNER put that
    /// entry there. Answer 7 on #1 would allow an administrator to take any entry off,
    /// and this route does not use that: a playlist is not an authorisation surface, the
    /// server answers no question about who edited it, and the record of who may remove
    /// what is only worth anything where the caller is known. The endpoints are where an
    /// administrator removes somebody else's entry, with the server's own answer about
    /// them in hand.
    /// </para>
    /// <para>
    /// The visible cost, so it is not discovered: an administrator who takes somebody
    /// else's row out of the shared playlist on a client finds it back after the next
    /// pass. The condition this issue carries is that the rule is never WIDER than answer
    /// 7, and being narrower is what that leaves.
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
            if (held.Contains(itemId))
            {
                continue;
            }

            var result = _store.RemoveShared(itemId, OwnerUserId, callerMayRemoveAnyEntry: false);

            if (result.Outcome == SharedWatchlistRemoveOutcome.Removed)
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
    /// Turns the shared entries into the rows the playlist should hold, in order.
    /// </summary>
    /// <param name="entries">The entries as the record holds them.</param>
    /// <param name="describer">What a library item is, for the owner.</param>
    /// <param name="episodes">What a series holds, for the owner.</param>
    /// <param name="ownerUserId">The owner, who is whose eyes decide.</param>
    /// <returns>The items to project, newest addition first.</returns>
    /// <remarks>
    /// The same three rules the private list follows, through the same functions: the
    /// gate that skips an entry whose item does not resolve, the series rule that turns a
    /// show into one episode, and the order that puts the newest addition at the head. A
    /// second spelling of any of them here would be the second copy the whole shape of
    /// the projection exists to avoid.
    /// </remarks>
    private static List<Guid> WantedFrom(
        IReadOnlyList<WatchlistEntry> entries,
        IWatchlistItemDescriber describer,
        ISeriesEpisodes episodes,
        Guid ownerUserId)
    {
        var rows = new List<Guid>();
        var held = new HashSet<Guid>();

        var ordered = WatchlistVisibility
            .Resolvable(entries, new WhatTheOwnerMayBeTold(describer, ownerUserId), ownerUserId)
            .OrderByDescending(entry => entry.AddedAt)
            .ThenBy(entry => entry.ItemId);

        foreach (var row in ordered.Select(entry => RowFor(entry, episodes, ownerUserId)))
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
    /// <param name="entry">The entry as the record holds it.</param>
    /// <param name="episodes">What a series holds, for the owner.</param>
    /// <param name="ownerUserId">The owner.</param>
    /// <returns>The library item to put in the playlist, or null.</returns>
    private static Guid? RowFor(WatchlistEntry entry, ISeriesEpisodes episodes, Guid ownerUserId) =>
        entry.Kind switch
        {
            WatchlistItemKind.Movie or WatchlistItemKind.Episode => entry.ItemId,
            WatchlistItemKind.Series => SeriesRow.OneEpisodeOf(episodes.Of(entry.ItemId, ownerUserId)),
            _ => null,
        };

    /// <summary>
    /// The question the gate is handed: whether this item resolves for the owner.
    /// </summary>
    /// <remarks>
    /// The owner rather than the reader, which is the decision this class is built on and
    /// is argued at the top of it. The gate itself is the one on the private path, so the
    /// rule for an entry whose item does not resolve is written once for both lists.
    /// </remarks>
    private sealed class WhatTheOwnerMayBeTold : IWatchlistItemResolver
    {
        private readonly IWatchlistItemDescriber _describer;
        private readonly Guid _ownerUserId;

        public WhatTheOwnerMayBeTold(IWatchlistItemDescriber describer, Guid ownerUserId)
        {
            _describer = describer;
            _ownerUserId = ownerUserId;
        }

        public bool Exists(Guid itemId) => _describer.Describe(itemId, _ownerUserId) is not null;
    }
}
