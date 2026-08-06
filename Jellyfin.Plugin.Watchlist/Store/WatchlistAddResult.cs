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
    /// The list could not be read, so nothing can be added to it. Writing over a
    /// document this plugin refused to read is how a downgrade drops entries.
    /// </summary>
    /// <returns>The result.</returns>
    public static WatchlistAddResult RefusedListUnavailable() =>
        new(WatchlistAddOutcome.RefusedListUnavailable, 0, 0);

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
        WatchlistAddOutcome.RefusedListIsFull => string.Format(
            CultureInfo.InvariantCulture,
            "Refused: the list holds {0} entries and the maximum is {1}. Nothing was added and nothing was removed.",
            EntryCount,
            Cap),
        _ => "Refused: this user's list could not be read, so nothing was added to it.",
    };
}
