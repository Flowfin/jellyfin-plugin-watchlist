#!/usr/bin/env bash
#
# Says which server the manifest's floor promises, and derives the image tag a
# run has to boot to read that promise (#324).
#
# WHAT A FLOOR IS. A server reads `targetAbi` as a floor and applies no upper
# bound, so a package declaring 10.11.0.0 is offered to every 10.11 server from
# 10.11.0 upward. Until this script existed, every server this repository booted
# was the newest patch of the line, written into a workflow by hand, and the
# other end of that promise had never been started. A package that binds the
# newest patch's assemblies passes every one of those runs and is `NotSupported`
# on the floor it names, which is what 0.1.0.0 shipped as.
#
# WHY THE TAG IS DERIVED AND NEVER WRITTEN INTO A JOB. The floor is a value in
# `build.yaml`, and a copy of it in a workflow is the copy that goes stale. The
# day somebody raises the floor - which is the ordinary way this value moves -
# a written tag keeps booting the old one and reports the new promise as read.
# The probe below asserts that a raised floor moves the image, because that is
# the mistake a hand-written tag makes and the one no reader would see.
#
# WHAT IT REFUSES. It refuses to print a reading it does not have: an absent
# manifest, a `targetAbi` that was emptied, and one that names no server all end
# the step rather than leaving a run that boots some default server and passes.
# A default would be the worst outcome available here, because it would make
# every run below a reading about a server nobody chose.
#
# WHAT IT DOES NOT JUDGE. Whether the declared ABI agrees with the package set is
# `ServerLineTests` in the suite, and whether the archive loads is the harness
# this feeds. This script decides one thing: which image tag the declared floor
# names. A second comparison here would be one rule written twice.
#
# THE MEANS. Bash inside a workflow step, which these workflows already are and
# which `say-which-server-lines-this-run-covers.sh` beside it already is. The
# value has to reach a job's container invocation in the run that boots the
# server, so it cannot live in the .NET suite, which runs against a checkout and
# never against a run. It adds no toolchain.
#
# Usage:
#
#   .github/scripts/say-which-floor-the-manifest-promises.sh
#   .github/scripts/say-which-floor-the-manifest-promises.sh --manifest build.yaml
#   .github/scripts/say-which-floor-the-manifest-promises.sh --prove-it-bites

set -euo pipefail

manifest="build.yaml"
repository="jellyfin/jellyfin"
prove="no"

while [ "$#" -gt 0 ]; do
  case "$1" in
    --manifest) manifest="$2"; shift 2 ;;
    --repository) repository="$2"; shift 2 ;;
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

# The image tag a four-position ABI names is its first three positions, because
# that is what a server release is called: a targetAbi of 10.11.0.0 is the
# 10.11.0 image. A prerelease suffix belongs to the package rather than to the
# server and is dropped before the positions are read.
#
# Three positions are required rather than defaulted. A value with two is a line
# and not a server, and there is no server tag to boot for it; guessing one would
# be this script inventing the floor it exists to read.
tag_of() {
  local version="$1"
  local numeric="${version%%-*}"
  numeric="${numeric%%+*}"

  local major minor patch rest

  case "${numeric}" in *.*) ;; *) return 1 ;; esac
  major="${numeric%%.*}"
  rest="${numeric#*.}"

  case "${rest}" in *.*) ;; *) return 1 ;; esac
  minor="${rest%%.*}"
  rest="${rest#*.}"
  patch="${rest%%.*}"

  case "${major}" in ''|*[!0-9]*) return 1 ;; esac
  case "${minor}" in ''|*[!0-9]*) return 1 ;; esac
  case "${patch}" in ''|*[!0-9]*) return 1 ;; esac

  printf '%s.%s.%s' "${major}" "${minor}" "${patch}"
}

