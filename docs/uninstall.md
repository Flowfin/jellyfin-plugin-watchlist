# What an uninstall leaves behind

Somebody removing this plugin needs to know what happens to their lists before they
remove it, not afterwards. This file is the decision and the measurement it was made
from.

## The decision

**The projected playlists survive an uninstall as ordinary playlists owned by the
users they belong to. So do the stored lists.** Nothing is deleted except the
plugin's own installed folder, which the server deletes by itself.

That is not a preference between two workable options. The server does not ask a
plugin anything on the way out: it deletes one directory and reports the plugin gone.
A plugin cannot run at uninstall time, so a decision that required it to clean up
after itself would be a decision nothing could carry out.

## What the server actually does, measured

Against a stock `jellyfin/jellyfin:10.11.11` container with the plugin installed and
loaded. Before the uninstall, with a stored list and a playlist directory in place:

    docker exec wl-1011 sh -c 'find /config/plugins /config/data/playlists -maxdepth 2 | sort'
    /config/data/playlists
    /config/data/playlists/Watchlist
    /config/data/playlists/Watchlist/playlist.xml
    /config/plugins
    /config/plugins/configurations
    /config/plugins/configurations/Jellyfin.Plugin.MusicBrainz.xml
    /config/plugins/configurations/Jellyfin.Plugin.Tmdb.xml
    /config/plugins/configurations/Jellyfin.Plugin.Watchlist.xml
    /config/plugins/.jellyfin-plugin
    /config/plugins/Jellyfin.Plugin.Watchlist
    /config/plugins/Jellyfin.Plugin.Watchlist/11111111111111111111111111111111.json
    /config/plugins/Watchlist_0.1.0.0
    /config/plugins/Watchlist_0.1.0.0/Jellyfin.Plugin.Watchlist.dll
    /config/plugins/Watchlist_0.1.0.0/meta.json

The uninstall:

    curl -X DELETE "$BASE/Plugins/6e1631d7aa49494da23bd5785853fc0a/0.1.0.0" ...
    204

Afterwards:

    docker exec wl-1011 sh -c 'find /config/plugins /config/data/playlists -maxdepth 2 | sort'
    /config/data/playlists
    /config/data/playlists/Watchlist
    /config/data/playlists/Watchlist/playlist.xml
    /config/plugins
    /config/plugins/configurations
    /config/plugins/configurations/Jellyfin.Plugin.MusicBrainz.xml
    /config/plugins/configurations/Jellyfin.Plugin.Tmdb.xml
    /config/plugins/configurations/Jellyfin.Plugin.Watchlist.xml
    /config/plugins/.jellyfin-plugin
    /config/plugins/Jellyfin.Plugin.Watchlist
    /config/plugins/Jellyfin.Plugin.Watchlist/11111111111111111111111111111111.json

    curl "$BASE/Plugins" ... | grep '"Name":"Watchlist"'
    (no Watchlist entry)

One directory gone, everything else untouched, and the server no longer reports the
plugin. That matches what the source does, which is delete the plugin's own path and
nothing else:

    git show v10.11.11:Emby.Server.Implementations/Plugins/PluginManager.cs | sed -n '648,655p'
        private bool DeletePlugin(LocalPlugin plugin)
        {
            // Attempt a cleanup of old folders.
            try
            {
                Directory.Delete(plugin.Path, true);
                _logger.LogDebug("Deleted {Path}", plugin.Path);
            }

## What is left, with the paths

Paths are shown as they are inside the official container image, where the server's
configuration directory is `/config`. On another installation the same three things
sit under whatever that installation uses for its configuration and data directories.

| What | Path | Holds |
| --- | --- | --- |
| The plugin's data folder | `/config/plugins/Jellyfin.Plugin.Watchlist/` | one JSON document per user, named after that user's identifier |
| The plugin's configuration | `/config/plugins/configurations/Jellyfin.Plugin.Watchlist.xml` | the server-wide settings an administrator saved |
| The projected playlists | `/config/data/playlists/<playlist name>/` | one directory per playlist, owned by the server, not by this plugin |

The data folder path is the one the base class computes from the assembly file name:

    git show v10.11.11:MediaBrowser.Common/Plugins/BasePluginOfT.cs | sed -n '49,53p'
            var dataFolderPath = Path.Combine(ApplicationPaths.PluginsPath, Path.GetFileNameWithoutExtension(assemblyFilePath));
            if (Version is not null && !Directory.Exists(dataFolderPath))
            {
                // Try again with the version number appended to the folder name.
                dataFolderPath += "_" + Version;
            }

`Version` is still null when that line runs, because it is set by the `SetAttributes`
call four lines below it, so the folder carries no version suffix and one folder
serves every version of the plugin. That is what makes an upgrade keep the lists
rather than start again.

The store takes its folder as a parameter rather than reading it from the plugin, so
nothing in this repository wires the two together yet:

    git grep -n 'DataFolderPath' -- Jellyfin.Plugin.Watchlist/
    Jellyfin.Plugin.Watchlist/Store/WatchlistDocumentStore.cs:76:    public string DataFolderPath => _dataFolderPath;

The path above is therefore where the documents are going to be and not where they
have been observed. The file planted in the measurement stands in for one.

## Removing the rest by hand

Someone who wants nothing of this plugin left removes the three paths above after
uninstalling. Deleting the data folder deletes every user's list, and there is no
undo, which is why the plugin does not do it on somebody's behalf.

## A reinstall

A reinstall meets the lists and the playlists it left behind. The stored documents
are read as they are, under the version rule, so an install that is really an upgrade
loses nothing. The playlists are met by the adoption rule in #41: a playlist owned by
a user whose name matches the configured one is adopted rather than duplicated. Until
that rule ships, a reinstall would create a second playlist beside the old one, which
is the reason #41 exists.

## Where this differs from what #37 assumed

#37 says the plugin writes "playlists, which are directories under the server's
playlist path, and nothing else". The measurement above shows two more: the data
folder holding the per-user documents, and the configuration file the server writes
when an administrator saves the settings page. Both survive an uninstall and both are
in the table.
