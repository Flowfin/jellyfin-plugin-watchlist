# First load on a stock server

The build check proves the plugin compiles and packages. That is a different claim
from "it loads". This file is the record of a run that installed the built assembly
on an unmodified server of each supported line and read back what the server said.

A later reader compares a new run against the rows here rather than against a memory
of one. Everything below was produced by the commands it sits under, and every
command runs without a graphical session on the server host: the servers are
containers and the readings are their log output and their HTTP API.

## What was installed

| | |
| --- | --- |
| Plugin version | 0.1.0.0 |
| Plugin identifier | 6e1631d7-aa49-494d-a23b-d5785853fc0a |
| Built from commit | d23a10f7af512da264284bf77a5ac36f7b90bbaf |
| Artifact | Jellyfin.Plugin.Watchlist.dll |
| Artifact sha256 | 73562b4be936f0c5f8343bbdafa0afd30fffa0d881e79446f57e75bc8addbee5 |

The version and the identifier are the ones `build.yaml` declared at the commit this
run was built from, which is what a server reads. The command names that commit
rather than the working tree, because this is a record of one run and the file has
moved since: #4 raised `targetAbi` to the line the project actually compiles
against, so an unpinned read of it prints a value this run never carried.

    git show d23a10f7af512da264284bf77a5ac36f7b90bbaf:build.yaml | grep -E '^(name|guid|version|targetAbi):'
    name: "Watchlist"
    guid: "6e1631d7-aa49-494d-a23b-d5785853fc0a"
    version: "0.1.0.0"
    targetAbi: "10.9.0.0"

The four values are unchanged. What was wrong is the order: `grep` prints the
file's order and the paste carried the order of the pattern, so the last two
lines were the other way round. It was that way on `dd24325`, the commit that
wrote this section, so the paste was arranged by hand rather than taken from the
command:

    git show dd24325:build.yaml | grep -nE '^(version|targetAbi):'
    42:version: "0.1.0.0"
    43:targetAbi: "10.9.0.0"

The artifact was produced from the tree:

    dotnet publish Jellyfin.Plugin.Watchlist/Jellyfin.Plugin.Watchlist.csproj -c Release -o pkg
    sha256sum pkg/Jellyfin.Plugin.Watchlist.dll
    73562b4be936f0c5f8343bbdafa0afd30fffa0d881e79446f57e75bc8addbee5

and its `meta.json` was written by hand from the fields in `build.yaml`, because on
the day of this run the release packaging that would produce one did not exist. That
is a difference between this run and an install a user performs, and it is stated in
"What this run did not cover" below, where what has changed since is written too.

## The servers

| Line | Image | Digest | Version the server reported |
| --- | --- | --- | --- |
| Current stable | `jellyfin/jellyfin:10.11.11` | `sha256:aefb67e6a7ff1debdd154a78a7bbb780fd0c873d8639210a7f6a2016ad2b35db` | 10.11.11 |
| Next | `jellyfin/jellyfin:12.0-rc4` | `sha256:db1df1d111c27ba1f10bb8fce6630892f66eb66b12c2b24e79011453ac18b3db` | 12.0.0 |

Both are the published images with nothing added to them. The plugin was copied into
a running container and the container was restarted:

    docker run -d --name wl-1011 -p 18096:8096 jellyfin/jellyfin:10.11.11
    docker exec wl-1011 mkdir -p /config/plugins/Watchlist_0.1.0.0
    docker cp pkg/Jellyfin.Plugin.Watchlist.dll wl-1011:/config/plugins/Watchlist_0.1.0.0/
    docker cp pkg/meta.json                     wl-1011:/config/plugins/Watchlist_0.1.0.0/
    docker restart wl-1011

The same three lines against `wl-12rc4` on port 18097 for the other image. The
assembly the server read is the assembly named above:

    docker exec wl-1011 sha256sum /config/plugins/Watchlist_0.1.0.0/Jellyfin.Plugin.Watchlist.dll
    73562b4be936f0c5f8343bbdafa0afd30fffa0d881e79446f57e75bc8addbee5

