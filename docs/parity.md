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
    {"bypass":[],"enforcement":"active","required":["call / build","call / test","Reject Trojan Source Unicode","Audit workflows (zizmor)","Alone on 10.11","Code scanning (csharp)","Coverage floor","DCO sign-off","Deterministic PR-hygiene checks","Run the suite three times","Suite on macos-latest","Suite on ubuntu-latest","Suite on windows-latest","Vulnerable dependency listing","With the family on 10.11","dependency-review"]}

That set has sixteen contexts in it. It had four when the paragraph below was
written and three before that, and each earlier reading was pasted here as
though it were stable, which is the drift the sentence above warns about and the
reason the set is printed rather than restated. The count of the sentences that
carried the set of three, at the commit that repair started from, is kept
because it measures that drift rather than today's set:

    git show 53ea2ea:docs/parity.md | grep -c 'three required contexts\|not required here until #63\|required there and reporting here\|Nothing here is required by the mainline'
    4

The set drifted exactly the way the sentence above warns about, which is why
this is a reading taken again rather than a rewording.

Requiring the whole adopted set is #63 and it has happened. Every check this
file marks as adopted and that reports a context on a pull request head is in
the set above, under the name it actually reports. What is adopted and NOT in
the set is out of it for a reason written in its own row: `Scorecard analysis`
reports on the mainline push and on a schedule and never on a pull request, so
requiring it would be a context that never arrives; `Restore audit` is a
property of every build rather than a context of its own; and the packaging,
inventory, provenance and alert legs live inside `.github/workflows/publish.yaml`
and report under no context at all. The section `Two instruments, each reporting
under two names` is where the two pairs are followed up, and both are decided by
name now rather than one of them.

## The two gates as measured

The other board's required set:

    gh api repos/iderex/jellyfin-plugin-sso/rulesets --jq '.[].id'
    18802863
    gh api repos/iderex/jellyfin-plugin-sso/rulesets/18802863 \
      --jq '[.rules[] | select(.type=="required_status_checks") | .parameters.required_status_checks[].context]'
    ["build","ABI floor build","Package (JPRM) / Build package","Package (JPRM) / Generate SBOM","CodeQL",
     "Analyze (csharp)","DCO sign-off","Deterministic PR-hygiene checks","Enforce greppable invariants",
     "Reject Trojan Source Unicode","Audit workflows (zizmor)","prettier","dependency-review"]

What actually reported on that board's mainline commit `4da1c25`, which was its
newest when this reading was taken and is not any more. That is a different
question from what is required, and it answers it for the checks that are not:

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

And what reported here, on the head of a merged pull request rather than on
`master`, because several of this board's checks run on a pull request and never
on a push, so reading `master` would report them missing. The head is #117's,
which was the newest merged pull request when this reading was taken:

    gh pr view 117 --json number,headRefOid --jq '"#\(.number) \(.headRefOid)"'
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

The pinning was done and the words around it were not. Three sentences in this
file called a pinned commit the newest or the last merged, and the board has moved
past all three: `4da1c25` above, `afb8ccc` in this section, and `385ae24` under
`Two instruments, each reporting under two names`. All three commits still answer
exactly what is pasted under them, which is what naming them bought. What stopped
being true is the word saying each was the freshest reading available, and each
of the three now names the pull request or the commit and says it was the newest
when the reading was taken.

The paragraph above this section also pasted a command that selects rather than
pins, `gh pr list --state merged --limit 1`, with `#117` under it. That output is
one merge old the moment anything lands, and the distance it had accumulated when
this was read is what a reader would have had to notice for themselves:

    gh pr list --state merged --limit 300 --json number,mergedAt \
      --jq '[.[] | select(.mergedAt > "2026-08-06T08:40:25Z")] | length'
    71

The command in that paragraph is a pull request view of #117 now, which prints the
same head for as long as that pull request exists.

No guard is offered for this and none is claimed. All four readings go over the
network, and the suite is refused the network by name:

    grep -c '^network' Jellyfin.Plugin.Watchlist.Tests/HeadlessRules.txt
    8

