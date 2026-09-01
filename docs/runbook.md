# Operator runbook

What installing this plugin does to a server, what each later operation changes on
disk, and what an administrator sees afterwards. Every reading below was taken from a
run against a stock Jellyfin container. Nothing here is derived from the source: where
a run could not be made, the section says so instead of describing what the code
implies.

Paths are shown as they are inside the official container image, where the server's
configuration directory is `/config`. On another installation the same paths sit under
whatever that installation uses for its configuration and data directories.

## The run these readings come from

| | |
| --- | --- |
| Built from commit | `b6fda94bae52bab724e3e7aeb721ba59429a9698` |
| First plugin version installed | 0.1.0.0, `4233b39885a9f494bc45ed08e2489b0a51f2d346e033af2284e39e7ac6558695` |
| Second plugin version installed | 0.1.0.1, `c7b7cf23817ac862443b268e34fc3e0028688f0482c490d963c2bc3b61db6b3a` |
| Current stable line | `jellyfin/jellyfin@sha256:aefb67e6a7ff1debdd154a78a7bbb780fd0c873d8639210a7f6a2016ad2b35db`, reported 10.11.11 |
| Next line | `jellyfin/jellyfin@sha256:db1df1d111c27ba1f10bb8fce6630892f66eb66b12c2b24e79011453ac18b3db`, reported 12.0.0 |

The two assemblies were produced from that commit in a container carrying the SDK for
the framework the projects target, so the build did not depend on what happens to be
installed on any one machine:

    docker run --rm -v <checkout>:/src:ro -v <out>:/out mcr.microsoft.com/dotnet/sdk:9.0 \
      sh -c 'cp -r /src /work && cd /work && dotnet publish \
        Jellyfin.Plugin.Watchlist/Jellyfin.Plugin.Watchlist.csproj -c Release -o /out'
    sha256sum Jellyfin.Plugin.Watchlist.dll
    4233b39885a9f494bc45ed08e2489b0a51f2d346e033af2284e39e7ac6558695

The second version was the same command with the version properties overridden on the
command line, because `build.yaml` carries one version and nothing has been released,
so there is no pair of published versions to move between:

    dotnet publish ... -p:Version=0.1.0.1 -p:AssemblyVersion=0.1.0.1 -p:FileVersion=0.1.0.1
    sha256sum Jellyfin.Plugin.Watchlist.dll
    c7b7cf23817ac862443b268e34fc3e0028688f0482c490d963c2bc3b61db6b3a

One server ran at a time against one persistent volume, so every later step met the
state the earlier one left:

    docker volume create wl67-config
    docker run -d --name wl67-1011 -p 18196:8096 -v wl67-config:/config \
      jellyfin/jellyfin:10.11.11

The startup wizard was completed through the API and an access token taken from it.
Every reading below is a log line, an HTTP response or a listing of the volume. No
browser was involved and nothing was rendered.

## The three paths this plugin owns

These are what every section below refers to, and they are what a backup has to carry.
The listing is from the volume with the plugin installed and a list stored:

    docker exec wl67-1011 sh -c 'find /config/plugins -maxdepth 2 | sort'
    /config/plugins
    /config/plugins/configurations
    /config/plugins/configurations/Jellyfin.Plugin.MusicBrainz.xml
    /config/plugins/configurations/Jellyfin.Plugin.Tmdb.xml
    /config/plugins/configurations/Jellyfin.Plugin.Watchlist.xml
    /config/plugins/.jellyfin-plugin
    /config/plugins/Jellyfin.Plugin.Watchlist
    /config/plugins/Jellyfin.Plugin.Watchlist/b701ce8a56e64b31ad888ede6991fde3.json
    /config/plugins/Watchlist_0.1.0.0
    /config/plugins/Watchlist_0.1.0.0/Jellyfin.Plugin.Watchlist.dll
    /config/plugins/Watchlist_0.1.0.0/meta.json

| What | Path | Holds |
| --- | --- | --- |
| The installed plugin | `/config/plugins/Watchlist_<version>/` | the assembly and its packaging metadata, one directory per installed version |
| The lists | `/config/plugins/Jellyfin.Plugin.Watchlist/` | one JSON document per user, named after that user's identifier with the hyphens removed |
| The server-wide settings | `/config/plugins/configurations/Jellyfin.Plugin.Watchlist.xml` | what an administrator saved on the plugin's page |

