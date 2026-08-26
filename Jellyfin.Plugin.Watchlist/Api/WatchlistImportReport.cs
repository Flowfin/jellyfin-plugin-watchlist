using System.Collections.Generic;
using System.Linq;

namespace Jellyfin.Plugin.Watchlist.Api;

/// <summary>
/// The whole answer to an import: what was read, what was written, and what was left
/// alone.
/// </summary>
/// <remarks>
/// The four outcome counts are derived from the entries rather than recorded beside
/// them, so a report cannot say it added three things and list two. The three counts
/// that are not derived are about lists this surface did not open at all, whose
/// entries are deliberately absent from <see cref="Entries"/>, and they are what stops
/// that absence from reading as a file that carried nothing.
/// </remarks>
public sealed record WatchlistImportReport
{
    /// <summary>
    /// Gets how many lists the file carried.
    /// </summary>
    public required int ListsRead { get; init; }

    /// <summary>
    /// Gets how many of those lists this surface did not import.
    /// </summary>
    public required int ListsNotImported { get; init; }

    /// <summary>
    /// Gets how many entries sat in the lists this surface did not import.
    /// </summary>
    public required int EntriesNotImported { get; init; }

    /// <summary>
    /// Gets one result per entry of every list this surface did read, in the order the
    /// file held them.
    /// </summary>
    public required IReadOnlyList<WatchlistImportEntryReport> Entries { get; init; }

    /// <summary>
    /// Gets how many entries this call put on the list.
    /// </summary>
    public int Added => Count(WatchlistImportOutcome.Added);

    /// <summary>
    /// Gets how many entries were on the list already.
    /// </summary>
    public int AlreadyOnTheList => Count(WatchlistImportOutcome.AlreadyOnTheList);

    /// <summary>
    /// Gets how many entries nothing on this server answered to.
    /// </summary>
    public int Unmatched => Count(WatchlistImportOutcome.Unmatched);

    /// <summary>
    /// Gets how many entries came out as an item here and were not written anyway.
    /// </summary>
    public int Refused => Count(WatchlistImportOutcome.Refused);

    private int Count(WatchlistImportOutcome outcome) =>
        Entries.Count(entry => entry.Outcome == outcome);
}