so there is nothing here that could run these commands and compare them with the
sentences over them. What found it was running each command in this file and
reading the paragraph above it against what the command printed. Comparing the
pasted output alone would have found none of the three, because all three outputs
are still correct.

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
| `build` | `call / build` | The build is a required context on both boards. It is the packaging leg: the job it calls checks out, sets up the SDK and runs jprm, so what it answers is whether this repository still produces the archive a server installs. That is why it stays beside this repository's own compiling legs rather than being replaced by them, which is #54's sixth condition asking this file to say so. The two answer different questions and neither covers the other. A tree that compiles can fail to package, because the archive is assembled from `build.yaml` and the packager's own rules rather than from the compiler's output alone; and a tree that packages can still be one this board refuses, because the coverage floor, the three platforms, the three consecutive runs and every guard test in `Jellyfin.Plugin.Watchlist.Tests` run in this repository's own workflows and in no leg the shared workflow offers. What this row does NOT record is a reason per supported line, because there is one line: the second leg is #54's second condition and waits on a stable 12.0 server release and then #4. |
| `ABI floor build` | Partly, under `call / test` | The half that compares the declared ABI with the package set the artifact is compiled from is in the suite, from #88, so it runs on every pull request that can change either value and reports under a context that is already required, and nothing new goes to #63 from this row. It sits there rather than in a workflow because the manifest and the project file are already read by the suite, and a second parser for either beside that one is a thing that can disagree with it. What is not run here is a build against the floor, which needs the second framework and the second package set from #4. #88 also stays open for the half that judges more than one artifact of one release, and there is one artifact today. |
| `Package (JPRM) / Build package` | Not yet | #73 packages one artifact per supported line and attaches each with its checksum. |
| `Package (JPRM) / Generate SBOM` | Declared, its jobs never run | Landed on #75, inside `.github/workflows/publish.yaml` rather than as a check of its own. The build job writes a CycloneDX inventory of the archive and refuses one that is not CycloneDX or does not name the archive it describes, the attest job mints a build provenance attestation, and the release job refuses a release carrying anything other than exactly one of each. That route BUILDS on a pushed tag and on nothing else, and no tag has been pushed, so no inventory has been produced here. The workflow now carries a second, manual trigger, and every job that builds, attests or releases is skipped on it, so a run of that workflow is no longer evidence that a build happened - which is why this sentence names the jobs rather than the run count, and hands the count to the reader: `gh api repos/Flowfin/jellyfin-plugin-watchlist/actions/workflows/publish.yaml/runs --jq .total_count`. `README.md` carries the commands a reader checks both with and says the same thing about them. What is not settled is one inventory per artifact once a release carries more than one, which is #73 behind #4. |
| `CodeQL`, `Analyze (csharp)` | `Code scanning (csharp)` | Landed on #57, after being declared and skipped on every event since the tree was made. A second context named `CodeQL` reports beside it, from code scanning rather than from the workflow. This row said #63 has to pick one of the two by name; it has, and the required set names this one and not that one. See the note under the tables. |
| `DCO sign-off` | `DCO sign-off` | Present and reporting, from this repository's own workflow. |
| `Deterministic PR-hygiene checks` | `Deterministic PR-hygiene checks` | Landed on #59. Body linkage, commit subject linkage and the changelog co-change fail; diff size is an observation. |
| `Reject Trojan Source Unicode` | `Reject Trojan Source Unicode` | Present, reporting and required on both boards. |
| `Audit workflows (zizmor)` | `Audit workflows (zizmor)` | Present, reporting and required on both boards. This row said it was not required here until #63, and the required set names it. A second context named `zizmor` reports beside it, from code scanning rather than from the workflow, and the set names this one and not that one. See the note under the tables. |
| `dependency-review` | `dependency-review` | Present and reporting. It reads the dependency diff of a pull request, so a package that no change introduces or upgrades is in no diff for it to read, and the graph this tree already resolves is not its subject. This row said #56 adds the failing tier for a vulnerable dependency, transitive ones included. That tier landed on `8a405f1` instead, on the restore rather than in a workflow, and it is the `Restore audit` row under `## Deviated upward`. Keeping the two apart is what this row is for, because they look alike and answer different questions: one asks what a change brings in, the other asks what the tree resolves. What #56 is still open for is a scan once per supported server line, with a line whose graph was not scanned failing rather than passing quietly, and the tree carries one package set. |
| `Scorecard analysis` | Declared, never run, push route repaired on #118 | Its push trigger named `main`, a branch this repository does not have. The whole run history of the workflow is empty rather than merely quiet, which is the reading in the section below. The trigger now names `master`, and the check cannot be a required context while push and schedule are its whole route, so nothing goes to #63 from this row. |
| `manifest-freshness` | Not yet | #89 checks that the published manifest still lists the newest release, which under one distribution route is the only thing between a green release and a user who never receives it. |
| `stryker-mutation` | Not yet | #61 adopts mutation testing over the reconciler, reported and never gating. |
| `publish`, `regenerate-manifest` | `publish` declared, its publishing jobs never run; `regenerate-manifest` not yet | The route this board would publish from is `.github/workflows/publish.yaml`, and its first pipeline landed on `b877b47`. This row said the pipeline it holds today landed there, and that stopped being true while the sentence stayed: five commits have changed that file since, carrying the packaged release notes from #72 and the inventory and the provenance bundle the `Package (JPRM) / Generate SBOM` row above credits to #75. Which commit holds what the file does today is derived rather than written here, with `git log origin/master --oneline -1 -- .github/workflows/publish.yaml`, and the distance from the first one with `git diff --stat b877b47 origin/master -- .github/workflows/publish.yaml`. It PUBLISHES on a pushed tag and on nothing else, and no tag has been pushed, so there is nothing installed anywhere to show for it. It is no longer a workflow with one trigger: a manual one was added on #78 whose single input forces the condition the last job watches for, and every job that builds, attests or releases is skipped on that path, so a run of this workflow no longer means a release was attempted. The three numbers a reader wants move and are derived rather than written here - `gh api repos/Flowfin/jellyfin-plugin-watchlist/actions/workflows/publish.yaml/runs --jq .total_count` for the runs, `gh api repos/Flowfin/jellyfin-plugin-watchlist/releases --jq 'length'` for the releases, and `git ls-remote --tags origin` for the tags - and it is the last two that say whether anything was published. That is a publishing trigger nobody has fired rather than a route that is not built, and this row said `Not yet` without separating the two. The distinction is the one the `Scorecard analysis` row is written for from the other side, where the route existed and could not fire at all. #73 is open for one artifact per supported line and there is one today, #74 for the manifest every install reads, and #134 for the first tag. Nothing here regenerates a manifest. |
| `Report any workflow that concluded non-success on the default branch` | `alert` inside `Publish Release`, declared, and fireable on demand without publishing | The check the other board's `publish-failure-alert` reports under. Landed on #78, as the last job of `.github/workflows/publish.yaml` rather than as a check of its own, so it reports under no context here and nothing goes to #63 from this row. It needs every other job of that workflow and runs with `always()`, which is what lets it report on a run that stopped before reaching the end; a step could not. It reads the release for the tag the run published and names every way the run left something nobody can install: no release, a draft, no archive, a second `.md5`, a missing checksum, missing packaging metadata, a missing inventory, a missing attestation bundle. The asset set it expects is read off the route above it rather than decided in the script. It raises one issue per run, keyed on the run id, so closing the issue for one run suppresses nothing for the next. It is the only job in this repository that writes to the tracker. The raising half can be exercised without spending a version number, which is what #78's fourth condition landed. The workflow takes a manual trigger with one boolean input, `deliberately-fail`; every job that builds, attests or releases is skipped on that path, and this job runs against a release that was never created, judges it as shipping nothing and raises the issue for real. The issue it raises on that path opens by saying it was a deliberate failure that published nothing, so it cannot be read later as a report about a release somebody should go looking for, and the tag name it judges is one no tag of this repository can carry. Whether it has been fired, and how often, is derived rather than written here: `gh api repos/Flowfin/jellyfin-plugin-watchlist/actions/workflows/publish.yaml/runs --jq .total_count` counts every run of the workflow, of which the publishing kind needs a pushed tag and none has been pushed. Two things are still not covered and #78 stays open for the first. The alert does not cover the scheduled manifest check, which is #89 and does not exist. And a run cancelled before its jobs start leaves nothing to run this at all, which no job inside that workflow can reach. |
| `Enforce greppable invariants` | `call / test` | Adopted on #60. Which invariants the table holds is read out of it rather than counted here, and `docs/lint-decisions.md` carries the command that lists them. It runs inside the suite rather than as a workflow of its own, because the scanner, the register of declared departures and the fixture shape already exist here for the headless guard, and a check inside the suite runs the same way on a contributor's machine. It reports under a context that is already required, so nothing new goes to #63. The reasoning, the proof that it bites on the real tree, and what a token-level lint cannot do are in `docs/lint-decisions.md`. |

