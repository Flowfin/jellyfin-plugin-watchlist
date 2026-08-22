# Check parity

The gate of the SSO plugin board is the target this board's gate is measured
against. This file is the comparison, check by check. Every row says one of three
things: this board adopts the check, this board deliberately does without it, or
this board carries something the other one does not. A gap with a reason next to
it is a decision. A gap with nothing next to it is a defect, and the row it sits
in is where a reader finds it.

What the mainline requires is printed rather than restated, because a set
written into a document drifts against the live one without anybody noticing:

    gh api repos/Flowfin/jellyfin-plugin-watchlist/rulesets/20456281 \
      --jq '{enforcement, bypass: .bypass_actors, required: [.rules[] | select(.type=="required_status_checks") | .parameters.required_status_checks[].context]}'
    {"bypass":[],"enforcement":"active","required":["call / build","call / test","Reject Trojan Source Unicode","Audit workflows (zizmor)"]}

That set has four contexts in it, and this file described a set of three until
the change carrying this paragraph. The sentences that carried the old set,
counted at the commit this change starts from:

    git show 53ea2ea:docs/parity.md | grep -c 'three required contexts\|not required here until #63\|required there and reporting here\|Nothing here is required by the mainline'
    4

The set drifted exactly the way the sentence above warns about, which is why
this is a reading taken again rather than a rewording.

Requiring the whole adopted set is #63 and it has not happened. What has changed
is that one context left the adopted-and-not-required group, and the section
`Two instruments, each reporting under two names` is where that is followed up,
because the name that was taken is one half of a pair this file asked #63 to
choose between.

## The two gates as measured

The other board's required set:

    gh api repos/iderex/jellyfin-plugin-sso/rulesets --jq '.[].id'
    18802863
    gh api repos/iderex/jellyfin-plugin-sso/rulesets/18802863 \
      --jq '[.rules[] | select(.type=="required_status_checks") | .parameters.required_status_checks[].context]'
    ["build","ABI floor build","Package (JPRM) / Build package","Package (JPRM) / Generate SBOM","CodeQL",
     "Analyze (csharp)","DCO sign-off","Deterministic PR-hygiene checks","Enforce greppable invariants",
     "Reject Trojan Source Unicode","Audit workflows (zizmor)","prettier","dependency-review"]

What actually reported on that board's newest mainline commit, which is a
different question from what is required and answers it for the checks that are
not:

    gh api "repos/iderex/jellyfin-plugin-sso/commits/4da1c25dd6090e8f43870a9fab01a70e2f6433d0/check-runs" \
      --jq '.check_runs[] | "\(.name) :: \(.conclusion)"' | sort -u
    ABI floor build :: success
    Analyze (actions) :: success
    Analyze (csharp) :: success
    Analyze (javascript-typescript) :: success
    Audit workflows (zizmor) :: success
    build :: success
    Enforce greppable invariants :: success
    Package (JPRM) / Build package :: success
    Package (JPRM) / Generate SBOM :: skipped
    prettier :: success
    Reject Trojan Source Unicode :: success
    Report any workflow that concluded non-success on the default branch :: success
    Scorecard analysis :: success
    submit-nuget :: success
    wiki-lint :: success

The longest of those names is a job name rather than a workflow name, and the
table below pairs it with the workflow it comes from:

    gh api repos/iderex/jellyfin-plugin-sso/contents/.github/workflows/publish-failure-alert.yml \
      --jq '.content' | base64 -d | grep -n 'name:' | head -2
    1:name: Publish failure alert
    50:    name: Report any workflow that concluded non-success on the default branch

And what reported here, on the head of the last merged pull request rather than
on `master`, because several of this board's checks run on a pull request and
never on a push, so reading `master` would report them missing:

    gh pr list --state merged --limit 1 --json number,headRefOid --jq '.[] | "#\(.number) \(.headRefOid)"'
    #117 afb8cccf4dfe82ad1d2ff628c7605ca10639b5ff
    gh api "repos/iderex/jellyfin-plugin-watchlist/commits/afb8cccf4dfe82ad1d2ff628c7605ca10639b5ff/check-runs" \
      --jq '.check_runs[] | "\(.name) :: \(.conclusion)"' | sort -u
    Audit workflows (zizmor) :: success
    call / Analyze :: skipped
    call / build :: success
    call / test :: success
    Coverage floor :: success
    DCO sign-off :: success
    dependency-review :: success
    Deterministic PR-hygiene checks :: success
    Reject Trojan Source Unicode :: success
    Run the suite three times :: success
    Suite on macos-latest :: success
    Suite on ubuntu-latest :: success
    Suite on windows-latest :: success
    zizmor :: success

