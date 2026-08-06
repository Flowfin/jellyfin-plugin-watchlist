using System.Text.Json;
using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.Watchlist.Export;

/// <summary>
/// The one place that decides what an export file looks like.
/// </summary>
/// <remarks>
/// It reads differently from <see cref="Store.WatchlistDocumentFormat"/> in one way,
/// and the difference is the whole point of having two. The stored document refuses a
/// member it does not know, because an unknown member there is a document from a newer
/// plugin or a mistyped hand edit. An export is offered to somebody else, and the
/// format promises that a later version may add fields, so a reader that refused an
/// unknown member would break on the first addition the promise allows.
/// </remarks>
public static class WatchlistExportFormat
{
    private static readonly JsonSerializerOptions SerializerOptions = BuildOptions();

    /// <summary>
    /// Writes an export as the text that goes in the file.
    /// </summary>
    /// <param name="export">The export to write.</param>
    /// <returns>The file text.</returns>
    public static string Write(WatchlistExport export) =>
        JsonSerializer.Serialize(export, SerializerOptions);

    /// <summary>
    /// Reads an export back.
    /// </summary>
    /// <param name="text">The file text.</param>
    /// <returns>The export, or null where the text is the JSON literal null.</returns>
    public static WatchlistExport? Read(string text) =>
        JsonSerializer.Deserialize<WatchlistExport>(text, SerializerOptions);

    private static JsonSerializerOptions BuildOptions()
    {
        var options = new JsonSerializerOptions
        {
            // Somebody writing a reader against this format opens the file and looks
            // at it, so it is written to be looked at.
            WriteIndented = true,

            // The file is an artefact that gets committed, diffed and compared, so the
            // line ending is part of the shape rather than a fact about the machine
            // that happened to write it.
            NewLine = "\n",

            // A field a later version added is skipped rather than refused. The
            // promise in docs/export-format.md is what this line implements.
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Skip,
        };

        // Names rather than numbers, so reordering an enum in this repository cannot
        // silently change what an exported entry means to a reader elsewhere.
        options.Converters.Add(new JsonStringEnumConverter());

        return options;
    }
}
