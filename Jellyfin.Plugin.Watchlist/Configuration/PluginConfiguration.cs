using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.Watchlist.Configuration;

/// <summary>
/// Plugin configuration.
/// </summary>
/// <remarks>
/// The template's four demonstration settings were removed in #90 and the rest of
/// this plugin's settings arrive with M5. What is here is the one bound the store
/// cannot do without, because a list with no bound is a memory and a disk problem on
/// a server this plugin does not own.
/// </remarks>
public class PluginConfiguration : BasePluginConfiguration
{
    /// <summary>
    /// The number of entries one user's list may hold when nothing says otherwise.
    /// </summary>
    /// <remarks>
    /// Ten thousand, which is the number the upstream attempt at a native watchlist
    /// chose for the same reason, in its MaxItemsPerUserList. Matching it means a user
    /// who ever moves between the two meets one bound rather than two, and it means
    /// the number is not one this board invented. It sits far above any list a person
    /// curates by hand and far below the size at which reading the document costs a
    /// server anything noticeable.
    /// </remarks>
    public const int DefaultMaxEntriesPerUser = 10000;

    /// <summary>
    /// Initializes a new instance of the <see cref="PluginConfiguration"/> class.
    /// </summary>
    public PluginConfiguration()
    {
        MaxEntriesPerUser = DefaultMaxEntriesPerUser;
    }

    /// <summary>
    /// Gets or sets the greatest number of entries one user's list may hold.
    /// </summary>
    /// <remarks>
    /// An add that would take a list past this is refused and nothing is written.
    /// Lowering it under an existing list removes nothing: that list stops growing and
    /// says why. Validating the value on save and on load is #34.
    /// </remarks>
    public int MaxEntriesPerUser { get; set; }
}