Which of them those are is a difference of the two sets rather than a number
written here:

    comm -13 \
      <(gh api "repos/iderex/jellyfin-plugin-watchlist/commits/88f0a8d/check-runs" --jq '.check_runs[].name' | sort -u) \
      <(gh api "repos/iderex/jellyfin-plugin-watchlist/commits/afb8ccc/check-runs" --jq '.check_runs[].name' | sort -u)
    Audit workflows (zizmor)
    DCO sign-off
    dependency-review
    Deterministic PR-hygiene checks
    zizmor

Both readings are of one commit each and move when the head does. The commit is
named in the command so a reader can see which one was read rather than trusting
that it was the newest at the time.

## The local command

    dotnet build Jellyfin.Plugin.Watchlist.sln --configuration Release \
      && dotnet test Jellyfin.Plugin.Watchlist.sln --configuration Release --no-build \
      && pattern=$(git show HEAD:.github/workflows/unicode-guard.yml | sed -n "s/^ *pattern='\(.*\)'\$/\1/p") \
      && rc=0 && { git grep -nIP "$pattern" -- . || rc=$?; } \
      && case "$rc" in 0) echo "unicode: refused" ; false ;; 1) echo "unicode: clean" ;; *) echo "unicode: scanner error $rc, refused" ; false ;; esac

Three legs, in the order the first three required contexts appear in the
ruleset: build, then the suite, then the Unicode scan. It fails at the first
failing leg, and the Unicode leg fails closed on a scanner error rather than
reading a broken scanner as a clean tree, which is how the workflow reads it
too.

It is no longer the whole required set. `Audit workflows (zizmor)` is required
now and this command does not run it, for the reason the last bullet under
`What this command does not run, and cannot` gives. So a green run of the
command above is three of the four contexts a merge waits for, and a contributor
who reads a green run as the whole gate is now wrong by one.

The scan pattern is read out of the workflow rather than retyped. A second copy
of that character class in this file would be the rule written twice, and the
copy would be the one that goes stale.

The three legs were run at `b5e0bd7`, and that commit is named in the paste
because the suite total moves whenever anybody adds a test. Set
`DOTNET_CLI_UI_LANGUAGE=en` first if the machine's locale is not English, because
the output below is what an English reader gets and a translated one cannot be
compared with it:

    dotnet build Jellyfin.Plugin.Watchlist.sln --configuration Release
    Build succeeded.
        0 Warning(s)
        0 Error(s)
    dotnet test Jellyfin.Plugin.Watchlist.sln --configuration Release --no-build
    Passed!  - Failed:     0, Passed:   264, Skipped:     0, Total:   264

That paragraph said the legs were run at the commit this file lands on, and
pasted a total of 114. The sentence was true once and stopped being true without
anybody editing it. The number entered on the commit that created this file and
was never taken again:

    git log --oneline -S'Passed:   114' b5e0bd7 -- docs/parity.md
    2c3f8ed Write the check parity table against the SSO board's gate [#53]
    git log --oneline --no-merges 2c3f8ed..b5e0bd7 -- docs/parity.md | wc -l
    6

Six later commits changed this file and none of them re-ran the leg the sentence
above them promised was re-run. What it cost a reader is the reason the commit is
now in the paste: somebody whose own run printed a total in the two hundreds had
this file telling them the gate passes 114, and no way to tell a paste that had
gone stale from a tree that had lost half its suite. Every check-run reading
further up names its commit for that reason, and this one did not.

The build leg and the coverage leg below both still reproduce at `b5e0bd7`, and
so does the runtime reading in the next paragraph. The suite total was the only
line that had moved.

The suite leg needs the runtime the projects target, and an SDK a major version
ahead of it is not that runtime. On a machine carrying the 10.0 SDK and no 9.0
runtime the leg stops before a single test runs:

    dotnet --version
    10.0.301
    dotnet test Jellyfin.Plugin.Watchlist.sln --configuration Release --no-build
    Framework: 'Microsoft.NETCore.App', version '9.0.0' (x64)
    The following frameworks were found:
      10.0.9 at [C:\Program Files\dotnet\shared\Microsoft.NETCore.App]

The reading above was taken with `DOTNET_ROLL_FORWARD=Major` in the environment,
which runs the same assemblies on the runtime that is present. That is a
different runtime from the one the gate uses, so a pass under it is a weaker
statement than a pass on the gate, and it is written here rather than folded into
the command. Installing a runtime is not the answer this file offers, because an
installer is not something a check tells somebody to run.

