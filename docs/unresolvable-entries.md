# An entry whose item is no longer there

Media gets deleted and libraries get rebuilt. An entry holds a library item
identifier and nothing that can be derived from the library, which is the shape
decided in #11, so an identifier that stops resolving is all the store ever sees.

## The rule

**An entry whose item does not resolve is skipped on read and left in the
document.** It is never dropped, and never dropped by one caller and kept by
another.

It is written once, in `WatchlistVisibility.Resolvable`, and a read that shows the
list goes through that one function. The API and the projection are its two
readers today and the scheduled reconciliation is the third when it lands, and
three copies of this rule would be three chances to keep two of them in step and
lose the third.

## What a read means here, and what is outside it

THIS SECTION REPLACES A SENTENCE SAYING EVERY READ PATH GOES THROUGH THE GATE. That
sentence was wider than the tree it described. Three parts of this plugin take the
entries off a stored document without calling the gate, and each of them is right
to:

- The export writes a copy of the stored document. An entry that has stopped
  resolving on this server has to survive the round trip to the one the file is
  restored on, and a gated export would silently drop exactly the entries the rule
  above exists to keep.
- Watched removal reads the entries to decide which of them a play retires, and it
  shows nobody anything. Gating it would make an unresolvable entry permanently
  unretirable rather than merely hidden.
- The store owns the document, and its own reads are the cap, the duplicate check
  and the removal.

So the rule is about a read that PRESENTS the list. Which file is which, and the
reason for each one, is in
`Jellyfin.Plugin.Watchlist.Tests/VISIBILITY-GATE-READERS.txt`. It declares six
readers, and the gate is named in three files:

    git grep -c 'WatchlistVisibility' -- Jellyfin.Plugin.Watchlist/
    Jellyfin.Plugin.Watchlist/Api/WatchlistController.cs:2
    Jellyfin.Plugin.Watchlist/Projection/UserProjectionTarget.cs:2
    Jellyfin.Plugin.Watchlist/Store/WatchlistVisibility.cs:1

The gap between the six and the three is the point of the register rather than a
defect in it: three of the six read the document for something other than showing
a list, and the register is where each of them says which.

## What refuses a bypass

`VisibilityGateTests` in the suite, so the paragraphs above stopped being prose on
the change that added it. It reads the plugin's own sources out of the test
assembly and reds the run, naming the file and the line, when a source takes the
entries off a stored document and no line of that register declares it, when a file
the register calls gated takes more reads than it makes gate calls or parks a read
in a local the rest of it can reach past the gate, when a file the register calls
outside reaches the gate anyway, and when a declaration covers no read at all.

WHAT IT WAS BUILT AGAINST is not a hypothetical. The projection became the third
reader this page names and did not go through the gate: it asked the describer
itself, which is the same predicate written a second time, and it landed in the
change that made the projection a reader at all with nothing red for it.

WHERE IT STOPS is written in the register's own header rather than here, because
that is the file somebody opens when the guard refuses them. The short of it is
that the scan reads one line at a time and matches the two spellings this tree uses,
so a read through a local under a third name, a read split across two lines, and
what a helper does with a collection it was handed are outside what it sees. A
green run is a floor and not a proof that every read of a stored list is gated.

## Why skipping rather than dropping

The two ways an item stops resolving look identical from inside the store, and
only one of them is permanent.

A user deleting a film is permanent. A library rebuild after a detached drive, a
mount that came up late, a path that moved, a scan that has not finished yet: all
of those make an identifier stop resolving for a while and then start resolving
again. Dropping the entry turns the second case into data loss the user cannot
undo, and they never asked for anything to be removed.

Keeping it costs a few dozen bytes per entry, bounded by the cap in #101. The
identifier is also the only thing a later reattachment could work from.

The reverse choice has one real advantage, which is that a list cannot fill up
with entries nobody will ever see again. The cap is what bounds that, and an
entry that is skipped on every read is invisible to the user in the meantime.

## What gets said about it

One line per pass, and only when something was skipped, so a server with nothing
deleted says nothing and the line that matters is not buried.

The line carries how many entries were skipped and whose list it was. It names no
title, because the document holds no title to name. That is a property of the
shape rather than a rule somebody has to remember.

## Deliberately out of scope for 1.0

**Retention.** Nothing ever removes an unresolvable entry, not after a month and
not after a year. A rule that removed them would need an age, and an age is a
promise about how long a user's drive may stay unplugged, which is not a promise
this plugin is in a position to make. The cap is the bound instead.

**Reattachment by provider identifier.** When media is removed and added again it
gets a new library identifier, so the old entry does not come back on its own. The
upstream attempt reattaches by provider identifier and its author records the
limitation:

    gh pr view 17504 --repo jellyfin/jellyfin --json body --jq .body | grep -i 'Reattachment'
    - **Limitation:** reattachment needs a provider ID, since `GetUserDataKeys()` falls back to the GUID otherwise

So reattachment works for the items that carry a provider identifier and silently
does not for the rest, which makes it a feature that behaves differently for two
users with the same library. Storing a provider identifier alongside the library
one would also put a second copy of something the server owns into the document,
which #11 refuses on purpose. Both are reasons to decide it separately rather than
to slip it in here.
