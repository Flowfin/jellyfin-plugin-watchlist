# Installing from the published catalogue

Everything before this proves the artifact exists. This is the record of installing
it the way a user does: adding the address to a stock server, letting that server
resolve the catalogue, install the archive and load it, and then walking one add,
one removal and one upgrade against the running result.

`first-load.md` beside this file is a different record and is easy to mistake for
this one. There the assembly was copied onto a server, which proves the code loads.
Here nothing was copied: the server was given an address and fetched what it found,
which is the path every installed copy takes and the only one a user has. A run that
proves the code loads says nothing about whether the catalogue entry, the archive it
points at and the checksum beside it are the ones a server can act on.

Both phases below ran on this machine on 2026-09-05, in containers, through the HTTP
API. Nothing was rendered, nobody looked at a screen, and no step needed elevated
rights on the host.

## The runs these readings come from

| | |
| --- | --- |
| Server image | `jellyfin/jellyfin@sha256:aefb67e6a7ff1debdd154a78a7bbb780fd0c873d8639210a7f6a2016ad2b35db`, reported 10.11.11 |
| Catalogue | `https://flowfin.dev/manifest.json` |
| Plugin identifier | 6e1631d7-aa49-494d-a23b-d5785853fc0a |
| Versions walked | 0.1.0.0 and 0.1.1.0, the two published releases |
| Phase A container | `wl76-clean`, on 127.0.0.1:18112 |
| Phase B container | `wl76-upgrade`, on 127.0.0.1:18113 |

Each phase started from an empty configuration directory and completed the startup
wizard through the API, so neither met state the other left. The versions are the two
this repository has released, and the identifier is the one it declares:

    grep -nE '^(name|guid|version|targetAbi):' build.yaml
    2:name: "Watchlist"
    3:guid: "6e1631d7-aa49-494d-a23b-d5785853fc0a"
    43:version: "0.1.1.0"
    61:targetAbi: "10.11.0.0"

The server line is 10.11 because that is the only line this tree carries a package
set for, and because `0.1.0.0` answers `NotSupported` on a 10.11 server below
10.11.11, so a walk of the older release on any lower floor would be a record of it
refusing to load rather than of an upgrade.

## Phase A: a clean install of the newest release

The address was added the way the dashboard adds one, and the server was then asked
what it could see:

    POST /Repositories  [{"Name":"Flowfin","Url":"https://flowfin.dev/manifest.json","Enabled":true}]
    -> 204

    GET /Packages
    -> 200
    {"name":"Watchlist","versions":["0.1.1.0","0.1.0.0"]}

Both published versions are resolvable, which is the first thing this run had to
establish: an entry a server cannot parse and an entry that is absent look the same
from the tracker.

    POST /Packages/Installed/Watchlist?assemblyGuid=6e1631d7-aa49-494d-a23b-d5785853fc0a&version=0.1.1.0&repositoryUrl=https%3A%2F%2Fflowfin.dev%2Fmanifest.json
    -> 204
    POST /System/Restart
    -> 204

The server's own log for that install and the load after the restart:

    [13:47:33] [INF] Emby.Server.Implementations.Updates.InstallationManager: Plugin installed: Watchlist 0.1.1.0
    [13:47:49] [INF] Emby.Server.Implementations.Plugins.PluginManager: Loaded assembly Jellyfin.Plugin.Watchlist, Version=0.1.1.0, Culture=neutral, PublicKeyToken=null from /config/plugins/Watchlist_0.1.1.0/Jellyfin.Plugin.Watchlist.dll
    [13:47:49] [INF] Emby.Server.Implementations.Plugins.PluginManager: Loaded plugin: Watchlist 0.1.1.0

and what it then listed, beside the five metadata plugins the image ships with:

    GET /Plugins
    {"Name":"Watchlist","Version":"0.1.1.0","Status":"Active"}

### One add and one removal, read the way a client reads them

A stock server has no media and the add endpoint refuses an item the library cannot
answer for, so one film was generated inside the container with the server's own
encoder and a movie library added over it. The readings, in the order they were
taken:

    POST /Watchlist/Items/1413323b6c377951e6a509c1c76a2641   -> 204
    GET  /Watchlist/Items                                    -> ["Catalogue Probe (2020)"]
    GET  /Items?userId=...&recursive=true&includeItemTypes=Playlist  -> []

The empty playlist listing is not decoration. It is read one step before the
assertion it guards: without it, a server that had held a matching playlist all
along would satisfy the next reading with the projection having done nothing.

    POST /ScheduledTasks/Running/614eaf0f77d38fa3aec4279a4958df02  -> 204, then Completed

    GET  /Items?userId=...&recursive=true&includeItemTypes=Playlist
    -> ["Watchlist (plugin)"], id 91b4a5b62a91c4cc81ffc2433649f5e1
    GET  /Playlists/91b4a5b62a91c4cc81ffc2433649f5e1/Items?userId=...
    -> [{"Name":"Catalogue Probe (2020)","PlaylistItemId":"1413323b6c377951e6a509c1c76a2641"}]

Those two are the queries a stock client issues, and they are the whole of the
claim this plugin makes about clients: the film is on a playlist the user owns,
named by the plugin's own configuration, with nothing installed on the client side.

The removal was made the way a client makes one, against the playlist rather than
against this plugin's endpoint:

    DELETE /Playlists/91b4a5b62a91c4cc81ffc2433649f5e1/Items?entryIds=1413323b6c377951e6a509c1c76a2641  -> 204
    POST   /ScheduledTasks/Running/614eaf0f77d38fa3aec4279a4958df02                                     -> Completed
    GET    /Watchlist/Items                                                                             -> []
    GET    /Playlists/91b4a5b62a91c4cc81ffc2433649f5e1/Items?userId=...                                 -> []