The Unicode leg is clean on this tree, and it was made to fail on purpose to show
it can. A file holding one U+200B, staged so `git grep` reads it, is matched and
the leg refuses; removing the file returns the leg to clean:

    printf 'let a = "x\xe2\x80\x8b b";\n' > zerowidth-probe.txt && git add zerowidth-probe.txt
    git grep -nIP "$pattern" -- . ; echo "rc=$?"
    zerowidth-probe.txt:1:let a = "x b";
    rc=0
    git rm -q --cached zerowidth-probe.txt && rm zerowidth-probe.txt
    git grep -nIP "$pattern" -- . ; echo "rc=$?"
    rc=1

The matched line is pasted with its zero-width character removed, because a file
holding the character is exactly what the leg refuses and this file would fail
the gate it documents.

That probe also fixes the bound of the leg: `git grep` reads tracked and staged
content, so an untracked file carrying the character is invisible to it. The
workflow reads a checkout, where every file is tracked, so the two agree; a
contributor running the leg over a working tree with the character still
untracked does not get the workflow's answer.

What this command does not run, and cannot:

- `Coverage floor`, `Run the suite three times` and `Suite on <os>` are the same
  suite under different conditions. The floor is in the test project file rather
  than in the workflow, so adding `-p:CollectCoverage=true` to the test leg runs
  it on this machine the same way:

      dotnet test Jellyfin.Plugin.Watchlist.sln --configuration Release --no-build -p:CollectCoverage=true
      | Module                    | Line | Branch | Method |
      | Jellyfin.Plugin.Watchlist | 100% | 100%   | 100%   |

  The platform sweep and the repeat are three machines and three runs, and one
  machine cannot be three. The leg is out of the command above because that
  command mirrors required contexts, and neither of these is required.
- `DCO sign-off`, `Deterministic PR-hygiene checks` and `dependency-review` read
  a pull request through the API. There is no pull request before you push.
- `Audit workflows (zizmor)` needs zizmor, which is not in this tree and is not
  a dependency of it. This is the one entry in this list that is required, so
  it is also the one gap between a green local run and a mergeable head. What
  closes it is either a run of zizmor a contributor has to install the tool for,
  or the pull request itself, and neither is the command above.

## Adopted

| Check, under the name it reports | Here | Reference |
| --- | --- | --- |
| `build` | `call / build` | The build is a required context on both boards. |
| `ABI floor build` | Partly, under `call / test` | The half that compares the declared ABI with the package set the artifact is compiled from is in the suite, from #88, so it runs on every pull request that can change either value and reports under a context that is already required, and nothing new goes to #63 from this row. It sits there rather than in a workflow because the manifest and the project file are already read by the suite, and a second parser for either beside that one is a thing that can disagree with it. What is not run here is a build against the floor, which needs the second framework and the second package set from #4. #88 also stays open for the half that judges more than one artifact of one release, and there is one artifact today. |
| `Package (JPRM) / Build package` | Not yet | #73 packages one artifact per supported line and attaches each with its checksum. |
| `Package (JPRM) / Generate SBOM` | Not yet | #75 publishes a component inventory and build provenance per artifact. |
| `CodeQL`, `Analyze (csharp)` | `Code scanning (csharp)` | Landed on #57, after being declared and skipped on every event since the tree was made. A second context named `CodeQL` reports beside it, from code scanning rather than from the workflow, and #63 has to pick one of the two by name. See the note under the tables. |
| `DCO sign-off` | `DCO sign-off` | Present and reporting, from this repository's own workflow. |
| `Deterministic PR-hygiene checks` | `Deterministic PR-hygiene checks` | Landed on #59. Body linkage, commit subject linkage and the changelog co-change fail; diff size is an observation. |
| `Reject Trojan Source Unicode` | `Reject Trojan Source Unicode` | Present, reporting and required on both boards. |
| `Audit workflows (zizmor)` | `Audit workflows (zizmor)` | Present, reporting and required on both boards. This row said it was not required here until #63, and the required set names it. A second context named `zizmor` reports beside it, from code scanning rather than from the workflow, and the set names this one and not that one. See the note under the tables. |
| `dependency-review` | `dependency-review` | Present and reporting. It reads the dependency diff of a pull request, so a package that no change introduces or upgrades is in no diff for it to read, and the graph this tree already resolves is not its subject. This row said #56 adds the failing tier for a vulnerable dependency, transitive ones included. That tier landed on `8a405f1` instead, on the restore rather than in a workflow, and it is the `Restore audit` row under `## Deviated upward`. Keeping the two apart is what this row is for, because they look alike and answer different questions: one asks what a change brings in, the other asks what the tree resolves. What #56 is still open for is a scan once per supported server line, with a line whose graph was not scanned failing rather than passing quietly, and the tree carries one package set. |
| `Scorecard analysis` | Declared, never run, push route repaired on #118 | Its push trigger named `main`, a branch this repository does not have. The whole run history of the workflow is empty rather than merely quiet, which is the reading in the section below. The trigger now names `master`, and the check cannot be a required context while push and schedule are its whole route, so nothing goes to #63 from this row. |
| `manifest-freshness` | Not yet | #89 checks that the published manifest still lists the newest release, which under one distribution route is the only thing between a green release and a user who never receives it. |
| `stryker-mutation` | Not yet | #61 adopts mutation testing over the reconciler, reported and never gating. |
| `publish`, `regenerate-manifest` | Not yet | #73 packages the artifacts and #74 publishes the manifest every install reads. |
| `Report any workflow that concluded non-success on the default branch` | Not yet | The check the other board's `publish-failure-alert` reports under. #78 is the same alert here, for a publish that reports success and ships nothing. |
| `Enforce greppable invariants` | `call / test` | Adopted on #60. Which invariants the table holds is read out of it rather than counted here, and `docs/lint-decisions.md` carries the command that lists them. It runs inside the suite rather than as a workflow of its own, because the scanner, the register of declared departures and the fixture shape already exist here for the headless guard, and a check inside the suite runs the same way on a contributor's machine. It reports under a context that is already required, so nothing new goes to #63. The reasoning, the proof that it bites on the real tree, and what a token-level lint cannot do are in `docs/lint-decisions.md`. |

