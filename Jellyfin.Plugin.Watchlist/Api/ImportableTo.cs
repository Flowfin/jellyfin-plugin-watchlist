using System;
using System.Collections.Generic;
using Jellyfin.Plugin.Watchlist.Export;
using Jellyfin.Plugin.Watchlist.Store;

namespace Jellyfin.Plugin.Watchlist.Api;

/// <summary>
/// The library lookup as one caller may use it: an item answers here only when that
/// user may see it and a watchlist would take it.
/// </summary>
/// <remarks>
/// <para>
/// Both halves are one rule rather than two answers. An entry pointing at something
/// this caller may not see, and an entry pointing at a music track, both come back
/// unmatched, which is what a caller is told about an item that is not here at all.
/// So an import cannot be used to ask what sits in a library the caller has no
/// access to, and the add endpoint answers the same way for the same reason.
/// </para>
/// <para>
/// A type of its own rather than a class nested in one controller, because both
/// import surfaces read an exported file against this server and there is one rule
/// for what an entry may become here. A second copy behind the administrative import
/// would be the copy that drifts, and the two surfaces would then disagree about
/// which entries a server can take.
/// </para>
/// </remarks>
internal sealed class ImportableTo : IProviderIdIndex, IWatchlistItemResolver
{
    private readonly Dictionary<Guid, WatchlistItemDescription?> _asked = [];
    private readonly IProviderIdIndex _index;
    private readonly IWatchlistItemDescriber _describer;
    private readonly Guid _userId;

    /// <summary>
    /// Initializes a new instance of the <see cref="ImportableTo"/> class.
    /// </summary>
    /// <param name="index">Which item here an identifier from elsewhere names.</param>
    /// <param name="describer">What the library will say about an item to a user.</param>
    /// <param name="userId">The caller whose access decides what answers.</param>
    public ImportableTo(IProviderIdIndex index, IWatchlistItemDescriber describer, Guid userId)
    {
        _index = index;
        _describer = describer;
        _userId = userId;
    }

    /// <inheritdoc />
    public Guid? ItemFor(string provider, string id) =>
        _index.ItemFor(provider, id) is { } itemId && Importable(itemId) is not null
            ? itemId
            : null;

    /// <inheritdoc />
    public bool Exists(Guid itemId) => Importable(itemId) is not null;

    /// <summary>
    /// What this caller may put on a list under that identifier, or null.
    /// </summary>
    /// <param name="itemId">The item on this server.</param>
    /// <returns>The description, or null where the entry may not be written.</returns>
    /// <remarks>
    /// It remembers what it was told. Without that the same item is described twice
    /// per entry, once to decide whether it may be written and once to record what
    /// kind it is, and the second answer could differ from the first.
    /// </remarks>
    public WatchlistItemDescription? Importable(Guid itemId)
    {
        if (!_asked.TryGetValue(itemId, out var description))
        {
            var answered = _describer.Describe(itemId, _userId);

            description = answered is not null && AcceptedWatchlistItemKinds.Accepts(answered.Kind)
                ? answered
                : null;

            _asked[itemId] = description;
        }

        return description;
    }
}