The lists folder carries no version in its name, which is what makes an upgrade keep
the lists rather than start again. `docs/uninstall.md` is where that is read out of
the server's own source.

## Install

The plugin directory is created, the assembly and its `meta.json` are placed in it,
and the server is restarted:

    docker exec wl67-1011 sh -c 'mkdir -p /config/plugins/Watchlist_0.1.0.0'
    docker cp Jellyfin.Plugin.Watchlist.dll wl67-1011:/config/plugins/Watchlist_0.1.0.0/
    docker cp meta.json                     wl67-1011:/config/plugins/Watchlist_0.1.0.0/
    docker restart wl67-1011

The server says it loaded the assembly it was given:

    docker exec wl67-1011 sh -c 'sha256sum /config/plugins/Watchlist_0.1.0.0/Jellyfin.Plugin.Watchlist.dll'
    4233b39885a9f494bc45ed08e2489b0a51f2d346e033af2284e39e7ac6558695
    docker logs wl67-1011 2>&1 | grep -i watchlist
    Emby.Server.Implementations.Plugins.PluginManager: Loaded assembly Jellyfin.Plugin.Watchlist, Version=0.1.0.0, Culture=neutral, PublicKeyToken=null from /config/plugins/Watchlist_0.1.0.0/Jellyfin.Plugin.Watchlist.dll
    Emby.Server.Implementations.Plugins.PluginManager: Loaded plugin: Watchlist 0.1.0.0

What an administrator sees afterwards is the entry the dashboard's plugin list is
built from:

    curl -s "$BASE/Plugins" -H "$H"
    {"Name": "Watchlist", "Version": "0.1.0.0",
     "ConfigurationFileName": "Jellyfin.Plugin.Watchlist.xml", "Description": "",
     "Id": "6e1631d7aa49494da23bd5785853fc0a", "CanUninstall": true,
     "HasImage": false, "Status": "Active"}

**What changes on disk.** Only `/config/plugins/Watchlist_0.1.0.0/`. The settings file
is not written until somebody saves the page, and the lists folder is not created
until something writes a list. Saving the page once produced the settings file:

    curl -s -X POST "$BASE/Plugins/6e1631d7aa49494da23bd5785853fc0a/Configuration" \
      -H "$H" -H 'Content-Type: application/json' -d '{"MaxEntriesPerUser":4242}'
    204
    docker exec wl67-1011 sh -c 'ls -1 /config/plugins/configurations/'
    Jellyfin.Plugin.MusicBrainz.xml
    Jellyfin.Plugin.Tmdb.xml
    Jellyfin.Plugin.Watchlist.xml

`Description` comes back empty because the `meta.json` beside the assembly carried a
short description while `build.yaml` carries a long one. That is a property of how
this run packaged the plugin and not of the plugin.

## Upgrade between plugin versions

The old directory is removed, the new one is put in its place, and the server is
restarted. Before the upgrade, the stored list and the settings file:

    docker exec wl67-1011 sh -c 'sha256sum /config/plugins/Jellyfin.Plugin.Watchlist/b701ce8a56e64b31ad888ede6991fde3.json'
    2c85fb711d543dc204980158cd5f72e22af7cd79dbdb6e6de4cbfe217c670524

The upgrade:

    docker exec wl67-1011 sh -c 'rm -rf /config/plugins/Watchlist_0.1.0.0 && mkdir -p /config/plugins/Watchlist_0.1.0.1'
    docker cp Jellyfin.Plugin.Watchlist.dll wl67-1011:/config/plugins/Watchlist_0.1.0.1/
    docker cp meta.json                     wl67-1011:/config/plugins/Watchlist_0.1.0.1/
    docker restart wl67-1011

Afterwards:

    docker logs wl67-1011 2>&1 | grep -i 'Loaded plugin: Watchlist'
    Emby.Server.Implementations.Plugins.PluginManager: Loaded plugin: Watchlist 0.1.0.0
    Emby.Server.Implementations.Plugins.PluginManager: Loaded plugin: Watchlist 0.1.0.1
    curl -s "$BASE/Plugins" -H "$H"
    {"Name": "Watchlist", "Version": "0.1.0.1", ... "Status": "Active"}

