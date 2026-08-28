#!/usr/bin/env bash
#
# Derives the sibling matrix from the hub's declaration and says, per row,
# whether there is anything to install (#104).
#
# WHY A DERIVATION AND NOT A LIST. The family is enumerated in one place already,
# `Flowfin/hub`, and a list pasted into this repository would be a second place
# that goes stale the day a plugin joins or leaves it. So the rows come out of
# the hub at run time: a sibling that joins the family joins this matrix in the
# same commit, with nothing here to edit.
#
# THE CRITERION IS MECHANICAL: the hub's published catalogue carries an
# installable version for the line this run boots, or it does not. Nothing here
# judges whether a sibling is ready, interesting or worth testing.
#
# WHAT A SKIPPED ROW IS FOR. A sibling with nothing to install is SHOWN and said
# to be skipped rather than left out. An absent row hides a fact - that the
# family declares a plugin this matrix has never run against - and a reader
# counting rows would take the matrix for complete. Building such a sibling from
# source instead would test a combination no operator can ever install, because
# operators install from the catalogue.
#
# `enabled: false` IN A SOURCE NEEDS NO HANDLING HERE, and this sentence is why
# rather than an omission. The hub's own build reads that flag when it assembles
# the catalogue, so a sibling declared off carries no entry there however many
# releases it has published, and it arrives here as a row with nothing to install
# like any other. One criterion, read in one place.
#
# WHAT IT READS.
#
#   the hub's sources     One file per declared family member, carrying the
#                         account and the repository it publishes from. This is
#                         the population, and it is the only place the family's
#                         size is written.
#   the hub's catalogue   What a server installs from: one entry per plugin that
#                         has something published, with a version, the ABI it
#                         targets, its archive URL and its checksum.
#   build.yaml            This board's own `targetAbi`, so a row is judged
#                         against the line this run boots rather than against any
#                         version the catalogue happens to hold.
#
# HOW A CATALOGUE ENTRY IS TIED TO A SOURCE. By the archive URL, which carries
# the account and the repository the source declares. The catalogue's entries are
# keyed by display name - `Playback Statistics` for `stats` - and no mapping from
# one to the other exists anywhere, so a name-based match would be a table
# somebody maintains by hand. The URL prefix ends in a slash on purpose: without
# it `jellyfin-plugin-requests` matches `jellyfin-plugin-requests-anything`, and
# the probe below holds that case.
#
# AN UNREAD CATALOGUE AND AN EMPTY ONE MUST NOT LOOK THE SAME. A catalogue that
# is not a JSON array ENDS the run. Read as an empty set it would report every
# row skipped, and the job above would go green having installed nothing while
# saying so in words a reader takes for a reading of the family.
#
# WHAT PROVES IT BITES. `--prove-it-bites` runs the row judgement over fabricated
# sources and catalogues and their one-change neighbours. It reads no network and
# starts nothing.
#
# THE MEANS. Bash, curl and jq, which is what every other gate script in this
# tree is written in and what its workflows already run. A .NET test cannot be
# the means: the suite here is refused the network by the headless rule, and this
# reads two documents over HTTP.
#
# Usage:
#
#   .github/scripts/say-which-siblings-the-hub-declares.sh
#   .github/scripts/say-which-siblings-the-hub-declares.sh --fetch <directory>
#   .github/scripts/say-which-siblings-the-hub-declares.sh --prove-it-bites

set -euo pipefail

manifest="build.yaml"
hub="Flowfin/hub"
hub_ref="main"
fetch=""
prove="no"
this_repository="${GITHUB_REPOSITORY:-}"
this_repository="${this_repository##*/}"

usage() {
  echo "Usage: say-which-siblings-the-hub-declares.sh [--fetch <directory>]" >&2
  echo "       say-which-siblings-the-hub-declares.sh --prove-it-bites" >&2
  exit 2
}

while [ "$#" -gt 0 ]; do
  case "$1" in
    --manifest) manifest="$2"; shift 2 ;;
    --hub) hub="$2"; shift 2 ;;
    --ref) hub_ref="$2"; shift 2 ;;
    --this-repository) this_repository="$2"; shift 2 ;;
    --fetch) fetch="$2"; shift 2 ;;
    --prove-it-bites) prove="yes"; shift ;;
    *)
      echo "Unknown argument: $1" >&2
      usage
      ;;
  esac
