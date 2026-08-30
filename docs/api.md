# The watchlist API

The endpoints this plugin adds to the server, what each one answers, and what it
refuses. It is a contract as soon as anything outside this repository calls it,
so it is written down here rather than left to be read out of the source.

This page and the code cannot drift apart in silence. The suite reads the route
set off the plugin assembly and fails when a route has no section here, and it
fails the other way too, on a section describing a route the plugin does not
have. A route added tomorrow reds the run until it is written down.

The answer tables below are read the same way. Each one is compared with the
status codes its own endpoint declares, so a code the endpoint gained and a row
left behind by one it lost both red the run naming the route and both readings.
What that comparison does not judge is whether a code is the right one for an
outcome. That decision is written here and nowhere else, and it is what a caller
reads this page for.

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

Two endpoints demand administrator rights to be reached, and they are the two
that make the shared list and take it away. Everything else on this surface is
open to any authenticated user of the server, and a third endpoint asks about
elevation once it has been reached: taking an entry off the shared list is
allowed to the person who put it there and to an administrator, so the removal
asks the server's own elevation policy about the caller and refuses on the
answer. Being an administrator gives no access to anybody's private list, and it
changes no answer on any endpoint other than the three named here.

Who is an administrator is the server's question throughout. This plugin carries
no rule of its own about it and asks the server's elevation policy every time.

Nothing in this plugin takes a user identifier, in a route, in a query or in a
body. There is deliberately no spelling of any of these requests that names
somebody else: a caller reads and changes their own list because a request is
from them, and the shared list is one object rather than a list per person.

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

## GET Watchlist/Shared/Items

Reads the one list the whole server shares, oldest entry first.

No parameters.

Each entry carries what the private list's entries carry and one thing more: the
identifier of the user who put it on the list. That is stored and returned on
purpose. A shared list is written by everybody, so a title on it is somebody's
suggestion rather than an anonymous fact, and it is also what tells a caller
which entries they may take off again.

Two callers can get different answers from this one list. An entry is left out
for a caller whose libraries do not hold the item, exactly as an unresolvable
entry is left out of a private list, so a shared list cannot be used to learn
what sits in a library the caller has no access to.

    curl -H 'Authorization: MediaBrowser Token="<token>"' \
      https://jellyfin.example/Watchlist/Shared/Items

| response | what it means |
| --- | --- |
| 200 | The shared list. It is empty when nobody has put anything on it, and that is not an error. |
| 401 | The request carried no user identity this plugin could read. |
| 404 | This server has no shared list. Nobody has made one. |
| 503 | The shared list exists and this plugin will not read it. |

The 404 and the empty 200 are different answers to different questions, and
collapsing them would be the more comfortable mistake. A server on which nobody
has made a shared list is not a server with an empty one, and a client told the
list is empty would show a user a shared list that does not exist.

## POST Watchlist/Shared/Items/{itemId}

Puts one library item on the shared list, and records the caller as the person
who put it there.

| parameter | in | what it is |
| --- | --- | --- |
| `itemId` | route | The library item, as the server's own identifier. |

No body. Anybody who may use this server may add to the shared list. Safe to
repeat, and a repeat by a second person changes nothing: the entry keeps the
name of whoever put it there first, because taking that name off a title
somebody else asked for would be the opposite of what the attribution is for.

    curl -X POST -H 'Authorization: MediaBrowser Token="<token>"' \
      https://jellyfin.example/Watchlist/Shared/Items/00000000000000000000000000000000

| response | what it means |
| --- | --- |
| 204 | The item is on the shared list. This call put it there, or it was already there. |
| 400 | The item is not of a kind a watchlist holds. |
| 401 | The request carried no user identity this plugin could read. |
| 404 | There is nothing here for this caller to add, or this server has no shared list. |
| 409 | The shared list is at its cap. Nothing was added and nothing was removed. |
| 503 | The shared list exists and this plugin will not write to it. |

