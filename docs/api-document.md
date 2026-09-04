# The endpoints in the server's generated API document

`WatchlistApiRouteTests` pins the routes by reading the attributes off the
assembly. That is a reading of what the source declares and never of what a
server builds from it, so no test in the suite can answer whether these
endpoints reach the document a server generates. This file is the record of a
run that asked one server of each supported line.

A later reader compares a new run against the rows here rather than against a
memory of one. Every block below is the output of the command above it.

What the host had to carry is a .NET SDK, for the publish step that produces the
assembly, and a container runtime for everything after it. Nothing rendered a
page and no browser was involved, so no reading here needs a graphical session.
Whether the session that ran these commands held the administrators role was not
read, so "without elevation" is a statement about what the commands ask for
rather than a measurement of what they had; no consent prompt appeared, which is
weaker evidence than a reading.

## What was read

| | |
| --- | --- |
| Built from commit | 0e4a8978948c5feefa1475622cbedd4af2c3bb2f |
| Artifact | Jellyfin.Plugin.Watchlist.dll |
| Artifact sha256 | 9b7243d4217324ae42f4891bc492debe6081668da565319bae89d4d542241681 |
| Document | `GET /api-docs/openapi.json` |

    dotnet publish Jellyfin.Plugin.Watchlist/Jellyfin.Plugin.Watchlist.csproj -c Release -o pkg
    sha256sum pkg/Jellyfin.Plugin.Watchlist.dll
    9b7243d4217324ae42f4891bc492debe6081668da565319bae89d4d542241681

## The servers

| Line | Image | Digest | Version the server reported |
| --- | --- | --- | --- |
| Current stable | `jellyfin/jellyfin:10.11.11` | `sha256:aefb67e6a7ff1debdd154a78a7bbb780fd0c873d8639210a7f6a2016ad2b35db` | 10.11.11 |
| Next | `jellyfin/jellyfin:12.0-rc4` | `sha256:db1df1d111c27ba1f10bb8fce6630892f66eb66b12c2b24e79011453ac18b3db` | 12.0.0 |

Both are the published images with nothing added to them, which is the same pair
`docs/first-load.md` records. The plugin was copied into a running container and
the container was restarted:

    docker run -d --name wl-1011 -p 18096:8096 jellyfin/jellyfin:10.11.11
    docker exec wl-1011 mkdir -p /config/plugins/Watchlist_0.1.0.0
    docker cp pkg/Jellyfin.Plugin.Watchlist.dll wl-1011:/config/plugins/Watchlist_0.1.0.0/
    docker cp pkg/meta.json                     wl-1011:/config/plugins/Watchlist_0.1.0.0/
    docker restart wl-1011

The same lines against `wl-12rc4` on port 18097 for the other image. Both
servers said they loaded it:

    docker logs wl-1011 2>&1 | grep -i watchlist
    [08:55:46] [INF] [10] Emby.Server.Implementations.Plugins.PluginManager: Loaded assembly Jellyfin.Plugin.Watchlist, Version=0.1.0.0, Culture=neutral, PublicKeyToken=null from /config/plugins/Watchlist_0.1.0.0/Jellyfin.Plugin.Watchlist.dll
    [08:55:46] [INF] [10] Emby.Server.Implementations.Plugins.PluginManager: Loaded plugin: Watchlist 0.1.0.0

    docker logs wl-12rc4 2>&1 | grep -i watchlist
    [09:00:24.947] [INF] [9] Emby.Server.Implementations.Plugins.PluginManager: Loaded assembly Jellyfin.Plugin.Watchlist, Version=0.1.0.0, Culture=neutral, PublicKeyToken=null from /config/plugins/Watchlist_0.1.0.0/Jellyfin.Plugin.Watchlist.dll
    [09:00:25.221] [INF] [9] Emby.Server.Implementations.Plugins.PluginManager: Loaded plugin: Watchlist 0.1.0.0

## A stock server carries no such path

The document was fetched from the 10.11 line before the plugin was copied in, so
what appears afterwards is this plugin and not something the server ships:

    curl -s -o openapi-before.json -w 'status %{http_code} bytes %{size_download}\n' \
      http://127.0.0.1:18096/api-docs/openapi.json
    status 200 bytes 2082318

    python -c "import json; d = json.load(open('openapi-before.json', encoding='utf-8')); print(len(d['paths']), sorted(p for p in d['paths'] if p.startswith('/Watchlist')))"
    315 []

and afterwards, from the same server:

    python -c "import json; d = json.load(open('openapi-1011.json', encoding='utf-8')); print(len(d['paths']), sorted(p for p in d['paths'] if p.startswith('/Watchlist')))"
    317 ['/Watchlist/Items', '/Watchlist/Items/{itemId}']

Two paths and three operations, because adding and removing an item share one
template and differ by verb. That is the reason the pin in the suite holds the
verb and the template together rather than the template alone.

