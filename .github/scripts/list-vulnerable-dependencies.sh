#!/usr/bin/env bash
#
# Lists every package in the resolved graph that carries a published advisory,
# transitive ones included, and fails when the list is not empty.
#
# WHY THIS EXISTS BESIDE THE RESTORE AUDIT. Directory.Build.props turns the NuGet
# audit on at mode `all` and level `low` with NU1901-NU1904 as errors, so a
# restore already REFUSES a vulnerable graph. What a restore does not do is list.
# It stops at what it trips over, and the entries in a graph are not independent:
# removing one forced pin changes what the next package resolves to, which was
# measured on this repository and written on #56. So a red restore names one
# advisory and a reader has no way to tell that from the whole set, and this job
# is the reading that names the set.
#
# It is therefore a second view of one graph rather than a second rule. The audit
# is what refuses a merge; this says what a person has to act on. Both are in
# docs/parity.md, one row each, and neither is the other's substitute.
#
# WHY THE RESTORE HERE TURNS THE AUDIT OFF. With the audit on, the restore this
# command needs fails before anything is listed, so the job would report the
# refusal the restore already reported and never reach the set. `NuGetAudit=false`
# is scoped to this restore, changes nothing tracked, and buys exactly the
# listing. The refusal is not weakened by it: it happens on every other route that
# builds this solution, including the one that gates the mainline.
#
# WHAT IT REFUSES BESIDES A FINDING. An output it cannot read. An empty document,
# a document naming no project, and a document declaring an output version this
# reader was not written against all end the job, because a reader that treats an
# unreadable answer as a clean one turns the check off silently the day the SDK
# moves its output shape. `--prove-it-bites` runs those refusals and their
# one-change neighbours ahead of the reading that matters.

set -euo pipefail

document=""
prove="no"

# Where the server line a finding was found on is derived. It is derived once, in
# the script the step beside this one runs, and read from there rather than parsed
# a second time here: two readers of one value is how the second copy goes stale
# while looking right, and the refusals that script makes on a manifest with no
# declared ABI are the refusals this block needs as well. The path is an argument
# so the probes below can hand it a reading that refuses.
server_lines_script=".github/scripts/say-which-server-lines-this-run-covers.sh"

while [ "$#" -gt 0 ]; do
  case "$1" in
    --document) document="$2"; shift 2 ;;
    --server-lines-script) server_lines_script="$2"; shift 2 ;;
    --prove-it-bites) prove="yes"; shift ;;
    *)
      echo "Unknown argument: $1" >&2
      exit 2
      ;;
  esac
done

# The one output version this reader was written against. A document declaring
# another is refused rather than read, because the shape below is what the fields
# are named in, and a reader guessing at a shape it has not seen reports clean on
# anything it fails to understand.
readonly READS_OUTPUT_VERSION=1

refuse() {
  echo "$1" >&2
  exit 1
}

