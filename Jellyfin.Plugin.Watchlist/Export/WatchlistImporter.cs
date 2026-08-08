using System;
using System.Collections.Generic;
using System.Linq;
using Jellyfin.Plugin.Watchlist.Store;

namespace Jellyfin.Plugin.Watchlist.Export;

/// <summary>
/// Reads an exported list against this server and says, entry by entry, what it found.
/// </summary>
/// <remarks>
/// The other direction of <see cref="WatchlistExporter"/>, and the same kind of thing:
/// a function over values. Nothing here reads a file, a clock, a document or a server,
/// so the rule below is exercised in the suite exactly as it runs in a server.
/// <para>
/// Two entries of one export can come out as the same item on this server, and nothing
/// here refuses that. The store is where an item that is already on a list is met, and
/// it answers that case with <see cref="WatchlistAddOutcome.AlreadyOnTheList"/>, so a
/// second rule here would be a second place the same question is answered.
/// </para>
/// </remarks>
public static class WatchlistImporter
{
    /// <summary>
    /// Lines every entry of an exported list up against this server.
    /// </summary>
    /// <param name="entries">The entries, as the export carried them.</param>
    /// <param name="providers">The index that answers which item a provider identifier names.</param>
    /// <param name="resolver">The resolver that answers whether an identifier still resolves here.</param>
    /// <returns>One result per entry, in the order they were given.</returns>
    /// <remarks>
    /// Provider identifiers are tried before the exporting server's own identifier,
    /// and the order is the rule rather than an implementation detail. The identifier
    /// the exporting server used is assigned by that server's library and does not
    /// survive a rebuild of it, so on any server but the one the file came from a
    /// match on it is a coincidence. A provider identifier means the same film
    /// everywhere. Trying the weaker one first would take a coincidence over an
    /// identity on exactly the move this format exists for.
    /// </remarks>
    public static WatchlistImportPlan Read(
        IReadOnlyList<ExportedEntry> entries,
        IProviderIdIndex providers,
        IWatchlistItemResolver resolver)
    {
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentNullException.ThrowIfNull(providers);
        ArgumentNullException.ThrowIfNull(resolver);

        return new WatchlistImportPlan
        {
            Entries = entries.Select(entry => ReadOne(entry, providers, resolver)).ToArray(),
        };
    }

    private static ImportedEntryMatch ReadOne(
        ExportedEntry entry,
        IProviderIdIndex providers,
        IWatchlistItemResolver resolver)
    {
        // A dictionary has no order of its own, and "the first provider identifier that
        // matches" has to name the same one twice or the answer depends on how the file
        // happened to deserialise. Ordinal, because the keys are provider names written
        // by another server and a culture-aware comparison would order them by the
        // machine reading the file.
        foreach (var provider in entry.ProviderIds.Keys.OrderBy(key => key, StringComparer.Ordinal))
        {
            var found = providers.ItemFor(provider, entry.ProviderIds[provider]);

            // The empty identifier is refused rather than accepted as an item, because
            // it is what an index answers when it means "nothing" without meaning to,
            // and an entry matched onto it would point at no item while counting as
            // matched.
            if (found is { } itemId && itemId != Guid.Empty)
            {
                return new ImportedEntryMatch
                {
                    Entry = entry,
                    Match = WatchlistImportMatch.ByProviderId,
                    ItemId = itemId,
                    Provider = provider,
                };
            }
        }

        // Same refusal at the other leg. A hand-written file can carry the empty
        // identifier here, and asking the library about it is asking a question with
        // an answer nobody meant.
        if (entry.ItemId != Guid.Empty && resolver.Exists(entry.ItemId))
        {
            return new ImportedEntryMatch
            {
                Entry = entry,
                Match = WatchlistImportMatch.ByItemId,
                ItemId = entry.ItemId,
                Provider = null,
            };
        }

        return new ImportedEntryMatch
        {
            Entry = entry,
            Match = WatchlistImportMatch.Unmatched,
            ItemId = null,
            Provider = null,
        };
    }
}
