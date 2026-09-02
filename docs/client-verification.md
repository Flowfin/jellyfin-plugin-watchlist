# What has been seen on a client

**NO PERSON HAS SEEN THIS LIST ON ANY CLIENT.** The table below has one row and it
is the other kind: a query against the server, taken by a harness. Read that
first, because a page with a matrix on it is easy to take for a page with results
on it, and one row is easier still to take for a check that was made.

This plugin's whole claim is that unmodified clients show the list. That claim is
a reading rather than a test: no suite here can drive a television, and the rule
that says so is `Jellyfin.Plugin.Watchlist.Tests/HEADLESS.md`, which refuses a
test needing a display and names the replacement that takes its place. So the
claim has to be recorded the way a measurement is recorded - with the client, the
versions, the date, what was done and what was seen - or it is not evidence at
all.

This page is that register. It held nothing for a long time and it was here
anyway, because the alternative is a claim in the README with nothing behind it
and nowhere for the absence to be visible.

## What is recorded, and what is still missing

THE TABLE IS NO LONGER EMPTY, AND THIS SECTION SAID NO SERVER HAD EVER LOADED THIS
PLUGIN. Both halves of that reason are gone. A stock server boots with the
packaged archive on every interoperability run, and since #52 a second harness
drives the whole loop against one: it adds an item through the plugin's endpoint,
runs the plugin's scheduled task, and reads the projected playlist back with the
two queries a client issues.

    git grep -l 'drive-the-whole-loop-on-a-line.sh' origin/master -- .github/
    origin/master:.github/scripts/drive-the-whole-loop-on-a-line.sh
    origin/master:.github/workflows/whole-loop.yaml

So the row below exists. What it is NOT is the row this page was built for. It
records that the server would answer a client asking, which is the cheap half of
the claim; the expensive half is a person looking at a screen, and nobody has done
that. The two are kept apart by the `How` column and by the rule under
`## What a row means` below, and a reader who takes the row for a client having
rendered anything is making exactly the substitution this register exists against.

WHAT STANDS BETWEEN THE TWO IS NOT WORK ON THIS BOARD. No suite here can drive a
television, which is the headless rule rather than a gap somebody could close with
a script, and no container renders a screen. What is missing is a person with a
client, a server and the archive, and this register is where what they saw goes.

The endpoints are a different matter and are not what this page is about. They are
covered by the suite and by the server the interoperability job boots; a client
does not call them, because the whole point of projecting into a playlist is that
no client has to be taught anything.

## The table

One row per client CHECKED. There are none, and the row below is not one: its
`How` is the other value, and its `Client` column says so rather than naming a
client it did not use.

| Client | Client version | Server version | Plugin version | Date | How | What was done | What was seen |
| --- | --- | --- | --- | --- | --- | --- | --- |
| none, and that is the point of the row | none | 10.11.11, `jellyfin/jellyfin:10.11.11` | 0.1.0.0 | 2026-09-01 | a query against the server | `.github/scripts/drive-the-whole-loop-on-a-line.sh` on run `33494240316`: a film added to the user's list through `POST /Watchlist/Items/{id}`, then one run of the plugin's scheduled task, then the two queries a client issues - `GET /Items?userId=…&recursive=true&includeItemTypes=Playlist` and `GET /Playlists/{id}/Items?userId=…`. Then the row removed the way a client removes one, `DELETE /Playlists/{id}/Items?entryIds=…`, and the task run again. | The server answered the playlist query with a playlist named `Watchlist (plugin)`, and its contents route with the film. After the removal and the second run, `GET /Watchlist/Items` no longer held the film. The plugin's own log line for the first run reads `Watchlist reconciliation finished: 1 users, 1 playlists created, 2 playlist writes, 0 skipped.` **Nothing was rendered and nobody looked at a screen.** |

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
from a table holding one row about no client at all.

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