read_the_floor() {
  local manifest="$1"

  [ -f "${manifest}" ] || refuse "There is no ${manifest} to read a declared ABI out of, so this run cannot say which server the floor promises."

  local abi
  abi="$(sed -n 's/^targetAbi:[[:space:]]*"\{0,1\}\([^"#]*\)"\{0,1\}[[:space:]]*$/\1/p' "${manifest}" | head -n 1)"
  abi="${abi%"${abi##*[![:space:]]}"}"

  [ -n "${abi}" ] || refuse "${manifest} declares no targetAbi. The floor is what this run would boot, so a run that cannot read it is refused rather than sent to a default server."

  local tag
  tag="$(tag_of "${abi}")" || refuse "The targetAbi ${abi} in ${manifest} names no server. Three leading numeric positions are what a server release is called, and there is no image tag to boot without them."

  printf '%s' "${tag}"
}

# Six probes. Two are the refusals, and the rest are the pair that matters most
# here: the floor as this tree declares it, and the same manifest with the floor
# raised. A script that returned a constant would pass the first and fail the
# second, and a constant is exactly what this replaces.
prove_it_bites() {
  local work
  work="$(mktemp -d)"

  local good="${work}/good.yaml"

  cat > "${good}" <<'FIXTURE'
version: "0.1.1.0"
targetAbi: "10.11.0.0"
framework: "net9.0"
FIXTURE

  local failures=0

  # The reading runs in a subshell on purpose. A refusal ends the process it is
  # in, and a probe that called it in this one would end the run at its first
  # successful refusal and report that as the script failing.
  must_refuse() {
    local what="$1" m="$2"
    if ( read_the_floor "${m}" ) > "${work}/out" 2> "${work}/err"; then
      echo "NOT REFUSED: ${what}. The reading returned:" >&2
      cat "${work}/out" >&2
      failures=$((failures + 1))
      return
    fi
    printf 'refused, as it must: %s\n' "${what}"
    sed 's/^/    /' "${work}/err"
  }

  must_derive() {
    local what="$1" m="$2" expected="$3" got
    if ! got="$( read_the_floor "${m}" 2> "${work}/err" )"; then
      echo "REFUSED A MANIFEST IT MUST READ: ${what}. It said:" >&2
      cat "${work}/err" >&2
      failures=$((failures + 1))
      return
    fi
    if [ "${got}" != "${expected}" ]; then
      echo "WRONG SERVER: ${what} must derive ${expected} and derived ${got}." >&2
      failures=$((failures + 1))
      return
    fi
    printf 'derived %s, as it must: %s\n' "${got}" "${what}"
  }

  must_derive "the floor this tree declares" "${good}" "10.11.0"

  # The probe that makes this a derivation rather than a constant. A floor raised
  # in build.yaml has to move the server this boots, and a hand-written tag is
  # the thing that does not.
  sed 's/^targetAbi: .*/targetAbi: "10.11.4.0"/' "${good}" > "${work}/raised.yaml"
  must_derive "the same manifest with the floor raised to 10.11.4.0" "${work}/raised.yaml" "10.11.4"

  # The value emptied rather than the key removed, which is what an edit leaves
  # behind when somebody clears the field meaning to retype it.
  sed 's/^targetAbi: .*/targetAbi: ""/' "${good}" > "${work}/empty.yaml"
  must_refuse "a manifest whose targetAbi was emptied" "${work}/empty.yaml"

  # Two positions where three are needed. 10.11 is a server LINE and names no
  # server, and it is what an ABI typed short looks like.
  sed 's/^targetAbi: .*/targetAbi: "10.11"/' "${good}" > "${work}/short.yaml"
  must_refuse "a targetAbi of 10.11, which names a line and no server" "${work}/short.yaml"

  must_refuse "a manifest that is not there" "${work}/absent.yaml"

  must_derive "the good manifest once more, after four neighbours" "${good}" "10.11.0"

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

tag="$(read_the_floor "${manifest}")"
image="${repository}:${tag}"

reading="  ${image}  is what ${manifest} promises as its floor"

# What a run at the floor does NOT cover is said here rather than left to be
# assumed. The newest patch of the line is a different server and a different
# reading, and the two fail apart: an assembly bound above the floor passes at
# the newest patch and refuses at the floor, which is how 0.1.0.0 shipped.
disclosure="A server reads targetAbi as a floor and applies no upper bound, so the promise
this package makes is every server from the one above upward. Booting it reads
the bottom of that promise and nothing else. The newest patch of the same line
is a different server and is read by the interoperability matrix, which is a
separate run for a separate reason: a package can load on one of the two and
not on the other, and that is the failure this leg exists for."

printf 'The floor this package promises\n\n%s\n\n%s\n' "${reading}" "${disclosure}"

if [ -n "${GITHUB_OUTPUT:-}" ]; then
  printf 'image=%s\n' "${image}" >> "${GITHUB_OUTPUT}"
fi

if [ -n "${GITHUB_STEP_SUMMARY:-}" ]; then
  {
    printf '### The floor this package promises\n\n'
    printf '```\n%s\n```\n\n' "${reading}"
    printf '%s\n' "${disclosure}"
  } >> "${GITHUB_STEP_SUMMARY}"
fi