## Deviated downward

This board does without these, and the line next to each is why.

| Check there | Why not here |
| --- | --- |
| `prettier` | Refused on #60. The non-code surface here is prose whose wrapping is deliberate, JSON that is generated or is fixture bytes under test, and one configuration page, so a formatter would take ownership of the files where the arguments live and of files a check already reads for drift. This row said that page is sixteen lines; it was on `c24b5b1` and is 104 today, and the condition the refusal names for revisiting it has since been met, because #31 gave the page a script. What would change the answer, the census the refusal is read off, and both of those readings are in `docs/lint-decisions.md`. |
| `Analyze (javascript-typescript)` | The tree carries no JavaScript or TypeScript to analyse. This row changes the day the configuration page grows a script. |
| `Analyze (actions)` | The scan here reads C# and nothing else, decided on #57. The workflows are the same class of release-critical surface the other board's are, and the instrument over them here is `Audit workflows (zizmor)`, which is required on both boards. A second analyser over the same files is the case the `opengrep` row is about, and the reason both rows carried, that the first instrument had not read this tree once, is spent here too: zizmor is required and reporting. What holds this row is that the workflows already have an instrument over them. This row is wrong the day zizmor is removed or the day a workflow does something zizmor has no query for. |
| `fuzz` | The other board parses tokens and assertions that arrive from outside the server. This plugin parses one thing, its own documents under the plugin data folder, which are written by this plugin and readable only by someone who already has file access to the server. That is a smaller surface, not an absent one, and if an endpoint ever accepts a document from a caller this row is wrong. |
| `opengrep` | A second static analyser over the same C# the first one reads. The reason recorded here was that the first one was not running. #57 landed and `Code scanning (csharp)` reports on every pull request, so that reason is spent and none has been written in its place. By this file's own opening that leaves this row a gap with nothing next to it rather than a decision, and it stays one until the deviation is taken again against an analyser that runs. |
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
| `Restore audit` | Landed on `8a405f1`. `NuGetAudit`, `NuGetAuditMode` set to `all`, `NuGetAuditLevel` set to `low`, and NU1901 to NU1904 named in `WarningsAsErrors`, all in `Directory.Build.props`. It sits on the restore rather than in a job, so a package carrying a published advisory refuses every build of this solution wherever it runs, a contributor's machine included. Mode `all` rather than `direct` is what reaches a package no project file references, which is how the advisory this board carried arrived. It refuses and names one package at a time rather than listing the graph, which is the half the `Vulnerable dependency listing` row below is for, and it says nothing about a server line whose package set is not in the tree. |
| `Run the suite three times` | Landed on #51. A suite that passes once and fails on the second run in a different order is a suite that proves nothing, and order randomisation only says so if the run happens more than once. |
| `Vulnerable dependency listing` | Landed on #56's first condition, in `.github/workflows/vulnerable-dependencies.yaml`. It reads the same graph the `Restore audit` row above refuses and answers a different question about it: the audit stops at the entry it trips over, and the entries of a graph are not independent, so a red restore names one advisory while the set behind it stays unread. This lists every package carrying a published advisory, transitive ones included, with the package, the resolved version, the severity, the advisory and the project whose graph resolved it, so a red run is enough to act on without re-running anything. Its restore turns the audit off for that one restore, because with it on the listing is never reached; every other route that builds this solution restores with it on. It refuses an answer it cannot read - an empty output, a document naming no project, an output version it was not written against - rather than reporting a clean graph, and the probe that shows each of those refusals runs in the job ahead of the reading. It reports and does not gate. What it does not do is scan once per supported server line and fail for a line it did not reach, which is #56's second condition and waits on a stable 12.0 server release and then on #4; what it does instead is say which line's graph it read. |
| `Mutation score` | Landed on #61, in `.github/workflows/mutation.yaml`. The other board has no instrument that asks this question and neither did this one: every check above says a line ran, compiled, or was not refused, and none of them says whether the suite would have NOTICED that line being wrong. This changes the code one mutation at a time and re-runs the suite against each change. IT REPORTS AND IT NEVER GATES, and that is a decision rather than a stage on the way to gating: a mutation score that blocks a merge is a number people learn to raise rather than a question about the suite. It carries no `pull_request` trigger, it runs weekly and on request, `--break-at 0` is what stops the instrument's own exit code from failing the job, and a test in the suite refuses the trigger being added here by accident rather than a sentence in the workflow's header asking nobody to. Its scope is three subjects - the store, the reconciler and the series rule - held in `Jellyfin.Plugin.Watchlist.Tests/stryker-config.json` beside the suite for the reason the coverage floor is, and a second test refuses that scope silently widening or losing a subject. What the run found is not a number on its own: `Jellyfin.Plugin.Watchlist.Tests/MUTATION.md` carries every surviving mutant with a verdict, because a score with no triage behind it is a number nobody acts on. Four bounds, all in that file. One method of the store is not mutated at all, because one mutation of it does not compile and the instrument's safe mode then removes every mutation in it. A timeout is scored as caught, which is right and is not the same evidence as an assertion failing. Mutants outside the scope and inside blocks no test reaches are filtered out and counted separately in the run's own output. And the run is taken on one supported server line, like every other row here. |
| `Alone on 10.11` | Landed on #104's first half, in `.github/workflows/interoperability.yaml` over `.github/scripts/boot-a-line-with-this-plugin.sh`. The other board's workflow list carries nothing that starts a server; its nearest row is `e2e-login`, which is under `## Deviated downward` above and is about a login path this plugin does not have. Every other check on this board reads the tree, builds it, packages it or greps it, so "this plugin loads" was a claim about source until this landed. The job builds the archive with the same packager and the same pin the release route runs, unpacks it into a stock server's plugin directory inside a container, completes first-time setup, and asserts three things: no error naming this plugin in the startup log, the plugin listed under the identifier `build.yaml` declares with status Active, and an administrator answered and an anonymous caller refused on the plugin list, on this plugin's configuration route and on `Watchlist/Items`. The last of those three is the one that says a real server registered this plugin's controller rather than a test host. A fourth assertion scans the running server for collisions - two loaded plugins claiming one identifier, one configuration file name, or one derived data folder; two scheduled tasks the server keys or the dashboard names the same; two OpenAPI paths the router answers as one; and this plugin loaded under an identifier `build.yaml` does not declare - and it refuses a list it could not read rather than reporting a clean server. Three probes run ahead of the run that matters: the log scan and the listing judgement over fabricated inputs and their one-change neighbours, every collision kind over a report built to carry it and a one-change neighbour that must be clean, and the harness pointed at the same image with no plugin installed, which it must refuse for being unlisted rather than merely exit non-zero. It needs no display, no elevation and no machine trust store. It reports and does not gate. Three things it does not do. It drives no loop through a projection, which is #52 and has no subject while the tree holds no projector. It boots one line, and the disclosure step in the job says which, because there is one package set here. And it installs no sibling: the stock image boots five bundled plugins beside this one, so the scan runs over a real population rather than a set of one, and a server holding this plugin and a sibling from this family is the row below rather than this one. |
| `With the family on 10.11` | Landed on #104's second half, in the same workflow over the same harness with `--alongside`. It derives the family from the hub's declaration in `.github/scripts/say-which-siblings-the-hub-declares.sh`, excludes this board, installs every sibling whose catalogue entry carries a version for the line being booted, and runs the same four assertions over the larger server. A sibling the hub carries no installable version for is a row that says so and is skipped, rather than one left out of the matrix or built from source, because operators install from the catalogue and a matrix exists to mirror their world. The rows are derived at run time and are written nowhere here, so a plugin joining or leaving the family joins or leaves this matrix with nothing to edit; what a run covered is in the run's own output. It needs the `Alone on 10.11` job to have passed, because a verdict about this plugin beside its family is unreadable while this plugin alone is broken. Two bounds. The archive is checked against the checksum the catalogue publishes, and the comparison is watched deciding a real run rather than watched refusing a corrupt archive - no corrupt archive has been fed to it. And it says nothing about a combination the hub does not publish: nine of the eleven declared siblings had nothing installable on the day it landed, and their rows are disclosed rather than exercised. |
| `Whole loop on 10.11` | Landed on #52, in `.github/workflows/whole-loop.yaml` over `.github/scripts/drive-the-whole-loop-on-a-line.sh`. The two rows above boot a server and read what it says ABOUT the plugin; this one drives what the plugin is for. It builds the archive with the same packager and the same pin the release route runs, unpacks it into a stock server in a container, completes first-time setup, generates one film inside the container with the server's own encoder and adds a movie library over it - a stock server has no media, and the add endpoint refuses an item the library cannot answer for - then asserts five things in the order the loop takes them: an item added through `Watchlist/Items` is on the stored list; no playlist carries the configured name before the projection has run; after a run of this plugin's scheduled task, started through the server's own route and waited for rather than slept past, a playlist carrying that name exists and holds the film, read with the two queries a client issues to list playlists and their contents; the stored list still holds the film at that point; and after the row is removed from the playlist the way a client removes one, and the task is run again, the stored list no longer holds it. The second and fourth are one-change neighbours of the third and the fifth rather than decoration: without them a pre-existing playlist would pass the projection assertion, and a store emptied for any reason at all would pass the removal assertion. Two probes run ahead of the run that matters: the four judgements the harness decides with, over fabricated answers and their one-change neighbours with no container, and the same image and archive with the projection turned off through the plugin's own configuration route, which the harness must refuse FOR THE PLAYLIST NEVER APPEARING rather than merely exit non-zero. Every refusal prints the transcript of the calls it made and the last 200 lines the server logged, so a red run is readable without re-running it. It needs no display, no elevation and no machine trust store. It reports and does not gate. Three bounds. It reads one user's PRIVATE list; the shared list is off until an administrator turns it on and is a second projection target with rules of its own. It boots one line, and the disclosure step in the job says which, because there is one package set here. And the triggers in that file are the smallest set that can run the harness at all - a push to the mainline and a pull request against it - because a harness nothing runs is one nobody has watched bite; the triggers are #62 and are decided there: a pull request whose change touches the harness, this file's own workflow, `build.yaml` or the plugin, a weekly schedule because what this reads is a published image rather than the tree alone, `workflow_dispatch` so a commit about to be tagged can be read on demand, and the release tag patterns `publish.yaml` itself triggers on. The job uploads the container log and the request transcript on every run, passing or failing, so a red run stays readable after its step log has scrolled past. ONE CLAUSE OF #62 IS NOT MET AND THE ROW SAYS SO RATHER THAN IMPLYING IT: the tag-triggered run starts beside the publishing chain and does not hold it, so the evidence exists AT a release rather than before one, and what closes that today is a step in `docs/RELEASING.md` that a person performs.

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