So the edit a user makes on a client reaches the stored list, on a server that got
the plugin from the catalogue and from nowhere else.

## Phase B: the previous release, then the upgrade

A second server, empty, was given the same address and asked for the older release:

    POST /Packages/Installed/Watchlist?...&version=0.1.0.0  -> 204
    [13:51:50] Plugin installed: Watchlist 0.1.0.0
    [13:52:14] Loaded plugin: Watchlist 0.1.0.0
    GET /Plugins -> {"Name":"Watchlist","Version":"0.1.0.0","Status":"Active"}

**THE REPOSITORY WAS DISABLED BEFORE THAT RESTART, AND THAT IS A FINDING RATHER
THAN A CONVENIENCE.** On the first attempt at this phase the older release was
installed, the server restarted, and its own update check installed the newer one
five seconds after loading the older one, with nobody having asked:

    [13:38:27] Plugin installed: Watchlist 0.1.0.0
    [13:38:42] Loaded assembly Jellyfin.Plugin.Watchlist, Version=0.1.0.0 ... from /config/plugins/Watchlist_0.1.0.0/...
    [13:38:43] Loaded plugin: Watchlist 0.1.0.0
    [13:38:47] Plugin installed: Watchlist 0.1.1.0

That is the stock server's `Update Plugins` task and not anything this plugin does.
It means an administrator who deliberately installs an older version of this plugin,
with the catalogue enabled, does not keep it: the newer one is fetched on the next
startup, and the only trace is a log line. So the upgrade below could not be walked
at all without turning the repository off for the length of the older release's life,
which is what `POST /Repositories` with `Enabled: false` does and what the run did.

With the older release in place, one film was put on the list and projected:

    POST /Watchlist/Items/1413323b6c377951e6a509c1c76a2641  -> 204
    POST /ScheduledTasks/Running/614eaf0f77d38fa3aec4279a4958df02 -> Completed
    GET  /Watchlist/Items -> ["Catalogue Probe (2020)"]
    GET  /Playlists/91b4a5b62a91c4cc81ffc2433649f5e1/Items?userId=... -> ["Catalogue Probe (2020)"]

Then the repository was enabled again and the newer release installed through the
same route the dashboard uses:

    POST /Repositories  (Enabled=true)                      -> 204
    POST /Packages/Installed/Watchlist?...&version=0.1.1.0  -> 204
    POST /System/Restart                                    -> 204
    [13:53:58] Plugin installed: Watchlist 0.1.1.0
    [13:54:16] Loaded plugin: Watchlist 0.1.1.0
    GET /Plugins -> {"Name":"Watchlist","Version":"0.1.1.0","Status":"Active"}

### What survived, read twice and in two places

Through the running server, with no task run in between, so this is the newer
assembly reading what the older one wrote:

    GET /Watchlist/Items -> ["Catalogue Probe (2020)"]
    GET /Items?userId=...&recursive=true&includeItemTypes=Playlist -> ["Watchlist (plugin)"]
    GET /Playlists/91b4a5b62a91c4cc81ffc2433649f5e1/Items?userId=... -> ["Catalogue Probe (2020)"]

and on disk, where the timestamps say which version wrote it:

    docker exec wl76-upgrade cat /config/plugins/Jellyfin.Plugin.Watchlist/2ed9a207c63b4121a041c7869d59b333.json
    {
      "SchemaVersion": 4,
      "UserId": "2ed9a207-c63b-4121-a041-c7869d59b333",
      "Entries": [
        {
          "ItemId": "1413323b-6c37-7951-e6a5-09c1c76a2641",
          "Kind": "Movie",
          "AddedAt": "2026-09-05T13:53:19.328003+00:00",
          "Source": "Api"
        }
      ],
      "Projection": {
        "PlaylistId": "91b4a5b6-2a91-c4cc-81ff-c2433649f5e1",
        "LastNameWritten": "Watchlist (plugin)",
        "ProjectedItemIds": ["1413323b-6c37-7951-e6a5-09c1c76a2641"],
        "WrittenAt": "2026-09-05T13:53:25.2657665+00:00"
      }
    }

Both timestamps are before 13:53:58, which is when the newer release was installed,
so the entry and the projection record in that file were written by 0.1.0.0 and read
back by 0.1.1.0. `SchemaVersion` is 4 on both sides.

The directory the upgrade left:

    docker exec wl76-upgrade ls -1 /config/plugins/
    configurations
    Jellyfin.Plugin.Watchlist
    Watchlist_0.1.1.0

The older release's directory is gone rather than left beside the new one, the store
directory the server never touches is untouched, and the plugin's own configuration
is where it was, still declaring the list name the playlist above carries.

## What this run did not cover, and it is four things

**One server line.** The first condition asks for a stock server of each supported
line. There is one package set in this tree and both catalogue entries declare
`10.11.0.0`, so what is recorded here is the 10.11 line and nothing else. The second
line is #4 and the stable 12.0 release #4 itself waits on.

**Nothing was rendered.** Every reading above is an HTTP response, a log line or a
listing of the volume. `client-verification.md` beside this file is the register for
a person looking at a screen, and this run adds no row to it.

**The catalogue was read, never audited.** The server was given the address and
allowed to fetch what it found. Nothing here compared the archive it downloaded
against the checksum the catalogue declares, or the catalogue against the releases
it was generated from; the server's own verification is what stands behind the
install, and this record does not restate it as a second check.

**A failed check does not block anything yet.** The fifth condition on #76 asks that
a failure of this walk stop a release rather than being noted afterwards. Nothing in
this repository makes that so: this page is a record, no run produces it and no gate
reads it, and building one is a change outside the `docs/` scope that issue declares.
That is stated here rather than left to be discovered by somebody who takes the
presence of this page for a control.