Three is what this run found and is no longer what the plugin declares. The shared
list's endpoints landed on #85 after the assembly above was built, so the count here
belongs to the run rather than to the tree, and the section that pins the routes says
what moved and what is therefore unmeasured.

## What the document carries

The document is authenticated nowhere in this reading: `GET /api-docs/openapi.json`
answered 200 to a request with no token on both servers.

    cat shape.py
    import json, sys

    document = json.load(open(sys.argv[1], encoding="utf-8"))
    for path in sorted(p for p in document["paths"] if p.startswith("/Watchlist")):
        for verb, operation in sorted(document["paths"][path].items()):
            print(verb.upper(), path)
            for parameter in operation.get("parameters", []):
                schema = parameter["schema"]
                print("  parameter", parameter["name"], parameter["in"], schema["type"], schema.get("format", ""))
            for code, response in sorted(operation["responses"].items()):
                body = response.get("content", {}).get("application/json")
                print("  response", code, json.dumps(body["schema"], sort_keys=True) if body else "(no body)")

On 10.11.11:

    python shape.py openapi-1011.json
    GET /Watchlist/Items
      response 200 {"items": {"$ref": "#/components/schemas/WatchlistEntryView"}, "type": "array"}
      response 401 {"$ref": "#/components/schemas/ProblemDetails"}
      response 403 (no body)
      response 503 (no body)
    DELETE /Watchlist/Items/{itemId}
      parameter itemId path string uuid
      response 204 (no body)
      response 401 {"$ref": "#/components/schemas/ProblemDetails"}
      response 403 (no body)
      response 503 (no body)
    POST /Watchlist/Items/{itemId}
      parameter itemId path string uuid
      response 204 (no body)
      response 400 {"$ref": "#/components/schemas/ProblemDetails"}
      response 401 {"$ref": "#/components/schemas/ProblemDetails"}
      response 403 (no body)
      response 404 {"$ref": "#/components/schemas/ProblemDetails"}
      response 409 {"$ref": "#/components/schemas/ProblemDetails"}
      response 503 (no body)

On 12.0.0 the same script prints the same twenty lines. The two documents
themselves are not identical, because the server generates the whole of its own
API into them and the two lines do not carry the same API:

    python -c "import json; print(json.load(open('openapi-1011.json', encoding='utf-8'))['openapi'], len(json.load(open('openapi-1011.json', encoding='utf-8'))['paths']))"
    3.0.1 317
    python -c "import json; print(json.load(open('openapi-12rc4.json', encoding='utf-8'))['openapi'], len(json.load(open('openapi-12rc4.json', encoding='utf-8'))['paths']))"
    3.0.4 296

The type the list comes back as is generated too, with every property the view
declares:

    python -c "import json,sys; d = json.load(open(sys.argv[1], encoding='utf-8')); print(json.dumps({k: (v.get('type') or 'WatchlistItemKind') for k, v in sorted(d['components']['schemas']['WatchlistEntryView']['properties'].items())}, sort_keys=True))" openapi-1011.json
    {"AddedAt": "string", "EpisodeNumber": "integer", "ItemId": "string", "Kind": "WatchlistItemKind", "Name": "string", "ProductionYear": "integer", "SeasonNumber": "integer", "SeriesName": "string"}

and the same on the other line.

## The set is the set the suite pins

`WatchlistApiRouteTests` holds twelve strings, verb and template together:

    grep -nE '"(GET|POST|DELETE) Watchlist' Jellyfin.Plugin.Watchlist.Tests/WatchlistApiRouteTests.cs
    32:        "DELETE Watchlist/Items/{itemId}",
    33:        "DELETE Watchlist/Shared",
    34:        "DELETE Watchlist/Shared/Items/{itemId}",
    35:        "GET Watchlist/Export",
    36:        "GET Watchlist/Items",
    37:        "GET Watchlist/Shared/Export",
    38:        "GET Watchlist/Shared/Items",
    39:        "POST Watchlist/Import",
    40:        "POST Watchlist/Items/{itemId}",
    41:        "POST Watchlist/Shared",
    42:        "POST Watchlist/Shared/Import",
    43:        "POST Watchlist/Shared/Items/{itemId}",
    99:            ["POST Watchlist/Something"],

It said six, then eight when the export and the import from #40 were taken again
here, then ten with the two administrative routes #87 added, and it says twelve with
the two that carry the shared list between servers. The count is stated rather than
left to the paste for the reason the paste exists: a reader compares the two, and a
reader who finds them disagreeing should trust neither and re-run the command.

The last of those is not a route. It is the near-miss the pin is proven with, and
the command is shown unfiltered so a reader who runs it meets it here rather than
wondering which of thirteen lines is the extra one.

