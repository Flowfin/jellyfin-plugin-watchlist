# The headless rule

Every test this plugin ships runs without a display, without elevation, and
without touching a machine-wide trust store. It also runs without the network,
without a real Jellyfin server, and without reading the machine clock or the
local time zone.

This is a birth requirement rather than a cleanup. A suite that needs any of
those runs on one machine and quietly stops running everywhere else, and the
first time anyone notices is when a regression ships. The same class is visible
on the board this one takes its gate from: a suite that requires administrator
rights on one operating system is an open defect there
(iderex/jellyfin-plugin-sso#1227). This board plans not to acquire it.

## What is refused, and what replaces it

**A test that drives the web interface in a browser on a desktop session.**
Refused, it needs a display. Instead the endpoints and the reconciler are called
directly in process, and the rendered page is checked by parsing its markup
rather than by rendering it.

**A test that asserts a television client shows the list.** Refused, it needs a
device and a screen. Instead, assert that the projected playlist is returned by
the server queries those clients issue. A manual check per client belongs in the
documentation, with versions and dates, marked as a reading rather than as a
test.

**A test that installs a certificate into a machine trust store to talk to a
local server over TLS.** Refused. Instead use loopback HTTP inside a container,
or an in-process message handler with no transport at all.

**A test that needs administrator rights**, which covers a machine-wide path, a
privileged port, and a symbolic link on an operating system that gates them.
Refused. Instead use a temporary directory the test owns, an ephemeral high
port, and a file copy where a link was wanted.

**A test that reads the machine clock or the local time zone.** Refused, it
fails at a date boundary on someone else's machine. Instead take an injected
clock.

**A test that reads the machine's locale.** Refused, it passes where it was
written and fails where the same text sorts or folds differently. Instead state
the culture, or use an ordinal comparison where no culture is meant.

**A test that writes straight into the shared temporary directory.** Refused,
two tests meeting on one path fail each other roughly one run in ten and the
failure never names the test that caused it. Instead take a directory from
`TemporaryDirectory`, which roots every path under one folder per run and
removes it afterwards.

**A test that reaches the network.** Refused. Instead use a fake for the one
interface that would have made the call.

**A test that requires a real Jellyfin server to be installed on the machine
running the suite.** Refused. That job belongs to the container harness, which
is a separate opt-in run and never a unit test.

## Where this rule is written down

Here, once. A test issue on this board points at this file instead of repeating
the list, so there is one place to change when the rule changes and no second
copy to drift against it.

## What refuses a violation

`HeadlessRuleGuardTests` reads every source file of this suite out of the test
assembly and scans it against the table in `HeadlessRules.txt`. A match reds the
run, naming the file, the line, the rule and the call it found. The sources reach
the scan through a wildcard in the project file rather than a list, so a file
added tomorrow is covered the moment it is added.

A departure is declared in `HEADLESS-EXCEPTIONS.txt`, one line per entry, naming
the file, the rule and the reason. The reason is required. An entry that matches
nothing in the tree is stale and reds the run as well, so a departure cannot
outlive the code that needed it.

What the guard reads is source text, so it holds against the calls the table
names and not against a way of reaching the same thing that the table does not.
Two examples of what it does not see today: a call reached through reflection, and
a package that brings a display or a network dependency in without any of these
names appearing in a source file. Add the pattern when the shape appears.

## What has actually been run

Reading a source is not running it, and a run whose log has expired is a claim
rather than a measurement. The readings are kept here so they outlive the logs
they came from.

`.github/workflows/test-platforms.yaml` runs the suite once on each of the three
platforms this rule names and prints the privilege the run had. Every leg runs the
same pair of commands:

    dotnet build --configuration Release --no-restore
    dotnet test --configuration Release --no-build --verbosity normal

The reading below was taken from the run at `454ce1c`, on all three platforms:

    gh api repos/Flowfin/jellyfin-plugin-watchlist/actions/runs/31534276029/jobs \
      --jq '.jobs[] | "\(.name) \(.conclusion)"'
    Suite on macos-latest success
    Suite on ubuntu-latest success
    Suite on windows-latest success

250 tests on each leg, none skipped. Linux and macOS are unprivileged because the
hosted images run the job that way:

    gh api repos/Flowfin/jellyfin-plugin-watchlist/actions/jobs/93921526688/logs \
      | grep -m1 -oE 'user=runner uid=[0-9]+ elevated=false'
    user=runner uid=1001 elevated=false
    gh api repos/Flowfin/jellyfin-plugin-watchlist/actions/jobs/93921526654/logs \
      | grep -m1 -oE 'user=runner uid=[0-9]+ elevated=false'
    user=runner uid=501 elevated=false

**The hosted Windows image does not run the job that way.** Its account carries
the built-in Administrators role, and a suite that needs elevation passes under
such an account exactly like a suite that does not, so the Windows job's own token
still reads:

    gh api repos/Flowfin/jellyfin-plugin-watchlist/actions/jobs/93921526720/logs \
      | grep -m1 -oE 'elevated=True'
    elevated=True

The account name is elided from that line and the value is not.

The suite is not run under that token. The Windows leg starts it through
`runas /trustlevel:0x20000`, which builds a token at the normal user level where
the Administrators group is present for denial only, and the process that runs the
suite reads its own privilege rather than taking the step's:

    gh api repos/Flowfin/jellyfin-plugin-watchlist/actions/jobs/93921526720/logs \
      | grep -aoE 'the process that ran the suite: elevated=[A-Za-z]+|the suite exited [0-9]+'
    the process that ran the suite: elevated=False
    the suite exited 0

What that reading covers and what it does not. It is the same account with one
privilege filtered out of its token, not a second account created for the run, so
it says the suite needs no administrator role and says nothing about a profile
that never had one. The leg fails closed on both ways a wrong answer could arrive:
a reading that is not `elevated=False` refuses the leg, and a run that leaves no
exit status behind is a failure rather than a pass. The second of those was
measured rather than intended, on the run before this one, where an empty status
file reddened a leg whose 250 tests had passed.

A reading taken off the gate, on a Windows desktop whose session answers
`elevated=False`, is what stood here before the leg existed. It is not repeated,
because the gate now takes the same reading on every commit and that one was of
one machine on one day.
