#!/usr/bin/env bash
#
# Says, in the run's own output, which server line this run built and tested.
#
# WHY A RUN HAS TO SAY THIS. This plugin claims two supported server lines and the
# tree carries a package set for one of them, so every build-and-test run here
# covers half of what the repository says it supports. Nothing in a green tick
# says which half. A reader who meets four green checks has been told that what
# ran passed, and has been told nothing about the line that never ran, which is
# the reading a run that covered less than the whole set must not be open to.
#
# WHAT IT REFUSES. It refuses to print a reading it does not have: a manifest with
# no declared ABI, a declared ABI that names no line, and a project referencing no
# server package all end the step rather than leaving a run that says nothing
# about its line and passes anyway. `--prove-it-bites` runs those three refusals
# against fixtures and their one-change neighbours before the reading that
# matters, so a step that had stopped refusing would not reach the real reading.
#
# WHAT IT DOES NOT JUDGE. Whether the declared ABI and the package set name the
# SAME line is `ServerLineTests` in the suite, and this script does not compare
# them. It prints every line the two name and lets the suite refuse a
# disagreement. A second comparison here would be one rule written twice, and the
# copy is the one that goes stale.
#
# THE MEANS. Bash inside a workflow step, which these workflows already are. The
# reading has to happen in the run whose output is being disclosed, so it cannot
# live in the .NET suite, which runs against a checkout and never against a run.
# It adds no toolchain: every runner this repository uses already has bash, and
# the fixtures below need nothing else.

set -euo pipefail

manifest="build.yaml"
project="Jellyfin.Plugin.Watchlist/Jellyfin.Plugin.Watchlist.csproj"
prove="no"

while [ "$#" -gt 0 ]; do
  case "$1" in
    --manifest) manifest="$2"; shift 2 ;;
    --project) project="$2"; shift 2 ;;
    --prove-it-bites) prove="yes"; shift ;;
    *)
      echo "Unknown argument: $1" >&2
      exit 2
      ;;
  esac
done

refuse() {
  echo "$1" >&2
  exit 1
}

# The line a version names is its first two positions, which is what a server
# release is called. The build position is not part of it: 10.11.11 and 10.11.3
# are one line whatever a project pins. Which build this repository pins is a
# separate question and a stricter one - the pin is the line's first build, so the
# assembly binds what the manifest promises - and it is answered beside the
# reference in Jellyfin.Plugin.Watchlist.csproj rather than here. A prerelease
# suffix belongs to the package rather than to the line and is dropped before the
# positions are read.
line_of() {
  local version="$1"
  local numeric="${version%%-*}"
  numeric="${numeric%%+*}"

  local major="${numeric%%.*}"
  local rest="${numeric#*.}"
  local minor="${rest%%.*}"

  # No dot at all leaves major and minor holding the same text, and both of them
  # numeric, so the digit tests below would pass a version that names no line.
  case "${numeric}" in *.*) ;; *) return 1 ;; esac
  case "${major}" in ''|*[!0-9]*) return 1 ;; esac
  case "${minor}" in ''|*[!0-9]*) return 1 ;; esac

  printf '%s.%s' "${major}" "${minor}"
}

