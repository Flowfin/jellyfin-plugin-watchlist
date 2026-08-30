using System;
using System.Collections.Generic;
using Jellyfin.Plugin.Watchlist.Export;
using Jellyfin.Plugin.Watchlist.Store;

namespace Jellyfin.Plugin.Watchlist.Api;

/// <summary>
/// The walk an import makes over a readable export: the lists of the kind the calling
/// surface owns are written, everything else is counted, and every entry of both comes
/// back in the report.
/// </summary>
/// <remarks>
/// <para>
/// One walk for both import surfaces. A private import writes the private lists and
/// counts the shared ones; the administrative import writes the shared list and counts
/// the private ones. That is one difference - which kind this surface owns - and a
/// second copy of the walk beside it would be the copy where an entry starts being
/// dropped in silence on one surface and not the other.
/// </para>
/// <para>
/// A list whose kind the file did not declare is written by neither, because the kind
/// is a claim about who may see the list and nobody made it. It falls out of the
/// comparison below rather than being a case of its own.
/// </para>
/// </remarks>
internal static class ImportedFile
{
    /// <summary>
    /// Walks the lists of a readable export in the order the file holds them.
    /// </summary>
    /// <param name="export">The export, already read and already of a known version.</param>
    /// <param name="written">The one kind of list this surface writes.</param>
    /// <param name="importable">What this caller may put on a list here.</param>
    /// <param name="write">Where an entry goes, and what came of it.</param>
    /// <returns>The report, entry by entry.</returns>
    internal static WatchlistImportReport Read(
        WatchlistExport export,
        ExportedListKind written,
        ImportableTo importable,
        Func<WatchlistEntry, WatchlistAddResult> write)
    {
        var lines = new List<WatchlistImportEntryReport>();
        var listsNotImported = 0;
        var entriesNotImported = 0;

        foreach (var list in export.Lists)
        {
            if (list.Kind != written)
            {
                // Counted with its entries, so a file that carried a list this surface
                // will not write does not read back as a file that carried nothing.
                listsNotImported++;
                entriesNotImported += list.Entries.Count;
                continue;
            }

            foreach (var match in WatchlistImporter.Read(list.Entries, importable, importable).Entries)
            {
                lines.Add(LineFor(match, OutcomeOf(match, importable, write)));
            }
        }

        return new WatchlistImportReport
        {
            ListsRead = export.Lists.Count,
            ListsNotImported = listsNotImported,
            EntriesNotImported = entriesNotImported,
            Entries = lines,
        };
    }

    /// <summary>
    /// One entry, written where the caller said, and what came of it.
    /// </summary>
    /// <param name="match">What the matching rule said about the entry.</param>
    /// <param name="importable">The lookup that already answered for the item.</param>
    /// <param name="write">Where the entry goes.</param>
    /// <returns>The outcome the report carries for this entry.</returns>
    /// <remarks>
    /// The entry keeps the instant the exporting server recorded, because a move that
    /// reset every date would tell a user they added their whole list on the day they
    /// changed servers.
    ///
    /// It records nobody as having added it. A shared entry names who put it there and
    /// that name is a user of the server the file came from, so carrying it across
    /// would attribute a title to a user identifier that names nobody here, and
    /// stamping the importing administrator on it would say they added titles they did
    /// not. What the absence costs is written at the route: an imported shared entry is
    /// removable by an administrator and by nobody else until somebody adds it again.
    /// </remarks>
    private static WatchlistImportOutcome OutcomeOf(
        ImportedEntryMatch match,
        ImportableTo importable,
        Func<WatchlistEntry, WatchlistAddResult> write)
    {
        if (match.ItemId is not { } itemId)
        {
            return WatchlistImportOutcome.Unmatched;
        }

        // The bang is carried by the line above it: an entry the rule matched is one
        // this lookup answered for, and it answered once because it remembers.
        var result = write(new WatchlistEntry
        {
            ItemId = itemId,
            Kind = importable.Importable(itemId)!.Kind,
            AddedAt = match.Entry.AddedAt,
            Source = WatchlistEntrySource.Import,
        });

        return result.Outcome switch
        {
            WatchlistAddOutcome.Added => WatchlistImportOutcome.Added,
            WatchlistAddOutcome.AlreadyOnTheList => WatchlistImportOutcome.AlreadyOnTheList,
            _ => WatchlistImportOutcome.Refused,
        };
    }

    private static WatchlistImportEntryReport LineFor(
        ImportedEntryMatch match,
        WatchlistImportOutcome outcome) => new()
        {
            ItemId = match.Entry.ItemId,
            Match = match.Match,
            Provider = match.Provider,
            MatchedItemId = match.ItemId,
            Outcome = outcome,
        };
}