## Deviated downward

This board does without these, and the line next to each is why.

| Check there | Why not here |
| --- | --- |
| `prettier` | Refused on #60. The non-code surface here is prose whose wrapping is deliberate, JSON that is generated or is fixture bytes under test, and one configuration page, so a formatter would take ownership of the files where the arguments live and of files a check already reads for drift. This row said that page is sixteen lines; it was on `c24b5b1` and is 104 today, and the condition the refusal names for revisiting it has since been met, because #31 gave the page a script. What would change the answer, the census the refusal is read off, and both of those readings are in `docs/lint-decisions.md`. |
| `Analyze (javascript-typescript)` | The tree carries no JavaScript or TypeScript to analyse. This row changes the day the configuration page grows a script. |
| `Analyze (actions)` | The scan here reads C# and nothing else, decided on #57. The workflows are the same class of release-critical surface the other board's are, and the instrument over them here is `Audit workflows (zizmor)`, which is required on both boards. A second analyser over the same files before the first one has read this tree once would be two instruments and no readings, which is the argument the `opengrep` row makes for C#. This row is wrong the day zizmor is removed or the day a workflow does something zizmor has no query for. |
| `fuzz` | The other board parses tokens and assertions that arrive from outside the server. This plugin parses one thing, its own documents under the plugin data folder, which are written by this plugin and readable only by someone who already has file access to the server. That is a smaller surface, not an absent one, and if an endpoint ever accepts a document from a caller this row is wrong. |
| `opengrep` | A second static analyser over the same C# the first one reads. #57 is the issue that gets one analyser actually running here, and a second one before the first one runs would be two instruments and no readings. |
| `e2e-login` | There is no login path in this plugin. The equivalent end to end proof is the whole loop on a real server, which is #52, and where it runs is #62. |
| `wiki-lint` | This board has no wiki. Its documentation is `docs/` in this repository, which every other check already reads. |
| `submit-nuget` | This plugin ships as a plugin package installed from a manifest, not as a NuGet package. #74 is the distribution route. |
| `nightly-betas`, `publish-beta`, `publish-jf12-beta`, `publish-jf12-stable` | Nothing has been released here yet and there is no beta channel to publish to. The two supported lines are answer 1 on #1 and the release surface for them is #73 and #74. |

## Deviated upward

This board carries these and the other one does not.