The 404 answers three questions with one answer here rather than two. An item
that is not in the library, an item this caller may not see, and a server with
no shared list all read the same. The first two are collapsed for the reason the
private add's 404 gives; the third joins them because a caller who wants to know
whether a shared list exists reads it, which answers that question directly.

## DELETE Watchlist/Shared/Items/{itemId}

Takes one item off the shared list, if this caller may.

| parameter | in | what it is |
| --- | --- | --- |
| `itemId` | route | The library item, as the server's own identifier. |

Who may: the user who put the entry there, and an administrator. Whether a
caller is an administrator is the server's answer rather than this plugin's, and
it is asked through the server's own elevation policy.

    curl -X DELETE -H 'Authorization: MediaBrowser Token="<token>"' \
      https://jellyfin.example/Watchlist/Shared/Items/00000000000000000000000000000000

| response | what it means |
| --- | --- |
| 204 | The item is not on the shared list. This call took it off, or it was never there. |
| 401 | The request carried no user identity this plugin could read. |
| 403 | The entry is somebody else's and this caller is not an administrator. Nothing was removed. |
| 404 | This server has no shared list. |
| 503 | The shared list exists and this plugin will not write to it. |

The 403 says something a 404 would hide, and here that is right. Every entry on
this list names who added it, so a caller who is told an entry is not theirs
could have read the same thing off the list a moment earlier. There is nothing
to protect by pretending the entry is absent, and a caller told 204 for an entry
that is still there would report a removal that did not happen.

Removed and never-there stay one answer, as on a private list.

## POST Watchlist/Shared

Makes the one shared list this server can have.

Who may: an administrator, and nobody else. Whether a caller is one is the
server's answer rather than this plugin's, asked through the server's own
elevation policy. This endpoint and the removal below are the only two here that
demand it to be reached at all.

Takes no parameters. There is exactly one shared list on a server, so there is
nothing to name and nothing to choose.

    curl -X POST -H 'Authorization: MediaBrowser Token="<token>"' \
      https://jellyfin.example/Watchlist/Shared

| response | what it means |
| --- | --- |
| 204 | This server has a shared list. This call made it, or one was already there. |
| 401 | The request carried no user identity this plugin could read. |
| 403 | The caller is not an administrator. Nothing was written. |
| 409 | This server is configured not to offer a shared list. Nothing was written. |

It never overwrites. A call on a server that already has a list leaves that list
exactly as it is, entries and owner included, and answers 204: what the caller
asked for is that this server has a shared list, and it does either way. The
alternative would empty a list people have been adding to, on the call an
administrator makes when they cannot remember whether the first one worked.

The 409 is the settings page and the record being one answer rather than two.
`SharedListEnabled` is where a server says whether it offers a shared list, and a
record made while that says no would leave the page telling an administrator the
server has none while every user could see one. Turn the setting on, then make
the list.

## DELETE Watchlist/Shared

Takes the shared list off this server.

Who may: an administrator, and nobody else, asked the same way as above.

Takes no parameters, and it is handed no caller at all once the elevation
question is answered, so what it does cannot depend on which administrator asked.

    curl -X DELETE -H 'Authorization: MediaBrowser Token="<token>"' \
      https://jellyfin.example/Watchlist/Shared

| response | what it means |
| --- | --- |
| 204 | This server has no shared list. This call removed it, or there was none. |
| 401 | The request carried no user identity this plugin could read. |
| 403 | The caller is not an administrator. Nothing was removed. |

Removed and never-there stay one answer, as everywhere else on this surface.

What it removes is the shared record and nothing else. This plugin projects no
shared playlist today, so there is none for the removal to take with it; the
projection that exists is per user and is not this list. The sentence naming what
happens to a shared playlist belongs here on the day there is one, which is #84.

Nothing about a user's own list moves. Removing the shared list is not a way to
reach anybody's private one, and no private document is read or written by it.

