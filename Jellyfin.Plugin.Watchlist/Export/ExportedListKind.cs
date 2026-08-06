namespace Jellyfin.Plugin.Watchlist.Export;

/// <summary>
/// Which kind of list an exported list is. Written into every list so a reader that
/// understands only one kind can skip the other instead of reading a shared list as
/// somebody's private one.
/// </summary>
public enum ExportedListKind
{
    /// <summary>
    /// The kind was not written. A document carrying this is refused rather than
    /// guessed at, because both other values are a claim about who may see the list.
    /// </summary>
    Unknown = 0,

    /// <summary>
    /// One user's own list, seen by that user and nobody else.
    /// </summary>
    Private = 1,

    /// <summary>
    /// A list the server offers to more than one user.
    /// </summary>
    Shared = 2,
}