| Check here | Why |
| --- | --- |
| `call / test` | The suite is its own required context rather than a step inside the build, so a green build with an empty or skipped suite is visibly different from a green suite. |
| `Coverage floor` | Landed on #50. The floor is `<Threshold>100</Threshold>` with `<ThresholdType>line,branch</ThresholdType>` in `Jellyfin.Plugin.Watchlist.Tests/Jellyfin.Plugin.Watchlist.Tests.csproj`, so it is in the test project rather than in the workflow and applies the same way on a contributor's machine. A new branch cannot arrive untested without somebody adding a line with a reason to the exclusion list. |
| `Suite on ubuntu-latest`, `Suite on macos-latest`, `Suite on windows-latest` | The headless rule refuses a test that needs a display, elevation, a machine trust store, the network or the machine clock, and a guard that reads the sources is not the same as running the suite where those calls would fail. The sweep also reports the privilege the run had, so an unprivileged green is a reading rather than a claim. |
| `Restore audit` | Landed on `8a405f1`. `NuGetAudit`, `NuGetAuditMode` set to `all`, `NuGetAuditLevel` set to `low`, and NU1901 to NU1904 named in `WarningsAsErrors`, all in `Directory.Build.props`. It sits on the restore rather than in a job, so a package carrying a published advisory refuses every build of this solution wherever it runs, a contributor's machine included. Mode `all` rather than `direct` is what reaches a package no project file references, which is how the advisory this board carried arrived. It refuses and names one package at a time rather than listing the graph, and it says nothing about a server line whose package set is not in the tree. |
| `Run the suite three times` | Landed on #51. A suite that passes once and fails on the second run in a different order is a suite that proves nothing, and order randomisation only says so if the run happens more than once. |

## What the code scan reads, and what it does not

The scan compiles the solution and analyses what that build produced, so what it
read is decided by what compiled and not by a file list.

Which supported server line it builds. Today there is one, so the scan reads the
whole of the source:

    grep -n 'TargetFramework>' Jellyfin.Plugin.Watchlist/Jellyfin.Plugin.Watchlist.csproj Jellyfin.Plugin.Watchlist.Tests/Jellyfin.Plugin.Watchlist.Tests.csproj
    Jellyfin.Plugin.Watchlist/Jellyfin.Plugin.Watchlist.csproj:4:    <TargetFramework>net9.0</TargetFramework>
    Jellyfin.Plugin.Watchlist.Tests/Jellyfin.Plugin.Watchlist.Tests.csproj:4:    <TargetFramework>net9.0</TargetFramework>

Answer 1 on #1 is that 1.0 supports two lines, and #4 is what puts a second
framework, a second package set and a second declared ABI in the tree. The day it
lands, this scan compiles one of the two, and the part it will not have compiled
is whatever the other line's build carries alone, which #82 says is the playlist
adapter's per-line implementation. That is a gap the scan cannot report on,
because code that does not compile into the database is code the queries never
see. This row is where the decision goes when there is one to make: either the
scan gains a second run, or the note says which line is read and calls the other
one accepted. #4 and #82 are the two issues that force the question, and neither
has landed, so no answer is written here yet and the absence is the answer for
today rather than an oversight.

What a pull request from a fork does. Not measured. Uploading an analysis needs
`security-events: write`, and a token on a `pull_request` event from a fork does
not carry it, so whether this job succeeds without uploading or fails outright is
a behaviour of the uploader that nobody here has watched. It matters only once a
context is required, because a required context an outside contribution cannot
report is a pull request nobody can merge. #63 is where that is decided, and it
should read a real fork run before it requires this one rather than take the
paragraph above as an answer.

## Two instruments, each reporting under two names

The workflow audit reports twice on this board, under two names produced by two
different apps:

    gh api "repos/iderex/jellyfin-plugin-watchlist/commits/afb8ccc/check-runs" \
      --jq '.check_runs[] | select(.name=="zizmor" or .name=="Audit workflows (zizmor)") | "\(.name) :: app=\(.app.slug) :: \(.conclusion)"'
    zizmor :: app=github-advanced-security :: success
    Audit workflows (zizmor) :: app=github-actions :: success

    grep -n 'name:' .github/workflows/zizmor.yml | head -2
    33:name: Workflow Security Analysis
    64:    name: Audit workflows (zizmor)

Read on `57b95e4`. The paste above said 21 and 41 until this sentence landed
beside it, and those were the numbers the command printed on the commit that
wrote this section:

    git show 2c3f8ed:.github/workflows/zizmor.yml | grep -n 'name:' | head -2
    21:name: Workflow Security Analysis
    41:    name: Audit workflows (zizmor)

