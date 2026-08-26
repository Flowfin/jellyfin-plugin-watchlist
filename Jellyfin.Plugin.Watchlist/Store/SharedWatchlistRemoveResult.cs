namespace Jellyfin.Plugin.Watchlist.Store;

/// <summary>
/// What a removal from the shared list did, or why it did nothing.
/// </summary>
/// <remarks>
/// One more answer than a user's own list has, and it is the one the shared list
/// exists to need. A private list has one writer, so "it is not yours" cannot arise.
/// A shared list is written by everybody, so an entry belongs to whoever put it there
/// and a removal by anyone else is refused rather than done quietly.
///
/// "It was there and it is gone" and "it was never there" stay one answer, exactly as
/// they are on a private list. Separating them would let a caller learn what is on a
/// list by trying to take things off it, and here the caller can read the list
/// anyway, so the distinction would buy nothing and cost a second code to keep.
/// </remarks>
/// <param name="Outcome">What happened.</param>
/// <param name="EntryCount">How many entries the list holds now.</param>
public sealed record SharedWatchlistRemoveResult(SharedWatchlistRemoveOutcome Outcome, int EntryCount)
{
    /// <summary>
    /// Gets a value indicating whether the list no longer holds the item because this
    /// call succeeded or because it never did.
    /// </summary>
    public bool IsOffTheList => Outcome
        is SharedWatchlistRemoveOutcome.Removed
        or SharedWatchlistRemoveOutcome.NotOnTheList;

    /// <summary>
    /// The entry left the list and the document was written.
    /// </summary>
    /// <param name="entryCount">How many entries the list holds now.</param>
    /// <returns>The result.</returns>
    public static SharedWatchlistRemoveResult Removed(int entryCount) =>
        new(SharedWatchlistRemoveOutcome.Removed, entryCount);

    /// <summary>
    /// The item was not on the list, so nothing was written.
    /// </summary>
    /// <param name="entryCount">How many entries the list holds, unchanged.</param>
    /// <returns>The result.</returns>
    public static SharedWatchlistRemoveResult NotOnTheList(int entryCount) =>
        new(SharedWatchlistRemoveOutcome.NotOnTheList, entryCount);

    /// <summary>
    /// The entry is somebody else's and this caller may not remove anybody's but their
    /// own. Nothing was written.
    /// </summary>
    /// <param name="entryCount">How many entries the list holds, unchanged.</param>
    /// <returns>The result.</returns>
    public static SharedWatchlistRemoveResult RefusedNotTheirEntry(int entryCount) =>
        new(SharedWatchlistRemoveOutcome.RefusedNotTheirEntry, entryCount);

    /// <summary>
    /// Nobody has made a shared list on this server, so there is nothing to remove
    /// from.
    /// </summary>
    /// <returns>The result.</returns>
    public static SharedWatchlistRemoveResult NoSharedList() =>
        new(SharedWatchlistRemoveOutcome.NoSharedList, 0);

    /// <summary>
    /// The shared list could not be read, so nothing was removed from it.
    /// </summary>
    /// <returns>The result.</returns>
    public static SharedWatchlistRemoveResult Unavailable() =>
        new(SharedWatchlistRemoveOutcome.RefusedListUnavailable, 0);
}