What a pull request from a fork does. Read under the token, not under the head,
and the difference between those two is what is left. Uploading an analysis needs
`security-events: write`, and a pull request from a fork runs with a read-only
`GITHUB_TOKEN`, so whether this job succeeds without uploading or fails outright
was a behaviour of the uploader nobody here had watched. It matters exactly
because the context is required: a required context an outside contribution
cannot report is a pull request nobody can merge. This paragraph said #63 should
read such a run before requiring it, and the requirement landed first.

THE READING ARRIVED THE SAME DAY, FROM THE OTHER ROUTE THAT RUNS ON A READ-ONLY
TOKEN. The Dependabot configuration landed on #55 and the automation opened three
pull requests within two minutes. Every context the mainline requires reported on
one of them, and every one concluded green:

    gh pr view 273 --repo Flowfin/jellyfin-plugin-watchlist --json statusCheckRollup \
      --jq '[.statusCheckRollup[] | (.name // .context) + " :: " + (.conclusion // .state)] | sort | unique | .[]'
    Alone on 10.11 :: SUCCESS
    Audit workflows (zizmor) :: SUCCESS
    Code scanning (csharp) :: SUCCESS
    CodeQL :: SUCCESS
    Coverage floor :: SUCCESS
    DCO sign-off :: SUCCESS
    Deterministic PR-hygiene checks :: SUCCESS
    Reject Trojan Source Unicode :: SUCCESS
    Run the suite three times :: SUCCESS
    Suite on macos-latest :: SUCCESS
    Suite on ubuntu-latest :: SUCCESS
    Suite on windows-latest :: SUCCESS
    Vulnerable dependency listing :: SUCCESS
    With the family on 10.11 :: SUCCESS
    call / build :: SUCCESS
    call / test :: SUCCESS
    dependency-review :: SUCCESS

