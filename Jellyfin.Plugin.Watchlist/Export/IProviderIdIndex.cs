using System;

namespace Jellyfin.Plugin.Watchlist.Export;

/// <summary>
/// Answers which item on this server is the one a provider identifier names.
/// </summary>
/// <remarks>
/// The inverse of <see cref="IProviderIdSource"/>, and a separate interface because
/// the two are asked at opposite ends of a move. An export asks what an item here is
/// called everywhere else; an import asks which item here an identifier from somewhere
/// else is. The implementation that matters asks the running server's library; the
/// suite passes one that answers from a table, which is what lets the matching rule be
/// exercised with no server present.
/// </remarks>
public interface IProviderIdIndex
{
    /// <summary>
    /// The item this server holds under one provider's identifier.
    /// </summary>
    /// <param name="provider">The provider name, as the exporting server wrote it.</param>
    /// <param name="id">That provider's identifier for the item, as a string.</param>
    /// <returns>
    /// The item, or null where this server holds nothing under that identifier. A
    /// provider name this server has never heard of is not an error and answers null,
    /// because the format says a reader treats the key set as open.
    /// </returns>
    Guid? ItemFor(string provider, string id);
}
