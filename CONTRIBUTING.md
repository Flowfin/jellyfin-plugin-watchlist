# Contributing

Every change starts as an issue and lands as a pull request. What follows is the
part that is particular to this repository: the one command that runs what the
gate runs, the rule about what a test is allowed to need, the shape an issue
takes, and the sign-off every commit carries.

## Run the gate before you push

One command runs the three legs the mainline requires, in the order the ruleset
lists them, and stops at the first failing leg. It is in `docs/parity.md`, under
`## The local command`.

It is not copied here. A second copy of a command is the copy that goes stale,
and the one in `docs/parity.md` sits beside the reading of the ruleset it
mirrors, beside the bound on its suite leg when the machine's SDK is a major
version ahead of the runtime the projects target, and beside the probe that
shows its Unicode leg refuses the byte it exists for.

The same section says which of the checks that report on a pull request that
command cannot run, and why. Some of them read the pull request itself through
the API, and there is no pull request before you push. Others are the same suite
under conditions one machine cannot produce, such as three operating systems or
three consecutive runs.

## The container harness

There is not one yet.

    git ls-files | grep -icE 'harness|compose|docker'
    0

#52 builds it. When it lands it is a separate run that somebody asks for, and
never part of the ordinary one, because it starts a real server in a container
and so needs a container runtime that nothing else in this tree needs. How to
start it, what it needs on the machine, and what a red run leaves behind to read
belong in this section then.

## What a test is not allowed to need

Every test this plugin ships runs without a display, without elevation and
without touching a machine-wide trust store. It also runs without the network,
without a real Jellyfin server, and without reading the machine clock or the
local time zone.

A test that needs any of those is refused rather than skipped. The rule, the
reason it is a birth requirement rather than a cleanup, and the replacement that
takes the place of each refused thing are in
`Jellyfin.Plugin.Watchlist.Tests/HEADLESS.md`, one refusal at a time. Read it
before adding a test that reaches outside its own temporary directory.

The rule is not only written down. `HeadlessRuleGuardTests` reads every source
file of the suite and scans it against the table in `HeadlessRules.txt`, and a
match reds the run naming the file, the line and the rule it broke. The sources
reach that scan through a wildcard rather than a list, so a file added today is
covered today. A departure is declared in `HEADLESS-EXCEPTIONS.txt` with a
reason per line, and an entry matching nothing in the tree reds the run as well,
so a departure cannot outlive the code that needed it.

## The shape of an issue

- An issue says what is wrong, what the evidence for it is, and what done means,
  and the last of those is a list a second person can check off rather than a
  sentence about being finished.
- A number carries the command that produced it, run against the reference the
  reader will have rather than against your own working tree.
- A claim you did not measure is written as a claim, and something that was not
  evaluated says so rather than going unmentioned.

A pull request is held to the first of those from the other end. `Deterministic
PR-hygiene checks` reads the body for an issue reference and every commit
subject for a bracketed one, so the change and the issue it answers cannot come
apart. It reds the check for an author who belongs to this repository and
leaves a note instead for a contribution from outside, because somebody
arriving from elsewhere has no way to know which issue number their change
belongs to before one exists. The linkage is wanted either way, and on an
outside contribution it is supplied by whoever picks it up.

## Sign your work

Every non-merge commit in a pull request carries a `Signed-off-by` trailer whose
name and address are that commit's own author. The check compares the whole
line, so the form is exact and a near miss is a failure:

    Signed-off-by: A Name <name@example.org>

`git commit -s` writes it from your git identity. On commits already made,
`git rebase --signoff <base>` adds it to each of them. The trailer is how an
author asserts the Developer Certificate of Origin over what they are sending.

What the trailer asserts is `DCO` at the root of this repository. It carries
version 1.1 of the Developer Certificate of Origin and nothing else, because a
certificate somebody signs is the wrong place for a local remark. Read it before
you sign, since the trailer is the assertion rather than a formality in front of
it.

The gate is `.github/workflows/dco.yml`. It walks every commit in the range the
pull request adds and it fails closed, so one commit without the trailer reds
the check for the whole branch. A commit it refuses prints a message naming this
document and `./DCO`, and both of those are in the tree.
