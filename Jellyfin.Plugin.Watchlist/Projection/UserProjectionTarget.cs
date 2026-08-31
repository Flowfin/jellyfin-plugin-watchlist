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
    private readonly TimeProvider _clock;

    private UserProjectionTarget(
        WatchlistDocumentStore store,
        PluginConfiguration configuration,
        IWatchlistItemDescriber describer,
        TimeProvider clock,
        Guid userId,
        bool isRecordAvailable,
        WatchlistProjectionState? remembered,
        IReadOnlyList<Guid> wanted)
    {
        _store = store;
        _configuration = configuration;
        _describer = describer;
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
    /// A KIND A PLAYLIST CANNOT HOLD IS LEFT OUT, AND THAT IS TODAY'S ANSWER RATHER
    /// THAN THE INTENDED ONE. A series is on the accepted set of the store and cannot
    /// be a playlist row: a server handed a folder adds its non-folder children, so an
    /// entry for a show would become every episode of it. What a show should project as
    /// is one episode, and that rule is issue #18 and is not in this tree. Until it
    /// lands a show sits on the list, is served by the endpoints, and appears in no
    /// playlist. That is a gap and it is stated here rather than hidden behind a
    /// projection that puts a whole show into a list.
    /// </para>
    /// </remarks>
    public IReadOnlyList<Guid> Wanted { get; }

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
        TimeProvider clock,
        Guid userId)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(describer);
        ArgumentNullException.ThrowIfNull(clock);

        var read = store.Read(userId);

        return new UserProjectionTarget(
            store,
            configuration,
            describer,
            clock,
            userId,
            read.IsAvailable,
            read.Document?.Projection,
            WantedFrom(read.Document?.Entries ?? [], describer, userId));
    }

    /// <summary>
    /// Turns one user's entries into the rows their playlist should hold, in order.
    /// </summary>
    /// <param name="entries">The entries as the document holds them.</param>
    /// <param name="describer">What a library item is, for this user.</param>
    /// <param name="userId">The user the question is asked for.</param>
    /// <returns>The items to project, newest addition first.</returns>
    private static List<Guid> WantedFrom(
        IReadOnlyList<WatchlistEntry> entries,
        IWatchlistItemDescriber describer,
        Guid userId) => WatchlistVisibility
            .Resolvable(entries, new WhatThisUserMayBeTold(describer, userId), userId)
            .Where(entry => ProjectsAsOneRow(entry.Kind))
            .OrderByDescending(entry => entry.AddedAt)
            .ThenBy(entry => entry.ItemId)
            .Select(entry => entry.ItemId)
            .Distinct()
            .ToList();

    /// <summary>
    /// Whether an entry of this kind is one playlist row.
    /// </summary>
    /// <param name="kind">The kind the entry was recorded under.</param>
    /// <returns>True where the item can be a row as it stands.</returns>
    /// <remarks>
    /// A film and an episode are single items and are rows. A show is not, and what it
    /// becomes is #18. Anything else never reached the list: the accepted set the
    /// endpoints enforce holds these three and nothing more, so an entry of another
    /// kind is one written by a plugin that is not this one.
    /// </remarks>
    private static bool ProjectsAsOneRow(WatchlistItemKind kind) =>
        kind is WatchlistItemKind.Movie or WatchlistItemKind.Episode;

    /// <inheritdoc />
    public bool Remember(WatchlistProjectionState projection) =>
        _store.SetProjection(OwnerUserId, projection);

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
