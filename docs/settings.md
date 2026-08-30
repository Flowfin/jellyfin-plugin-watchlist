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

Every value is judged, and the two directions answer differently on purpose. A
save is refused, so what the server holds does not move and the person who typed
the value is the one who fixes it. A configuration file that was edited by hand is
repaired to the defaults when the plugin loads, one setting at a time, because at
that moment there is nobody to tell and the alternative is a plugin that throws on
every pass. Each row below states the bound its setting is judged against.

What an administrator sees is narrower than what the refusal says. The controls on
the page carry the same bounds, so a browser refuses a bad value before it is
posted; the plugin's own refusal is the line behind that, for anything reaching the
server another way, and the server's endpoint has no route for a plugin's message,
so the setting is named in the server log rather than on the page.

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

Bounds: at least one character, at most 128, with no leading or trailing space. An
untrimmed name is refused rather than trimmed, because a name with a trailing
space looks identical in the field and a silent trim tells you your value was
taken when it was changed. The length is a display bound - the name is what a
client renders in a list row - and no database or filesystem limit was measured.

Where it is stored: the plugin's configuration, one value for the whole server.
Every user's list carries the same name, because it is one name for the server
rather than a per-user preference.

**When this plugin stops writing the name.** A projected playlist is renamed only
where its label is still the one this plugin last wrote for it. Where the two
differ the user named that playlist, and this plugin never writes its name again,
on this setting change or on any later one; the contents keep being reconciled
through the identifier, which the label does not affect. The comparison is what
the code asks, because nothing here can ask a server whether a person typed a
name.

Two consequences of that rule are worth knowing before you meet them. A user who
renames their playlist to exactly what this setting later becomes is
indistinguishable from one who never renamed it, and this plugin manages the name
again from then on. And a second playlist of that user carrying this name is not
adopted or renamed: the identifier decides which one is the projection, the server
resolves such a collision on the directory rather than on the name, and the plugin
says so once rather than on every pass.

Read on a running server by nothing yet, and the reason has moved. The projection
is built - it renames under the rule above and creates under #17 - and nothing
constructs it, because neither it nor the playlist seam is registered with the
server. So this value is saved, kept, and rendered nowhere until a pass runs.

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

Bounds: 0 to 1000000. Zero and one are legal and mean what they say, because
nothing removes entries when the cap falls under an existing list. A negative
number is refused, and so is anything above the ceiling, which exists so that one
extra digit does not turn the cap back into no cap.

Where it is stored: the plugin's configuration, which the server holds outside a
user's document, because it is one value for the whole server rather than one per
person.

What reads it: the route that adds one item, the route that reads a whole list back
in, and the adoption of a playlist somebody made by hand, each refusing a write that
would take a list past the bound. The third of those is why adoption can take fewer
rows than the playlist offered.

    git grep -l 'MaxEntriesPerUser' -- Jellyfin.Plugin.Watchlist/ | grep -v Configuration/
    Jellyfin.Plugin.Watchlist/Api/WatchlistController.cs
    Jellyfin.Plugin.Watchlist/Api/WatchlistTransferController.cs
    Jellyfin.Plugin.Watchlist/Projection/UserProjectionTarget.cs

### RemoveWhenWatched

Whether an entry leaves a user's private list once that user has watched it.

Default: false. A list somebody curated losing entries without being asked is a
surprise rather than a feature, and it is not one they can undo, because nothing
records what was taken off.

What changes when it moves: turned on, a watched entry is removed from the private
list it is on, series-aware, which is the rule in #21. A film marked played leaves
the list. An episode marked played takes the episode entry off, and takes the
series entry off only once every episode of that series has been played, so
finishing one episode of a series somebody is halfway through leaves the series
where it is. Marking something unplayed again puts nothing back. It never touches
the shared list: by answer 9 on #1 that list is pruned by nobody but a person,
because watched is individual and taking an entry off would take from one person
something another still wants to see.

When it takes effect: the next time the server records that this user played
something. Turning it on does not sweep what was already watched before it was
turned on, because nothing goes looking; the rule runs on the event and on nothing
else.

