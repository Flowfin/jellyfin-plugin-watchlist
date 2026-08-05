# An entry whose item is no longer there

Media gets deleted and libraries get rebuilt. An entry holds a library item
identifier and nothing that can be derived from the library, which is the shape
decided in #11, so an identifier that stops resolving is all the store ever sees.

## The rule

**An entry whose item does not resolve is skipped on read and left in the
document.** It is never dropped, and never dropped by one caller and kept by
another.

It is written once, in `WatchlistVisibility.Resolvable`, and every read path goes
through that one function. The API, the projection and the scheduled
reconciliation are three readers, and three copies of this rule are three chances
to keep two of them in step and lose the third.

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