done

refuse() {
  echo "$1" >&2
  exit 1
}

# The hub is public and these reads need no credential. A token is used where one
# is in the environment, for one reason: the unauthenticated rate limit is per
# address, a runner's address is shared, and a run refused for somebody else's
# traffic reads as the hub being unreachable. Taken without one on 2026-08-28 the
# twelfth source answered 403. Nothing here is written and no private document is
# reached.
authorization=()
if [ -n "${GITHUB_TOKEN:-}" ]; then
  authorization=(--header "Authorization: Bearer ${GITHUB_TOKEN}")
fi

# Fetches one path out of the hub, as raw content.
hub_curl() {
  curl --silent --show-error --fail --location \
    --header 'Accept: application/vnd.github.raw' \
    "${authorization[@]}" \
    "https://api.github.com/repos/${hub}/contents/$1?ref=${hub_ref}"
}

# Reads one scalar written at column zero of the manifest. It refuses an absent
# key rather than falling back to a default, because a default ABI would judge
# every row against a line this run does not boot.
scalar_of() {
  local key="$1" value

  value="$(sed -n "s/^${key}: *//p" "${manifest}" | head -n 1 | tr -d '\r')"
  value="${value%\"}"
  value="${value#\"}"

  [ -n "${value}" ] \
    || refuse "${manifest} declares no ${key} at column zero. This derivation refuses rather than judging a row against a default."

  printf '%s' "${value}"
}

# The whole judgement, over two documents and two scalars. It is one jq program
# so the probes below can run it over fabricated inputs with no network, and so
# a row's state is decided in one place rather than in one for the run and one
# for the proof.
#
# Each row is a tab-separated line: state, slug, repository, name, version,
# checksum, url. A skipped row carries the state and the two fields its source
# gave it, and dashes for what a catalogue entry would have supplied.
rows_from() {
  local sources="$1" catalogue="$2" abi="$3" self="$4"

  jq -r -n \
    --argjson sources "${sources}" \
    --argjson catalogue "${catalogue}" \
    --arg abi "${abi}" \
    --arg self "${self}" '
    def numeric: [splits("\\.") | tonumber];

    $sources[]
    | select(.repository != $self)
    | . as $s
    | ( $catalogue
        | map(. as $entry
              | .versions[]
              | select(.targetAbi == $abi)
              | select(.sourceUrl
                       | startswith("https://github.com/" + $s.account + "/" + $s.repository + "/"))
              | {name: $entry.name, version: .version, checksum: .checksum, url: .sourceUrl})
        | sort_by(.version | numeric)
        | last ) as $pick
    | if $pick == null
      then ["skipped", $s.slug, $s.repository, "-", "-", "-", "-"]
      else ["installs", $s.slug, $s.repository, $pick.name, $pick.version, $pick.checksum, $pick.url]
      end
    | @tsv
  ' | tr -d '\r'
}

# Reads a document out of the hub. It refuses a body that is not a JSON array
# rather than handing an empty set to the judgement above, for the reason at the
# top of this file.
hub_document() {
  local path="$1" body

  body="$(hub_curl "${path}" 2>&1)" \
    || refuse "The hub did not answer for ${path}: ${body}"

  if ! printf '%s' "${body}" | jq -e 'if type == "array" then . else empty end' >/dev/null 2>&1; then
    refuse "${path} in ${hub} is not a JSON array. A run that read it as an empty set would report every sibling skipped and install nothing, which is the state this refusal exists to separate from a family that has published nothing."
  fi

  printf '%s' "${body}"
}