**THE PIN HAS GROWN SINCE THE RUN RECORDED ABOVE, AND THIS SECTION SAID IT HELD
THREE.** It held three, and the paste under this command showed lines 32, 33 and 34
as the three private routes. The shared list's three endpoints landed on #85 and
joined the pinned set, which moved what the third line is and made the count wrong,
and that change did not carry this paste. Read again at `b893ea9`.

Read again on `7306873` before that. Those numbers were 46, 47 and 48, and they
moved when the reader that class uses went to a file of its own, in a change that
also left this paste behind. Twice now, which is the argument for a mechanism rather
than for a third careful reader.

**SO THE RUN BELOW COVERS LESS THAN THE PIN DOES**, and that is the part to read
carefully rather than the line numbers. Everything measured in this file was
measured against a server carrying the assembly built from
`0e4a8978948c5feefa1475622cbedd4af2c3bb2f`, which predates the shared endpoints, so
the two paths and three operations it found are the three private routes and nothing
was asked about the other three. Whether the shared endpoints reach a generated
document is unmeasured, not measured and found working. The same holds for the
`WatchlistEntryView` block above: the view has since gained the member that names
who added an entry, and no run here has seen it.

For the three it did cover, the reflection pin, `docs/api.md` and the document a
server generates describe one set, and the pin is what a later run is compared
against. A run that covers the other three is one taken after somebody takes it.

## What this run did not cover

The prose written at each endpoint does not reach the document. Every response
in `WatchlistController` carries a `<response>` line saying what that code means
for a caller, and what the document shows instead is the framework's own word
for the status code, `Success`, `Unauthorized`, `Forbidden`, `Server Error` on
one line and `OK`, `No Content`, `Service Unavailable` on the other.

The obvious explanation is wrong and was checked rather than assumed. `dotnet
publish` writes an XML documentation file beside the assembly, and the install
above copies only the assembly and its `meta.json`, so the first guess is that
the prose is missing because that file is missing. It is not. Copying the file
in and restarting the server produces the same document, byte for byte:

    docker cp pkg/Jellyfin.Plugin.Watchlist.xml wl-1011:/config/plugins/Watchlist_0.1.0.0/
    docker restart wl-1011
    docker exec wl-1011 ls -1 /config/plugins/Watchlist_0.1.0.0/
    Jellyfin.Plugin.Watchlist.dll
    Jellyfin.Plugin.Watchlist.xml
    meta.json

    sha256sum openapi-1011.json openapi-1011-withxml.json
    280a5abfd3442ad478513684e9d1f078dce856490b2187aeabf33c4e6e960cd1 *openapi-1011.json
    280a5abfd3442ad478513684e9d1f078dce856490b2187aeabf33c4e6e960cd1 *openapi-1011-withxml.json

So the server does not read a plugin's XML documentation into the document it
generates, and shipping that file in a package would not change what a client
author sees. The shape comes from this document and the meaning from
`docs/api.md`, and that is a fact about the route rather than a gap in the
controller.

`403` appears on every operation and no endpoint declares it. It is added by the
server's own authorisation handling rather than by this plugin, so the set of
codes in the document is a superset of the set in the source, and a later run
that finds a code here which the controller does not name has not necessarily
found a defect.

Nothing was rendered. Every reading above is an HTTP response or a log line, so
no claim here is about what a person saw on a screen.

No endpoint of this plugin was called. This run reads the document a server
builds and says nothing about what any of the three operations does when it is
invoked, which needs a user, a library and the harness in #52.

The plugin was installed by copying it into a container rather than through the
server's own install route, and the `meta.json` beside the assembly was written
by hand from `build.yaml`, because on the day of this run nothing had been
published from this repository. `docs/first-load.md` records the same two
departures for the same reason. Two releases have published since, so a run that
takes the install route is no longer waiting on one to exist; what it waits on is
a catalogue entry, which is #76.

The assembly read here was built on `net9.0` against the 10.9.11 package set,
which is not what the tree declares any more. #4 moved the references to the 10.11
line and the manifest with them, and #134 then moved the pin down to that line's
first build, so the assembly binds the floor the manifest declares rather than a
newer patch. This paragraph has already been wrong about that value once, so it is
pasted under the command that prints it, where `DocumentPasteTests` re-runs it and
reds the suite the day the two come apart:

    git grep -nE 'Jellyfin\.(Controller|Model)" Version' -- Jellyfin.Plugin.Watchlist/Jellyfin.Plugin.Watchlist.csproj
    Jellyfin.Plugin.Watchlist/Jellyfin.Plugin.Watchlist.csproj:58:    <PackageReference Include="Jellyfin.Controller" Version="10.11.0" >
    Jellyfin.Plugin.Watchlist/Jellyfin.Plugin.Watchlist.csproj:61:    <PackageReference Include="Jellyfin.Model" Version="10.11.0">

So the pair this run compiled is a line behind the pair a build produces today, and
the 12.0 half of #4 is still open. A run whose artifact is the artifact a user
installs is one taken after that half lands and from the published catalogue.
