# Settings

Every setting this plugin has, what it changes, when the change takes effect, and
where the value is kept. A setting with a label and no explanation is where a
support question comes from, so the rule here is that a setting cannot ship
without a row.

That rule is held by the suite rather than by care. A test reads the public
properties of the configuration class and fails when one of them has no heading
in this file, so adding a setting and forgetting to describe it reds the run. A
second test reads what each row states as its default and fails when that is not
what a fresh configuration holds, so a default moved in the class leaves this
file red rather than stale.

## Server-wide settings

These belong to the server as a whole. An administrator sets them on the plugin's
configuration page, and the server keeps them in the plugin's own configuration
file, which it writes when the page is saved.

Two of them are off when nobody has touched them, and both are off for the same
kind of reason rather than for tidiness: `SharedListEnabled`, because a list every
user can see is a thing a server should not gain without being asked, and
`RemoveWhenWatched`, because a list losing entries by itself is a surprise nobody
can undo. Everything else is set so that a fresh install works with no visit to
this page at all.

No value here is refused yet. Validating a setting on save and repairing one that
was hand-edited into the configuration file is #34, and the bounds that issue
enforces are the ones stated in the rows below rather than a second set invented
there.

### ProjectionEnabled

Whether the projection runs at all.

Default: true. The projection is this plugin's whole client surface, so a server
where it is off holds every list and shows none of them, and a user would have no
way to tell that from the plugin not working.

What changes when it moves: turned off, no playlist is written or updated and the
scheduled pass does nothing. Nothing stored is deleted, and turning it back on
reconciles from the store, which is the same promise #38 makes about disabling the
whole plugin.

When it takes effect: on the next reconciliation, whether that is an event or the
scheduled pass. Turning it off does not remove a playlist that is already there.

Where it is stored: the plugin's configuration, one value for the whole server.

Not read by anything today. The projection is M3 and is not built, so this value
is saved and kept and drives nothing yet.

### ProjectedListName

The displayed name of each user's projected private list.

Default: Watchlist (plugin). It is deliberately not the bare word, and the rule
behind that is below rather than in a release note.

What changes when it moves: the projected playlist is renamed rather than joined
by a second one, which is #35. **The rule this default follows:** the projected
playlist takes a name a server's own list would not take, and the default says
which plugin made it rather than claiming the generic word. A server may grow a
watchlist of its own - the upstream work is open and measured in
[coexistence](coexistence.md) - and two lists both called "Watchlist" in one
client is a failure a user cannot resolve by looking, because a playlist does not
say what created it. Moving this value off its default is choosing to take that
risk knowingly.

When it takes effect: on the next reconciliation.

Where it is stored: the plugin's configuration, one value for the whole server.
Every user's list carries the same name, because it is one name for the server
rather than a per-user preference.

### MaxEntriesPerUser

The greatest number of entries one user's list may hold.

Default: 10000. It is the number the upstream attempt at a native watchlist chose
for the same purpose, so a user who ever moves between the two meets one bound
rather than two.

What changes when it moves: an add that would take a list past the bound is
refused and nothing is written. Lowering it under an existing list removes
nothing. That list stops growing and says why.

When it takes effect: on the next add. There is no pass over existing lists, so
nothing happens to a stored document at the moment the value is saved.

Where it is stored: the plugin's configuration, which the server holds outside a
user's document, because it is one value for the whole server rather than one per
person.

### RemoveWhenWatched

Whether an entry leaves a user's private list once that user has watched it.

Default: false. A list somebody curated losing entries without being asked is a
surprise rather than a feature, and it is not one they can undo, because nothing
records what was taken off.

What changes when it moves: turned on, a watched entry is removed from the private
list it is on, series-aware, which is the rule in #21. It never touches the shared
list: by answer 9 on #1 that list is pruned by nobody but a person, because
watched is individual and taking an entry off would take from one person something
another still wants to see.

