using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.Watchlist.Store;

/// <summary>
/// The settings that are a user's own rather than the server's, as they are written
/// into that user's document beside their entries.
/// </summary>
/// <remarks>
/// <para>
/// Every member is nullable, and null means nobody answered rather than answered no.
/// That is the whole of the precedence rule this record exists for: a value that is
/// present wins over the server-wide one, and a value that is absent leaves the
/// server-wide one in force. <see cref="Jellyfin.Plugin.Watchlist.Configuration.EffectiveSettings"/>
/// is the one place that applies it.
/// </para>
/// <para>
/// A per-user value that happens to equal the server-wide value today is present and
/// still wins. Collapsing it into an absence for being equal would make a user's
/// answer move when an administrator saves the page, without either of them touching
/// that setting, so absent has to mean nobody answered and never it happened to match.
/// </para>
/// <para>
/// Both members are suppressed when they are null, for the same reason the block
/// itself is: a user who answered one question is not made to carry an explicit null
/// for the other.
/// </para>
/// </remarks>
public sealed record WatchlistUserPreferences
{
    /// <summary>
    /// Gets this user's answer to whether the projection runs for them, or null where
    /// they have not answered.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? ProjectionEnabled { get; init; }

    /// <summary>
    /// Gets this user's answer to whether a watched item leaves their list, or null
    /// where they have not answered.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? RemoveWhenWatched { get; init; }

    /// <summary>
    /// Gets a value indicating whether this block holds any answer at all. A block
    /// where every member is null says the same thing as no block, and the store
    /// writes no block rather than an empty one.
    /// </summary>
    [JsonIgnore]
    public bool IsEmpty => ProjectionEnabled is null && RemoveWhenWatched is null;
}
