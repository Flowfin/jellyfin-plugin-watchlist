# What has been seen on a client

**NOTHING HAS BEEN VERIFIED ON ANY CLIENT.** The table below is empty. Read that
first, because a page with a matrix on it is easy to take for a page with results
on it.

This plugin's whole claim is that unmodified clients show the list. That claim is
a reading rather than a test: no suite here can drive a television, and the rule
that says so is `Jellyfin.Plugin.Watchlist.Tests/HEADLESS.md`, which refuses a
test needing a display and names the replacement that takes its place. So the
claim has to be recorded the way a measurement is recorded - with the client, the
versions, the date, what was done and what was seen - or it is not evidence at
all.

This page is that register. It is empty today and it is here anyway, because the
alternative is a claim in the README with nothing behind it and nowhere for the
absence to be visible.

## Why there is nothing to record yet

A RUNNING SERVER DOES MAKE PLAYLISTS NOW, AND THIS PARAGRAPH SAID IT MAKES NONE.
The pass that reconciles every user's projection is registered, and the server finds
what runs it by scanning this assembly for the scheduled-task interface:

    git grep -n 'IScheduledTask' -- Jellyfin.Plugin.Watchlist/PluginServiceRegistrator.cs
    Jellyfin.Plugin.Watchlist/PluginServiceRegistrator.cs:70:        // IScheduledTask registration below: it finds the type by scanning this assembly
    Jellyfin.Plugin.Watchlist/PluginServiceRegistrator.cs:123:        serviceCollection.AddSingleton<IScheduledTask, WatchlistReconciliationTask>();

So the reason this table is empty has moved and is now the plainer one: no server has
ever loaded this plugin. Nothing has been published from this repository, and nothing
here starts a server of its own - the harness that boots a stock one in a container is
#52 and where it runs is #62. A row written today would have to be written from a
screen nobody has seen, which is the substitution this register exists against.

The endpoints are a different matter and are not what this page is about. They are
covered by the suite and by the server the interoperability job boots; a client
does not call them, because the whole point of projecting into a playlist is that
no client has to be taught anything.

## The table

One row per client CHECKED. There are none.

| Client | Client version | Server version | Plugin version | Date | How | What was done | What was seen |
| --- | --- | --- | --- | --- | --- | --- | --- |

## What a row means, so that a row added later means the same thing

**`How` is `a person on a device` or `a query against the server`, and never both
in one row.** They answer different questions and the difference is the whole
value of this page. A person on a device saw a list on a screen. A query against
the server saw that the server would answer a client asking. A server that answers
correctly and a client that renders it are two claims, and a row that blurred them
would let the cheap one stand in for the expensive one.

**A failure is a row, and so is a partial result.** A client where the list is
visible and cannot be edited is a row that says exactly that, in `What was seen`,
and it is not a client left out for looking bad. The row that never gets written
is the one this page exists against.

**`What was seen` is what was on the screen**, not what should have been. Where
they differ that is the finding, and the finding is the reason to have taken the
reading at all.

## What was not checked

**Every client.** Not a subset of them, and not the ones that were awkward: the
absence here is total, and it is stated as a total rather than left to be inferred
from an empty table.

There is deliberately no list of clients-not-yet-checked beside that sentence. A
list of client names typed here would be this repository's own guess at what the
Jellyfin project ships today, going stale on its own, and a reader could not tell
a client missing from the list because nobody thought of it from one missing
because it does not exist. `Every client` cannot go stale in that direction.

## When this is refreshed

At each release. A row is a reading of one plugin version against one server
version and one client version, and all three move.

**A row that has gone stale is MARKED stale and stays.** It is not deleted and it
is not quietly kept as though it were current. A reading taken against a plugin
version nobody runs any more is still evidence of what that version did, and
deleting it removes the only record that the check was ever made; leaving it
unmarked lets it read as a claim about today. So the `Date` and the three version
columns are what a reader compares against the release they are on, and a row
whose plugin version is behind the current release carries `stale` in front of
`What was seen`.

Nothing enforces any of that. No check here reads this file, no run compares its
rows against a release, and a stale row that nobody marks looks exactly like a
current one to every route in this tree. That is the residual, it is stated rather
than hidden, and what it would take to close it is a route that reads a release
and this table together.
