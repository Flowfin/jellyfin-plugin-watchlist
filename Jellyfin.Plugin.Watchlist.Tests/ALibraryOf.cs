using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Data.Enums;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Database.Implementations.Enums;
using MediaBrowser.Controller.Dto;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Audio;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Controller.Resolvers;
using MediaBrowser.Controller.Sorting;
using MediaBrowser.Model.Configuration;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.IO;
using MediaBrowser.Model.Library;
using MediaBrowser.Model.Querying;

namespace Jellyfin.Plugin.Watchlist.Tests;

/// <summary>
/// A library that answers the two questions <see cref="Jellyfin.Plugin.Watchlist.Api.LibraryProviderIds"/>
/// asks it, out of a table, and refuses every other one loudly.
/// </summary>
/// <remarks>
/// <para>
/// The interface is wide and this plugin uses two members of it. Every other member
/// throws rather than returning a default, so a call this fake was never meant to
/// answer fails the test that made it instead of passing on an invented value.
/// </para>
/// <para>
/// It exists because width is not unreachability. The adapter it stands under holds no
/// static and reaches nothing a test cannot build, so excluding that file from the
/// coverage floor would have been a waiver on the cost of typing rather than on
/// anything the suite could not do.
/// </para>
/// </remarks>
internal sealed class ALibraryOf : ILibraryManager
{
    private readonly Dictionary<Guid, BaseItem> _items = [];

    /// <summary>
    /// Initializes a new instance of the <see cref="ALibraryOf"/> class.
    /// </summary>
    /// <param name="items">The items this library holds.</param>
    public ALibraryOf(params BaseItem[] items)
    {
        foreach (var item in items)
        {
            _items[item.Id] = item;
        }
    }

    /// <summary>
    /// The one item under that identifier, or null.
    /// </summary>
    /// <param name="id">The identifier.</param>
    /// <returns>The item, or null.</returns>
    public BaseItem? GetItemById(Guid id) => _items.GetValueOrDefault(id);

    /// <summary>
    /// The items matching a query, which this fake reads for the two things the
    /// adapter puts in one: the provider identifier pair, and the kinds it bounds the
    /// search to.
    /// </summary>
    /// <param name="query">The query.</param>
    /// <returns>What matched, in the order this library was given its items.</returns>
    public IReadOnlyList<BaseItem> GetItemList(InternalItemsQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);

        var wanted = query.HasAnyProviderId ?? [];

