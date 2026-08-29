using System;
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
/// </remarks>
public sealed class UserProjectionTarget : IProjectionTarget
{
    private readonly WatchlistDocumentStore _store;

    private UserProjectionTarget(
        WatchlistDocumentStore store,
        Guid userId,
        string configuredName,
        bool isRecordAvailable,
        WatchlistProjectionState? remembered)
    {
        _store = store;
        OwnerUserId = userId;
        ConfiguredName = configuredName;
        IsRecordAvailable = isRecordAvailable;
        Remembered = remembered;
    }

    /// <inheritdoc />
    public Guid OwnerUserId { get; }

    /// <inheritdoc />
    public string ConfiguredName { get; }

    /// <inheritdoc />
    public bool IsRecordAvailable { get; }

    /// <inheritdoc />
    public WatchlistProjectionState? Remembered { get; }

    /// <summary>
    /// Reads one user's document and presents it as a target for one pass.
    /// </summary>
    /// <param name="store">The store.</param>
    /// <param name="userId">The user, who is both the owner of the playlist and the
    /// holder of the record.</param>
    /// <param name="configuredName">The name a playlist made for them is created under.</param>
    /// <returns>The target.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="store"/> is null.</exception>
    /// <remarks>
    /// A user with no document at all is a target with nothing remembered rather than
    /// an unavailable one: a read of a file that is not there is an empty list, and a
    /// user who has never used the plugin is exactly that.
    /// </remarks>
    public static UserProjectionTarget For(WatchlistDocumentStore store, Guid userId, string configuredName)
    {
        ArgumentNullException.ThrowIfNull(store);

        var read = store.Read(userId);

        return new UserProjectionTarget(
            store,
            userId,
            configuredName,
            read.IsAvailable,
            read.Document?.Projection);
    }

    /// <inheritdoc />
    public bool Remember(WatchlistProjectionState projection) =>
        _store.SetProjection(OwnerUserId, projection);
}