When it takes effect: on the next reconciliation. Turning it on does not sweep
what was already watched before it was turned on.

Where it is stored: the plugin's configuration, one value for the whole server.

Not read by anything today. The rule is #21 and the pass that would run it is M3.

### ReconciliationIntervalHours

The number of hours between scheduled reconciliation runs.

Default: 6. The scheduled pass converges what the server's own events missed
rather than being how a list is kept current, so the interval is chosen against
what a run costs and not against how fresh a list feels. A pass over lists that
are already correct issues no write, which #24 asks for and proves, so a run that
finds nothing is close to free; four a day puts a missed event right within a
working day without anybody noticing.

What changes when it moves: the scheduled task's trigger. Lowering it makes the
pass more frequent and finds a missed event sooner; raising it does the reverse.
Neither changes what a pass does when it runs.

When it takes effect: on the next reconciliation, when the trigger is next read.

Where it is stored: the plugin's configuration, one value for the whole server.

Not read by anything today. The scheduled task is #24 and is not built.

### SharedListEnabled

Whether this server offers a shared list.

Default: false. This is a privacy default rather than a convenience one. A server
that gains a list every user can see without an administrator asking for one is a
surprise, and by answer 8 on #1 an entry on that list carries and shows who put it
there, so what a user adds is attributable to them in front of everybody on the
server.

What changes when it moves: turned on, one shared list exists, every user can read
it and add to it, and removal stays with administrators and with whoever added the
entry. There is exactly one such list rather than several, by answer 6 on #1,
which is why its name and its bound are settings here and not fields on a record.

When it takes effect: on the next reconciliation. Turning it off stops projecting
the shared list and deletes nothing stored.

Where it is stored: the plugin's configuration, one value for the whole server.

Not read by anything today. The shared record is #83 and its projection is #84.

### SharedListName

The displayed name of the shared list.

Default: Shared Watchlist (plugin). The same rule as `ProjectedListName`, and it
binds harder here, because one name is met by everybody on the server rather than
by one person.

What changes when it moves: the shared list's projected playlist is renamed. The
value is kept while the list is switched off, so turning the list off and on again
does not lose the name an administrator chose.

When it takes effect: on the next reconciliation.

Where it is stored: the plugin's configuration, one value for the whole server.

### MaxEntriesInSharedList

The greatest number of entries the shared list may hold.

Default: 10000. The same number as the per-user bound and for the same reason: it
is the size at which a list stops being something a person reads. It is a separate
setting rather than the same one because the two lists are grown by different
numbers of people.

What changes when it moves: an add that would take the shared list past the bound
is refused and nothing is written, and lowering it under a list that is already
larger removes nothing. Everybody on the server may add to this list, by answer 7
on #1, so the number of people who can grow it is the number of people on the
server, which is why it is bounded on its own rather than sharing the per-user
number.

When it takes effect: on the next add to the shared list.

Where it is stored: the plugin's configuration, one value for the whole server.

## Per-user settings

None yet.

A per-user preference belongs with that user's document rather than in the plugin
configuration, which is what #33 lands, and until it does there is nothing on this
side to describe. The precedence question that comes with it, which value wins
when a user's answer and the server's answer differ, is decided there and stated
here in one line once it exists. That is the one part of #68 this file does not
answer, and it is written as an absence rather than left blank.

## What is not here

Nothing. The server-wide set above is the whole set #32 fixes, and a setting
beyond it needs its own issue and a reason rather than an extra row here.

What is worth reading twice is how much of it is inert. One of the eight settings
above is read by the code that ships:

    git grep -l 'MaxEntriesPerUser' -- Jellyfin.Plugin.Watchlist/ | grep -v Configuration/
    Jellyfin.Plugin.Watchlist/Api/WatchlistController.cs

The other seven are saved and kept and read by nothing, because the projection,
the scheduled task and the shared record are not built. A server can set any of
them today and see no effect, and that is what this file says rather than
something a person has to discover.