if [ "${prove}" = "yes" ]; then
  # Two declared siblings and this board. The catalogue below is shaped like what
  # the hub publishes: a display name matching no slug, and versions in whatever
  # order the hub wrote them.
  sources='[{"account":"Flowfin","repository":"jellyfin-plugin-requests","slug":"requests","enabled":true},
            {"account":"Flowfin","repository":"jellyfin-plugin-stats","slug":"stats","enabled":true},
            {"account":"Flowfin","repository":"jellyfin-plugin-watchlist","slug":"watchlist","enabled":true}]'
  base='https://github.com/Flowfin/jellyfin-plugin-requests/releases/download'
  # Ascending, and across a decimal place: a judgement taking the first entry, the
  # last entry, or the lexically greatest string picks 0.9.0.0 out of this.
  catalogue='[{"name":"Requests","versions":[
      {"version":"0.9.0.0","targetAbi":"10.11.0.0","checksum":"aaa","sourceUrl":"'"${base}"'/0.9.0.0-stable/requests_0.9.0.0.zip"},
      {"version":"0.10.0.0","targetAbi":"10.11.0.0","checksum":"bbb","sourceUrl":"'"${base}"'/0.10.0.0-stable/requests_0.10.0.0.zip"}]}]'

  rows="$(rows_from "${sources}" "${catalogue}" "10.11.0.0" "jellyfin-plugin-watchlist")"

  [ "$(printf '%s\n' "${rows}" | wc -l)" -eq 2 ] \
    || refuse "The derivation returned $(printf '%s\n' "${rows}" | wc -l) rows where three sources are declared and this board is one of them, so the matrix would not be the family minus itself."

  printf '%s\n' "${rows}" | grep -q "^installs	requests	jellyfin-plugin-requests	Requests	0.10.0.0	bbb	" \
    || refuse "The derivation did not read the sibling with a catalogue entry for this line as installable at its newest version, so the run that matters would install nothing and pass."

  printf '%s\n' "${rows}" | grep -q "^skipped	stats	jellyfin-plugin-stats	" \
    || refuse "The derivation did not read a sibling with no catalogue entry as skipped, so a sibling with nothing to install would either be installed or dropped from the matrix."

  if printf '%s\n' "${rows}" | grep -q "watchlist"; then
    refuse "The derivation returned a row for this board, so the matrix would install this plugin twice and read the second copy as a sibling."
  fi

  # The one-change neighbour of the installable row: the same catalogue, asked
  # for a line this board does not build against.
  if rows_from "${sources}" "${catalogue}" "12.0.0.0" "jellyfin-plugin-watchlist" | grep -q "^installs"; then
    refuse "The derivation read a catalogue version as installable for a line it does not target, so this matrix would install an archive the booted server cannot load and report the refusal as a collision."
  fi

  # And the near miss on the tie between a source and a catalogue entry: a
  # repository whose name a sibling's is a prefix of.
  neighbour="${catalogue//jellyfin-plugin-requests\/releases/jellyfin-plugin-requests-classic\/releases}"
  if rows_from "${sources}" "${neighbour}" "10.11.0.0" "jellyfin-plugin-watchlist" | grep -q "^installs"; then
    refuse "The derivation matched a catalogue entry published from a repository whose name merely begins with a sibling's, so a row would install somebody else's archive."
  fi

  # An empty catalogue is a legal state and every row is skipped in it. An
  # unreadable one is not, and it is refused in hub_document rather than here.
  [ "$(rows_from "${sources}" '[]' "10.11.0.0" "jellyfin-plugin-watchlist" | grep -c '^skipped')" -eq 2 ] \
    || refuse "The derivation did not report every row as skipped against an empty catalogue, so a family that has published nothing would read as something else."

  echo "The derivation returns the family minus this board, reads a catalogue entry for this line as installable at its newest version, and reads a sibling with no entry as skipped."
  echo "It refuses a version targeting another line and a catalogue entry from a repository whose name merely begins with a sibling's, and reports every row skipped against an empty catalogue."
  exit 0
fi

for tool in curl jq unzip md5sum; do
  command -v "${tool}" >/dev/null 2>&1 \
    || refuse "This derivation needs ${tool} and it is not on the path. It installs nothing."
done

[ -n "${this_repository}" ] \
  || refuse "This repository's name is neither in GITHUB_REPOSITORY nor given with --this-repository. Without it the matrix cannot exclude this board and would install this plugin as its own sibling."

abi="$(scalar_of targetAbi)"
listing="$(hub_document "sources")"
catalogue="$(hub_document "docs/manifest.json")"

