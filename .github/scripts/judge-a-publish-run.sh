#!/usr/bin/env bash
#
# Says whether a finished publish run left a release somebody can install, and
# names every way in which it did not.
#
# THE CASE THIS IS FOR. A release pipeline that fails loudly is fine. The one
# worth planning for reports success and ships nothing, and it has happened to a
# plugin release pipeline built the same way. `.github/workflows/publish.yaml`
# already refuses several shapes that end in a half-published release - a tag
# whose version disagrees with the manifest, a release that already exists, an
# upload matching no file, an asset count that is not one - and every one of those
# steps judges its own inputs. None of them judges the RELEASE, which is the thing
# a user receives, and a run whose last steps did not happen ends green with a
# release that is absent, still a draft, or missing half its assets.
#
# WHAT IT ASSERTS, AND WHERE THE LIST COMES FROM. The asset set is read off the
# publish route rather than invented here: that route writes exactly one archive,
# exactly one packaging metadata document, exactly one component inventory,
# exactly one attestation bundle, exactly one `.md5`, and a `.sha256` beside the
# archive, the bundle and the inventory. A release carrying fewer than that is one
# the route did not finish. The `.md5` is the one with a count rather than a floor,
# because a Jellyfin catalogue reads the plugin checksum out of it by filename and
# cannot choose between two.
#
# WHAT IT REFUSES. A report it cannot read. A run with no conclusion, a document
# that is not JSON, and a release that says neither found nor missing all end this
# with exit 2 rather than with a verdict, because a judge that reads an
# unintelligible answer as a healthy release is the same silence this exists to
# break.
#
# EXIT CODES. 0 the release is complete, 1 it is not and the reasons are on
# stdout, 2 the report could not be read and the reason is on stderr.

set -euo pipefail

report=""
prove="no"

while [ "$#" -gt 0 ]; do
  case "$1" in
    --report) report="$2"; shift 2 ;;
    --prove-it-bites) prove="yes"; shift ;;
    *)
      echo "Unknown argument: $1" >&2
      exit 2
      ;;
  esac
done

unreadable() {
  echo "$1" >&2
  exit 2
}

# One entry per asset shape the publish route produces. `min` is a floor and `max`
# is empty where there is none, so a shape with an exact count carries both.
judge_the_run() {
  local report="$1"

  [ -s "${report}" ] || unreadable "The publish report is empty, so nothing was judged. An empty report is refused rather than read as a complete release."
  jq -e . "${report}" > /dev/null 2>&1 \
    || unreadable "The publish report is not readable as JSON, so nothing was judged."

  local conclusion
  conclusion="$(jq -r '.run.conclusion // ""' "${report}")"
  [ -n "${conclusion}" ] \
    || unreadable "The publish report names no conclusion for the run. A run whose outcome is unknown is refused rather than read as a success."

  local found
  found="$(jq -r 'if (.release | type) != "object" then "" elif (.release.found | type) != "boolean" then "" else (.release.found | tostring) end' "${report}")"
  [ -n "${found}" ] \
    || unreadable "The publish report does not say whether a release exists for the tag. A missing answer is refused rather than read as one."

  local findings=()

  if [ "${conclusion}" != "success" ]; then
    findings+=("The run concluded ${conclusion}. A publish that did not succeed may still have created the release before it stopped, so what it left behind is read below rather than assumed empty.")
  fi

  if [ "${found}" != "true" ]; then
    findings+=("There is no release for the tag this run published. The run reported ${conclusion} and a user has nothing to install.")
  else
    if [ "$(jq -r '.release.draft // false' "${report}")" = "true" ]; then
      findings+=("The release exists and is a draft. A draft is invisible to every server asking for a plugin, so this is a run that reported ${conclusion} and shipped nothing.")
    fi

    local assets count
    assets="$(jq -r '[.release.assets[]? | .] | length' "${report}")"
    if [ "${assets}" -eq 0 ]; then
      findings+=("The release carries no assets at all. The release was created and nothing was attached to it.")
    fi

    count_of() {
      jq -r --arg suffix "$1" '[.release.assets[]? | select(endswith($suffix))] | length' "${report}"
    }

    count="$(count_of ".zip")"
    [ "${count}" -ge 1 ] \
      || findings+=("The release carries no plugin archive. Every other asset describes the archive, so a release without one is a release of documents about a file nobody received.")

    count="$(count_of ".md5")"
    [ "${count}" -eq 1 ] \
      || findings+=("The release carries ${count} assets ending .md5 and a release carries exactly one. A catalogue reads the plugin checksum out of it by filename and cannot choose between two, and zero means a server has nothing to verify the archive against.")

    count="$(count_of ".sha256")"
    [ "${count}" -ge 1 ] \
      || findings+=("The release carries no .sha256. The publish route writes one beside the archive, the attestation bundle and the component inventory, so none at all is a route that stopped before its checksums were attached.")

    count="$(count_of ".meta.json")"
    [ "${count}" -ge 1 ] \
      || findings+=("The release carries no packaging metadata. It travels with the release so a catalogue can be built from the package that was shipped rather than from a file read back out of the tree.")

    count="$(count_of ".cdx.json")"
    [ "${count}" -ge 1 ] \
      || findings+=("The release carries no component inventory. What is inside the archive is then answerable only from the run that built it, which expires.")

    count="$(count_of ".sigstore.json")"
    [ "${count}" -ge 1 ] \
      || findings+=("The release carries no attestation bundle. The provenance of the archive is then unverifiable by anybody who downloads it.")
  fi

  if [ "${#findings[@]}" -eq 0 ]; then
    return 0
  fi

  local finding
  for finding in "${findings[@]}"; do
    printf -- '- %s\n' "${finding}"
  done
  return 1
}