Seventeen lines, and sixteen of them are the required set; `CodeQL` is the app's
half of the pair the section below is about and is not required itself.
`Code scanning (csharp)` is the one this paragraph was about, it carries no
condition on who opened the pull request, and it concluded green with no write on
its token.

WHAT IS STILL NOT READ IS THE HEAD AND NOT THE TOKEN, and it is narrower than
what stood here. A Dependabot pull request has a read-only token and a head
branch inside this repository; a fork pull request has the same token and a head
outside it. The only thing in this tree that reads which of the two it is, is the
condition on the SARIF upload in `.github/workflows/zizmor.yml`, and that
condition switches the upload OFF for both cases and carries
`continue-on-error`, so the job it sits in concludes on its findings either way.
No other required context reads the head repository at all:

    grep -rn 'head.repo.full_name' .github/workflows/
    .github/workflows/zizmor.yml:100:        if: (github.event_name == 'push' && github.ref == 'refs/heads/master') || (github.event.pull_request.head.repo.full_name == github.repository && github.event.pull_request.user.login != 'dependabot[bot]')

So the residual is one step, in one job, written to skip on exactly the case it
is not read on. Every pull request this repository has carried was still opened
from a branch inside it, and that is the sentence a fork run replaces:

    gh pr list --repo Flowfin/jellyfin-plugin-watchlist --state all --limit 400 --json headRepositoryOwner --jq '[.[] | .headRepositoryOwner.login] | unique'
    ["Flowfin"]

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
    Code scanning (csharp)