# Reads one document and prints one line per vulnerable package. Exit 0 with no
# output is a clean graph; exit 1 is either a finding or an unreadable answer, and
# the two say which they are.
read_the_findings() {
  local document="$1"

  [ -s "${document}" ] || refuse "The vulnerability listing produced no output, so this run read nothing. An empty answer is refused rather than reported as a clean graph."

  jq -e . "${document}" > /dev/null 2>&1 \
    || refuse "The vulnerability listing is not readable as JSON, so this run read nothing. An unreadable answer is refused rather than reported as a clean graph."

  local version
  version="$(jq -r '.version // "none"' "${document}")"
  [ "${version}" = "${READS_OUTPUT_VERSION}" ] \
    || refuse "The listing declares output version ${version} and this reader was written against ${READS_OUTPUT_VERSION}. A shape this reader has not seen is refused rather than read, because every field it looks for would be absent and absent reads as clean."

  local projects
  projects="$(jq -r '.projects | length' "${document}")"
  [ "${projects}" -gt 0 ] \
    || refuse "The listing names no project. A run that scanned nothing is refused rather than reported as having found nothing."

  # Both buckets are read. A transitive package is the case this check is most
  # for: it is in nobody's project file, so nothing but the graph reports it.
  jq -r '
    .projects[] as $p
    | ($p.frameworks // [])[] as $f
    | (($f.topLevelPackages // []) + ($f.transitivePackages // []))[]
    | select((.vulnerabilities // []) | length > 0)
    | . as $package
    | .vulnerabilities[]
    | "  " + ($p.path | split("/") | last | split("\\") | last)
      + "  " + $f.framework
      + "  " + $package.id
      + " " + ($package.resolvedVersion // "?")
      + "  " + (.severity // "?")
      + "  " + (.advisoryurl // "?")
  ' "${document}"
}

# The block a reader meets when the graph carries something, written as one
# function so the probes below can assert what it SAYS rather than only what the
# reading found.
#
# The packages are printed before the line is read, and the line is read in a
# condition rather than in the block's flow, because the two failures are not
# equal. A graph with an advisory in it is the thing this job exists to report; a
# manifest this run could not read a line out of is a second, smaller fault. A
# repair that let the second swallow the first would turn a red job into a red job
# reporting nothing, on exactly the graph the job is for, and that is the near
# miss the probes below are written against.
report_the_findings() {
  local findings="$1" script="$2"
  local reading

  echo "Packages in this graph carrying a published advisory, transitive ones included:"
  echo
  echo "  project  framework  package version  severity  advisory"
  echo "${findings}"
  echo

  if reading="$(bash "${script}" --just-the-reading 2>&1)"; then
    echo "  The server line this graph was resolved for:"
    echo
    echo "${reading}"
    echo
    echo "Each line above names the package, the advisory and the project whose graph resolved it, and the reading beside them names the server line that graph belongs to, so this log is enough to act on without re-running anything."
  else
    echo "  Which server line this graph was resolved for was NOT read, and the packages above stand without it:"
    echo
    echo "${reading}" | sed 's/^/    /'
    echo
    echo "Each line above names the package, the advisory and the project whose graph resolved it. The server line is missing from this report and the packages are not, which is the right way round: act on them and repair the reading separately."
  fi

  echo
  echo "The restore audit in Directory.Build.props refuses the same graph on every route that builds this solution; what this adds is the whole set rather than the first entry a restore trips over."
}

prove_it_bites() {
  local work
  work="$(mktemp -d)"
  local failures=0

  # The shape the command produces when one transitive package carries an
  # advisory. The fields are the ones the reader looks for and nothing else.
  cat > "${work}/one-finding.json" <<'EOF'
{
  "version": 1,
  "parameters": "--vulnerable --include-transitive",
  "projects": [
    {
      "path": "/repo/Jellyfin.Plugin.Watchlist/Jellyfin.Plugin.Watchlist.csproj",
      "frameworks": [
        {
          "framework": "net9.0",
          "topLevelPackages": [],
          "transitivePackages": [
            {
              "id": "System.Text.Json",
              "resolvedVersion": "8.0.4",
              "vulnerabilities": [
                {
                  "severity": "High",
                  "advisoryurl": "https://github.com/advisories/GHSA-8g4q-xg66-9fp4"
                }
              ]
            }
          ]
        }
      ]
    }
  ]
}
EOF

  # The one-change neighbour, and the reason this is the near miss worth having:
  # a reader that looked for the WORD vulnerabilities rather than for a non-empty
  # list would refuse this document too, and every clean graph with it.
  jq '.projects[0].frameworks[0].transitivePackages[0].vulnerabilities = []' \
    "${work}/one-finding.json" > "${work}/no-finding.json"

  # A clean graph as the command actually prints one: projects with no frameworks
  # key at all.
  cat > "${work}/clean.json" <<'EOF'
{
  "version": 1,
  "parameters": "--vulnerable --include-transitive",
  "projects": [
    { "path": "/repo/Jellyfin.Plugin.Watchlist/Jellyfin.Plugin.Watchlist.csproj" },
    { "path": "/repo/Jellyfin.Plugin.Watchlist.Tests/Jellyfin.Plugin.Watchlist.Tests.csproj" }
  ]
}
EOF

  # The output version moved, which is the drift that would otherwise turn this
  # check off without anybody editing it.
  jq '.version = 2' "${work}/clean.json" > "${work}/newer-shape.json"

  # The command failed and left nothing, which a reader that only counted
  # findings would report as a clean graph.
  : > "${work}/empty.json"

  # A run that scanned nothing, which is what a bad project argument leaves.
  jq '.projects = []' "${work}/clean.json" > "${work}/no-projects.json"

  must_refuse() {
    local what="$1" doc="$2"
    if ( read_the_findings "${doc}" ) > "${work}/out" 2> "${work}/err"; then
      if [ -s "${work}/out" ]; then
        # A finding is a refusal too: the caller below fails on a non-empty list.
        printf 'reported, as it must: %s\n' "${what}"
        sed 's/^/  /' "${work}/out"
        return
      fi
      echo "NOT REFUSED AND NOTHING REPORTED: ${what}" >&2
      failures=$((failures + 1))
      return
    fi
    printf 'refused, as it must: %s\n' "${what}"
    sed 's/^/    /' "${work}/err"
  }

  must_be_clean() {
    local what="$1" doc="$2"
    if ( read_the_findings "${doc}" ) > "${work}/out" 2> "${work}/err"; then
      if [ -s "${work}/out" ]; then
        echo "REPORTED A FINDING ON A CLEAN DOCUMENT: ${what}" >&2
        cat "${work}/out" >&2
        failures=$((failures + 1))
        return
      fi
      printf 'clean, as it must be: %s\n' "${what}"
      return
    fi
    echo "REFUSED A DOCUMENT IT MUST READ: ${what}. It said:" >&2
    cat "${work}/err" >&2
    failures=$((failures + 1))
  }

  # Asserts that the block carries two things at once. Both are asserted in one
  # probe on purpose: a block that named the line and dropped the packages would
  # satisfy a probe watching only for the line, and it is the worse of the two
  # failures.
  must_carry_both() {
    local what="$1" findings="$2" script="$3" first="$4" second="$5"
    local block
    # A block that ends itself rather than printing is one of the failures being
    # probed for, so its status is taken rather than allowed to end the run: the
    # empty block below is what the assertion then reports.
    block="$(report_the_findings "${findings}" "${script}")" || true
    if printf '%s\n' "${block}" | grep -qE "${first}" \
      && printf '%s\n' "${block}" | grep -qE "${second}"; then
      printf 'carried the packages and said what it knew of the line, as it must: %s\n' "${what}"
      return
    fi
    echo "THE BLOCK DID NOT CARRY BOTH: ${what}. It was missing one of /${first}/ and /${second}/, and it said:" >&2
    printf '%s\n' "${block}" >&2
    failures=$((failures + 1))
  }

  must_refuse "a graph carrying one vulnerable transitive package" "${work}/one-finding.json"
  must_be_clean "the same graph with that advisory list emptied" "${work}/no-finding.json"
  must_be_clean "a clean graph as the command prints one" "${work}/clean.json"
  must_refuse "a listing declaring an output version this reader was not written against" "${work}/newer-shape.json"
  must_refuse "a listing that produced no output at all" "${work}/empty.json"
  must_refuse "a listing that scanned no project" "${work}/no-projects.json"

  # The two probes below judge the BLOCK rather than the reading. The pair a line
  # is derived from is a fixture rather than this tree's own files, so a probe
  # that went green here proves the block and not the state of the repository on
  # the day it ran.
  cat > "${work}/lines.yaml" <<'EOF'
version: "0.1.0.0"
targetAbi: "10.11.0.0"
framework: "net9.0"
EOF

  cat > "${work}/lines.csproj" <<'EOF'
<Project Sdk="Microsoft.NET.Sdk">
  <ItemGroup>
    <PackageReference Include="Jellyfin.Controller" Version="10.11.0" />
  </ItemGroup>
</Project>
EOF

  cat > "${work}/reading-holds.sh" <<EOF
#!/usr/bin/env bash
exec bash .github/scripts/say-which-server-lines-this-run-covers.sh \\
  --just-the-reading --manifest "${work}/lines.yaml" --project "${work}/lines.csproj"
EOF

  # The reading refused, which is what an emptied targetAbi leaves. Spelled as a
  # refusal rather than as a missing file, because a missing file is the case
  # somebody notices and a refusal is the one that arrives on a green-looking
  # tree.
  cat > "${work}/reading-refuses.sh" <<'EOF'
#!/usr/bin/env bash
echo "build.yaml declares no targetAbi. A run that cannot name the line it covered is refused rather than reported as covering one." >&2
exit 1
EOF

  local findings
  findings="$(read_the_findings "${work}/one-finding.json")" || true

  must_carry_both \
    "a finding beside a reading that holds" \
    "${findings}" "${work}/reading-holds.sh" \
    'System\.Text\.Json' 'declares targetAbi 10\.11\.0\.0'

  must_carry_both \
    "a finding beside a reading that refuses" \
    "${findings}" "${work}/reading-refuses.sh" \
    'System\.Text\.Json' 'was NOT read'

  rm -rf "${work}"

  if [ "${failures}" -ne 0 ]; then
    echo "${failures} of the probes above did not do what this script claims it does. The graph below is not read." >&2
    return 1
  fi

  echo "Every probe did what it must. The graph below is read by the same function."
}

if [ "${prove}" = "yes" ]; then
  prove_it_bites
  exit 0
fi

work=""
if [ -z "${document}" ]; then
  work="$(mktemp -d)"
  document="${work}/vulnerable.json"

  # The audit is off for this restore alone, so the listing is reached rather
  # than pre-empted by the refusal it would otherwise raise. Every other route
  # that builds this solution restores with it on.
  #
  # --locked-mode is named here even though RestoreLockedMode in
  # Directory.Build.props already turns it on for every project in this
  # solution. It is not redundant and it is not to be deleted as such: the
  # graph this listing reports is only the graph the lock files pin if the
  # restore that produced it refused to resolve anything else, and a reader
  # of this one line cannot see the property that makes that true. Naming
  # the flag makes the command say what the build already enforces, and it
  # is what the Scorecard pinning alert on this line asked for.
  DOTNET_CLI_UI_LANGUAGE=en dotnet restore --locked-mode -p:NuGetAudit=false

  DOTNET_CLI_UI_LANGUAGE=en dotnet list package \
    --vulnerable --include-transitive --no-restore \
    --format json --output-version 1 > "${document}"
fi

findings="$(read_the_findings "${document}")"
[ -n "${work}" ] && rm -rf "${work}"

if [ -n "${findings}" ]; then
  report_the_findings "${findings}" "${server_lines_script}"
  exit 1
fi

echo "No package in this graph carries a published advisory, transitive ones included."
echo
echo "This is a reading of the one package set this tree carries. A server line with no package set here has no graph to resolve and was not scanned, and this job says so rather than covering it: which line was read is printed by the step beside this one, and #56 is where a refusal for a declared line that went unscanned is still owed."