The lists and the settings are untouched, which is the question an operator actually
asks about an upgrade:

    docker exec wl67-1011 sh -c 'sha256sum /config/plugins/Jellyfin.Plugin.Watchlist/b701ce8a56e64b31ad888ede6991fde3.json /config/plugins/configurations/Jellyfin.Plugin.Watchlist.xml'
    2c85fb711d543dc204980158cd5f72e22af7cd79dbdb6e6de4cbfe217c670524  /config/plugins/Jellyfin.Plugin.Watchlist/b701ce8a56e64b31ad888ede6991fde3.json
    fa0b4eb60df05247fef0e5ee2fd535c6be58f8e0cf95151edaaaf7f7708c9b19  /config/plugins/configurations/Jellyfin.Plugin.Watchlist.xml
    curl -s "$BASE/Plugins/6e1631d7aa49494da23bd5785853fc0a/Configuration" -H "$H"
    {"MaxEntriesPerUser":4242}

The saved value of 4242 survived, and it is not the default, so this reading is the
settings file being kept rather than a default being written again.

**What changes on disk.** One directory under `/config/plugins/` is replaced by
another. Leaving the old directory in place is what an operator must not do: both
would be loaded and the server would report two versions of the same plugin.

## Upgrade the server across a supported line

The container is stopped and a container of the other line is started against the same
volume. Nothing about the plugin is touched:

    docker stop wl67-1011
    docker run -d --name wl67-12rc4 -p 18197:8096 -v wl67-config:/config \
      jellyfin/jellyfin:12.0-rc4

    curl -s "$BASE/System/Info/Public"
    server version: 12.0.0 | startup wizard completed: True
    docker logs wl67-12rc4 2>&1 | grep -i watchlist
    Emby.Server.Implementations.Plugins.PluginManager: Loaded assembly Jellyfin.Plugin.Watchlist, Version=0.1.0.1, Culture=neutral, PublicKeyToken=null from /config/plugins/Watchlist_0.1.0.1/Jellyfin.Plugin.Watchlist.dll
    Emby.Server.Implementations.Plugins.PluginManager: Loaded plugin: Watchlist 0.1.0.1

The stored list and the settings are the same bytes across the line change:

    docker exec wl67-12rc4 sh -c 'sha256sum /config/plugins/Jellyfin.Plugin.Watchlist/b701ce8a56e64b31ad888ede6991fde3.json /config/plugins/configurations/Jellyfin.Plugin.Watchlist.xml'
    2c85fb711d543dc204980158cd5f72e22af7cd79dbdb6e6de4cbfe217c670524  /config/plugins/Jellyfin.Plugin.Watchlist/b701ce8a56e64b31ad888ede6991fde3.json
    fa0b4eb60df05247fef0e5ee2fd535c6be58f8e0cf95151edaaaf7f7708c9b19  /config/plugins/configurations/Jellyfin.Plugin.Watchlist.xml

**What changes on disk.** The server migrates its own database and writes its own
files. None of the three paths this plugin owns is among them.

**The direction this was run in.** Forward only, from the current stable line to the
next one. Going back to the earlier line on a volume a later server has migrated was
not attempted, so nothing here says whether that works. An operator who wants a way
back takes the backup below before starting the newer server, and restores it.

## Disable

    curl -s -X POST "$BASE/Plugins/6e1631d7aa49494da23bd5785853fc0a/0.1.0.1/Disable" -H "$H"
    204

The entry changes immediately, and it says a restart is owed rather than that the
plugin is off:

    {"Name": "Watchlist", "Version": "0.1.0.1", ... "Status": "Restart"}

After the restart the server skips it and the entry says so. Note that the disabled
entry no longer carries `ConfigurationFileName`, so the plugin's page is not offered:

    docker restart wl67-1011
    docker logs wl67-1011 2>&1 | grep -i 'disabled plugin'
    Emby.Server.Implementations.Plugins.PluginManager: Skipping disabled plugin 0.1.0.1 of Watchlist
    curl -s "$BASE/Plugins" -H "$H"
    {"Name": "Watchlist", "Version": "0.1.0.1", "Description": "",
     "Id": "6e1631d7aa49494da23bd5785853fc0a", "CanUninstall": true,
     "HasImage": false, "Status": "Disabled"}

The lists are kept:

    docker exec wl67-1011 sh -c 'sha256sum /config/plugins/Jellyfin.Plugin.Watchlist/b701ce8a56e64b31ad888ede6991fde3.json'
    2c85fb711d543dc204980158cd5f72e22af7cd79dbdb6e6de4cbfe217c670524

