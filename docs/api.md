# The watchlist API

The endpoints this plugin adds to the server, what each one answers, and what it
refuses. It is a contract as soon as anything outside this repository calls it,
so it is written down here rather than left to be read out of the source.

This page and the code cannot drift apart in silence. The suite reads the route
set off the plugin assembly and fails when a route has no section here, and it
fails the other way too, on a section describing a route the plugin does not
have. A route added tomorrow reds the run until it is written down.

## Where the endpoints live

The server scans a plugin's assembly for controllers and adds them to its own
route table, so these sit alongside the server's own endpoints rather than under
a prefix of their own making. The base is the server's base.

    https://jellyfin.example/Watchlist/...

## Who may call them

Every endpoint requires an authenticated user of that server, and no more than
that. Authorisation is the server's, under its default policy, so a request
carries whatever token the server already issued to that user and nothing here
asks for a permission a watchlist has no reason to ask for.

Nothing in this plugin requires administrator rights. Nothing in this plugin
takes a user identifier either, in a route, in a query or in a body. There is
deliberately no spelling of any of these requests that names somebody else: a
caller reads and changes their own list because a request is from them.

The examples below use a placeholder host and a placeholder token. No value in
them came from a real server.

    curl -H 'Authorization: MediaBrowser Token="<token>"' \
      https://jellyfin.example/Watchlist/Items

## What a refusal carries

Its status code, and nothing else. No refusal from this plugin returns a body, so
none of them can carry a media title, a path, a stack trace or an exception
message.

That is not a habit. A refusal that explains itself is one sentence away from
telling a caller which of two answers they met, and the tables below give the
same answer to an item that is not in the library and an item this caller may not
see. The explanation is the leak, not the code. So the rule is refused rather
than remembered: the suite reads this plugin's own sources and fails on a refusal
written with an argument, under the invariant named `api-refusal-body` in
`Jellyfin.Plugin.Watchlist.Tests/Invariants.txt`.

What it does not reach is a refusal the server itself produces before a request
arrives here, and a body the framework writes for a request this plugin's code
never sees is not something this plugin decides.

## GET Watchlist/Items

Reads the calling user's list, oldest entry first, as the stored document holds
it.

No parameters.

Each entry carries what the store recorded and what the library says about the
item now, which is enough to draw a row without asking about each item
separately: the item identifier, the kind it was recorded as, when it was added,
the name, the year, and for an episode its series, season and episode numbers.

An entry whose item no longer resolves for this user is left out of the answer
and stays in the stored document. What that means and why is in
[unresolvable-entries.md](unresolvable-entries.md).

| response | what it means |
| --- | --- |
| 200 | The list. It is empty for a user who never added anything, and that is not an error. |
| 401 | The request carried no user identity this plugin could read. |
| 503 | The list exists and this plugin will not read it. |

The 503 is worth its own sentence. A stored document this plugin refuses to read
is a list that exists and is unavailable, not an empty list, and answering with
an empty one is how a refusal becomes an overwrite the next time something
writes.

## POST Watchlist/Items/{itemId}

Puts one library item on the calling user's list.

| parameter | in | what it is |
| --- | --- | --- |
| `itemId` | route | The library item, as the server's own identifier. |

No body. Safe to repeat: a second call with the same item leaves one entry and
answers the same way as the first, so a client that retries after a timeout does
not put the item on the list twice and does not have to read the list to find
out. The repeat does not restamp the entry either, so an item does not move to
the top of a list nobody touched.

    curl -X POST -H 'Authorization: MediaBrowser Token="<token>"' \
      https://jellyfin.example/Watchlist/Items/00000000000000000000000000000000

| response | what it means |
| --- | --- |
| 204 | The item is on the list. This call put it there, or it was already there. |
| 400 | The item is not of a kind a watchlist holds. |
| 401 | The request carried no user identity this plugin could read. |
| 404 | There is nothing here for this caller to add. |
| 409 | The list is at its cap. Nothing was added and nothing was removed. |
| 503 | The list exists and this plugin will not write to it. |

The 404 answers two questions with one answer, on purpose. An item that is not
in the library and an item this user is not allowed to see are the same answer,
because a caller that could tell them apart could ask about identifiers until it
learned what sits in a library it has no access to.

The 400 is about what a watchlist is for. A film, a whole show and one episode
may go on a list; anything else the library holds may not. The rule is written
as a set of what is accepted rather than a set of what is refused, so a kind the
server grows later does not arrive on the list by default.

The 409 is the cap from [settings.md](settings.md). An add that would take a list
past the bound is refused and nothing is written, rather than the oldest entry
being dropped quietly to make room.

## DELETE Watchlist/Items/{itemId}

Takes one item off the calling user's list.

| parameter | in | what it is |
| --- | --- | --- |
| `itemId` | route | The library item, as the server's own identifier. |

Safe to repeat, and it asks the library nothing. An entry whose item has been
deleted from the library is the entry a user most wants to be able to remove,
and a removal that first asked whether the item still resolves would refuse
exactly that one.

    curl -X DELETE -H 'Authorization: MediaBrowser Token="<token>"' \
      https://jellyfin.example/Watchlist/Items/00000000000000000000000000000000

| response | what it means |
| --- | --- |
| 204 | The item is not on the list. This call took it off, or it was never there. |
| 401 | The request carried no user identity this plugin could read. |
| 503 | The list exists and this plugin will not write to it. |

Removed and never-there are one answer for the same reason the add's 404 is one
answer. The caller asked for the list not to hold the item, and it does not.
Separating the two would be a way of reading a list by writing to it.

## Setting a per-user preference

There is no endpoint for one, and there is no place on the configuration page
for one either. A per-user preference belongs with that user's own document
rather than in the plugin configuration, which is #33, and until that lands
there is nothing per user to set through any surface. When it does land, this
API is where it will be set, because the configuration page is the server's and
it is one page for the whole server.

Everything the configuration page does carry is server-wide and is described in
[settings.md](settings.md).

## What is not promised here

That this list is complete for a server you are running. It is complete for the
plugin in this repository, checked against the code on every run of the suite.
Whether the server's own generated API document agrees with it is a reading
somebody has to take on a running server, and it has not been taken. #30 carries
that gap.
