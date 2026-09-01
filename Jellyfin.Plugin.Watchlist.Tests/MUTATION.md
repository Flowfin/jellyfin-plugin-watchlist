# The mutation run, and every mutant that survived it

Coverage says a line ran. This instrument says whether the suite would have
NOTICED that line being wrong. It changes the code one mutation at a time and
re-runs the tests: a mutant the suite catches is killed, and a mutant that
survives is a change nothing here objects to.

`.github/workflows/mutation.yaml` runs it weekly and on request, never on a pull
request, and it never gates a merge. `stryker-config.json` beside this file holds
the scope.

## Why these three subjects and not the tree

The store, the reconciler and the series rule. A difference calculation, an
on-disk format and a completion rule are where an off-by-one hides behind a green
coverage number, and every one of their mistakes looks like a passing test until
somebody's list is wrong. Running the instrument over the rest would spend a
quarter of an hour reporting that a record's property returns its own field.

## THE SCORE IS NOT THE POINT AND IT NEVER GATES

A score that blocks a merge is a number people learn to raise. What this is for is
the list below: every surviving mutant, with a verdict, so the next run is read
against a decision rather than against a number.

The reading, taken by hand on this machine, at the head this file landed on, with
`dotnet-stryker` 4.16.0:

    dotnet stryker --config-file Jellyfin.Plugin.Watchlist.Tests/stryker-config.json
    Killed:   218
    Survived:  11
    Timeout:    0
    Errors:    0
    The final mutation score is 95.20 %

That run and a run tomorrow are two different readings. Re-run it rather than
citing this one.

WHICH VERSION TOOK IT IS PART OF THE READING, which is why the number is above and why
`.github/workflows/mutation.yaml` installs that one rather than whatever the feed
serves. A score that moved between two runs is the suite moving or the instrument
moving, and an unpinned install leaves a reader unable to tell which. That is #293:
Scorecard reports the unpinned form as `nugetCommand not pinned by hash`.

A version pinned in a workflow is not a version anybody has watched run there. This
workflow has never run - it is weekly and manual, and the tracker shows no run of it -
so what stands behind the number is a hand run on one machine, and this sentence is the
whole of that disclosure.

## The eleven that survived, one verdict each

### Nine in the reconciler

Eight are `.ConfigureAwait(false)` turned into `.ConfigureAwait(true)`, at every
await in `WatchlistReconciler`. EQUIVALENT under this suite and not in general.
The flag decides which context a continuation resumes on, the test host installs
no synchronization context, so both spellings resume the same way and no
assertion can separate them. What would separate them is a host that has one,
which is a server rather than this suite. They are kept as `false` because that is
what a library does when it does not care, and because a plugin runs inside a
server whose context it knows nothing about.

One is `if (place <= previous)` turned into `if (place < previous)`, in the test
that asks whether the rows that are staying are already in the wanted order.
EQUIVALENT, and the reason is worth reading because it is the kind of mutant that
usually is not. The equality arm is unreachable: the rows that stay are
deduplicated by item before this runs, so no two of them carry one item, and the
wanted set gives each item a place of its own. So `place` is never `previous` and
the two spellings agree on every input this function can be handed. The `<=` is
kept because it is the total statement of the rule - the order is strictly
increasing - and the strict form would be a rule that happens to be true rather
than the rule.

### Two in the store

`ArgumentNullException.ThrowIfNull(document)` deleted from `WriteShared`.
EQUIVALENT. The staging call on the next line refuses the same argument, with the
same exception type and the same parameter name, so nothing a caller can observe
moves. The guard is kept because the refusal belongs where the argument arrives,
and a reader of `WriteShared` should not have to open `Stage` to learn that null
is refused.

The message in `throw new JsonException("The document text is not a JSON object.")`
emptied. NOT EQUIVALENT, AND DELIBERATELY NOT KILLED, which is a third verdict
beside the two the issue that built this asked for, and it is written out rather
than folded into either. The mutant changes a sentence, the sentence is real - it
is the difference between a file this plugin wrote badly and a file something else
wrote - but `ParseDocument` is private and its exception is caught by the store and
turned into a refusal, so reaching that text from a test means asserting what a
logger formatted. That pins the logging call rather than the store's rule. The two
readers whose messages a caller CAN reach are asserted, in
`DocumentTextRefusalTests`.

## What the run did not judge

**A part of the store was not mutated at all.** One mutation in
`WatchlistDocumentStore.ReadSchemaVersion` does not compile, and the instrument's
safe mode responds by removing every mutation in that method. So the schema-version
read carries no verdict here in either direction, and a green score says nothing
about it.

**Most mutants were never run.** The scope filter removes the mutants outside the
three subjects, and a coverage filter removes mutants in blocks no test reaches.
Both are counted separately in the run's own output, which is where to read them;
a number here would drift against the run.

**A timeout counts as killed.** A mutant that turns a loop infinite is reported as
a timeout and scored as caught, which is right - the suite noticed - but it is not
the same evidence as an assertion failing.

**Nothing re-runs the pastes in this file.** The document-paste check reads
`README.md`, `CONTRIBUTING.md` and the pages under `docs/`. This file is beside the
suite rather than in that population, so the block above is a reading somebody took
and not one a run keeps true.
