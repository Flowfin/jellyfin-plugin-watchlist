using System;
using System.Collections.Generic;
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
    /// Gets the library items this list should be projected as, in the order they
    /// should appear in the playlist.
    /// </summary>
    /// <remarks>
    /// <para>
    /// THIS IS WHERE THE TWO KINDS OF LIST DIFFER AND THE RECONCILER DOES NOT. What a
    /// target puts here has already been through the rules that are ITS: which entries
    /// are on the list, who may see them, and what an entry of a kind a playlist cannot
    /// hold becomes. The reconciler takes the answer and computes a difference against
    /// the playlist, and it asks no question that could have a second answer on a
    /// shared list.
    /// </para>
    /// <para>
    /// Distinct, because a playlist row and a list entry are not the same thing and a
    /// wanted set holding one item twice would ask for a second row that no later pass
    /// could tell from a duplicate somebody made by hand.
    /// </para>
    /// <para>
    /// THE ORDER IS THE NEWEST ADDITION FIRST, and it is decided here rather than in
    /// the reconciler so that one order holds for every target. A watchlist is a record
    /// of an intention, and the intention a person had a minute ago is the one they are
    /// looking for when they open the list; a client shows a playlist from its head, so
    /// the head is where the newest entry belongs. The rule is total - entries added in
    /// the same instant fall back to the item identifier - because an order that is
    /// merely usually the same makes a rebuild happen for no reason.
    /// </para>
    /// <para>
    /// Read once, when the target is made for one pass, for the same reason the record
    /// is: two reads inside a pass would let the difference be computed against one
    /// state and written against another.
    /// </para>
    /// </remarks>
    IReadOnlyList<Guid> Wanted { get; }

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

    /// <summary>
    /// Takes the rows of a playlist this target is adopting into the list itself.
    /// </summary>
    /// <param name="itemIds">The library items the playlist holds.</param>
    /// <returns>How many of them went onto the list.</returns>
    /// <remarks>
    /// This is on the target rather than in the projector because it is the second
    /// thing the two kinds of list differ in. What an entry carries is not the same on
    /// both - an entry on a list several people write says who put it there - and the
    /// bound it is added under is a different setting for each. What is common is that
    /// a playlist has rows and the list should end up holding them, and that is the
    /// whole of what the projector knows.
    ///
    /// The count is what was TAKEN rather than what was offered, because a row that is
    /// already on the list, one whose item the user may not see, and one of a kind a
    /// list does not hold are all rows that arrive here and do not become entries.
    /// </remarks>
    int Adopt(IReadOnlyList<Guid> itemIds);

    /// <summary>
    /// Takes the edits somebody made to the playlist on a client back into the list.
    /// </summary>
    /// <param name="rows">The library items the playlist holds now.</param>
    /// <returns>How many entries were added and how many were taken off.</returns>
    /// <remarks>
    /// <para>
    /// THREE CASES AND ONE OF THEM MEANS DELETE, which is why this is a comparison
    /// against what the projector last wrote rather than against the list.
    /// </para>
    /// <list type="bullet">
    /// <item><description>A row the projector wrote and that is gone is a REMOVAL made
    /// on a client, and the entry leaves the list.</description></item>
    /// <item><description>A row the projector never wrote is an ADDITION made on a
    /// client, and it goes onto the list recorded as one.</description></item>
    /// <item><description>An entry on the list the projector has not written yet is
    /// neither. It is projected on this pass and is never read as a
    /// removal.</description></item>
    /// </list>
    /// <para>
    /// The three are indistinguishable to anything comparing only the list against the
    /// playlist, and that is what the projected set on the record exists for.
    /// </para>
    /// <para>
    /// THE ONE WEAKNESS, AND IT IS NOT FIXABLE FROM HERE. A change made while the server
    /// was down arrives at the next pass looking exactly like one made through the
    /// plugin, because neither side carries a time this could compare. Somebody removing
    /// a row on a client while the server is off has it read as a removal, which is
    /// right; a playlist edited by something that is not a person in the same window
    /// gets the same reading, and nothing on disk separates the two.
    /// </para>
    /// <para>
    /// It is on the target rather than in the pass for the same reason
    /// <see cref="Adopt"/> is: what an entry carries is not the same on a private list
    /// and on a shared one, and who may take an entry off differs between them.
    /// </para>
    /// </remarks>
    PlaylistEditsTaken TakeEdits(IReadOnlyList<Guid> rows);
}