**What changes on disk.** The `meta.json` inside the installed plugin directory, and
nothing else of this plugin's. That was measured by hashing every file under `/config`
before and after a disable, on the next line, and reading the difference:

    < d7752b1cfab19df32ada8544ee541d3a  /config/plugins/Watchlist_0.1.0.1/meta.json
    > fff40de5c096009ac25ebd0a1727d0bc  /config/plugins/Watchlist_0.1.0.1/meta.json

    docker exec wl67-12rc4 sh -c 'grep -o "\"status\": *\"[^\"]*\"" /config/plugins/Watchlist_0.1.0.1/meta.json'
    "status": "Disabled"

The other file the same comparison showed changing is the server's own database shared
memory file, which every request touches.

That the flag lives in the installed directory is worth knowing before an upgrade: a
disabled plugin whose directory is replaced by a newly unpacked one comes back
enabled, because the new directory carries the packaged `status` rather than the one
the server wrote.

## Enable again

    curl -s -X POST "$BASE/Plugins/6e1631d7aa49494da23bd5785853fc0a/0.1.0.1/Enable" -H "$H"
    204
    docker restart wl67-1011
    docker logs wl67-1011 2>&1 | grep -i 'Loaded plugin: Watchlist'
    Emby.Server.Implementations.Plugins.PluginManager: Loaded plugin: Watchlist 0.1.0.1
    curl -s "$BASE/Plugins" -H "$H"
    {"Name": "Watchlist", "Version": "0.1.0.1",
     "ConfigurationFileName": "Jellyfin.Plugin.Watchlist.xml", ... "Status": "Active"}

**What the projection does across this, and it was NOT observed in the run below.** The
runs recorded in this section predate the projection. When they were taken nothing in the
plugin ran on its own, so a disable stopped nothing and an enable started nothing. It does
now: the scheduled pass and the two subscriptions stop when the server stops the plugin
and start when it starts it, no stored document is touched while it is off, and the first
pass after it comes back reconciles from the store and takes onto the list whatever was
done to the playlist in the meantime. That is the ordinary pass rather than a route of its
own, which is why nothing in the plugin knows it was ever disabled. It is held by
`DisabledPluginTests` in the suite, and no server has been watched doing it.

The lists are the same bytes they were before the disable, and the same value the
`status` field is written back to:

    docker exec wl67-12rc4 sh -c 'grep -o "\"status\": *\"[^\"]*\"" /config/plugins/Watchlist_0.1.0.1/meta.json'
    "status": "Active"

**What changes on disk.** The same `meta.json`, in the other direction.

## Back up

Back up while the server is stopped. The volume is archived by overriding the image's
entrypoint, because the entrypoint of the official image is the server:

    docker stop wl67-1011
    docker run --rm --entrypoint tar -v wl67-config:/from jellyfin/jellyfin:10.11.11 \
      czf - -C /from . > backup.tgz
    wc -c < backup.tgz
    57816
    sha256sum backup.tgz
    47fc28fdb2e7bac2c83fb05ad959c96bcfedff354510a59a57ccc0b61f6c7ca1

Without the override the arguments go to the server rather than to `tar`, a second
server starts against the volume being read, and the archive is empty. That failure
reports itself only in the size of the file it wrote:

    docker run --rm -v wl67-config:/from jellyfin/jellyfin:10.11.11 tar czf - -C /from . > backup.tgz
    exit=139
    wc -c < backup.tgz
    0
    Unhandled exception. System.InvalidOperationException: Expected to find only .jellyfin-cache but found marker for /from/.jellyfin-data.

Check the archive before trusting it. An archive of a whole configuration volume that
is zero bytes long, or that does not contain the three paths, is not a backup:

    tar -tzf backup.tgz | grep -i watchlist
    ./plugins/configurations/Jellyfin.Plugin.Watchlist.xml
    ./plugins/Jellyfin.Plugin.Watchlist/
    ./plugins/Jellyfin.Plugin.Watchlist/b701ce8a56e64b31ad888ede6991fde3.json
    ./plugins/Watchlist_0.1.0.0/
    ./plugins/Watchlist_0.1.0.0/Jellyfin.Plugin.Watchlist.dll
    ./plugins/Watchlist_0.1.0.0/meta.json