read_the_lines() {
  local manifest="$1"
  local project="$2"

  [ -f "${manifest}" ] || refuse "There is no ${manifest} to read a declared ABI out of, so this run cannot say which server line it covered."
  [ -f "${project}" ] || refuse "There is no ${project} to read a package set out of, so this run cannot say which server line it covered."

  local abi
  abi="$(sed -n 's/^targetAbi:[[:space:]]*"\{0,1\}\([^"#]*\)"\{0,1\}[[:space:]]*$/\1/p' "${manifest}" | head -n 1)"
  abi="${abi%"${abi##*[![:space:]]}"}"

  [ -n "${abi}" ] || refuse "${manifest} declares no targetAbi. A run that cannot name the line it covered is refused rather than reported as covering one."

  local abi_line
  abi_line="$(line_of "${abi}")" || refuse "The targetAbi ${abi} in ${manifest} names no server line. Two leading numeric positions are what a line is, so this run cannot say which one it covered."

  # The references are read off the whole file rather than line by line, because
  # this tree writes some of them with the closing bracket on a later line and a
  # line-by-line read would report those as absent.
  local references
  references="$(tr '\n' ' ' < "${project}" \
    | grep -oE '<PackageReference[[:space:]]+Include="Jellyfin\.[^"]+"[[:space:]]+Version="[^"]+"' || true)"

  [ -n "${references}" ] || refuse "${project} references no Jellyfin server package, so there is no line this run was built against. A reference dropped by a bad merge is how that happens, and it is refused here rather than read as agreement."

  printf '  %-8s %s declares targetAbi %s\n' "${abi_line}" "${manifest}" "${abi}"

  local reference package version package_line
  while IFS= read -r reference; do
    [ -n "${reference}" ] || continue
    package="${reference#*Include=\"}"
    package="${package%%\"*}"
    version="${reference##*Version=\"}"
    version="${version%%\"*}"
    package_line="$(line_of "${version}")" || refuse "The version ${version} of ${package} in ${project} names no server line, so this run cannot say which line it was built against."
    printf '  %-8s %s %s\n' "${package_line}" "${package}" "${version}"
  done <<EOF
${references}
EOF
}

# Three fixtures, each a one-character-class mistake somebody makes while editing
# the pair, and each with the neighbour that has it corrected. The refusal and the
# pass are both asserted, because a reader who watches only the refusals cannot
# tell a script that refuses the right thing from one that refuses everything.
prove_it_bites() {
  local work
  work="$(mktemp -d)"

  local good_manifest="${work}/good.yaml"
  local good_project="${work}/good.csproj"

  cat > "${good_manifest}" <<'EOF'
version: "0.1.0.0"
targetAbi: "10.11.0.0"
framework: "net9.0"
EOF

  cat > "${good_project}" <<'EOF'
<Project Sdk="Microsoft.NET.Sdk">
  <ItemGroup>
    <PackageReference Include="Jellyfin.Controller" Version="10.11.11" >
      <ExcludeAssets>runtime</ExcludeAssets>
    </PackageReference>
  </ItemGroup>
</Project>
EOF

  local failures=0

  # The reading is run in a subshell on purpose. A refusal ends the process it is
  # in, and a probe that called it in this one would end the run at its first
  # successful refusal and report that as the script failing.
  must_refuse() {
    local what="$1" m="$2" p="$3"
    if ( read_the_lines "${m}" "${p}" ) > "${work}/out" 2> "${work}/err"; then
      echo "NOT REFUSED: ${what}. The reading returned:" >&2
      cat "${work}/out" >&2
      failures=$((failures + 1))
      return
    fi
    printf 'refused, as it must: %s\n' "${what}"
    sed 's/^/    /' "${work}/err"
  }

  must_pass() {
    local what="$1" m="$2" p="$3"
    if ( read_the_lines "${m}" "${p}" ) > "${work}/out" 2> "${work}/err"; then
      printf 'passed, as it must: %s\n' "${what}"
      return
    fi
    echo "REFUSED A NEIGHBOUR IT MUST PASS: ${what}. It said:" >&2
    cat "${work}/err" >&2
    failures=$((failures + 1))
  }

  must_pass "the pair as this tree writes it" "${good_manifest}" "${good_project}"

  # The value emptied rather than the key removed, which is what an edit leaves
  # behind when somebody clears the field meaning to retype it.
  sed 's/^targetAbi: .*/targetAbi: ""/' "${good_manifest}" > "${work}/empty-abi.yaml"
  must_refuse "a manifest whose targetAbi was emptied" "${work}/empty-abi.yaml" "${good_project}"
  must_pass "the same manifest with the value back" "${good_manifest}" "${good_project}"

  # One position where two are needed. 10 is a server major and names no line, and
  # it is what a version typed short looks like.
  sed 's/^targetAbi: .*/targetAbi: "10"/' "${good_manifest}" > "${work}/short-abi.yaml"
  must_refuse "a targetAbi of 10, which names no line" "${work}/short-abi.yaml" "${good_project}"

  # The reference gone, which is how a bad merge turns the comparison off.
  grep -v 'PackageReference' "${good_project}" > "${work}/no-package.csproj"
  must_refuse "a project referencing no server package" "${good_manifest}" "${work}/no-package.csproj"
  must_pass "the same project with the reference back" "${good_manifest}" "${good_project}"

  rm -rf "${work}"

  if [ "${failures}" -ne 0 ]; then
    echo "${failures} of the probes above did not do what this script claims it does. The reading below is not taken." >&2
    return 1
  fi

  echo "Every probe did what it must. The reading below is taken by the same function."
}

if [ "${prove}" = "yes" ]; then
  prove_it_bites
  exit 0
fi

covered="$(read_the_lines "${manifest}" "${project}")"

# The lines this run did NOT cover are not enumerated here. The supported set is
# declared in README.md and a copy of it in this script would be the copy that
# goes stale, so what this says is that the set is wider than the reading above
# and where to read it. That sentence stays true on the day a second package set
# arrives, and the reading above grows a row on its own.
disclosure="This run builds and tests the one package set this tree carries, so a green run
here is a reading about the line or lines named above and about no other. Every
other server line this plugin says it supports was neither built nor tested by
this run, and is absent from it rather than reported. README.md, under \"Which
servers it supports\", is where the supported set and the reason only one of them
is built today are written. Issue #4 is where a second package set arrives, and
issue #54 is where the second leg that would build it does."

printf 'Server lines this run covers\n\n%s\n\n%s\n' "${covered}" "${disclosure}"

if [ -n "${GITHUB_STEP_SUMMARY:-}" ]; then
  {
    printf '### Server lines this run covers\n\n'
    printf '```\n%s\n```\n\n' "${covered}"
    printf '%s\n' "${disclosure}"
  } >> "${GITHUB_STEP_SUMMARY}"
fi
