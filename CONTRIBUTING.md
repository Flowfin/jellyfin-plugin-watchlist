# Contributing

Every change starts as an issue and lands as a pull request. What follows is the
part that is particular to this repository: the one command that runs what the
gate runs, the rule about what a test is allowed to need, the shape an issue
takes, and the sign-off every commit carries.

## Run the gate before you push

One command runs the legs of the required set that a machine here can run, in
the order the ruleset lists them, and stops at the first failing leg. It is in
`docs/parity.md`, under `## The local command`. A green run of it is not the
whole gate: that section names the required context the command does not reach
and says why, and it is the authority for which legs those are rather than this
paragraph.

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

There is one, it is not part of the ordinary run, and this section says how to
start it and what it needs on the machine. This paragraph replaces one saying
there was none, which was true when it was written and stopped being true when
`.github/workflows/interoperability.yaml` landed. The reading under it went
stale in silence, because it matched a word no path in this tree carries:

    git grep -c 'boot-a-line-with-this-plugin.sh' -- .github/workflows/interoperability.yaml
    .github/workflows/interoperability.yaml:5

It boots a stock Jellyfin in a container, unpacks the packaged archive into the
server's plugin directory the way a server does, completes first-time setup,
creates an administrator, and asserts that the plugin loaded, is listed as
active, answers that administrator, refuses an anonymous caller and collides
with nothing else on the server. What each assertion covers and what it does
not is in `docs/parity.md`, in the `Alone on 10.11` row, which is the authority
for that rather than this section.

**It is never part of the ordinary run.** It starts a real server in a container
and so needs a container runtime that no other check here needs, and the local
command in `docs/parity.md` does not chain it for the same reason it does not
chain the workflow audit: a leg that is absent on most machines is a leg whose
green means nothing. Run it when you have changed the packaging, the manifest,
the plugin's identity or anything the server reads at load time.

**What it needs.** `docker`, `unzip`, `curl`, `jq` and `bash`. No display, no
elevation and no machine-wide trust store - the server answers plain HTTP on a
port bound to the loopback address, so nothing trusts a certificate - and it
starts no daemon, so a container runtime that is not already running ends the
run with that sentence rather than with a verdict about the plugin. The script
says so itself, which is where to read it rather than here:

    git grep -n 'It needs no display, no elevated rights' -- .github/scripts/
    .github/scripts/boot-a-line-with-this-plugin.sh:118:# It needs no display, no elevated rights and no machine trust store. The server
    .github/scripts/scan-a-server-for-collisions.sh:88:# It needs no display, no elevated rights and no machine trust store.

**How to start it.** The one form that needs nothing but the script proves the
harness bites and starts no container at all:

    bash .github/scripts/boot-a-line-with-this-plugin.sh --prove-it-bites

The two that boot a server take the image and, for the run that matters, an
archive:

    bash .github/scripts/boot-a-line-with-this-plugin.sh \
      --image jellyfin/jellyfin:10.11.11 --package <package.zip>
    bash .github/scripts/boot-a-line-with-this-plugin.sh \
      --image jellyfin/jellyfin:10.11.11 --without-the-plugin

The image tag is pinned rather than floating in the job that runs this, so a
local run naming a different tag is answering about a different server. The
archive is the one the packager produces, which is `jprm` and is not in this
tree: the job installs it through an action, so producing one locally means
installing that packager yourself. A `bin/` directory copied into the container
is not a substitute - the archive is the subject, and the two come apart at the
artifact list, the framework directory and the declared ABI, which the compiler
never sees.

**What a red run leaves to read.** The container's log, printed into the output
of the run that failed, and no artifact:

    git grep -n 'docker logs' -- .github/scripts/boot-a-line-with-this-plugin.sh
    .github/scripts/boot-a-line-with-this-plugin.sh:471:  docker logs "${container}" 2>&1 | tail -n 120 >&2
    .github/scripts/boot-a-line-with-this-plugin.sh:526:docker logs "${container}" > "${log}" 2>&1 || true

So a red run in the gate is readable in that run's own log for as long as this
repository keeps it, and a red run here is readable in your terminal. Whether
that should be an uploaded artifact instead is #62 and is not settled here.

**This is not the harness #52 asks for.** That one drives the whole loop - add
an item through the API, assert the playlist holds it, remove it as a client
would, assert the store dropped it - and the two middle assertions have no
subject while nothing in this plugin writes to a playlist. #52 is where that is
read and where the harness that does it is built. When it lands it is the same
shape as the one above, and this section grows the part that is new rather than
being rewritten.

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

Linking a change to an issue and closing that issue are two different things,
and only one of them is a keyword. A bracketed `[#12]` in a subject links and
closes nothing. A body or a commit message carrying `close`, `fixes` or
`resolves` in front of a reference closes that issue the moment the change
merges, and the pair is read wherever it appears in the text, so a sentence
written to say a change does not close an issue closes it. That has happened
twice here. #29 closed on the merge of #145 and #56 closed on the merge of
#199, and in both cases the body carried a sentence saying the change did not
finish the issue. Both were reopened by hand. The second one is the reason
this paragraph exists: the first was written into the pull request that caused
it and nowhere a later contributor would read it.

    for n in 29 56; do echo -n "#$n "; gh api \
      "repos/Flowfin/jellyfin-plugin-watchlist/issues/$n/timeline?per_page=100" \
      --jq '[.[] | select(.event=="closed" or .event=="reopened") | .event] | join(",")'; done
    #29 closed,reopened
    #56 closed,reopened

Nothing refuses it. `Deterministic PR-hygiene checks` reads the keyword form
as one acceptable way to link and judges no other thing about it, so a change
that finishes part of an issue and says so is green while the issue closes
under it. Write the outstanding part without the keyword: `Refs #12` and a
sentence naming the condition that is not met says the same thing and closes
nothing.

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