## GET Watchlist/Export

Writes the calling user's list out in the exchange format, so it can be carried to
another server running this plugin. The format is fixed in
[export-format.md](export-format.md) and this endpoint hands back exactly what that
document describes.

No parameters. There is no spelling of this request that names somebody else, so a
caller can move their own list and nobody else's.

Nothing is left out. An entry whose media has since been deleted from this library
leaves in the export with no provider identifiers at all, which is what tells a
reader on the other server that the entry was there and could not be described.
Dropping it would make an entry that could not be described indistinguishable from
an entry that was never on the list.

    curl -H 'Authorization: MediaBrowser Token="<token>"' \
      https://jellyfin.example/Watchlist/Export

| response | what it means |
| --- | --- |
| 200 | The export. A user with nothing on their list gets a list with no entries, which is a valid export of nothing. |
| 401 | The request carried no user identity this plugin could read. |
| 503 | The list exists and this plugin will not read it. |

The shared list is not in it. Reading the shared list is its own route, and putting
it in a per-user export would make what one caller gets depend on what their
libraries hold, so an export taken by two people on one server would carry two
different shared lists under one name.

## POST Watchlist/Import

Reads an exported file against this server and puts what it matched on the calling
user's list.

The body is an export document, and its `FormatVersion` has to be one this plugin
knows. A version it does not know is refused rather than read as far as it goes,
because a partial import is the outcome the format's version field exists to
prevent.

The list the file names is not the list this writes. An export made on another
server carries that server's identifier for its owner, and the person importing it
is somebody else there, so what an import writes is always the caller's own list.
That is what makes this the calling user's own operation.

Entries are matched by provider identifier first and by the exporting server's own
identifier second, and never the other way round; [export-format.md](export-format.md)
carries the reason. An entry is matched only where this caller may see the item and
a watchlist would take it, so an entry pointing at a library this caller has no
access to comes back the same way as an entry pointing at nothing.

Nothing is dropped in silence. Every entry of every list this endpoint read comes
back in the report, including the ones nothing here answered to, and a list it did
not read is counted along with how many entries sat in it.

    curl -X POST -H 'Authorization: MediaBrowser Token="<token>"' \
      -H 'Content-Type: application/json' \
      --data-binary @watchlist-export.json \
      https://jellyfin.example/Watchlist/Import

| response | what it means |
| --- | --- |
| 200 | The report. Entries that matched nothing here are in it too, and so are the counts for lists this endpoint did not read. |
| 400 | The body is not an export this plugin can read, or it declares a format version this plugin does not know. |
| 401 | The request carried no user identity this plugin could read. |
| 503 | The list exists and this plugin will not write to it. |

A shared list in the file is counted and left alone. Writing the shared list is a
write to a list other people read, which is an administrative operation rather than
this one, and a list whose kind the file did not declare is left alone for the same
reason: the kind is a claim about who may see the list, and nobody made it.

Importing the same file twice leaves one entry per item and reports the second run
as `AlreadyOnTheList`, exactly as calling the add endpoint twice does.

## Setting a per-user preference

There is no endpoint for one yet, and there is no place on the configuration page
for one either. What there is now is somewhere for the answer to live: a per-user
preference belongs with that user's own document rather than in the plugin
configuration, and the two settings a user may answer, with the rule saying which
value wins, are in [settings.md](settings.md).

So the storage exists and the surface does not, which is a state worth reading
exactly: nothing today can set a per-user preference, because no route into it has
shipped. This API is where it will be set when one does, because the configuration
page is the server's and it is one page for the whole server.

Everything the configuration page does carry is server-wide and is described in
[settings.md](settings.md).

## What is not promised here

That this list is complete for a server you are running. It is complete for the
plugin in this repository, checked against the code on every run of the suite.
Whether the server's own generated API document agrees with it is a reading
somebody has to take on a running server, and it has not been taken. #30 carries
that gap.
