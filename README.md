> [!NOTE]
>
> **Part of [Flowfin](https://github.com/Flowfin).** It works with any Jellyfin
> server, and with the Flowfin clients.

# Watchlist

A private per-user watchlist for Jellyfin, kept on the server, shown by clients
that were never changed, and appearing as a playlist owned by the user whose list
it is.

Each user gets their own list. It is held on the server, so it is the same list
on every device that user signs in from, and it is not visible to anyone else.
There is a shared list as well, one the whole server can see, kept separately
from the private ones. A server has it only if an administrator turns it on:
`SharedListEnabled` is off until somebody sets it, so nothing on this server
becomes readable by everybody without being asked for.

## Where the list appears

In the client you already use. The plugin projects a user's list into a playlist
owned by that user, and a playlist is a surface every stock client already
renders, which is why nothing on the client side has to change for the list to
show up.

Adding an item to that playlist from a client puts it on the list, and removing
it takes it off, so the list can be worked from the same place it is read.

**A show on your list appears in the playlist under an episode name, not the name
of the show.** A playlist cannot hold a show: a server handed a folder puts every
non-folder thing inside it into the list, so a single show would become every
episode of it. Instead the show becomes one row, and that row is the earliest
episode you have not played - the first episode of a show you have not started.
Play it and the next pass moves the row to the next episode you have not played,
so the entry follows the show. A show the server holds no episodes of for you
stays on your list and appears in the playlist as nothing, because there is
nothing to point at. Which episode is chosen, and why that one, is in
[docs/storage-decision.md](docs/storage-decision.md).

**If you already made a playlist with that name, it is taken over rather than
duplicated.** The workaround people use before a plugin like this exists is a
hand-made playlist called something like Watchlist, and creating a second one
beside it would leave you adding to one list while the plugin writes to another.
So on the first pass for a user, a playlist that user owns whose name is exactly
the configured list name becomes their projected list: the plugin remembers it,
and reads what is already in it onto the list. Rows it cannot put on a list are
left where they are, which is anything that is not a film, a show or an episode,
and anything you no longer have access to.

Two limits of that, both deliberate. If you own **more than one** playlist with
that name, none of them is taken over and the plugin makes its own, because
nothing in either list says which one you meant and guessing is wrong half the
time on somebody else's data. And a playlist owned by another user is never taken
over, whatever it is called.

If you would rather keep your own list to yourself, rename it, or change the name
the plugin uses; both are in [docs/settings.md](docs/settings.md), together with
the rule that stops the plugin renaming a list once you have named it yourself.

## Which servers it supports

Two server lines, 10.11 and 12.0. They do not share a runtime and they do not
share the playlist interface this plugin leans on, so an artifact is built per
line and you install the one that matches your server.

Version 1.0 carries the 10.11 artifact and no other. The 12.0 line has no stable
package for a plugin to compile against - every 12.0 version of the server
packages is a release candidate - and an artifact published against a moving
target can stop loading on a server released the month after. So the 12.0
artifact follows a stable 12.0 release rather than shipping with 1.0, and a
server on that line has nothing to install until then. That is a decision taken
on the tracker, on 2026-08-24, rather than a state of the code.

The artifact a 10.11 server receives is one archive, named for this plugin and the
version it carries and for nothing else: the `name` key of `build.yaml` in lower
case, an underscore, then the `version` key. A run of the packaging leg produced
`watchlist_0.1.0.0.zip` from this tree, and [docs/RELEASING.md](docs/RELEASING.md)
carries the download that read it. Everything else in a release takes its name from
that archive - the packaging metadata, the two checksums, the provenance bundle and
the component inventory - so a release has one name in it and the rest are derived.

The name does not say which line the archive is for. The line is inside it, in the
packaged metadata, and [docs/RELEASING.md](docs/RELEASING.md) is where the naming,
the command that reads that metadata and the rule for the day there are two archives
are all written. A server on the 12.0 line receives no archive at all until the
artifact above it arrives.

Installing from the manifest you type no archive name at all. The server reads the
manifest, takes the archive for itself and unpacks it; the name is here for somebody
checking a release by hand, which is what `Checking a release` below is about.

Which of the two the code in this repository is built for today is a narrower
answer again, and it is under `What is built so far` below rather than repeated
here.

## Installing

Installation is from a plugin manifest published by this repository, rather than
from the official Jellyfin plugin catalogue. In the server dashboard, open
Plugins, then Repositories, and add this repository's manifest URL. The plugin
then appears in the catalogue list on that server and installs and updates from
there like any other.

No release is published yet, so there is no manifest URL to add today. The
section above describes the route that will be used, and it is written here
because the README is where somebody looks for it.

## What it stores about you, and where

One document per user, in the plugin's own data folder on the server, holding
that user's entries. Nothing is written to a user's media. Where the data does
not go is in the next section rather than repeated here.

A list has an upper bound on how many entries it may hold, ten thousand by
default. An add that would take a list past the bound is refused and nothing is
written, rather than the oldest entry being dropped quietly.

## What it does not do

No external service is contacted. Nothing in this plugin opens a socket or builds
an HTTP client, so a list has no route off the server that holds it.

No client is modified. What a user is shown is a playlist, which every stock
client already renders, so nothing is patched, forked, side-loaded or installed
beside the client, and there is no browser extension and no modified web client.

No user's private list is visible to anyone else. A request is answered for the
user it came from: who a request is from is read in one place, and a request
carrying no identity this plugin will use is refused rather than answered with a
default. The shared list is a list of its own rather than a view over anybody's
private one.

## What refuses to happen

An entry whose item stops resolving is kept rather than deleted, so nothing a
rescan does removes it from the stored list, and a drive that comes back brings
its entries back with it. What a user sees is a narrower statement than that:
such an entry is skipped on every read while it does not resolve, and media that
is removed and added again gets a new library identifier the old entry cannot
follow, so a rebuilt library can leave a list that is full and shows nothing.
Which of those two a rescan produces, and why reattaching by provider identifier
is out of scope for the first release, are in
[docs/unresolvable-entries.md](docs/unresolvable-entries.md).

Nothing takes an entry off a list because time has passed, because a scan ran, or
because somebody else did something. There is one automatic removal and it is a list
losing what its own owner has watched, which is off until an administrator turns it
on. What it does when it is on is under `What is built so far` below and in
[docs/settings.md](docs/settings.md), rather than a second time here.

A document written by a newer version of the plugin is refused rather than read
with a guess, so downgrading a server does not corrupt a list.

## The language it ships in

English, and nothing else, for the first release. The configuration page, the log
lines and every string this plugin produces are English. Other languages are not
on their way, and if your server runs in another language this is deliberate
rather than an oversight. What the server does and does not offer a plugin here,
and what would change the answer, are in
[docs/page-language.md](docs/page-language.md).

## If your server grows a watchlist of its own

This plugin keeps its own list and its own playlist either way. It never writes
into a list the server keeps, it does not refuse to load next to one, and it
carries no migration into something that has not shipped. How to tell the two
apart on a client, and the way out to something that is not this plugin, are in
[docs/coexistence.md](docs/coexistence.md).

## What is built so far

The store is built: the per-user document, its format and version, its atomic
write, its bound, and the rule for an entry whose item can no longer be
resolved.

The HTTP endpoints are built. They read a user's own list and put items on it and
take items off, they do the same three things for the shared list, and they carry a
list out of this server and read one back in. No count is written here, because a
count in this file goes stale while the routes move: [docs/api.md](docs/api.md) is
the list, and the suite compares it against the endpoints on every run.

The configuration page is built and carries the whole set of server-wide settings,
each described in [docs/settings.md](docs/settings.md). Most of them are saved and
read by nothing yet, because the things they steer are the ones named below as not
built. No count is written here, because a count in this file goes stale each time
one of those things lands: that file says which setting is read today and which is
not, rather than leaving an administrator to set a value and watch for an effect.

Taking a watched entry off a list is built, and it is the first thing this plugin
does on its own rather than when somebody calls it. It is off until an administrator
turns it on. Turned on, a film leaves that user's list once it is played and a series
leaves it once every episode of that series is played, so finishing one episode of a
show somebody is halfway through leaves the show where it is. It never touches the
shared list. The rule, the setting that decides it and the moment it runs are in
[docs/settings.md](docs/settings.md).

The format a list leaves in is fixed, in
[docs/export-format.md](docs/export-format.md), together with the code that
writes it and the rule that matches an imported entry onto this server's items,
and four endpoints carry it. Two are your own: one hands your list out in that
format and one reads such a file back onto it, and a shared list inside such a
file is counted and left alone by both. The other two are the shared list's, and
an administrator is who they answer for, because reading the whole of a list
everybody can see and writing into it are not a user's own operations. Who added
each entry is not in the file, so a shared list that arrives on another server
carries its titles and not the names beside them.

The projection into a playlist does not run, and it is the part a user would
notice, because until it does a list is reachable over the API and appears on no
client. What has changed is what is missing rather than whether anything is: the
half that decides which playlist a list belongs in is written and tested - it
creates one on demand, takes over a matching one you already had, and keeps the
name in step - and none of it is wired into a running server, so no playlist is
created, renamed or read on any server this plugin is installed on. What is still
unwritten is the half that puts the entries in and keeps them there, and the pass
that would call either.

That is true of both kinds of list, which is the part of the shared list that is
missing rather than the whole of it: the shared record, the setting that turns it
on, the endpoints that read and change it, the administrative surface that makes
it and takes it away, and the pair that carries it between servers are all here,
and what is absent is the playlist it would appear in. That is why there is no
release.

One of the two server lines above is not built either. The plugin compiles
against the 10.11 package set, on the framework that line runs, and the packaging
metadata declares the same line, so the one artifact this tree can produce is the
10.11 one. Nothing here builds against the 12.0 package set, so a server on that
line has no artifact waiting for it even once a release exists. For 1.0 that is
also what was decided rather than only what is built, which the support section
above states.

The description above says what the plugin is for, not what the code does today.
The tracker carries the rest.

## Checking a release

Every release carries the plugin archive, a component inventory saying what is in
it, and a signed statement tying it to the commit and the workflow run that built
it. Both are attached to the release page beside the archive, so they outlive the
run, and both carry a `.sha256` of their own.

The statement is checked with the GitHub CLI. The first form reads it out of this
repository's attestation store, the second reads the copy attached to the release,
so a reader who downloaded the whole page needs nothing else:

```
gh attestation verify <archive>.zip --repo Flowfin/jellyfin-plugin-watchlist
gh attestation verify <archive>.zip --repo Flowfin/jellyfin-plugin-watchlist \
  --bundle <archive>.sigstore.json
```

The inventory is CycloneDX, so any tool that reads that format reads it. What it
says about this archive, without such a tool:

```
sha256sum -c <archive>.cdx.json.sha256
jq -r '.metadata.component | .name, (.hashes[] | .alg + " " + .content)' <archive>.cdx.json
jq -r '.components[] | select(.scope == "required") | [.type, .name] | @tsv' <archive>.cdx.json
jq -r '.components[] | select(.type != "file")
  | [.name, .version, .scope, ((.licenses // []) | map(.license.id // .license.name // .expression) | join(", "))]
  | @tsv' <archive>.cdx.json
```

The second command names the archive the document is about and repeats its digest,
so an inventory downloaded next to the wrong archive is visible rather than
assumed. The third lists what the archive ships. The fourth lists every package
the release build resolved, with the licence each package declares: `required`
means the archive carries it, `excluded` means the plugin was compiled against it
and the bytes are not in the zip. The licences are read out of the packages
themselves by the generator and are not asserted here.

Nothing has been released from this repository yet, so these commands describe what
the route produces and have not been run against a release.

## Licence

GPLv3, in [LICENSE](LICENSE). A compiled Jellyfin plugin links against the
Jellyfin NuGet packages, which are GPLv3, so the built artifact is GPLv3
whatever this repository says.

That is the position for the shipped artifact as well as for the source. The
inventory above is where a reader sees it per component rather than as a sentence:
the Jellyfin packages this plugin compiles against declare `GPL-3.0-only`
themselves, which is what makes the paragraph above a reading rather than a
preference.

See [NOTICE.md](NOTICE.md) for the intended-use notice.
