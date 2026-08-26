using System.Globalization;

namespace Jellyfin.Plugin.Watchlist.Store;

/// <summary>
/// What an add did, or why it did nothing.
/// </summary>
/// <remarks>
/// The three answers are distinct on purpose. A caller that cannot tell a refusal
/// from a success reports the item as added and the user finds it missing later, and
/// a caller that cannot tell a full list from an unreadable one tells the user to
/// remove something when removing something would not help.
/// </remarks>
public sealed record WatchlistAddResult
{
    private WatchlistAddResult(WatchlistAddOutcome outcome, int entryCount, int cap)
    {
        Outcome = outcome;
        EntryCount = entryCount;
        Cap = cap;
    }

    /// <summary>
    /// Gets what happened.
    /// </summary>
    public WatchlistAddOutcome Outcome { get; }

    /// <summary>
    /// Gets how many entries the list holds now. Unchanged by a refusal, which is what
    /// makes "the cap was lowered under an existing list" readable rather than a
    /// surprise.
    /// </summary>
    public int EntryCount { get; }

    /// <summary>
    /// Gets the cap the add was judged against.
    /// </summary>
    public int Cap { get; }

    /// <summary>
    /// Gets a value indicating whether the entry is on the list now because of this call.
    /// </summary>
    public bool WasAdded => Outcome == WatchlistAddOutcome.Added;

    /// <summary>
    /// Gets a value indicating whether the entry is on the list, whether this call put
    /// it there or found it there.
    /// </summary>
    /// <remarks>
    /// This is the question a caller asking for an item to be on a list is actually
    /// asking, and it is separate from <see cref="WasAdded"/> because the two answers
    /// come apart on a repeat. A caller that could only ask the second one would have
    /// to report a retry after a timeout as a failure, which is the shape that makes a
    /// client add the same thing twice.
    /// </remarks>
    public bool IsOnTheList => Outcome is WatchlistAddOutcome.Added or WatchlistAddOutcome.AlreadyOnTheList;

    /// <summary>
    /// The entry went on the list and the document was written.
    /// </summary>
    /// <param name="entryCount">How many entries the list holds now.</param>
    /// <param name="cap">The cap it was judged against.</param>
    /// <returns>The result.</returns>
    public static WatchlistAddResult Added(int entryCount, int cap) =>
        new(WatchlistAddOutcome.Added, entryCount, cap);

    /// <summary>
    /// The list is at or over its cap. Nothing was written and nothing was removed to
    /// make room, because a list that silently drops its oldest entry is a list a user
    /// cannot trust.
    /// </summary>
    /// <param name="entryCount">How many entries the list holds, unchanged.</param>
    /// <param name="cap">The cap it was judged against.</param>
    /// <returns>The result.</returns>
    public static WatchlistAddResult RefusedListIsFull(int entryCount, int cap) =>
        new(WatchlistAddOutcome.RefusedListIsFull, entryCount, cap);

    /// <summary>
    /// The item was already on the list, so nothing was written. It is not a refusal:
    /// the list holds what the caller asked it to hold, and writing the entry a second
    /// time would leave the list holding one item twice.
    /// </summary>
    /// <param name="entryCount">How many entries the list holds, unchanged.</param>
    /// <param name="cap">The cap it was judged against.</param>
    /// <returns>The result.</returns>
    public static WatchlistAddResult AlreadyOnTheList(int entryCount, int cap) =>
        new(WatchlistAddOutcome.AlreadyOnTheList, entryCount, cap);

    /// <summary>
    /// The list could not be read, so nothing can be added to it. Writing over a
    /// document this plugin refused to read is how a downgrade drops entries.
    /// </summary>
    /// <returns>The result.</returns>
    public static WatchlistAddResult RefusedListUnavailable() =>
        new(WatchlistAddOutcome.RefusedListUnavailable, 0, 0);

    /// <summary>
    /// There is no shared list on this server, so nothing can be added to it. Making
    /// one is a decision somebody takes, and an add is not the place it gets taken.
    /// </summary>
    /// <returns>The result.</returns>
    public static WatchlistAddResult RefusedNoSharedList() =>
        new(WatchlistAddOutcome.RefusedNoSharedList, 0, 0);

    /// <summary>
    /// A sentence an operator can read, naming the numbers.
    /// </summary>
    /// <returns>The description.</returns>
    public string Describe() => Outcome switch
    {
        WatchlistAddOutcome.Added => string.Format(
            CultureInfo.InvariantCulture,
            "Added. The list holds {0} of at most {1} entries.",
            EntryCount,
            Cap),
        WatchlistAddOutcome.AlreadyOnTheList => string.Format(
            CultureInfo.InvariantCulture,
            "Already on the list. Nothing was written. The list holds {0} of at most {1} entries.",
            EntryCount,
            Cap),
        WatchlistAddOutcome.RefusedListIsFull => string.Format(
            CultureInfo.InvariantCulture,
            "Refused: the list holds {0} entries and the maximum is {1}. Nothing was added and nothing was removed.",
            EntryCount,
            Cap),
        WatchlistAddOutcome.RefusedNoSharedList =>
            "Refused: this server has no shared list, so nothing was added to one.",
        _ => "Refused: this user's list could not be read, so nothing was added to it.",
    };
}
