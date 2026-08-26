namespace Jellyfin.Plugin.Watchlist.Api;

/// <summary>
/// What an import did with one entry of the file it was given.
/// </summary>
/// <remarks>
/// Every entry that went in comes back carrying one of these, including the ones
/// nothing happened to. An import that reported only what it added would be shorter
/// than the file it read, and the person comparing the two counts would have nothing
/// to read the difference off.
/// </remarks>
public enum WatchlistImportOutcome
{
    /// <summary>
    /// Nothing on this server answered to the entry, so nothing was written for it.
    /// The entry is reported and counted rather than dropped.
    /// </summary>
    Unmatched = 0,

    /// <summary>
    /// The entry came out as an item here and this call put it on the caller's list.
    /// </summary>
    Added = 1,

    /// <summary>
    /// The entry came out as an item here and the caller's list already held it. Not
    /// an error: importing a file twice leaves one entry, exactly as calling the add
    /// endpoint twice does.
    /// </summary>
    AlreadyOnTheList = 2,

    /// <summary>
    /// The entry came out as an item here and the store refused the write. The list
    /// being at its cap is what produces this, and so is a list this plugin will not
    /// read at the moment the write was tried.
    /// </summary>
    Refused = 3,
}