**The files that carry the lists** are the ones under
`/config/plugins/Jellyfin.Plugin.Watchlist/`, one per user. A backup that captures the
server's database and misses that directory restores a server whose users have lost
their lists, and nothing in the restored server will report that anything is missing,
because a user with no document reads as a user with an empty list. The settings file
is the second thing worth having and the installed plugin directory is the third,
since that one can be put back by installing again.

## Restore from a backup

Restoring replaces the whole configuration volume, so it puts the server back to the
moment the archive was taken and not to some merged state. Here the archive was taken
before the plugin was upgraded to 0.1.0.1 and before the server line changed, and the
restore is what proves it: the server comes back on the earlier line with 0.1.0.0.

    docker stop wl67-12rc4 && docker rm wl67-12rc4
    docker volume rm wl67-config
    docker volume create wl67-config
    docker run --rm -v wl67-config:/to --entrypoint sh jellyfin/jellyfin:10.11.11 \
      -c 'ls -A /to | wc -l'
    0
    cat backup.tgz | docker run --rm -i -v wl67-config:/to --entrypoint tar \
      jellyfin/jellyfin:10.11.11 xzf - -C /to
    docker run -d --name wl67-restored -p 18198:8096 -v wl67-config:/config \
      jellyfin/jellyfin:10.11.11

What came back:

    curl -s "$BASE/System/Info/Public"
    server version: 10.11.11 | wizard completed: True
    docker logs wl67-restored 2>&1 | grep -i 'Loaded plugin: Watchlist'
    Emby.Server.Implementations.Plugins.PluginManager: Loaded plugin: Watchlist 0.1.0.0
    curl -s "$BASE/Plugins" -H "$H"
    {"Name": "Watchlist", "Version": "0.1.0.0",
     "ConfigurationFileName": "Jellyfin.Plugin.Watchlist.xml", ... "Status": "Active"}
    curl -s "$BASE/Plugins/6e1631d7aa49494da23bd5785853fc0a/Configuration" -H "$H"
    {"MaxEntriesPerUser":4242}
    docker exec wl67-restored sh -c 'sha256sum /config/plugins/Jellyfin.Plugin.Watchlist/b701ce8a56e64b31ad888ede6991fde3.json /config/plugins/configurations/Jellyfin.Plugin.Watchlist.xml'
    2c85fb711d543dc204980158cd5f72e22af7cd79dbdb6e6de4cbfe217c670524  /config/plugins/Jellyfin.Plugin.Watchlist/b701ce8a56e64b31ad888ede6991fde3.json
    fa0b4eb60df05247fef0e5ee2fd535c6be58f8e0cf95151edaaaf7f7708c9b19  /config/plugins/configurations/Jellyfin.Plugin.Watchlist.xml

The list document and the settings file are the same bytes the backup was taken from,
and the saved value of 4242 is back. The uninstall performed between the backup and
the restore was undone by it, which is the point: a restore is not selective.

**What changes on disk.** Everything under the configuration directory is replaced.
Anything written after the backup is gone, including lists a user added in that
window.

## Uninstall

    curl -s -X DELETE "$BASE/Plugins/6e1631d7aa49494da23bd5785853fc0a/0.1.0.1" -H "$H"
    204

Afterwards the installed directory is gone and the server no longer reports the
plugin:

    docker exec wl67-12rc4 sh -c 'find /config/plugins -maxdepth 2 | sort'
    /config/plugins
    /config/plugins/configurations
    /config/plugins/configurations/Jellyfin.Plugin.MusicBrainz.xml
    /config/plugins/configurations/Jellyfin.Plugin.Tmdb.xml
    /config/plugins/configurations/Jellyfin.Plugin.Watchlist.xml
    /config/plugins/.jellyfin-plugin
    /config/plugins/Jellyfin.Plugin.Watchlist
    /config/plugins/Jellyfin.Plugin.Watchlist/b701ce8a56e64b31ad888ede6991fde3.json
    curl -s "$BASE/Plugins" -H "$H"
    no Watchlist entry

**What remains.** The lists and the settings file, at the two paths named in the table
above. That is the decision recorded in `docs/uninstall.md`, and the reading here is a
second one taken on the other supported line, which agrees with it. **Removing the
remainder by hand** and the reason the plugin does not do it on somebody's behalf are
in that file; the procedure is not repeated here, because a second copy of a path list
is the copy that goes stale.

