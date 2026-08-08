namespace Jellyfin.Plugin.Watchlist.Export;

/// <summary>
/// How an entry in an export was lined up against this server, or that it was not.
/// </summary>
/// <remarks>
/// Recorded per entry rather than summed, because the three values do not carry the
/// same confidence. An entry matched by a provider identifier is the same film on two
/// servers. An entry matched by the identifier the exporting server used is the same
/// film only if the import is being read back on the server it came from, which is the
/// restore case rather than the move case.
/// </remarks>
public enum WatchlistImportMatch
{
    /// <summary>
    /// Nothing on this server answered to the entry. The entry is kept and counted;
    /// what a caller does with it is the caller's decision and never a silent drop.
    /// </summary>
    Unmatched = 0,

    /// <summary>
    /// A provider identifier the entry carries names an item in this server's library.
    /// </summary>
    ByProviderId = 1,

    /// <summary>
    /// No provider identifier matched, and the identifier the exporting server used
    /// resolves here. That happens on a restore onto the same library and it happens
    /// by coincidence nowhere else, because the library assigns those identifiers.
    /// </summary>
    ByItemId = 2,
}
