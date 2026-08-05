> [!NOTE]
>
> **Part of [Flowfin](https://github.com/Flowfin).** It works with any Jellyfin
> server, and with the Flowfin clients.

# Watchlist

A private per-user watchlist for Jellyfin, kept on the server and shown by
clients that were never changed.

Each user gets their own list. It is held on the server, so it is the same list
on every device that user signs in from, and it is not visible to anyone else.
There is a shared list as well, one the whole server can see, kept separately
from the private ones.

## Where the list appears

In the client you already use. The plugin projects a user's list into a playlist
owned by that user, and a playlist is a surface every stock client already
renders. Nothing has to be patched, forked, side-loaded or installed alongside
it, and there is no browser extension and no modified web client.

Adding an item to that playlist from a client puts it on the list, and removing
it takes it off, so the list can be worked from the same place it is read.

## Which servers it supports

Two server lines, 10.11 and 12.0. They do not share a runtime and they do not
share the playlist interface this plugin leans on, so a release carries one
artifact per line and you install the one that matches your server.

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
that user's entries. Nothing is written to a user's media, nothing is sent
anywhere off the server, and no third party is contacted.

A list has an upper bound on how many entries it may hold, ten thousand by
default. An add that would take a list past the bound is refused and nothing is
written, rather than the oldest entry being dropped quietly.

## What refuses to happen

An entry whose item has been removed from the library does not silently
disappear from the list, and a library rescan does not empty a list. What such
an entry does instead is written in
[docs/unresolvable-entries.md](docs/unresolvable-entries.md).

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
resolved. The projection into a playlist, the HTTP endpoints and the
configuration page are planned and not yet built, which is why there is no
release.

This section is here so the description above is not read as a description of
what the code does today. The tracker carries the rest.

## Licence

GPLv3, in [LICENSE](LICENSE). A compiled Jellyfin plugin links against the
Jellyfin NuGet packages, which are GPLv3, so the built artifact is GPLv3
whatever this repository says.

See [NOTICE.md](NOTICE.md) for the intended-use notice.