# The contents listing gives file names; each file holds one declaration.
#
# The carriage returns are stripped here and after the row judgement above,
# because a jq built for Windows writes its output in text mode and this script
# is meant to run the same way on a workstation as it does on a runner. Left in,
# a file name carrying one is rejected by curl as a malformed URL and a row's
# last field is an archive URL nothing can download - two failures that read as
# the hub being unreachable rather than as this machine's line endings.
declarations="$(printf '%s' "${listing}" | jq -r '.[] | select(.name | endswith(".json")) | .name' | tr -d '\r')"
[ -n "${declarations}" ] \
  || refuse "${hub} declares no sources, so this matrix has no population and a green run would say nothing about the family."

collected="[]"
for file in ${declarations}; do
  declaration="$(hub_curl "sources/${file}" 2>&1)" \
    || refuse "The hub did not answer for sources/${file}: ${declaration}"

  if ! printf '%s' "${declaration}" | jq -e 'has("account") and has("repository") and has("slug")' >/dev/null 2>&1; then
    refuse "sources/${file} in ${hub} declares no account, repository or slug, so the row it produces could be tied to no catalogue entry."
  fi

  collected="$(jq -c -n --argjson have "${collected}" --argjson one "${declaration}" '$have + [$one]')"
done

if ! printf '%s' "${collected}" | jq -e --arg self "${this_repository}" 'map(.repository) | index($self)' >/dev/null 2>&1; then
  refuse "${hub} declares no source publishing from ${this_repository}, so excluding this board would exclude nothing and the matrix would install this plugin as its own sibling."
fi

declared="$(printf '%s' "${collected}" | jq 'length')"

echo "Hub:      ${hub}@${hub_ref}"
echo "Line:     targetAbi ${abi}, read from ${manifest}"
echo "Declared: ${declared} source(s), of which this board is one"
echo

rows="$(rows_from "${collected}" "${catalogue}" "${abi}" "${this_repository}")"
[ -n "${rows}" ] \
  || refuse "${hub} declares this board and nothing else, so there is no sibling to run against and a green run here would say only what the alone job already says."

printf '%-9s %-20s %s\n' "STATE" "SLUG" "WHAT THIS ROW INSTALLS"
while IFS=$'\t' read -r state slug repository name version checksum url; do
  if [ "${state}" = "installs" ]; then
    printf '%-9s %-20s %s %s\n' "${state}" "${slug}" "${name}" "${version}"
  else
    printf '%-9s %-20s %s\n' "${state}" "${slug}" "no installable version in the hub for this line, row skipped"
  fi
done <<< "${rows}"

installs="$(printf '%s\n' "${rows}" | grep -c '^installs' || true)"
skipped="$(printf '%s\n' "${rows}" | grep -c '^skipped' || true)"
echo
echo "${installs} sibling(s) install, ${skipped} skipped."

[ -n "${fetch}" ] || exit 0

mkdir -p "${fetch}"
echo

while IFS=$'\t' read -r state slug repository name version checksum url; do
  [ "${state}" = "installs" ] || continue

  archive="${fetch}/${slug}.zip"
  curl --silent --show-error --fail --location --output "${archive}" "${url}" \
    || refuse "The archive for ${slug} did not download from ${url}. This matrix installs what an operator installs, so a row whose archive is unreachable ends the run rather than being quietly skipped."

  # The catalogue's checksum is what a server checks before it unpacks, so this
  # checks the same thing rather than trusting the transfer. A mismatch is a
  # refusal and never a skip: a published catalogue and a published asset
  # disagreeing is a fact about the family that a green run must not swallow.
  got="$(md5sum "${archive}" | cut -d' ' -f1)"
  [ "${got}" = "${checksum}" ] \
    || refuse "The archive for ${slug} hashes to ${got} and the hub's catalogue declares ${checksum}."

  # Named the way the server's own installer names it, from the plugin's name and
  # version, because the collision scan derives a plugin's data folder from that
  # same pair. A directory named anything else would make that one kind blind.
  into="${fetch}/${name// /}_${version}"
  mkdir -p "${into}"
  unzip -q -o "${archive}" -d "${into}"
  rm -f "${archive}"

  unpacked="$(find "${into}" -mindepth 1 -maxdepth 1 | wc -l)"
  [ "${unpacked}" -gt 0 ] \
    || refuse "The archive for ${slug} unpacked to nothing, so the server would boot without it and the run would report a set it never held."

  echo "Fetched ${name} ${version} into $(basename "${into}"): ${unpacked} entr(ies), checksum as the catalogue declares."
done <<< "${rows}"
