using System;
using System.Collections.Generic;
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
        WatchlistProjectionState? remembered)
    {
        _store = store;
        _configuration = configuration;
        _describer = describer;
        _clock = clock;
        OwnerUserId = userId;
        IsRecordAvailable = isRecordAvailable;
        Remembered = remembered;
    }

    /// <inheritdoc />
    public Guid OwnerUserId { get; }

    /// <inheritdoc />
    public string ConfiguredName => _configuration.ProjectedListName;

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
            read.Document?.Projection);
    }

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
}