**What an administrator sees afterwards.** No entry in the plugin list, no
configuration page, and no warning that anything was left behind. Somebody who wants a
clean server has to know to go and look.

## What this run did not cover

**The install route a user will actually take.** The plugin was put on the server by
copying the assembly into a directory. A user installs from a manifest added to their
server as a repository URL, and there is no release, no artifact and no manifest yet.
So what is written above is what happens once the files are in place, and not the
procedure an administrator follows to get them there. #73 packages the artifacts, #74
publishes the manifest, and the install section gains that route when they land.

**The packaging.** The `meta.json` beside each assembly was written by hand from the
fields in `build.yaml`, for the same reason. A package produced by a release will not
necessarily carry the same file, and the empty `Description` in every reading above is
one visible consequence.

**The second plugin version.** 0.1.0.1 does not exist as a release. It is the same
source with the version properties overridden on the command line, so the upgrade
section proves that the server replaces one installed version with another and keeps
the data, and not that any published upgrade behaves that way.

**The list itself.** The document used throughout was placed by hand in the form the
store reads, and it stands in for a real one. The route that would have written a real
one was in the assemblies this run installed, so it is a route the run did not take
rather than one that does not exist:

    git grep -n 'HttpPost("Items/{itemId}")' b6fda94 -- Jellyfin.Plugin.Watchlist/Api/WatchlistController.cs
    b6fda94:Jellyfin.Plugin.Watchlist/Api/WatchlistController.cs:132:    [HttpPost("Items/{itemId}")]

Nothing writes one unprompted, and that is the narrower absence. That endpoint and its
removal counterpart are the only callers of the store's write path, with no event
handler and no scheduled task behind them, so a document appears where somebody asks
for one through the API and nowhere else:

    git grep -n '_store.Add(\|_store.Remove(' b6fda94 -- Jellyfin.Plugin.Watchlist/
    b6fda94:Jellyfin.Plugin.Watchlist/Api/WatchlistController.cs:224:        var result = _store.Add(userId, entry, _configuration.MaxEntriesPerUser);
    b6fda94:Jellyfin.Plugin.Watchlist/Api/WatchlistController.cs:251:        var result = _store.Remove(userId, itemId);
    git grep -c 'IScheduledTask\|UserDataSaved\|IEventConsumer' b6fda94 -- Jellyfin.Plugin.Watchlist/ ; echo "exit=$?"
    exit=1

So no reading here is of a document this plugin wrote. What the readings show is that
the file at that path is carried through an upgrade, a line change, a disable and a
restore untouched, which is a property of the paths rather than of the contents.

**What a user sees.** Every reading above is an administrator's reading, taken from
the plugin list endpoint the dashboard is built from. No client was opened and no
watchlist was shown to anybody, because the projection that would put a list where a
client can see it is not built. #65 is the matrix that records client readings when
there is something to look at.

**What removing the shared list leaves.** No shared list was made on this run, so none
was removed, and nothing below is a reading of a server. It is written here because
#301 asks that this file say what a removal leaves, and the honest answer from this run
is that it says what the route is built to do and marks the reading as untaken.

Since #301 the route removes the shared record AND the one playlist the shared list was
projected into, in that order:

    git grep -n 'DeleteAsync(projected.PlaylistId' -- Jellyfin.Plugin.Watchlist/Api/WatchlistController.cs
    Jellyfin.Plugin.Watchlist/Api/WatchlistController.cs:523:                .DeleteAsync(projected.PlaylistId, owner, cancellationToken)

So what an administrator is left with on a server whose playlists could be reached is
neither the record nor the playlist, and the private lists and their playlists are
untouched. What they are left with when the playlists could NOT be reached is the
playlist, still visible to every user, and one line in the server log naming it and its
owner - the record goes either way, because every route over the shared list answers
from the record. `docs/api.md` carries the whole of that rule at
`## DELETE Watchlist/Shared`; it is not repeated here, and neither half of it has been
watched happen on a server.

**Downgrades.** Neither a plugin downgrade nor a server downgrade was attempted.

**The next line is a release candidate.** 12.0.0 here is `12.0-rc4`. A reading against
it is a reading against a moving target, and it is worth taking again when that line
has a stable release.
