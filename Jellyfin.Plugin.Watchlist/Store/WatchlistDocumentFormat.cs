using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.Watchlist.Store;

/// <summary>
/// The one place that decides what a stored document looks like. Every writer and
/// every reader goes through it, so the on-disk shape is one decision rather than
/// one per call site.
/// </summary>
public static class WatchlistDocumentFormat
{
    private static readonly JsonSerializerOptions SerializerOptions = BuildOptions();

    /// <summary>
    /// Writes a document as the text that goes on disk.
    /// </summary>
    /// <param name="document">The document to write.</param>
    /// <returns>The document text.</returns>
    public static string Write(WatchlistDocument document) =>
        JsonSerializer.Serialize(document, SerializerOptions);

    /// <summary>
    /// Reads a document from the text on disk.
    /// </summary>
    /// <param name="text">The document text.</param>
    /// <returns>The document.</returns>
    /// <exception cref="JsonException">The text is not a document this plugin wrote.</exception>
    public static WatchlistDocument Read(string text) =>
        JsonSerializer.Deserialize<WatchlistDocument>(text, SerializerOptions)
        ?? throw new JsonException("The document text is the JSON literal null rather than a document.");

    private static JsonSerializerOptions BuildOptions()
    {
        var options = new JsonSerializerOptions
        {
            // A person reads this file when something has gone wrong on their server.
            WriteIndented = true,

            // The text is a stored artefact, so the line ending is part of the shape
            // and may not follow whichever machine happened to write it.
            NewLine = "\n",

            // An unknown member is a document written by a newer plugin, or a hand
            // edit that misspelled a name. Both are refused rather than dropped.
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        };

        // Names rather than numbers, so a document stays readable and reordering the
        // enum cannot silently change what every stored entry means.
        options.Converters.Add(new JsonStringEnumConverter());

        return options;
    }
}
