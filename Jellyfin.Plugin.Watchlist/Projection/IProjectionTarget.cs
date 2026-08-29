using System;
using Jellyfin.Plugin.Watchlist.Store;

namespace Jellyfin.Plugin.Watchlist.Projection;

/// <summary>
/// One thing a list is projected into a playlist for: whose playlist it is, what it
/// is called, and where the identity of that playlist is remembered between passes.
/// </summary>
/// <remarks>
/// <para>
/// This exists so the projector is written once. A user's own list and the shared
/// list differ in whose playlist is made and in which record remembers it, and in
/// nothing the projection itself does, so those two differences are the whole of this
/// interface and the projector below it knows about neither.
/// </para>
/// <para>
/// Nothing here says the target belongs to whoever asked. The owner is a value on the
/// target rather than a caller's identity, so a list several users can see is a target
/// like any other rather than a second projector.
/// </para>
/// <para>
/// The read is a snapshot taken when the target is made for one pass, and the write
/// goes through the record. A target that re-read on every access would let one pass
/// see two states of the same document.
/// </para>
/// </remarks>
public interface IProjectionTarget
{
    /// <summary>
    /// Gets the user the playlist belongs to, which is who the server makes it for and
    /// who every later operation on it is made as.
    /// </summary>
    Guid OwnerUserId { get; }

    /// <summary>
    /// Gets the name a playlist made for this target is created under.
    /// </summary>
    string ConfiguredName { get; }

    /// <summary>
    /// Gets a value indicating whether the record that remembers this target's
    /// playlist could be read at all.
    /// </summary>
    /// <remarks>
    /// False is a document this build refuses, and it is not the same as a target with
    /// no playlist yet. A projector that could not tell them apart would make a second
    /// playlist for every user whose document it cannot read, once per pass.
    /// </remarks>
    bool IsRecordAvailable { get; }

    /// <summary>
    /// Gets what is remembered about this target's playlist, or null where no playlist
    /// has been made for it.
    /// </summary>
    WatchlistProjectionState? Remembered { get; }

    /// <summary>
    /// Remembers a playlist for this target.
    /// </summary>
    /// <param name="projection">The playlist and the name it was written under.</param>
    /// <returns>False where the record could not be written.</returns>
    bool Remember(WatchlistProjectionState projection);
}
