# Check parity

The gate of the SSO plugin board is the target this board's gate is measured
against. This file is the comparison, check by check. Every row says one of three
things: this board adopts the check, this board deliberately does without it, or
this board carries something the other one does not. A gap with a reason next to
it is a decision. A gap with nothing next to it is a defect, and that is what
this file exists to make impossible to leave lying around.

Nothing here is required by the mainline yet. What the mainline requires is
printed rather than restated, because a set written into a document drifts
against the live one without anybody noticing:

    gh api repos/iderex/jellyfin-plugin-watchlist/rulesets/20456281 \
      --jq '{enforcement, bypass: .bypass_actors, required: [.rules[] | select(.type=="required_status_checks") | .parameters.required_status_checks[].context]}'
    {"bypass":[],"enforcement":"active","required":["call / build","call / test","Reject Trojan Source Unicode"]}

Requiring the adopted set is #63, and it happens once, after the checks are
green.

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

Three legs, in the order the three required contexts appear in the ruleset:
build, then the suite, then the Unicode scan. It fails at the first failing leg,
and the Unicode leg fails closed on a scanner error rather than reading a broken
scanner as a clean tree, which is how the workflow reads it too.

The scan pattern is read out of the workflow rather than retyped. A second copy
of that character class in this file would be the rule written twice, and the
copy would be the one that goes stale.

The three legs were run at the commit this file lands on. Set
`DOTNET_CLI_UI_LANGUAGE=en` first if the machine's locale is not English, because
the output below is what an English reader gets and a translated one cannot be
compared with it:

    dotnet build Jellyfin.Plugin.Watchlist.sln --configuration Release
    Build succeeded.
        0 Warning(s)
        0 Error(s)
    dotnet test Jellyfin.Plugin.Watchlist.sln --configuration Release --no-build
    Passed!  - Failed:     0, Passed:   114, Skipped:     0, Total:   114

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
  machine cannot be three. The leg is out of the command above because the three
  required contexts are what that command mirrors, and this one is not among
  them until #63 says so.
- `DCO sign-off`, `Deterministic PR-hygiene checks` and `dependency-review` read
  a pull request through the API. There is no pull request before you push.
- `Audit workflows (zizmor)` needs zizmor, which is not in this tree and is not
  a dependency of it.

## Adopted

| Check, under the name it reports | Here | Reference |
| --- | --- | --- |
| `build` | `call / build` | The build is a required context on both boards. |
| `ABI floor build` | Not yet | #4 puts the framework, the package set and the declared ABI on both supported lines, and #88 is the check that refuses a package whose declared ABI and build line disagree. |
| `Package (JPRM) / Build package` | Not yet | #73 packages one artifact per supported line and attaches each with its checksum. |
| `Package (JPRM) / Generate SBOM` | Not yet | #75 publishes a component inventory and build provenance per artifact. |
| `CodeQL`, `Analyze (csharp)` | Declared, never runs | See the section on the two checks that never run. #57 makes code scanning a check that runs under a stable name. |
| `DCO sign-off` | `DCO sign-off` | Present and reporting, from this repository's own workflow. |
| `Deterministic PR-hygiene checks` | `Deterministic PR-hygiene checks` | Landed on #59. Body linkage, commit subject linkage and the changelog co-change fail; diff size is an observation. |
| `Reject Trojan Source Unicode` | `Reject Trojan Source Unicode` | Present, reporting and required on both boards. |
| `Audit workflows (zizmor)` | `Audit workflows (zizmor)` | Present and reporting. Required there, not required here until #63. A second context named `zizmor` reports beside it, from code scanning rather than from the workflow, and #63 has to pick one of the two by name. See the note under the tables. |
| `dependency-review` | `dependency-review` | Present and reporting. #56 adds the failing tier for a vulnerable dependency, transitive ones included. |
| `Scorecard analysis` | Declared, not observed | Its push trigger names a branch this repository does not have, so that route never fires. #118 owns it. See the section below. |
| `manifest-freshness` | Not yet | #89 checks that the published manifest still lists the newest release, which under one distribution route is the only thing between a green release and a user who never receives it. |
| `stryker-mutation` | Not yet | #61 adopts mutation testing over the reconciler, reported and never gating. |
| `publish`, `regenerate-manifest` | Not yet | #73 packages the artifacts and #74 publishes the manifest every install reads. |
| `Report any workflow that concluded non-success on the default branch` | Not yet | The check the other board's `publish-failure-alert` reports under. #78 is the same alert here, for a publish that reports success and ships nothing. |

## Deviated downward

This board does without these, and the line next to each is why.

| Check there | Why not here |
| --- | --- |
| `prettier` | There is no JavaScript, no TypeScript, no JSON API surface and no stylesheet in this tree beyond one configuration page. #60 decides whether a formatter check is adopted and records the answer either way, so this row is a decision that is owed rather than one that has been taken. |
| `Enforce greppable invariants` | The invariants that check enforces are that board's, and this board has not yet written its own. #60 carries the same decision. |
| `Analyze (javascript-typescript)` | The tree carries no JavaScript or TypeScript to analyse. This row changes the day the configuration page grows a script. |
| `Analyze (actions)` | Not a decision yet. The workflows here are the same class of release-critical surface as the other board's, and `Audit workflows (zizmor)` covers part of it. #57 owns which languages the scan reads. |
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
| `Run the suite three times` | Landed on #51. A suite that passes once and fails on the second run in a different order is a suite that proves nothing, and order randomisation only says so if the run happens more than once. |