prove_it_bites() {
  local work
  work="$(mktemp -d)"
  local failures=0

  # A release as the publish route leaves one when every step ran.
  cat > "${work}/complete.json" <<'EOF'
{
  "run": { "id": 1, "conclusion": "success", "html_url": "https://example.invalid/1", "tag": "0.1.0.0-stable" },
  "release": {
    "found": true,
    "draft": false,
    "assets": [
      "jellyfin-plugin-watchlist_0.1.0.0.zip",
      "jellyfin-plugin-watchlist_0.1.0.0.md5",
      "jellyfin-plugin-watchlist_0.1.0.0.sha256",
      "jellyfin-plugin-watchlist_0.1.0.0.zip.meta.json",
      "jellyfin-plugin-watchlist_0.1.0.0.cdx.json",
      "jellyfin-plugin-watchlist_0.1.0.0.cdx.json.sha256",
      "attestation.sigstore.json",
      "attestation.sigstore.json.sha256"
    ]
  }
}
EOF

  jq '.release = { "found": false }' "${work}/complete.json" > "${work}/no-release.json"
  jq '.release.draft = true' "${work}/complete.json" > "${work}/draft.json"
  jq '.release.assets = []' "${work}/complete.json" > "${work}/no-assets.json"
  jq '.run.conclusion = "failure"' "${work}/complete.json" > "${work}/failed.json"

  # The one that matters most and looks least like a failure: a second .md5 beside
  # the first. The run is green, the release exists, the archive is there, and a
  # catalogue picks a checksum by filename.
  jq '.release.assets += ["sbom.md5"]' "${work}/complete.json" > "${work}/two-md5.json"
  # Its one-change neighbour: the same release with that one asset removed.
  jq '.release.assets -= ["sbom.md5"]' "${work}/two-md5.json" > "${work}/one-md5.json"

  # The archive missing while every document about it is attached, which is what a
  # partial upload leaves.
  jq '.release.assets -= ["jellyfin-plugin-watchlist_0.1.0.0.zip"]' "${work}/complete.json" > "${work}/no-archive.json"

  jq 'del(.run.conclusion)' "${work}/complete.json" > "${work}/no-conclusion.json"
  jq 'del(.release.found)' "${work}/complete.json" > "${work}/no-answer.json"
  : > "${work}/empty.json"

  must_alert() {
    local what="$1" doc="$2" code
    set +e
    ( judge_the_run "${doc}" ) > "${work}/out" 2> "${work}/err"
    code=$?
    set -e
    if [ "${code}" -eq 1 ]; then
      printf 'alerted, as it must: %s\n' "${what}"
      sed 's/^/    /' "${work}/out"
      return
    fi
    echo "DID NOT ALERT (exit ${code}): ${what}" >&2
    cat "${work}/out" "${work}/err" >&2
    failures=$((failures + 1))
  }

  must_be_silent() {
    local what="$1" doc="$2" code
    set +e
    ( judge_the_run "${doc}" ) > "${work}/out" 2> "${work}/err"
    code=$?
    set -e
    if [ "${code}" -eq 0 ]; then
      printf 'silent, as it must be: %s\n' "${what}"
      return
    fi
    echo "ALERTED ON A COMPLETE RELEASE (exit ${code}): ${what}" >&2
    cat "${work}/out" "${work}/err" >&2
    failures=$((failures + 1))
  }

  must_refuse() {
    local what="$1" doc="$2" code
    set +e
    ( judge_the_run "${doc}" ) > "${work}/out" 2> "${work}/err"
    code=$?
    set -e
    if [ "${code}" -eq 2 ]; then
      printf 'refused, as it must: %s\n' "${what}"
      sed 's/^/    /' "${work}/err"
      return
    fi
    echo "DID NOT REFUSE AN UNREADABLE REPORT (exit ${code}): ${what}" >&2
    failures=$((failures + 1))
  }

  must_be_silent "a green run that left a complete release" "${work}/complete.json"
  must_alert "a green run with no release for its tag" "${work}/no-release.json"
  must_alert "a green run whose release is still a draft" "${work}/draft.json"
  must_alert "a green run whose release carries no assets" "${work}/no-assets.json"
  must_alert "a green run whose release carries no archive" "${work}/no-archive.json"
  must_alert "a green run whose release carries a second .md5" "${work}/two-md5.json"
  must_be_silent "the same release with that second .md5 removed" "${work}/one-md5.json"
  must_alert "a run that concluded failure" "${work}/failed.json"
  must_refuse "a report naming no conclusion" "${work}/no-conclusion.json"
  must_refuse "a report that does not say whether a release exists" "${work}/no-answer.json"
  must_refuse "an empty report" "${work}/empty.json"

  rm -rf "${work}"

  if [ "${failures}" -ne 0 ]; then
    echo "${failures} of the probes above did not do what this script claims it does." >&2
    return 1
  fi

  echo "Every probe did what it must. A real report is judged by the same function."
}

if [ "${prove}" = "yes" ]; then
  prove_it_bites
  exit 0
fi

[ -n "${report}" ] || unreadable "No report was given. Pass --report <file>."
judge_the_run "${report}"
