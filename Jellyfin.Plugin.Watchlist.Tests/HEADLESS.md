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

The reading below was taken from the run at `badbf34`, which is the newest run of
that workflow on the mainline where every leg reached the suite:

    gh api repos/iderex/jellyfin-plugin-watchlist/actions/runs/31111170958/jobs \
      --jq '.jobs[] | "\(.name) \(.conclusion)"'
    Suite on windows-latest success
    Suite on macos-latest success
    Suite on ubuntu-latest success

138 tests on each leg, none skipped. Two of the three were unprivileged:

    gh api repos/iderex/jellyfin-plugin-watchlist/actions/jobs/92649053525/logs \
      | grep -m1 'user='
    user=runner uid=1001 elevated=false
    gh api repos/iderex/jellyfin-plugin-watchlist/actions/jobs/92649053438/logs \
      | grep -m1 'user='
    user=runner uid=501 elevated=false

**The Windows leg of that run was elevated, so a green result there does not
answer the question this rule asks.** The hosted Windows image runs the job under
an account carrying the built-in Administrators role, and a suite that needs
elevation passes under such an account exactly like a suite that does not:

    gh api repos/iderex/jellyfin-plugin-watchlist/actions/jobs/92649053271/logs \
      | grep -m1 'elevated='
    ... elevated=True

The account name is elided from that line and the value is not.

So the unprivileged Windows half is a reading taken beside the gate rather than in
it. On a Windows desktop whose session answers

    powershell -NoProfile -NonInteractive -Command \
      '$p = New-Object Security.Principal.WindowsPrincipal(
         [Security.Principal.WindowsIdentity]::GetCurrent())
       Write-Output ("elevated=" + $p.IsInRole(
         [Security.Principal.WindowsBuiltInRole]::Administrator))'
    elevated=False

the suite passes on the tree this change makes, which differs from `6e3e136` in
this file and in the workflow beside it and nowhere else. That machine carries no
.NET 9 runtime, so the run rolled forward onto the runtime it has, and it is a
reading about privilege rather than about the runtime either server line uses:

    DOTNET_CLI_UI_LANGUAGE=en DOTNET_ROLL_FORWARD=LatestMajor \
      dotnet test --configuration Release
    Passed!  - Failed:     0, Passed:   138, Skipped:     0, Total:   138

One run that is both unprivileged and Windows inside the gate is what is still
missing. Until it exists this section stands in its place and says which half each
reading covers. #44 is where that gap is tracked and it stays open for it.
