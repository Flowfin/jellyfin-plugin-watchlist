using System;
using System.Collections.Generic;
using System.Linq;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.Watchlist.Export;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;

namespace Jellyfin.Plugin.Watchlist.Api;

/// <summary>
/// The two provider questions, answered by asking the server. It is the only thing in
/// this plugin that knows a library can be searched.
/// </summary>
/// <remarks>
/// <para>
/// One class for both directions rather than two, because they are the same fact read
/// from opposite ends and a server that answers one and not the other is not a state
/// worth having. An export asks what an item here is called everywhere else; an import
/// asks which item here an identifier from somewhere else names.
/// </para>
/// <para>
/// Everything above it takes <see cref="IProviderIdSource"/> and
/// <see cref="IProviderIdIndex"/>, so the endpoints, the export rule and the matching
/// rule never touch a server type and the suite drives all three with tables.
/// </para>
/// </remarks>
public sealed class LibraryProviderIds : IProviderIdSource, IProviderIdIndex
{
    private readonly ILibraryManager _library;

    /// <summary>
    /// Initializes a new instance of the <see cref="LibraryProviderIds"/> class.
    /// </summary>
    /// <param name="library">The server's library.</param>
    public LibraryProviderIds(ILibraryManager library)
    {
        _library = library;
    }

    /// <inheritdoc />
    /// <remarks>
    /// An item this server can no longer read answers with nothing rather than
    /// throwing. The entry still leaves in the export carrying no way to resolve it,
    /// which is what <see cref="IProviderIdSource"/> asks for and is better than
    /// dropping it.
    /// </remarks>
    public IReadOnlyDictionary<string, string> ProviderIdsFor(Guid itemId)
    {
        var item = _library.GetItemById(itemId);

        return item is null
            ? new Dictionary<string, string>(StringComparer.Ordinal)
            : new Dictionary<string, string>(item.ProviderIds, StringComparer.Ordinal);
    }

    /// <inheritdoc />
    /// <remarks>
    /// The query is bounded to the three kinds a watchlist takes, so an identifier a
    /// music library also carries cannot answer here. A library holding the same title
    /// twice answers with one of the two, chosen by the identifier rather than by the
    /// order the query happened to return, so two reads of one library agree.
    /// </remarks>
    public Guid? ItemFor(string provider, string id)
    {
        if (string.IsNullOrEmpty(provider) || string.IsNullOrEmpty(id))
        {
            return null;
        }

        var query = new InternalItemsQuery
        {
            HasAnyProviderId = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [provider] = id,
            },
            IncludeItemTypes = [BaseItemKind.Movie, BaseItemKind.Series, BaseItemKind.Episode],
            Recursive = true,
        };

        return _library.GetItemList(query)
            .Select(item => item.Id)
            .OrderBy(itemId => itemId)
            .Cast<Guid?>()
            .FirstOrDefault();
    }
}