Two later changes to the workflow moved them, and neither of them is about this
file:

    git log --oneline 2c3f8ed..57b95e4 -- .github/workflows/zizmor.yml
    dd615dc Make the workflow audit's case from this tree's own release surface [#168]
    3c5afc4 Point the workflow audit at the branch this repository has [#166]

    git log --oneline --no-merges 2c3f8ed..57b95e4 -- docs/parity.md | wc -l
    8

Eight commits changed this file while the numbers stood, and the paste was
written once and never taken again:

    git log --oneline -S'    41:    name: Audit workflows (zizmor)' 57b95e4 -- docs/parity.md
    2c3f8ed Write the check parity table against the SSO board's gate [#53]

The sibling reading of `.github/workflows/scan-codeql.yaml` further down still
reproduces, so what went wrong here is a paste that was correct when written
rather than a section that was never right.

`Audit workflows (zizmor)` is the job in this repository's own workflow. `zizmor`
comes from the code scanning app rather than from the workflow, and the workflow
uploads a SARIF file for it. Whether the two can conclude differently is not
measured here; both were successful on the commit above and that is one reading.

Of that pair the required set names the workflow's job and not the app's:

    gh api repos/Flowfin/jellyfin-plugin-watchlist/rulesets/20456281 \
      --jq '.rules[] | select(.type=="required_status_checks") | .parameters.required_status_checks[].context' \
      | grep -iE 'zizmor|codeql|code scanning'
    Audit workflows (zizmor)

One line out of the four contexts these two instruments report under, and it is
the half produced by a workflow in this tree rather than by the scanning app.
This file asked #63 to pick one of the two by name so the choice was not made by
picking whichever was nearer to hand, and the half in the required set is the
one that issue argued for: the workflow's job concludes on its findings whether
or not the upload happened, and the upload is switched off for a pull request
whose head sits outside this repository.

What that leaves unread is what it left unread before. Whether the app's context
is then absent on such a run, rather than red, is not measured here, and
requiring the workflow's name is what keeps a merge from waiting on the answer.

The code scan has the same shape, and this file described it as though one name
reported. Both contexts are on the head of the newest merged pull request:

    gh api "repos/Flowfin/jellyfin-plugin-watchlist/commits/385ae24/check-runs?per_page=100" \
      --jq '.check_runs[] | select(.name=="CodeQL" or .name=="Code scanning (csharp)") | "\(.name) :: app=\(.app.slug) :: \(.conclusion)"' | sort -u
    Code scanning (csharp) :: app=github-actions :: success
    CodeQL :: app=github-advanced-security :: success

    grep -n 'name:' .github/workflows/scan-codeql.yaml | head -2
    22:name: Code scanning
    48:    name: Code scanning (csharp)

`Code scanning (csharp)` is the job in this repository's own workflow. `CodeQL`
comes from the code scanning app, over the analysis that job uploads. This is
the pair that is still open, so the choice belongs to #63 once now rather than
twice. It is also the pair the reading above cannot settle, because there the
job and the upload are one step and separating them is what a fork run would
answer.

The two halves of this pair are not interchangeable, and the reason is the
unmeasured behaviour the section above records. The workflow's context concludes
on the job whether or not the upload happened; whether the app's context
concludes at all when the upload did not happen is not measured here. That is
the same question a pull request from a fork raises, and requiring the app's
name rather than the workflow's would make the answer to it a precondition of
every merge.

The row for the code scan said the workflow's name is the one #63 requires. That
sentence was a decision written in the table rather than taken on the issue, and
it is the reason this pair went unrecorded while the other one was named. It has
been removed, and the row now says what the zizmor row says.

## The check that was declared here and never ran, and the one that still does not

Both were the same defect and neither was a deviation anybody decided on. One is
repaired and the record of what it was stays, because a repair whose reason is
deleted is a repair nobody can argue with later.

`call / Analyze` was the CodeQL job, and it was skipped on every event. Counted
over the whole run history of the workflow path rather than over a window, so the
runs of the job that replaced it come back in the same output and the two are
told apart by the name each ran under:

    gh api --paginate "repos/Flowfin/jellyfin-plugin-watchlist/actions/workflows/scan-codeql.yaml/runs?per_page=100" \
      --jq '.workflow_runs[] | "\(.name) | \(.event) | \(.conclusion)"' | sort | uniq -c | sort -rn
         52 Code scanning | pull_request | success
         45 Code scanning | push | success
         33 🔬 Run CodeQL | pull_request | skipped
         31 🔬 Run CodeQL | push | skipped
          2 Code scanning | push | cancelled
          1 🔬 Run CodeQL | schedule | skipped
          1 Code scanning | schedule | success

Sixty-five runs under the old name, on all three events it triggered on, and not
one of them anything but skipped. The rows under the new name move as it runs
again; the rows under the old one cannot, because that workflow is gone.

This paragraph pasted a window over the last forty runs of the repository until
the change carrying these sentences, and that paste had stopped reproducing. The
command returns nothing now, because forty runs of this repository no longer
reach back past the replacement:

    gh run list --limit 40 --json workflowName,conclusion,event \
      --jq '[.[] | .workflowName + " | " + .event + " | " + (.conclusion // "-")] | unique | .[]' | grep CodeQL ; echo "rc=$?"
    rc=1

It is the same defect the `Scorecard analysis` paragraphs below record against
themselves, where a count over the last forty runs was read as a history and the
field carrying the whole history was one call away. That correction was made for
that check and left its two neighbours in the shape it was correcting, which is
why both are taken again here.

The reusable workflow it called was guarded on the repository name, and this
repository passed the template's name rather than its own:

    gh api repos/jellyfin/jellyfin-meta-plugins/contents/.github/workflows/scan-codeql.yaml?ref=eb99033a7ff644881b014bc0b4169916c854a68b \
      --jq '.content' | base64 -d | grep -n 'if:'
    17:    if: ${{ github.repository == inputs.repository-name }}
    git show a664d9e:.github/workflows/scan-codeql.yaml | grep -n 'repository-name'
    30:      repository-name: jellyfin/jellyfin-plugin-template

So no line of this repository had been read by code scanning. The check was green
in the sense that it was not red, which is the worst way for an instrument to be
missing. #57 replaced the delegation with a workflow in this tree, and the section
below says what that scan reads and what it does not.

`📝 Create/Update Release Draft & Release Bump PR` is skipped for exactly the
same reason, and its guard is in the same place:

    gh api repos/jellyfin/jellyfin-meta-plugins/contents/.github/workflows/changelog.yaml?ref=eb99033a7ff644881b014bc0b4169916c854a68b \
      --jq '.content' | base64 -d | grep -n 'if:' | head -1
    24:    if: ${{ github.repository == inputs.repository-name }}

That one is not a check and gates nothing, but it is the route a release draft
and a version bump were supposed to arrive by, and its job is skipped the same
way:

    gh api --paginate "repos/Flowfin/jellyfin-plugin-watchlist/actions/workflows/changelog.yaml/runs?per_page=100" \
      --jq '.workflow_runs[] | "\(.name) | \(.event) | \(.conclusion)"' | sort -u
    📝 Create/Update Release Draft & Release Bump PR | push | skipped

One line over the whole history of that workflow rather than over a slice of it.
Every run of it came from a push and every one was skipped, and that output gains
a second line the moment either half of that stops being true.

How many runs that is comes out of the same command with `uniq -c` in place of
`sort -u`, and this paragraph pasted that number until the change these sentences
arrive in. It said eighty-one, and the count had already moved when the command
was run again, because this workflow triggers on every push to the repository
while the property the sentence rests on does not move at all. So the number was
a reading that had to be taken again for a reason that had nothing to do with
what it was quoted for. It is left out here rather than refreshed: a reader who
wants the size runs the command with the count in it, and a reader who wants the
claim reads a line that changes only when the claim does.

The window this replaced still reproduced on the day it was replaced, and it is
taken again anyway. It is the same command shape as the one above it, over a
route nothing has repaired, so what stands between it and the paste above is the
number of runs it takes to push a workflow out of the last forty. Correcting one
of the pair and leaving the other would leave the correction to be made a second
time by whoever meets it next.

The workflow triggers and the job inside it does not run, which is why the
tracker shows the route as present. #72 owns the changelog and #74 owns the
manifest.

`Scorecard analysis` is a third case, and it turned out to be the plainest of the
three rather than the mildest. Its push trigger named a branch this repository
does not have:

    git show a664d9e:.github/workflows/scorecard.yml | grep -n -A1 '^  push:'
    32:  push:
    33-    branches: [main]
    gh repo view --json defaultBranchRef --jq .defaultBranchRef.name
    master

The earlier reading here was that no run appeared in the last forty runs of the
repository, and it said in the same breath that forty runs is a window and not
the history. The count over the whole history was one API field away, and before
the repair below it was zero:

    gh api repos/iderex/jellyfin-plugin-watchlist/actions/workflows/scorecard.yml/runs --jq .total_count
    0

So the weekly schedule had not produced a run either, and the sentence that the
check was "not dead" was a claim about a cron expression rather than a reading.
The workflow had never run, nothing on this board had been scored by it, and the
score the badge would publish did not exist.

The repair is the push trigger naming `master`. It is one line, and what it buys
is that every push to the mainline re-scores, which is what makes the weekly
schedule a floor rather than the only route.

Those sentences stood in the present tense until the change carrying this
paragraph, and the repair they describe had already made every one of them
false. The same field answers differently now:

    gh api repos/Flowfin/jellyfin-plugin-watchlist/actions/workflows/scorecard.yml/runs --jq .total_count
    50
    gh api "repos/Flowfin/jellyfin-plugin-watchlist/actions/workflows/scorecard.yml/runs?per_page=100" \
      --jq '[.workflow_runs[].event] | group_by(.) | map({event: .[0], runs: length})'
    [{"event":"push","runs":49},{"event":"schedule","runs":1}]

Forty-nine runs from the mainline pushes the repair bought and one from the
weekly schedule, so both routes produce runs rather than only the one the repair
added. That count moves with every push and the paste is one moment of it.

A score exists as well, which is the half the paragraph above was really about:

    curl -s https://api.securityscorecards.dev/projects/github.com/Flowfin/jellyfin-plugin-watchlist \
      | jq -c '{date, commit: .repo.commit, score}'
    {"date":"2026-08-16T21:46:29Z","commit":"b5e0bd75fd6cd29780af7346cd1b1918fa054f70","score":6.4}

What that number says about this repository is not read here and no claim is
made about it. What it settles is the sentence above it: the instrument that was
declared and never ran now runs and publishes.

The old owner path in the pasted command above still reaches this repository,
because the transfer left a redirect, so it is left as it was read rather than
rewritten into a command that was never run:

    gh api repos/iderex/jellyfin-plugin-watchlist --jq .full_name
    Flowfin/jellyfin-plugin-watchlist

A required context is a different question and the answer is no. A ruleset
requires a context to report on the head of a pull request, and this workflow
has no `pull_request` trigger at all:

    git show HEAD:.github/workflows/scorecard.yml \
      | sed -n '/^on:/,/^permissions:/{/^permissions:/d;p;}' | grep -vE '^ *#|^$'
    on:
      branch_protection_rule:
      schedule:
        - cron: "27 4 * * 1"
      push:
        branches: [master]

That absence is deliberate and the reason is written at the trigger: the
pull-request path is experimental upstream and cannot publish results. So
`Scorecard analysis` reports on a push to `master` and never on a pull request,
and requiring it would be requiring a context no pull request can ever produce.
Nothing goes from here to #63. This row changes the day the workflow gains a
route that reports on a pull request.

The first run after the repair is recorded on #118 with the command that read
it, because that run could only be produced by a push to `master` and the change
carrying the repair was still on a branch. The readings above are taken here
rather than left there, since a push to `master` is no longer something this file
is waiting for.

## What this file does not say

It does not say whether the checks it calls adopted are enough. It compares this
board with one other board, and a check neither of them carries does not appear
here at all.

It has a row per check and not per workflow, so a workflow that reports no check
and gates nothing has no row. `sync-labels.yaml` here is the one such file. It
still has none, because it reports on nothing and gates nothing, and a row for
it would say it is a check.

A decision has been taken about that file, which is what this paragraph used to
say had not happened, so it is recorded here rather than left to the workflow's
own comment. The sync it called replaced this board's labels with a shared set
and deleted every label that set does not name, which is nine of the twenty this
board uses, the whole `area:` vocabulary among them. #196 measured that and this
is the position taken: the shared set is still read, from its own URL at the
moment of the run, and this repository's own labels are handed to the same run
in `.github/labels.yaml` beside it. `delete-other-labels` is kept, because
removing a label nobody declared is the job; what changed is that this board's
labels are now declared. The shared set is not copied into this tree, so the two
files cannot drift against each other.

What that leaves undecided is deliberate. Whether this board should keep a
vocabulary of its own or move onto the shared one is a question about the labels
rather than about the workflow, #196 declines to settle it, and keeping both
sets is the state the board was already in.

It does not say that a green run of the adopted set means the change is right.
Nothing in the required set reads a pull request for whether it does what its
issue asked for, and #63 does not change that.

The rows marked "not yet" are claims about what an issue will do, not
measurements. Each names its issue and each is wrong the moment that issue
closes without doing it, which is what a row rather than a sentence is for.