Two lines out of the four contexts these two instruments report under, and each
is the half produced by a workflow in this tree rather than by the scanning app.
This paste named one line until #63 took the second pair as well. This file
asked that issue to pick one of each pair by name so the choice was not made by
picking whichever was nearer to hand, and the half in the required set is the
one it argued for: the workflow's job concludes on its findings whether or not
the upload happened, and for zizmor the upload is switched off for a pull request
whose head sits outside this repository.

What that leaves unread is what it left unread before. Whether the app's context
is then absent on such a run, rather than red, is not measured here, and
requiring the workflow's name is what keeps a merge from waiting on the answer.

The code scan has the same shape, and this file described it as though one name
reported. Both contexts are on the head of #150, which was the newest merged pull
request when this reading was taken:

    gh api "repos/Flowfin/jellyfin-plugin-watchlist/commits/385ae24/check-runs?per_page=100" \
      --jq '.check_runs[] | select(.name=="CodeQL" or .name=="Code scanning (csharp)") | "\(.name) :: app=\(.app.slug) :: \(.conclusion)"' | sort -u
    Code scanning (csharp) :: app=github-actions :: success
    CodeQL :: app=github-advanced-security :: success

    grep -n 'name:' .github/workflows/scan-codeql.yaml | head -2
    22:name: Code scanning
    48:    name: Code scanning (csharp)

