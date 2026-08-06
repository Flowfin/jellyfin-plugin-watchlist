# Settings

Every setting this plugin has, what it changes, when the change takes effect, and
where the value is kept. A setting with a label and no explanation is where a
support question comes from, so the rule here is that a setting cannot ship
without a row.

That rule is held by the suite rather than by care. A test reads the public
properties of the configuration class and fails when one of them has no heading
in this file, so adding a setting and forgetting to describe it reds the run.

## Server-wide settings

These belong to the server as a whole. An administrator sets them on the plugin's
configuration page, and the server keeps them in the plugin's own configuration
file, which it writes when the page is saved.

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

## Per-user settings

None yet.

A per-user preference belongs with that user's document rather than in the plugin
configuration, which is what #33 lands, and until it does there is nothing on this
side to describe. The precedence question that comes with it, which value wins
when a user's answer and the server's answer differ, is decided there and stated
here in one line once it exists. That is the one part of #68 this file does not
answer, and it is written as an absence rather than left blank.

## What is not here

The settings the rest of the configuration surface brings: the name of the
projected list, whether the projection runs at all, whether a watched item is
removed, the reconciliation interval, and everything a shared list needs. Those
are fixed on #32, which is waiting on a decision, and none of them is in the
configuration class today. The test that reads that class is what adds them here,
one row at a time, as they arrive.
