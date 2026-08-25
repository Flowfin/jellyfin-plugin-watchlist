using System;
using Jellyfin.Plugin.Watchlist.Store;

namespace Jellyfin.Plugin.Watchlist.Configuration;

/// <summary>
/// Which value applies to a user, where the server has answered a setting and the
/// user may have answered it too.
/// </summary>
/// <remarks>
/// <para>
/// The rule is one sentence and it is written once, in <see cref="Resolve{T}"/>: a
/// per-user value wins wherever it is present, and where it is absent the server-wide
/// value applies. There is no third source and no per-setting exception, so a setting
/// added later gets the rule by calling this rather than by its author remembering it.
/// </para>
/// <para>
/// The configuration is passed in rather than read from the plugin, for the same
/// reason the store's cap is: nothing here needs a server to be exercised, and the
/// caller stays the one place that decides which configuration applies.
/// </para>
/// <para>
/// A user whose document does not exist yet reaches this with a null block, which is
/// the same case as a user whose document exists and holds no block. Both are a user
/// who answered nothing, and neither is a case this has to be told about separately.
/// </para>
/// </remarks>
public static class EffectiveSettings
{
    /// <summary>
    /// Whether the projection runs for this user.
    /// </summary>
    /// <param name="serverWide">The server's configuration.</param>
    /// <param name="preferences">The user's own answers, or null where they have none.</param>
    /// <returns>The value that applies.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="serverWide"/> is null.</exception>
    public static bool ProjectionEnabled(PluginConfiguration serverWide, WatchlistUserPreferences? preferences)
    {
        ArgumentNullException.ThrowIfNull(serverWide);

        return Resolve(preferences?.ProjectionEnabled, serverWide.ProjectionEnabled);
    }

    /// <summary>
    /// Whether a watched item leaves this user's list.
    /// </summary>
    /// <param name="serverWide">The server's configuration.</param>
    /// <param name="preferences">The user's own answers, or null where they have none.</param>
    /// <returns>The value that applies.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="serverWide"/> is null.</exception>
    public static bool RemoveWhenWatched(PluginConfiguration serverWide, WatchlistUserPreferences? preferences)
    {
        ArgumentNullException.ThrowIfNull(serverWide);

        return Resolve(preferences?.RemoveWhenWatched, serverWide.RemoveWhenWatched);
    }

    /// <summary>
    /// The precedence rule itself, and the only place it is written.
    /// </summary>
    /// <typeparam name="T">The setting's type.</typeparam>
    /// <param name="perUser">What the user answered, or null where they did not.</param>
    /// <param name="serverWide">What the server answers.</param>
    /// <returns>The value that applies.</returns>
    private static T Resolve<T>(T? perUser, T serverWide)
        where T : struct => perUser ?? serverWide;
}