`Code scanning (csharp)` is the job in this repository's own workflow. `CodeQL`
comes from the code scanning app, over the analysis that job uploads. This was
the pair still open, and #63 has taken it the same way as the other: the
required set above names the workflow's job.

The two halves of each pair are not interchangeable, and the reason is the
unmeasured behaviour the section above records. The workflow's context concludes
on the job whether or not the upload happened; whether the app's context
concludes at all when the upload did not happen is not measured here. That is
the same question a pull request from a fork raises, and requiring the app's
name rather than the workflow's would make the answer to it a precondition of
every merge.

WHAT THIS PAIR STILL LEAVES UNREAD, AND IT IS WIDER THAN THE ZIZMOR ONE. There
the upload is a step of its own, switched off for a pull request whose head sits
outside this repository and for one Dependabot opened, and carrying
`continue-on-error`, so the job's verdict is independent of the upload by
construction. Here the analysis and its upload are one step and no such
condition exists, so whether this job concludes green under the read-only token
such a pull request runs with was not settled by reading this tree. IT IS READ
NOW, ON THIS BOARD, and the paragraph under `## What the code scan reads, and
what it does not` carries the whole listing:

    gh pr view 273 --repo Flowfin/jellyfin-plugin-watchlist --json statusCheckRollup       --jq '[.statusCheckRollup[] | (.name // .context) + " :: " + (.conclusion // .state)] | sort | unique | .[]' | grep -iE 'csharp|codeql'
    Code scanning (csharp) :: SUCCESS
    CodeQL :: SUCCESS

Both halves of the pair conclude green on a pull request whose token cannot write
security events, so the shape this paragraph was written against - a required
context that cannot report and a merge that therefore waits forever - is not the
shape this pair has. What is left is the head rather than the token, and it is
one step in the zizmor job written to skip on exactly that case. The row for the
code scan said the workflow's name is the one #63 requires before that had been
taken on the issue; it has been taken now, and the row says what the zizmor row
says.

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