Where it is stored: the plugin's configuration, one value for the whole server.

What reads it: the watched rule, which the plugin subscribes to the server's user
data event to hear. The third line is the handler the other two attach and detach:

    git grep -n 'UserDataSaved' -- Jellyfin.Plugin.Watchlist/
    Jellyfin.Plugin.Watchlist/Watched/UserDataWatchedSubscription.cs:47:        _userData.UserDataSaved += OnUserDataSaved;
    Jellyfin.Plugin.Watchlist/Watched/UserDataWatchedSubscription.cs:55:        _userData.UserDataSaved -= OnUserDataSaved;
    Jellyfin.Plugin.Watchlist/Watched/UserDataWatchedSubscription.cs:101:    private void OnUserDataSaved(object? sender, UserDataSaveEventArgs args)

That paragraph said the value was read by nothing and named the pass that would run
it as M3 work. It is read now, and by an event handler rather than by a pass: a
scheduled reconciliation is #24 and is a different thing from this.

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

Bounds: 1 to 168 hours. Below an hour the converging pass is being asked to do the
job of the events it converges behind; above a week it stops being a convergence.
Somebody who wants it rarer than that wants the projection switched off, which is
its own setting.

Where it is stored: the plugin's configuration, one value for the whole server.

Not read by anything today. The scheduled task is #24 and is not built.

### SharedListEnabled

Whether this server offers a shared list.

Default: false. This is a privacy default rather than a convenience one. A server
that gains a list every user can see without an administrator asking for one is a
surprise, and by answer 8 on #1 an entry on that list carries and shows who put it
there, so what a user adds is attributable to them in front of everybody on the
server.

What changes when it moves: turned on, this server may have one shared list, and an
administrator makes it with `POST Watchlist/Shared`; once it is there every user can
read it and add to it, and removal of an entry stays with administrators and with
whoever added it. Turning the switch on does not by itself make a list, which is the
half a reader gets wrong: the switch says the server offers one and the endpoint is
what makes it. There is exactly one such list rather than several, by answer 6 on #1,
which is why its name and its bound are settings here and not fields on a record.

When it takes effect: immediately for the creation endpoint, which reads it on every
call, and on the next reconciliation for the projection, which does not exist yet.
Turning it off deletes nothing stored, so a list that was made stays on disk and is
removed with `DELETE Watchlist/Shared` rather than by moving this value.

WHAT TURNING IT OFF DOES NOT DO, STATED BECAUSE #87 MADE IT REACHABLE. The endpoints
over the list's contents key on whether a list exists rather than on this value, so a
server that turns the switch off with a list already made goes on serving that list to
every user, and this page says the server offers none. Before #87 nothing could make a
list, so the gap was documented and could not be reached; it can be reached now. The
repair is either those endpoints reading this value or the switch being refused while
a list exists, and both are the contents surface rather than the administrative one.
#277 is where that is decided, and turning the switch off is not a way to take a
shared list away until it is.

Where it is stored: the plugin's configuration, one value for the whole server.

Read by one thing, and this row said it was read by nothing until #87. The endpoint
that makes the shared list asks it and refuses with a conflict when it says no, so
the setting and the record cannot disagree: a server whose page says it does not
offer a shared list cannot be given one behind that page. That is the decision this
row asked #87 to take, and it is taken this way rather than the other because two
answers to whether this server offers a shared list is the shape a reader is caught
by, not a redundancy.

WHAT STILL READS IT IS NOTHING ELSE, and that half of the old sentence stands. The
record landed on #83 and the endpoints over its contents on #85, and none of those
asks this question: what they answer is decided by whether a shared list exists.
Removing the list does not ask it either, because turning the switch off and then
being unable to undo the list it made would be the worse failure. Its projection is
#84 and does not exist, so nothing here stops projecting when the switch moves.

### SharedListName

The displayed name of the shared list.

Default: Shared Watchlist (plugin). The same rule as `ProjectedListName`, and it
binds harder here, because one name is met by everybody on the server rather than
by one person.

What changes when it moves: the shared list's projected playlist is renamed. The
value is kept while the list is switched off, so turning the list off and on again
does not lose the name an administrator chose.

