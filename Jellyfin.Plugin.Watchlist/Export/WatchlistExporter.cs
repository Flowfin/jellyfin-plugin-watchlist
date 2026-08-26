using System;
using System.Collections.Generic;
using System.Linq;
using Jellyfin.Plugin.Watchlist.Store;

namespace Jellyfin.Plugin.Watchlist.Export;

/// <summary>
/// Turns what the store holds into the shape docs/export-format.md describes.
/// </summary>
/// <remarks>
/// Everything here is a function over values. Nothing reads a file, a clock or a
/// server, so the same code runs in a test and in the server and a sample written by
/// the suite is the sample the server would write.
/// </remarks>
public static class WatchlistExporter
{
    /// <summary>
    /// Describes one user's stored document as a private list.
    /// </summary>
    /// <param name="document">The stored document.</param>
    /// <param name="providers">Where the provider identifiers come from.</param>
    /// <returns>The list as it goes into an export.</returns>
    public static ExportedList PrivateList(WatchlistDocument document, IProviderIdSource providers)
    {
        ArgumentNullException.ThrowIfNull(document);

        return new ExportedList
        {
            Kind = ExportedListKind.Private,
            OwnerUserId = document.UserId,

            // A private list is identified by the user it belongs to. Minting an
            // identifier here would be inventing one, and a reader cannot tell an
            // invented identifier from a recorded one.
            ListId = null,
            Name = null,
            Entries = Describe(document.Entries, providers),
        };
    }

    /// <summary>
    /// Describes a shared list.
    /// </summary>
    /// <param name="listId">The identifier the shared record carries, or null where it carries none.</param>
    /// <param name="name">The name the list is shown under, or null where it has none.</param>
    /// <param name="ownerUserId">The user who owns it, or null where no single user does.</param>
    /// <param name="entries">The entries the list holds.</param>
    /// <param name="providers">Where the provider identifiers come from.</param>
    /// <returns>The list as it goes into an export.</returns>
    /// <remarks>
    /// The pieces are passed in rather than read from a shared record. That said the
    /// record was not built yet, and it is: what has not been written is the caller
    /// that maps one onto this call, and the format does not move when it is.
    /// </remarks>
    public static ExportedList SharedList(
        Guid? listId,
        string? name,
        Guid? ownerUserId,
        IReadOnlyList<WatchlistEntry> entries,
        IProviderIdSource providers) => new()
        {
            Kind = ExportedListKind.Shared,
            OwnerUserId = ownerUserId,
            ListId = listId,
            Name = name,
            Entries = Describe(entries, providers),
        };

    /// <summary>
    /// Puts lists into an export.
    /// </summary>
    /// <param name="lists">The lists, in the order they should appear.</param>
    /// <returns>The export.</returns>
    public static WatchlistExport Export(IReadOnlyList<ExportedList> lists) => new()
    {
        FormatVersion = WatchlistExport.CurrentFormatVersion,
        Lists = lists,
    };

    private static ExportedEntry[] Describe(
        IReadOnlyList<WatchlistEntry> entries,
        IProviderIdSource providers)
    {
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentNullException.ThrowIfNull(providers);

        return entries.Select(entry => new ExportedEntry
        {
            ItemId = entry.ItemId,
            Kind = entry.Kind,
            AddedAt = entry.AddedAt,
            ProviderIds = providers.ProviderIdsFor(entry.ItemId),
        }).ToArray();
    }
}
