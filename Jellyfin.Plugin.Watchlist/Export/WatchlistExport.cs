using System.Collections.Generic;

namespace Jellyfin.Plugin.Watchlist.Export;

/// <summary>
/// The whole of one export: a version and the lists it carries.
/// </summary>
/// <remarks>
/// This is the shape offered to a reader that is not this plugin, so it is separate
/// from <see cref="Store.WatchlistDocument"/> on purpose. The stored document is this
/// plugin talking to itself and may change whenever the plugin does. This one is a
/// promise, and what the promise covers is written in docs/export-format.md.
/// </remarks>
public sealed record WatchlistExport
{
    /// <summary>
    /// The version this plugin writes.
    /// </summary>
    public const int CurrentFormatVersion = 1;

    /// <summary>
    /// Gets the version of the format, not of the plugin and not of the server. A
    /// reader that does not know the number it finds here stops rather than guessing.
    /// </summary>
    public required int FormatVersion { get; init; }

    /// <summary>
    /// Gets the lists in the export. An export with no lists is a valid export of
    /// nothing, which is what a server with no lists produces.
    /// </summary>
    public required IReadOnlyList<ExportedList> Lists { get; init; }
}