When it takes effect: on the next reconciliation.

Bounds: the same as `ProjectedListName`.

Where it is stored: the plugin's configuration, one value for the whole server.

Not read by anything today, for the same reason as `ProjectedListName`. Projecting
the shared list is #84.

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

Bounds: 0 to 1000000, the same as the per-user cap and for the same reasons.

Where it is stored: the plugin's configuration, one value for the whole server.

What reads it: the route that adds an item to the shared list, refusing a write that
would take that list past the bound.

    git grep -l 'MaxEntriesInSharedList' -- Jellyfin.Plugin.Watchlist/ | grep -v Configuration/
    Jellyfin.Plugin.Watchlist/Api/WatchlistController.cs

WHAT AN ADMINISTRATOR WHO MOVES THIS NUMBER SEES TODAY IS STILL NOTHING, and that is
a different sentence from the one above rather than a softening of it. The cap is
read on every add to the shared list, and no server has a shared list, because
nothing in this plugin creates one. Creating it is #87. So this setting has a reader
and that reader is not reachable yet, which is why an administrator sees no effect
rather than because nothing looks at the value.

## Per-user settings

Two of the settings above are a user's own as well as the server's, because they
say what that person wants rather than what the server allows. A user may answer
either of them for themselves, and their answer is kept with their own document
rather than in the plugin configuration, which is one document for the whole
server and is rewritten wholesale whenever the page is saved.

- `ProjectionEnabled` - whether this user gets a projected list at all.
- `RemoveWhenWatched` - whether a watched item leaves this user's list.

**The precedence rule.** A per-user answer wins wherever it is present. Where it
is absent the server-wide value applies. There is no third source and no
per-setting exception, and this is the only place the rule is written in prose;
in the code it is written once as well.

Present means the answer is in that user's document. An answer that happens to
equal the server-wide value today is present and still wins. Collapsing it into an
absence for being equal would move that person's setting the next time an
administrator saves the page, without either of them touching it, so absent means
nobody answered and never it happened to match.

Where it is stored: that user's own document, beside their entries, under the same
atomic write and the same schema version. A user who has answered nothing has no
block in their document at all, and a user who answered one of the two carries
that one and not the other. Withdrawing the last answer takes the block back out.

How a user sets one: through this plugin's own API, and only there for version 1.
The configuration page belongs to the server and is one page for the whole server,
so it is the wrong surface for an answer that belongs to one person.
[The API document](api.md) says the same thing from the caller's side.

One of the two is read and the other is not. This paragraph said both were read by
nothing, and that stopped being true when the watched removal landed on #21.

`RemoveWhenWatched` is read every time the server records that a user played
something, and a user's own answer wins over the server-wide one there, which is the
precedence rule above running rather than a second rule:

    git grep -n 'Resolve(preferences?.RemoveWhenWatched' -- Jellyfin.Plugin.Watchlist/
    Jellyfin.Plugin.Watchlist/Configuration/EffectiveSettings.cs:55:        return Resolve(preferences?.RemoveWhenWatched, serverWide.RemoveWhenWatched);

`ProjectionEnabled` is read by nothing, so a user's answer to it is stored and kept
and changes no behaviour yet, exactly as the server-wide value is.

## What is not here

Nothing. The server-wide set above is the whole set #32 fixes, and a setting
beyond it needs its own issue and a reason rather than an extra row here.

What is worth reading twice is how much of it is inert, and no count of that is
written here any more. A count in this file is wrong from the moment one more
setting gets a reader, and that has happened twice without this paragraph moving:
once when the shared list got its endpoints on #85, and once when the watched
removal landed on #21. It said two of the eight settings were read by the code that
ships, on a tree where three of them were.

Each row above answers for its own setting instead, so the answer moves in the same
change as the reader arrives. A row carrying a `What reads it:` line is read today
and names what reads it; a row saying it is read by nothing is not read, and says
what would have to be built for it to be.

Every setting here that is read by nothing is read by nothing for the same reason:
the projection and the scheduled task are not built. There is none that is inert
for a reason of its own.
