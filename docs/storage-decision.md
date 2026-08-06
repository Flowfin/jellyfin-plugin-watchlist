# Where the watchlist lives

The plugin has one job: a watchlist held on the server that a client shows
without being changed. Where the list lives decides everything else, so the
options are measured here, the decision is taken here, and the reasons are
written down so a later reader does not have to reconstruct them.

The candidates are the surfaces a stock client already renders: a library, a
collection, a playlist, the favourites flag, and anything the plugin stores
itself.

## The decision

**The plugin keeps its own per-user store as the record, and projects that store
into one private playlist per user as the surface a client renders.**

## What was measured, and with which command

Every command below is run in a checkout of `jellyfin/jellyfin` at the tag of the
current stable line, and the output is what that command printed.

A playlist is owned by one user and is private unless shared:

    git show v10.11.11:MediaBrowser.Controller/Playlists/Playlist.cs | sed -n '35,45p'
            public Playlist()
            {
                Shares = [];
                OpenAccess = false;
            }

            public Guid OwnerUserId { get; set; }

            public bool OpenAccess { get; set; }

            public IReadOnlyList<PlaylistUserPermissions> Shares { get; set; }

A collection is not per user. Its visibility is decided by which library folders
a user can see:

    git show v10.11.11:MediaBrowser.Controller/Entities/Movies/BoxSet.cs | sed -n '170,178p'
                    var userLibraryFolderIds = GetLibraryFolderIds(user);
                    var libraryFolderIds = LibraryFolderIds ?? GetLibraryFolderIds();

                    if (libraryFolderIds.Length == 0)
                    {
                        return true;
                    }

                    return userLibraryFolderIds.Any(i => libraryFolderIds.Contains(i));

and changing one needs a permission most users do not have:

    git show v10.11.11:MediaBrowser.Controller/Entities/Movies/BoxSet.cs | sed -n '108,111p'
            public override bool IsAuthorizedToDelete(User user, List<Folder> allCollectionFolders)
            {
                return user.HasPermission(PermissionKind.IsAdministrator) || user.HasPermission(PermissionKind.EnableCollectionManagement);
            }

A playlist cannot hold a series as one entry. Anything that is a folder is
expanded into its non-folder children when it is added:

    git show v10.11.11:MediaBrowser.Controller/Playlists/Playlist.cs | sed -n '217,229p'
                if (item is Folder folder)
                {
                    var query = new InternalItemsQuery(user)
                    {
                        Recursive = true,
                        IsFolder = false,
                        MediaTypes = [MediaType.Audio, MediaType.Video],
                        EnableTotalRecordCount = false,
                        DtoOptions = options
                    };

                    return folder.GetItemList(query);
                }

and playlist children are non-folders by construction:

    git show v10.11.11:MediaBrowser.Controller/Playlists/Playlist.cs | sed -n '164,168p'
            private IReadOnlyList<BaseItem> GetPlayableItems(User user, InternalItemsQuery query)
            {
                query ??= new InternalItemsQuery(user);

                query.IsFolder = false;

A playlist is a directory on disk and its path is de-duplicated by appending a
digit, so a name is not an identity:

    git show v10.11.11:Emby.Server.Implementations/Playlists/PlaylistManager.cs | sed -n '184,189p'
            private static string GetTargetPath(string path)
            {
                while (Directory.Exists(path))
                {
                    path += "1";
                }

A plugin has a data folder of its own:

    git show v10.11.11:MediaBrowser.Common/Plugins/BasePlugin.cs | grep -n 'DataFolderPath'
    49:        public string DataFolderPath { get; private set; }
    84:            DataFolderPath = dataFolderPath;

## The reasons, one per rejected option and one per half of the decision

- **Collection-backed is refused** because a collection is visible by library
  permission rather than by ownership, so on a server with two users a watchlist
  would be a shared object, and changing one needs a permission most users do
  not have.
- **A per-user library folder is refused** because it means writing files into a
  media path, paying a library scan for every change, and duplicating metadata
  entries for the same media.
- **The favourites flag is refused** because it already exists with a different
  meaning for users, and taking it over destroys data a user already has.
- **Playlist-backed alone is refused as the record** because a playlist cannot
  hold a series, its name is not an identity, and a user can rename or delete it
  at any time. A record that a user can delete by accident is not a record.
- **An own store alone is refused** because nothing renders it. The requirement
  is that unchanged clients show the list, and no client knows a plugin's private
  files.
- **The pair works** because each half does what the other cannot: the store
  holds the truth, including series and the order and the times, and the playlist
  is a rendering of it that every client already knows how to show and to edit.

## What the decision costs

Two costs, in plain words, and neither of them goes away later.

**A series appears in the playlist as one episode.** The store can hold a series
as one entry; the playlist cannot, because the server expands a folder into its
non-folder children on the way in, as measured above. Which episode is shown, or
whether a series is shown at all, is decided in M3.

**A user who deletes the playlist loses the surface but not the list.** The
record is the plugin's own document, so the entries survive. What the user loses
until the next reconciliation is the thing they can see and touch on a client.

## The means

The plugin is C# on the runtime the server itself runs. That is forced rather
than preferred: the server loads plugin assemblies into its own process, so
anything written in another means would have to be wrapped in this one anyway.

The forced part is held to its smallest surface. The store is plain files a
person can read and a test can write with no server present, nothing in the plan
adds a second runtime, a database engine or a service to the machine, and the
tests are the suite the project already has rather than a parallel apparatus.
The three things the means has to carry are carried: a rule can be refused in
code, a proof can be executed by the suite, and a number can carry the command
that produced it.

## What this note does not decide

Whether a list is ever shared between users, and what happens if a server line
grows a watchlist of its own, are open. They are collected in issue #1 and are
not answered here.