        return _items.Values
            .Where(item => KindOf(item) is { } kind && query.IncludeItemTypes.Contains(kind))
            .Where(item => wanted.Any(pair =>
                item.ProviderIds.TryGetValue(pair.Key, out var held)
                && string.Equals(held, pair.Value, StringComparison.Ordinal)))
            .ToList();
    }

    private static BaseItemKind? KindOf(BaseItem item) => item switch
    {
        MediaBrowser.Controller.Entities.Movies.Movie => BaseItemKind.Movie,
        MediaBrowser.Controller.Entities.TV.Series => BaseItemKind.Series,
        MediaBrowser.Controller.Entities.TV.Episode => BaseItemKind.Episode,
        _ => null,
    };

    private static NotSupportedException Unasked() =>
        new("This plugin asks a library two questions and this is not one of them.");

    public event EventHandler<ItemChangeEventArgs>? ItemAdded { add { } remove { } }

    public event EventHandler<ItemChangeEventArgs>? ItemUpdated { add { } remove { } }

    public event EventHandler<ItemChangeEventArgs>? ItemRemoved { add { } remove { } }

    public AggregateFolder RootFolder => throw Unasked();

    public bool IsScanRunning => throw Unasked();

    public BaseItem? ResolvePath( FileSystemMetadata fileInfo, Folder? parent = null, IDirectoryService? directoryService = null) => throw Unasked();

    public IEnumerable<BaseItem> ResolvePaths( IEnumerable<FileSystemMetadata> files, IDirectoryService directoryService, Folder parent, LibraryOptions libraryOptions, CollectionType? collectionType = null) => throw Unasked();

    public Person? GetPerson(string name) => throw Unasked();

    public BaseItem? FindByPath(string path, bool? isFolder) => throw Unasked();

    public MusicArtist GetArtist(string name) => throw Unasked();

    public MusicArtist GetArtist(string name, DtoOptions options) => throw Unasked();

    public Studio GetStudio(string name) => throw Unasked();

    public Genre GetGenre(string name) => throw Unasked();

    public MusicGenre GetMusicGenre(string name) => throw Unasked();

    public Year GetYear(int value) => throw Unasked();

    public Task ValidatePeopleAsync(IProgress<double> progress, CancellationToken cancellationToken) => throw Unasked();

    public Task ValidateMediaLibrary(IProgress<double> progress, CancellationToken cancellationToken) => throw Unasked();

    public Task ValidateTopLibraryFolders(CancellationToken cancellationToken, bool removeRoot = false) => throw Unasked();

    public Task UpdateImagesAsync(BaseItem item, bool forceUpdate = false) => throw Unasked();

    public List<VirtualFolderInfo> GetVirtualFolders() => throw Unasked();

    public List<VirtualFolderInfo> GetVirtualFolders(bool includeRefreshState) => throw Unasked();

    public T? GetItemById<T>(Guid id) where T : BaseItem => throw Unasked();

    public T? GetItemById<T>(Guid id, Guid userId) where T : BaseItem => throw Unasked();

    public T? GetItemById<T>(Guid id, User? user) where T : BaseItem => throw Unasked();

    public Task<IEnumerable<Video>> GetIntros(BaseItem item, User user) => throw Unasked();

    public void AddParts( IEnumerable<IResolverIgnoreRule> rules, IEnumerable<IItemResolver> resolvers, IEnumerable<IIntroProvider> introProviders, IEnumerable<IBaseItemComparer> itemComparers, IEnumerable<ILibraryPostScanTask> postScanTasks) => throw Unasked();

    public IEnumerable<BaseItem> Sort(IEnumerable<BaseItem> items, User? user, IEnumerable<ItemSortBy> sortBy, SortOrder sortOrder) => throw Unasked();

    public IEnumerable<BaseItem> Sort(IEnumerable<BaseItem> items, User? user, IEnumerable<(ItemSortBy OrderBy, SortOrder SortOrder)> orderBy) => throw Unasked();

    public Folder GetUserRootFolder() => throw Unasked();

    public void CreateItem(BaseItem item, BaseItem? parent) => throw Unasked();

    public void CreateItems(IReadOnlyList<BaseItem> items, BaseItem? parent, CancellationToken cancellationToken) => throw Unasked();

    public Task UpdateItemsAsync(IReadOnlyList<BaseItem> items, BaseItem parent, ItemUpdateType updateReason, CancellationToken cancellationToken) => throw Unasked();

    public Task UpdateItemAsync(BaseItem item, BaseItem parent, ItemUpdateType updateReason, CancellationToken cancellationToken) => throw Unasked();

    public Task ReattachUserDataAsync(BaseItem item, CancellationToken cancellationToken) => throw Unasked();

    public BaseItem RetrieveItem(Guid id) => throw Unasked();

    public CollectionType? GetContentType(BaseItem item) => throw Unasked();

    public CollectionType? GetInheritedContentType(BaseItem item) => throw Unasked();

    public CollectionType? GetConfiguredContentType(BaseItem item) => throw Unasked();

    public CollectionType? GetConfiguredContentType(string path) => throw Unasked();

    public List<FileSystemMetadata> NormalizeRootPathList(IEnumerable<FileSystemMetadata> paths) => throw Unasked();

    public void RegisterItem(BaseItem item) => throw Unasked();

    public void DeleteItem(BaseItem item, DeleteOptions options) => throw Unasked();

    public void DeleteItemsUnsafeFast(IEnumerable<BaseItem> items) => throw Unasked();

    public void DeleteItem(BaseItem item, DeleteOptions options, bool notifyParentItem) => throw Unasked();

    public void DeleteItem(BaseItem item, DeleteOptions options, BaseItem parent, bool notifyParentItem) => throw Unasked();

    public UserView GetNamedView( User user, string name, Guid parentId, CollectionType? viewType, string sortName) => throw Unasked();

    public UserView GetNamedView( User user, string name, CollectionType? viewType, string sortName) => throw Unasked();

    public UserView GetNamedView( string name, CollectionType viewType, string sortName) => throw Unasked();

    public UserView GetNamedView( string name, Guid parentId, CollectionType? viewType, string sortName, string uniqueId) => throw Unasked();

    public UserView GetShadowView( BaseItem parent, CollectionType? viewType, string sortName) => throw Unasked();

    public int? GetSeasonNumberFromPath(string path, Guid? parentId) => throw Unasked();

    public bool FillMissingEpisodeNumbersFromPath(Episode episode, bool forceRefresh) => throw Unasked();

    public ItemLookupInfo ParseName(string name) => throw Unasked();

    public Guid GetNewItemId(string key, Type type) => throw Unasked();

    public IEnumerable<BaseItem> FindExtras(BaseItem owner, IReadOnlyList<FileSystemMetadata> fileSystemChildren, IDirectoryService directoryService) => throw Unasked();

    public List<Folder> GetCollectionFolders(BaseItem item) => throw Unasked();

    public List<Folder> GetCollectionFolders(BaseItem item, IEnumerable<Folder> allUserRootChildren) => throw Unasked();

    public LibraryOptions GetLibraryOptions(BaseItem item) => throw Unasked();

    public IReadOnlyList<PersonInfo> GetPeople(BaseItem item) => throw Unasked();

    public IReadOnlyList<PersonInfo> GetPeople(InternalPeopleQuery query) => throw Unasked();

    public IReadOnlyList<Person> GetPeopleItems(InternalPeopleQuery query) => throw Unasked();

    public void UpdatePeople(BaseItem item, List<PersonInfo> people) => throw Unasked();

    public Task UpdatePeopleAsync(BaseItem item, IReadOnlyList<PersonInfo> people, CancellationToken cancellationToken) => throw Unasked();

    public IReadOnlyList<Guid> GetItemIds(InternalItemsQuery query) => throw Unasked();

    public IReadOnlyList<string> GetPeopleNames(InternalPeopleQuery query) => throw Unasked();

    public QueryResult<BaseItem> QueryItems(InternalItemsQuery query) => throw Unasked();

    public string GetPathAfterNetworkSubstitution(string path, BaseItem? ownerItem = null) => throw Unasked();

    public Task<ItemImageInfo> ConvertImageToLocal(BaseItem item, ItemImageInfo image, int imageIndex, bool removeOnFailure = true) => throw Unasked();

    public IReadOnlyList<BaseItem> GetItemList(InternalItemsQuery query, bool allowExternalContent) => throw Unasked();

    public IReadOnlyList<BaseItem> GetItemList(InternalItemsQuery query, List<BaseItem> parents) => throw Unasked();

    public IReadOnlyList<BaseItem> GetLatestItemList(InternalItemsQuery query, IReadOnlyList<BaseItem> parents, CollectionType collectionType) => throw Unasked();

    public IReadOnlyList<string> GetNextUpSeriesKeys(InternalItemsQuery query, IReadOnlyCollection<BaseItem> parents, DateTime dateCutoff) => throw Unasked();

    public QueryResult<BaseItem> GetItemsResult(InternalItemsQuery query) => throw Unasked();

    public bool IgnoreFile(FileSystemMetadata file, BaseItem parent) => throw Unasked();

    public Guid GetStudioId(string name) => throw Unasked();

    public Guid GetGenreId(string name) => throw Unasked();

    public Guid GetMusicGenreId(string name) => throw Unasked();

    public Task AddVirtualFolder(string name, CollectionTypeOptions? collectionType, LibraryOptions options, bool refreshLibrary) => throw Unasked();

    public Task RemoveVirtualFolder(string name, bool refreshLibrary) => throw Unasked();

    public void AddMediaPath(string virtualFolderName, MediaPathInfo mediaPath) => throw Unasked();

    public void UpdateMediaPath(string virtualFolderName, MediaPathInfo mediaPath) => throw Unasked();

    public void RemoveMediaPath(string virtualFolderName, string mediaPath) => throw Unasked();

    public QueryResult<(BaseItem Item, ItemCounts ItemCounts)> GetGenres(InternalItemsQuery query) => throw Unasked();

    public QueryResult<(BaseItem Item, ItemCounts ItemCounts)> GetMusicGenres(InternalItemsQuery query) => throw Unasked();

    public QueryResult<(BaseItem Item, ItemCounts ItemCounts)> GetStudios(InternalItemsQuery query) => throw Unasked();

    public QueryResult<(BaseItem Item, ItemCounts ItemCounts)> GetArtists(InternalItemsQuery query) => throw Unasked();

    public IReadOnlyDictionary<string, MusicArtist[]> GetArtists(IReadOnlyList<string> names) => throw Unasked();

    public QueryResult<(BaseItem Item, ItemCounts ItemCounts)> GetAlbumArtists(InternalItemsQuery query) => throw Unasked();

    public QueryResult<(BaseItem Item, ItemCounts ItemCounts)> GetAllArtists(InternalItemsQuery query) => throw Unasked();

    public int GetCount(InternalItemsQuery query) => throw Unasked();

    public ItemCounts GetItemCounts(InternalItemsQuery query) => throw Unasked();

    public Task RunMetadataSavers(BaseItem item, ItemUpdateType updateReason) => throw Unasked();

    public BaseItem GetParentItem(Guid? parentId, Guid? userId) => throw Unasked();

    public void QueueLibraryScan() => throw Unasked();

    public void CreateShortcut(string virtualFolderPath, MediaPathInfo pathInfo) => throw Unasked();
}