## The server said it loaded

    docker logs wl-1011 2>&1 | grep -i watchlist
    [21:37:36] [INF] [9] Emby.Server.Implementations.Plugins.PluginManager: Registering whitelisted assemblies for plugin "Watchlist"...
    [21:37:36] [INF] [9] Emby.Server.Implementations.Plugins.PluginManager: Loaded assembly Jellyfin.Plugin.Watchlist, Version=0.1.0.0, Culture=neutral, PublicKeyToken=null from /config/plugins/Watchlist_0.1.0.0/Jellyfin.Plugin.Watchlist.dll
    [21:37:36] [INF] [9] Emby.Server.Implementations.Plugins.PluginManager: Loaded plugin: Watchlist 0.1.0.0

    docker logs wl-12rc4 2>&1 | grep -i watchlist
    [21:43:19.337] [INF] [10] Emby.Server.Implementations.Plugins.PluginManager: Registering whitelisted assemblies for plugin "Watchlist"...
    [21:43:19.349] [INF] [10] Emby.Server.Implementations.Plugins.PluginManager: Loaded assembly Jellyfin.Plugin.Watchlist, Version=0.1.0.0, Culture=neutral, PublicKeyToken=null from /config/plugins/Watchlist_0.1.0.0/Jellyfin.Plugin.Watchlist.dll
    [21:43:19.662] [INF] [10] Emby.Server.Implementations.Plugins.PluginManager: Loaded plugin: Watchlist 0.1.0.0

An assembly targeting `net9.0` therefore loads on both, including the line whose
runtime is a major version further on.

## The server lists it, with the name and version from build.yaml

`GET /Plugins` is the endpoint the dashboard's plugin list is built from. The reading
below is that query and not a rendered page; no browser was involved anywhere in this
run.

    curl -s "$BASE/Plugins" -H "Authorization: $AUTH, Token=\"$TOKEN\""

On 10.11.11, the entry for this plugin:

    {"Name":"Watchlist","Version":"0.1.0.0","ConfigurationFileName":"Jellyfin.Plugin.Watchlist.xml",
     "Description":"","Id":"6e1631d7aa49494da23bd5785853fc0a","CanUninstall":true,
     "HasImage":false,"Status":"Active"}

On 12.0.0, byte for byte the same entry. `Status` is `Active` on both, which is the
server saying the plugin is running rather than disabled, unsupported or
malfunctioned.

`Description` comes back empty because the hand-written `meta.json` carried a short
description while `build.yaml` carries a long one. It is a property of this run's
packaging step and not of the plugin.

## The configuration page is served

    curl -s -o page.html -w 'status %{http_code} bytes %{size_download} type %{content_type}\n' \
      "$BASE/web/ConfigurationPage?name=Watchlist" -H "Authorization: $AUTH, Token=\"$TOKEN\""
    status 200 bytes 555 type text/html; charset=UTF-8

on both lines, and the two responses are the same file:

    sha256sum page-1011.html page-12rc4.html
    4e7825c637f609a754d44801d2fd9bc42e0d8443078492059502771df7393e55  page-1011.html
    4e7825c637f609a754d44801d2fd9bc42e0d8443078492059502771df7393e55  page-12rc4.html

The page is also listed for the plugin:

    curl -s "$BASE/web/ConfigurationPages?enableInMainMenu=false" ...
    {"Name":"Watchlist","EnableInMainMenu":false,"DisplayName":"Watchlist",
     "PluginId":"6e1631d7aa49494da23bd5785853fc0a"}

The page that came back is the upstream template's demonstration page. Serving it
proves the route works. Its contents have been replaced since this run, so the hash
above is a reading of what that build served and not of what a run today would fetch:

    gh issue view 31 --repo Flowfin/jellyfin-plugin-watchlist --json state,title --jq '"\(.state) \(.title)"'
    CLOSED Replace the template configuration page with this plugin's own

## What this run did not cover

The `meta.json` beside the assembly was written by hand from `build.yaml`. A user
installs a package built by the release packaging, which did not exist on the day of
this run, so what is proven here is that a server loads this assembly and not that
the package a release produces is well formed. That packaging exists now and two
releases have come out of it, so this paragraph is a bound on THIS run rather than a
state of the board. #73 is the packaging and #71 is why the version in the assembly
and the version in the manifest agree.

Nothing was rendered. Every reading above is a log line or an HTTP response, so
"visible in the dashboard" is proven as far as the endpoint the dashboard reads and
no further. A row that claims a person saw it on a screen would be a different row
and this is not one.

The plugin was installed by copying it into a container rather than through the
server's own install route, so the install path a user takes is not covered here
either. The runbook in #67 is where that belongs.

Nothing was exercised beyond loading. No list was created, no playlist was written
and no endpoint of this plugin was called. There was no endpoint to call: the build
this run installed carried no API at all, and the plugin has one now.

    git ls-tree -r --name-only d23a10f -- Jellyfin.Plugin.Watchlist/ | grep -c '/Api/'
    0
    git ls-tree -r --name-only origin/master -- Jellyfin.Plugin.Watchlist/ | grep -c '/Api/'
    7

So the bound stands as a reading of this run and it has grown rather than shrunk. A
run taken today leaves more uncovered than this one did, and the endpoints are
covered by the suite rather than by any reading here.