## One workflow, two contexts with the same subject

The workflow audit reports twice on this board, under two names produced by two
different apps:

    gh api "repos/iderex/jellyfin-plugin-watchlist/commits/afb8ccc/check-runs" \
      --jq '.check_runs[] | select(.name=="zizmor" or .name=="Audit workflows (zizmor)") | "\(.name) :: app=\(.app.slug) :: \(.conclusion)"'
    zizmor :: app=github-advanced-security :: success
    Audit workflows (zizmor) :: app=github-actions :: success

    grep -n 'name:' .github/workflows/zizmor.yml | head -2
    21:name: Workflow Security Analysis
    41:    name: Audit workflows (zizmor)

`Audit workflows (zizmor)` is the job in this repository's own workflow. `zizmor`
comes from the code scanning app rather than from the workflow, and the workflow
uploads a SARIF file for it. Whether the two can conclude differently is not
measured here; both were successful on the commit above and that is one reading.
Which of them #63 requires is a decision for #63, and this file records that
there are two so the decision is taken deliberately rather than by picking
whichever name is nearer to hand.

## Two checks that are declared here and never run

Both are the same defect and neither is a deviation anybody decided on.

`call / Analyze` is the CodeQL job, and it is skipped on every event:

    gh run list --limit 40 --json workflowName,conclusion,event \
      --jq '[.[] | .workflowName + " | " + .event + " | " + (.conclusion // "-")] | unique | .[]' | grep CodeQL
    🔬 Run CodeQL | pull_request | skipped
    🔬 Run CodeQL | push | skipped

The reusable workflow it calls is guarded on the repository name, and this
repository passes the template's name rather than its own:

    gh api repos/jellyfin/jellyfin-meta-plugins/contents/.github/workflows/scan-codeql.yaml?ref=eb99033a7ff644881b014bc0b4169916c854a68b \
      --jq '.content' | base64 -d | grep -n 'if:'
    17:    if: ${{ github.repository == inputs.repository-name }}
    grep -n 'repository-name' .github/workflows/scan-codeql.yaml
    30:      repository-name: jellyfin/jellyfin-plugin-template

So no line of this repository has ever been read by code scanning. The check is
green in the sense that it is not red, which is the worst way for an instrument
to be missing. #57 owns it.

`📝 Create/Update Release Draft & Release Bump PR` is skipped for exactly the
same reason, and its guard is in the same place:

    gh api repos/jellyfin/jellyfin-meta-plugins/contents/.github/workflows/changelog.yaml?ref=eb99033a7ff644881b014bc0b4169916c854a68b \
      --jq '.content' | base64 -d | grep -n 'if:' | head -1
    24:    if: ${{ github.repository == inputs.repository-name }}

That one is not a check and gates nothing, but it is the route a release draft
and a version bump were supposed to arrive by, and its job is skipped the same
way:

    gh run list --limit 40 --json workflowName,conclusion,event \
      --jq '[.[] | select(.workflowName | test("Release Draft")) | .workflowName + " | " + .event + " | " + (.conclusion // "-")] | unique | .[]'
    📝 Create/Update Release Draft & Release Bump PR | push | skipped

The workflow triggers and the job inside it does not run, which is why the
tracker shows the route as present. #72 owns the changelog and #74 owns the
manifest.

`Scorecard analysis` is a third case and a milder one. Its push trigger names a
branch this repository does not have:

    grep -n -A1 '  push:' .github/workflows/scorecard.yml
    32:  push:
    33-    branches: [main]
    gh repo view --json defaultBranchRef --jq .defaultBranchRef.name
    master

The weekly schedule still fires on the default branch, so the check is not dead,
but no run of it appears in the last forty runs of this repository and the push
route never fires:

    gh run list --limit 40 --json workflowName --jq '[.[].workflowName] | unique | .[]'
    Coverage
    DCO
    Dependency review
    PR hygiene
    Repeated test run
    Suite on every platform
    Workflow Security Analysis
    unicode-guard
    🏗️ Build Plugin
    📝 Create/Update Release Draft & Release Bump PR
    🔬 Run CodeQL
    🧪 Test Plugin

Forty runs is a window rather than the history, so that output says the workflow
has not run recently and does not say it has never run. #118 carries the repair
and the reading that would replace this one.

## What this file does not say

It does not say whether the checks it calls adopted are enough. It compares this
board with one other board, and a check neither of them carries does not appear
here at all.

It has a row per check and not per workflow, so a workflow that reports no check
and gates nothing has no row. `sync-labels.yaml` here is the one such file, and
the reason it is named in this paragraph rather than given a row is that giving
it one would say a decision was taken about it as a check, which is not true.

It does not say that a green run of the adopted set means the change is right.
Nothing in the required set reads a pull request for whether it does what its
issue asked for, and #63 does not change that.

The rows marked "not yet" are claims about what an issue will do, not
measurements. Each names its issue and each is wrong the moment that issue
closes without doing it, which is what a row rather than a sentence is for.
