#!/usr/bin/env bash
#
# Installs this plugin the way a user installs it - from the published catalogue,
# on a stock server, with nothing copied in - and walks the upgrade between the two
# newest versions that catalogue lists (#347).
#
# WHY THIS EXISTS BESIDE THE HARNESSES THAT ALREADY BOOT A SERVER. The loop harness,
# the floor harness and the interoperability matrix all unpack the packaged archive
# into the server's plugin directory. That proves the archive works. It says nothing
# about the route every user actually takes: an address added once in the dashboard,
# a catalogue entry the server has to parse, an archive it has to fetch, and a
# checksum it has to accept. An entry that does not resolve, an archive that does not
# download and a checksum that does not match are all invisible to a run that put the
# files there itself, and they are the whole of what a user meets first.
#
# `docs/install-from-the-catalogue.md` is the record of that walk made by hand on
# 2026-09-05. This is the same walk as a run, which is what #76's fifth condition
# asked for and what its own scope could not carry.
#
# WHAT IT ASSERTS, in two phases on two servers, because a phase that met the state
# the other left would be reading its own leftovers:
#
#   Phase A, a clean install of the newest version the catalogue lists:
#     A1. A stock server given only the address can see this plugin in the
#         catalogue, and sees both versions rather than one.
#     A2. The newest version installs through the route the dashboard uses.
#     A3. After the restart the server lists it as active, at that version.
#
#   Phase B, the upgrade between the two newest versions the catalogue lists:
#     B1. The older version installs from the same address and loads.
#     B2. An item added through this plugin's endpoint under the older version is
#         on the stored list.
#     B3. The newer version installs over it from the catalogue and loads.
#     B4. The stored list still holds that item, read through the newer assembly.
#
# B2 IS B4'S ONE-CHANGE NEIGHBOUR AND IS READ ONE STEP BEFORE IT, which is the
# ordering the loop harness beside this one uses. Without B2, a store that never held
# the entry at all would satisfy B4 by holding nothing on both sides of the upgrade,
# and the run would report an upgrade that preserves a list it never wrote.
#
# THE REPOSITORY IS TURNED OFF FOR THE LENGTH OF THE OLDER VERSION'S LIFE, and that
# is a finding rather than a convenience. `docs/install-from-the-catalogue.md`
# records it: a server that has the catalogue enabled runs its own `Update Plugins`
# task at startup and fetches the newer version seconds after loading the older one,
# with nobody having asked. Without the repository disabled between B1 and B3 there
# is no upgrade to walk, because the server has already made it.
#
# WHAT IT DOES NOT ASSERT. It does not run the projection or read a playlist: that is
# the loop harness, on the packaged archive, and duplicating it here would be two
# harnesses drifting against one assertion. It reads one user's private list. It
# reads one server line, because this tree carries one package set. It does not
# compare the catalogue's checksums against the release assets - that is a reading of
# the catalogue rather than of a server, and it is #89.
#
# WHAT IT DOES WHEN THE CATALOGUE HAS NOT CAUGHT UP WITH A RELEASE. It reports the
# lag by name and does not go red for it. The catalogue is rebuilt on the hub on its
# own schedule and reaches the address through a merge there, so a release is not in
# it for a period neither this repository nor this run can end; a check that reddened
# for that would be red after every release, for as long as somebody else's queue
# takes, and would be a red run somebody learns to ignore. What this run judges is
# whether what IS published can be installed and upgraded. Whether the catalogue has
# fallen behind a release is a reading of the catalogue against the releases, and it
# is #89's subject rather than this one's.
#
# THE LAG READING IS BEST-EFFORT AND SAYS SO. It asks the public releases API for the
# newest release of this repository. If that cannot be read the run prints that the
# lag is unknown and carries on, because the lag is a report here and never a verdict.
#
# WHAT PROVES IT BITES. Two probes, ahead of the run that matters, which is the
# ordering every other guard in this repository uses:
#
#   --prove-it-bites             Runs the judgements this harness decides with over
#                                fabricated answers and their one-change neighbours.
#                                Starts no container and fetches nothing.
#   --drop-the-store-before-the-upgrade
#                                The same two phases, with this plugin's store
#                                directory emptied inside the container between B2
#                                and B3. Exits zero ONLY where this refused it for
#                                the stored list not surviving the upgrade, and
#                                non-zero for every other outcome, including a run
#                                that died before it asserted anything.
#
# THE NEAR MISS READS THE SHAPE OF THE FAILURE AND NEVER THE EXIT STATUS, which is
# the rule the boot harness and the loop harness both carry: a step written as "run
# it and expect a non-zero exit" accepts a harness that fell over at first-time setup
# having asserted nothing. The shape is carried by the marker below and compared by
# name.
#
# THE MEANS. Bash, curl and jq with docker, which is what the harnesses beside it are
# written in and what the workflows here already run. A .NET test cannot be the
# means: the suite is refused the network and a process launch by the headless rule
# in `Jellyfin.Plugin.Watchlist.Tests/HEADLESS.md`, and this has to start a container,
# reach a public address and speak HTTP. Node or Python would add a runtime this tree
# does not carry, for one script.
#
# It needs no display, no elevated rights and no machine trust store. Both servers
# answer plain HTTP on ports bound to the loopback address. It starts no daemon: a
# container runtime somebody else's session owns is not a thing to switch on in
# passing, and its absence ends the run with that sentence rather than with a verdict
# about the plugin.
#
# Usage:
#
#   .github/scripts/install-from-the-catalogue-on-a-line.sh \
#     --image jellyfin/jellyfin:10.11.11
#   .github/scripts/install-from-the-catalogue-on-a-line.sh \
#     --image jellyfin/jellyfin:10.11.11 --collect <directory>
#   .github/scripts/install-from-the-catalogue-on-a-line.sh \
#     --image jellyfin/jellyfin:10.11.11 --drop-the-store-before-the-upgrade
#   .github/scripts/install-from-the-catalogue-on-a-line.sh --prove-it-bites

set -euo pipefail

manifest="build.yaml"
readme="README.md"
image=""
address=""
port_a="18101"
port_b="18102"
prove="no"
drop_the_store="no"
collect_into=""

# The shape the near miss compares by name, for the reason the header gives.
NO_SURVIVAL="[no-survival]"

# This harness authenticates as a user it creates on each server. The password is
# written here because the servers it is given to live for the length of one run on
# loopback ports and are destroyed at the end of it; nothing this value protects
# outlives the run.
admin="catalogue-administrator"
secret="4d1e77a0-catalogue-harness"
client='MediaBrowser Client="catalogue-harness", Device="ci", DeviceId="catalogue-harness", Version="1.0.0.0"'

# The one film phase B is driven with. The year is part of the directory and the file
# name because that is the shape the server's own movie resolver reads.
film="Catalogue Probe (2020)"

usage() {
  echo "Usage: install-from-the-catalogue-on-a-line.sh --image <image> [--address <url>] [--collect <directory>] [--drop-the-store-before-the-upgrade]" >&2
  echo "       install-from-the-catalogue-on-a-line.sh --prove-it-bites" >&2
  exit 2
}

while [ "$#" -gt 0 ]; do
  case "$1" in
    --manifest) manifest="$2"; shift 2 ;;
    --readme) readme="$2"; shift 2 ;;
    --image) image="$2"; shift 2 ;;
    --address) address="$2"; shift 2 ;;
    --port-a) port_a="$2"; shift 2 ;;
    --port-b) port_b="$2"; shift 2 ;;
    --collect) collect_into="$2"; shift 2 ;;
    --drop-the-store-before-the-upgrade) drop_the_store="yes"; shift ;;
    --prove-it-bites) prove="yes"; shift ;;
    *)
      echo "Unknown argument: $1" >&2
      usage
      ;;
  esac
done

# Reads one scalar written at column zero of the manifest, refusing an absent key
# rather than falling back to a default, for the reason the harnesses beside this one
# give: a run asserting against the wrong identity is worse than one that did not run.
scalar_of() {
  local key="$1" value

  value="$(sed -n "s/^${key}: *//p" "${manifest}" | head -n 1 | tr -d '\r')"
  value="${value%\"}"
  value="${value#\"}"

  if [ -z "${value}" ]; then
    echo "${manifest} declares no ${key} at column zero. This harness refuses rather than asserting against a default." >&2
    exit 1
  fi

  printf '%s' "${value}"
}

# THE ADDRESS IS READ OUT OF THE README RATHER THAN TYPED HERE. It is the one value
# in this run that a user also has, it is written down in exactly one place in this
# tree, and a second copy of it here would be the copy that goes stale on the day the
# family moves the name. `README.md` under `## Installing` carries it as the indented
# line a reader is told to paste into the dashboard.
address_of() {
  local found

  found="$(sed -n 's|^ *\(https://[A-Za-z0-9./_-]*manifest\.json\) *$|\1|p' "${readme}" | head -n 1 | tr -d '\r')"

  if [ -z "${found}" ]; then
    echo "${readme} carries no catalogue address as an indented line, so this harness has no address to give a server and refuses rather than guessing one." >&2
    exit 1
  fi

  printf '%s' "${found}"
}

# Percent-encodes the address for the query string the install route takes. Only the
# characters an https URL can carry are handled, because that is the whole population
# an address read out of the README can be.
encoded() {
  printf '%s' "$1" | sed -e 's|%|%25|g' -e 's|:|%3A|g' -e 's|/|%2F|g' -e 's|?|%3F|g' -e 's|&|%26|g' -e 's|=|%3D|g'
}

# EVERY JUDGEMENT BELOW STRIPS A CARRIAGE RETURN OUT OF WHAT jq PRINTS, and that is
# not tidying. A jq built for Windows writes its lines with CRLF, so a judgement that
# compared what it printed would answer differently on a workstation and on the runner
# - and it would answer 'the versions are wrong' rather than 'this machine writes
# different bytes', which is the reading somebody would then chase in the wrong tree.
# Measured: without the strip the first probe below refuses on a machine whose jq
# writes CRLF, with the two readings one carriage return apart.
#
# THE STRIP IS A PIPE AND THE EXIT STATUS STILL COMES FROM jq, WHICH IS THE HALF TO
# READ BEFORE COPYING THE SHAPE. Every judgement here decides `unreadable` from jq's
# status, and a pipeline normally reports its LAST command - so a `| tr` after jq
# would report success for an answer jq could not parse, and an address serving
# something that is not a catalogue would be judged as one merely carrying no entry.
# What stops that is `pipefail` at the top of this file, and taking it out breaks
# four judgements at once without moving a line near them.
#
# THE HOST SIDE OF A MOUNT IS THE ONE PATH HERE THAT IS NOT THE CONTAINER'S, and on
# a workstation running Git Bash the two are spelled differently. `mktemp -d` there
# returns a POSIX path the container runtime does not know, and a `--volume` naming
# it MOUNTS NOTHING rather than failing: the image's own empty directory is what the
# container then sees, the film cannot be written into it, and the run refuses saying
# the film could not be generated - a statement about the mount, pointing a reader at
# the encoder. Measured on a workstation, twice, before this existed.
#
# Where `cygpath` is on the path the host side is spelled the way that runtime reads
# it; everywhere else - every runner this repository uses - this is the identity and
# costs one process.
host_path() {
  if command -v cygpath >/dev/null 2>&1; then
    cygpath -w "$1"
  else
    printf '%s' "$1"
  fi
}

# Compares two identifiers written in either of the forms a server returns: the
# hyphenated form and the bare hexadecimal one the API hands back.
same_identifier() {
  local left right

  left="$(printf '%s' "$1" | tr -d '-' | tr '[:upper:]' '[:lower:]')"
  right="$(printf '%s' "$2" | tr -d '-' | tr '[:upper:]' '[:lower:]')"

  [ "${left}" = "${right}" ]
}

# Judges the published catalogue. Prints one word, or the versions of this plugin's
# entry newest first, one per line, and nothing else - so the probe can run it over
# fabricated answers with no network.
#
#   unreadable      the answer is not the array the catalogue is
#   absent          no entry carries this plugin's identifier, or the entry carries
#                   no version a server could install
#   <versions>      the versions of that entry, newest first by the timestamp each
#                   one declares
#
# THE ORDER IS TAKEN FROM THE TIMESTAMPS AND NEVER FROM THE ARRAY. A catalogue is
# generated, so the order its array happens to be in is a property of the generator
# rather than a promise to a reader, and a run that took the first element for the
# newest would walk the upgrade backwards on the day that generator changed.
catalogue_versions() {
  local catalogue="$1" wanted="$2" found

  if ! found="$(printf '%s' "${catalogue}" \
    | jq -er --arg wanted "${wanted}" '
        if type == "array" then
          (map(select(((((.guid // .Guid) // "") | ascii_downcase | gsub("-";"")))
                      == ($wanted | ascii_downcase | gsub("-";""))))
            | if length == 0 then "absent"
              else
                (((.[0].versions // .[0].Versions) // [])
                  | map(select((((.version // .Version) // "")) != ""))
                  | sort_by(((.timestamp // .Timestamp) // ""))
                  | reverse
                  | map(((.version // .Version)))
                  | if length == 0 then "absent" else join("\n") end)
              end)
        else
          empty
        end' 2>/dev/null | tr -d '\r')"; then
    printf 'unreadable'
    return 0
  fi

  printf '%s' "${found}"
}

# Judges what a server can see in the catalogue it was given. Prints one word, or the
# versions the server offers for this plugin, one per line, in the order the server
# gave them.
#
#   unreadable      the answer is not the array this route returns
#   absent          the server offers no package with this plugin's identifier, or
#                   offers it with no version
#   <versions>      the versions it offers
server_offers() {
  local packages="$1" wanted="$2" found

  if ! found="$(printf '%s' "${packages}" \
    | jq -er --arg wanted "${wanted}" '
        if type == "array" then
          (map(select(((((.guid // .Guid) // "") | ascii_downcase | gsub("-";"")))
                      == ($wanted | ascii_downcase | gsub("-";""))))
            | if length == 0 then "absent"
              else
                (((.[0].versions // .[0].Versions) // [])
                  | map(((.version // .Version) // ""))
                  | map(select(. != ""))
                  | if length == 0 then "absent" else join("\n") end)
              end)
        else
          empty
        end' 2>/dev/null | tr -d '\r')"; then
    printf 'unreadable'
    return 0
  fi

  printf '%s' "${found}"
}

# Judges the server's own plugin listing, which is the only place that says what the
# server actually loaded. Prints one word, or the version and the status of this
# plugin's entry separated by one space.
#
#   unreadable      the answer is not the array this route returns
#   absent          the server lists no plugin with this identifier
#   <version> <status>
installed_as() {
  local plugins="$1" wanted="$2" found

  if ! found="$(printf '%s' "${plugins}" \
    | jq -er --arg wanted "${wanted}" '
        if type == "array" then
          (map(select(((((.Id // .id) // "") | ascii_downcase | gsub("-";"")))
                      == ($wanted | ascii_downcase | gsub("-";""))))
            | if length == 0 then "absent"
              else (((.[0].Version // .[0].version) // "?") + " " + ((.[0].Status // .[0].status) // "?"))
              end)
        else
          empty
        end' 2>/dev/null | tr -d '\r')"; then
    printf 'unreadable'
    return 0
  fi

  printf '%s' "${found}"
}

# Judges this plugin's own reading of the stored list.
#
#   unreadable      the answer is not the array the endpoint returns
#   absent          no entry carries this item
#   held            an entry carries it
store_holds() {
  local listing="$1" item="$2" count index id

  if ! count="$(printf '%s' "${listing}" | jq -er 'if type == "array" then length else empty end' 2>/dev/null | tr -d '\r')"; then
    printf 'unreadable'
    return 0
  fi

  index=0
  while [ "${index}" -lt "${count}" ]; do
    id="$(printf '%s' "${listing}" | jq -r ".[${index}] | (.ItemId // .itemId // \"\")" | tr -d '\r')"

    if same_identifier "${id}" "${item}"; then
      printf 'held'
      return 0
    fi

    index=$((index + 1))
  done

  printf 'absent'
}

if [ "${prove}" = "yes" ]; then
  refuse_probe() { echo "$1" >&2; exit 1; }

  guid="$(scalar_of guid)"
  other="7f9619ff-8b86-d011-b42d-00c04fc964ff"
  item="6f9619ff-8b86-d011-b42d-00c04fc964ff"

  # The catalogue judgement decides which two versions the whole run walks, so every
  # assertion below it is about whichever pair this returns.
  entry="[{\"guid\":\"${other}\",\"name\":\"Other\",\"versions\":[{\"version\":\"9.9.9.9\",\"timestamp\":\"2026-01-01T00:00:00Z\"}]},{\"guid\":\"${guid}\",\"name\":\"Watchlist\",\"versions\":[{\"version\":\"0.1.0.0\",\"timestamp\":\"2026-09-03T10:26:50Z\"},{\"version\":\"0.1.1.0\",\"timestamp\":\"2026-09-04T12:52:42Z\"}]}]"

  [ "$(catalogue_versions "${entry}" "${guid}")" = "$(printf '0.1.1.0\n0.1.0.0')" ] \
    || refuse_probe "The catalogue judgement did not return this plugin's versions newest first by timestamp, so the run would walk the upgrade backwards."
  [ "$(catalogue_versions "${entry}" "${other}")" = "9.9.9.9" ] \
    || refuse_probe "The catalogue judgement did not select by identifier, so it would answer about whichever entry came first."
  [ "$(catalogue_versions '[]' "${guid}")" = "absent" ] \
    || refuse_probe "The catalogue judgement read an empty catalogue as an entry, so a catalogue that has never carried this plugin would look like one that does."
  [ "$(catalogue_versions "[{\"guid\":\"${guid}\",\"name\":\"Watchlist\",\"versions\":[]}]" "${guid}")" = "absent" ] \
    || refuse_probe "The catalogue judgement read an entry carrying no versions as a hit, so a withdrawn plugin would look like an installable one."
  [ "$(catalogue_versions 'not json at all' "${guid}")" = "unreadable" ] \
    || refuse_probe "The catalogue judgement read an unreadable answer as a catalogue, so an address that stopped answering and one carrying no entry would look the same."

  # The server's own view of that catalogue. A1 rests on it, and so does the reason
  # the run refuses before it installs anything.
  packages="[{\"guid\":\"${guid}\",\"name\":\"Watchlist\",\"versions\":[{\"version\":\"0.1.1.0\"},{\"version\":\"0.1.0.0\"}]}]"
  [ "$(server_offers "${packages}" "${guid}")" = "$(printf '0.1.1.0\n0.1.0.0')" ] \
    || refuse_probe "The offer judgement did not return the versions a server says it can install, so A1 could never pass."
  [ "$(server_offers "${packages}" "${other}")" = "absent" ] \
    || refuse_probe "The offer judgement answered about a package the server does not offer under this identifier, so A1 would pass for somebody else's plugin."
  [ "$(server_offers '[]' "${guid}")" = "absent" ] \
    || refuse_probe "The offer judgement read an empty package listing as a hit, so a server that could not resolve the address would look like one that did."
  [ "$(server_offers 'not json at all' "${guid}")" = "unreadable" ] \
    || refuse_probe "The offer judgement read an unreadable answer as a listing, so an unread server and one offering nothing would look the same."

  # The listing judgement is the only reading that says what the server LOADED, and
  # A3, B1 and B3 all rest on it.
  plugins="[{\"Id\":\"$(printf '%s' "${guid}" | tr -d '-')\",\"Name\":\"Watchlist\",\"Version\":\"0.1.1.0\",\"Status\":\"Active\"}]"
  [ "$(installed_as "${plugins}" "${guid}")" = "0.1.1.0 Active" ] \
    || refuse_probe "The listing judgement did not read the version and the status of the plugin the server loaded, so A3 could never pass."
  [ "$(installed_as "${plugins}" "${other}")" = "absent" ] \
    || refuse_probe "The listing judgement answered about a plugin carrying another identifier, so A3 would pass for a sibling on the same server."
  [ "$(installed_as "${plugins//Active/Malfunctioned}" "${guid}")" = "0.1.1.0 Malfunctioned" ] \
    || refuse_probe "The listing judgement did not carry the status back, so a plugin the server installed and refused to run would read as a working one."
  [ "$(installed_as "${plugins//0.1.1.0/0.1.0.0}" "${guid}")" = "0.1.0.0 Active" ] \
    || refuse_probe "The listing judgement did not carry the version back, so the upgrade could not be told from the install that came before it."
  [ "$(installed_as '[]' "${guid}")" = "absent" ] \
    || refuse_probe "The listing judgement read an empty plugin listing as a hit, so an install that landed nothing would look like one that did."
  [ "$(installed_as 'not json at all' "${guid}")" = "unreadable" ] \
    || refuse_probe "The listing judgement read an unreadable answer as a listing, so an unread server and one with no plugin would look the same."

  # The store judgement is what B2 and B4 rest on, in the same direction, so it is
  # proved against an emptied list as well as a held one.
  stored="[{\"ItemId\":\"${item}\",\"Kind\":\"Movie\",\"Name\":\"Catalogue Probe\"}]"
  [ "$(store_holds "${stored}" "${item}")" = "held" ] \
    || refuse_probe "The store judgement did not find an entry the list holds, so B2 could never pass."
  [ "$(store_holds "${stored}" "${other}")" = "absent" ] \
    || refuse_probe "The store judgement found an entry for an item the list does not hold, so B4 would pass for the wrong film."
  [ "$(store_holds '[]' "${item}")" = "absent" ] \
    || refuse_probe "The store judgement read an empty list as holding the item, so B4 would pass on an upgrade that dropped the store."
  [ "$(store_holds 'not json at all' "${item}")" = "unreadable" ] \
    || refuse_probe "The store judgement read an unreadable answer as a list, so B4 would take an unread server for a surviving list."

  # The address is the one value a user also has, and a run pointed at nothing would
  # install nothing and report the catalogue broken.
  found="$(address_of)"
  case "${found}" in
    https://*manifest.json) : ;;
    *) refuse_probe "The address read out of ${readme} is not an https catalogue address, so the run would give a server something it cannot resolve: ${found}" ;;
  esac
  [ "$(encoded "${found}")" != "${found}" ] \
    || refuse_probe "The encoder left the address unchanged, so the install route would receive an unencoded URL in its query string."

  echo "The catalogue judgement returns this plugin's versions newest first by timestamp, and refuses another entry, an empty catalogue, an entry with no versions and an unreadable answer."
  echo "The offer judgement returns what a server says it can install, and refuses another identifier, an empty listing and an unreadable answer."
  echo "The listing judgement returns the version and the status the server loaded, and refuses another identifier, an empty listing and an unreadable answer; a refused plugin reads as refused rather than as working."
  echo "The store judgement finds a held entry, and refuses another film, an emptied list and an unreadable answer."
  echo "The address is read out of ${readme} as ${found} and is encoded for the query string the install route takes."
  exit 0
fi

if [ -z "${image}" ]; then
  usage
fi

for tool in docker curl jq; do
  command -v "${tool}" >/dev/null 2>&1 \
    || { echo "This harness needs ${tool} and it is not on the path. It installs nothing." >&2; exit 1; }
done

if ! runtime="$(docker version --format '{{.Server.Version}}' 2>&1)"; then
  echo "No container runtime answered. This harness needs one and does not start one: a daemon somebody else's session owns is not a thing to switch on in passing." >&2
  echo "${runtime}" >&2
  exit 1
fi

name="$(scalar_of name)"
guid="$(scalar_of guid)"
[ -n "${address}" ] || address="$(address_of)"

root="$(mktemp -d)"
transcript="${root}/transcript.txt"
: > "${transcript}"

container_a="catalogue-clean-$$"
container_b="catalogue-upgrade-$$"
container=""
base=""
token=""
user=""
item=""

echo "Container runtime: ${runtime}"
echo "Plugin:            ${name} ${guid}"
echo "Image:             ${image}"
echo "Address:           ${address}"
if [ "${drop_the_store}" = "yes" ]; then
  echo "Mode:              the near miss, with the store emptied between B2 and B3"
fi

# The containers and the temporary directory go whichever way the run ends, and this
# is also where the collection happens - a failing run leaves through a refusal
# rather than through the last line of this file, so anything written after the
# assertions is written only on the runs that did not need it.
collect() {
  [ -n "${collect_into}" ] || return 0

  mkdir -p "${collect_into}" || return 0
  docker logs "${container_a}" > "${collect_into}/clean-server.log" 2>&1 || true
  docker logs "${container_b}" > "${collect_into}/upgrade-server.log" 2>&1 || true
  cp "${transcript}" "${collect_into}/transcript.txt" 2>/dev/null || true

  echo "Collected both server logs and the request transcript into ${collect_into}."
}

cleanup() {
  collect
  docker rm --force "${container_a}" >/dev/null 2>&1 || true
  docker rm --force "${container_b}" >/dev/null 2>&1 || true
  rm -rf "${root}" 2>/dev/null || echo "Left behind ${root}."
}
trap cleanup EXIT

# Every refusal goes through here, so a red run carries the transcript and the log of
# the server it was talking to without anybody re-running it.
#
# IT IS ALSO WHERE THE NEAR MISS IS DECIDED, AND IT COMPARES THE SHAPE BY NAME. A
# near-miss run that ended non-zero anywhere else - a container that never came up, a
# catalogue that did not answer, a library that never scanned - asserted nothing about
# the store surviving the upgrade, and a step reading only the exit status would
# record that as a proof.
refuse() {
  if [ "${drop_the_store}" = "yes" ]; then
    case "$1" in
      "${NO_SURVIVAL}"*)
        echo ""
        echo "$1"
        echo ""
        echo "The harness refuses an upgrade that found the stored list gone, for the list not surviving. B4 bites."
        exit 0
        ;;
    esac
  fi

  echo "" >&2
  echo "$1" >&2
  echo "" >&2
  echo "--- the calls this harness made, in order ---" >&2
  cat "${transcript}" >&2

  if [ -n "${container}" ]; then
    echo "" >&2
    echo "--- the last 200 lines ${container} logged ---" >&2
    docker logs "${container}" 2>&1 | tail -n 200 >&2
  fi

  exit 1
}

# Calls the server this run is currently talking to. Prints the body on standard
# output and the status code on the last line, and records the server, the route and
# the status in the transcript the refusal above prints.
call() {
  local route="$1" method="${2:-GET}" body="${3:-}" tok="${4:-}"
  local authorization="${client}" answer

  if [ -n "${tok}" ]; then
    authorization="${client}, Token=\"${tok}\""
  fi

  if [ -n "${body}" ]; then
    answer="$(curl --silent --show-error --write-out '\n%{http_code}' \
      --request "${method}" \
      --header "Authorization: ${authorization}" \
      --header 'Content-Type: application/json' \
      --data "${body}" \
      "${base}${route}")"
  else
    answer="$(curl --silent --show-error --write-out '\n%{http_code}' \
      --request "${method}" \
      --header "Authorization: ${authorization}" \
      "${base}${route}")"
  fi

  printf '%s %s %s -> %s\n' "${container}" "${method}" "${route}" "$(printf '%s' "${answer}" | tail -n 1)" >> "${transcript}"
  printf '%s' "${answer}"
}

status_of() { printf '%s' "$1" | tail -n 1; }
body_of() { printf '%s' "$1" | sed '$d'; }

# Waits for a route to answer 200. Two routes are waited on at boot rather than one,
# for the reason the harnesses beside this one state: the public route says the
# server is listening, the wizard route says it is ready. After a restart only the
# first is waited on, because the wizard is finished by then and answers differently.
wait_for() {
  local route="$1" seconds="$2" deadline answer status
  deadline=$(( $(date +%s) + seconds ))

  while [ "$(date +%s)" -lt "${deadline}" ]; do
    if answer="$(call "${route}" 2>/dev/null)"; then
      status="$(status_of "${answer}")"

      if [ "${status}" = "200" ]; then
        return 0
      fi
    fi

    sleep 2
  done

  refuse "The server did not answer ${base}${route} with 200 within ${seconds}s."
}

boot() {
  local this="$1" this_port="$2"

  container="${this}"
  base="http://127.0.0.1:${this_port}"

  mkdir -p "${root}/${this}/config" "${root}/${this}/cache" "${root}/${this}/media/Movies/${film}"
  # The server writes into the mounted media directory as the user inside the
  # container, which is not the user running this.
  chmod -R 0777 "${root}/${this}/media"

  MSYS_NO_PATHCONV=1 docker run --detach --name "${this}" \
    --publish "127.0.0.1:${this_port}:8096" \
    --volume "$(host_path "${root}/${this}/config"):/config" \
    --volume "$(host_path "${root}/${this}/cache"):/cache" \
    --volume "$(host_path "${root}/${this}/media"):/media" \
    "${image}" >/dev/null

  wait_for "/System/Info/Public" 240
  wait_for "/Startup/Configuration" 240
}

# THE GET ON /Startup/User IS NOT A READ. It is what creates the first user, and
# posting without it answers 404. The server's own source is the authority:
#
#     gh api "repos/jellyfin/jellyfin/contents/Jellyfin.Api/Controllers/StartupController.cs?ref=v10.11.11" \
#       -H "Accept: application/vnd.github.raw" | grep -n 'GetFirstUser\|UpdateStartupUser'
complete_setup() {
  local created answer status pair

  created="$(call "/Startup/User")"
  [ "$(status_of "${created}")" = "200" ] \
    || refuse "The server did not create a first user: /Startup/User answered $(status_of "${created}")."

  for pair in \
    "/Startup/Configuration|{\"UICulture\":\"en-US\",\"MetadataCountryCode\":\"US\",\"PreferredMetadataLanguage\":\"en\"}" \
    "/Startup/User|$(jq -n --arg n "${admin}" --arg p "${secret}" '{Name:$n,Password:$p}')" \
    "/Startup/RemoteAccess|{\"EnableRemoteAccess\":true,\"EnableAutomaticPortMapping\":false}" \
    "/Startup/Complete|{}"; do
    answer="$(call "${pair%%|*}" POST "${pair#*|}")"
    status="$(status_of "${answer}")"
    [ "${status}" -lt 400 ] \
      || refuse "First-time setup failed at ${pair%%|*} with status ${status}: $(body_of "${answer}" | head -c 400)"
  done
}

authenticate() {
  local answer

  answer="$(call "/Users/AuthenticateByName" POST "$(jq -n --arg u "${admin}" --arg p "${secret}" '{Username:$u,Pw:$p}')")"
  [ "$(status_of "${answer}")" = "200" ] \
    || refuse "Authenticating the administrator this harness created returned $(status_of "${answer}")."

  token="$(body_of "${answer}" | jq -r '.AccessToken // .accessToken // ""')"
  user="$(body_of "${answer}" | jq -r '.User.Id // .user.id // ""')"

  [ -n "${token}" ] && [ -n "${user}" ] \
    || refuse "The authentication response carried no access token or no user identifier, so every step below would run as nobody and pass for the wrong reason."
}

# Replaces the server's repository list with this one address, enabled or not. The
# route takes the whole list rather than one entry, which is what the dashboard sends.
set_repository() {
  local enabled="$1" answer

  answer="$(call "/Repositories" POST \
    "$(jq -nc --arg u "${address}" --argjson e "${enabled}" '[{Name:"Flowfin",Url:$u,Enabled:$e}]')" "${token}")"
  [ "$(status_of "${answer}")" -lt 400 ] \
    || refuse "Setting the repository list to the published address answered $(status_of "${answer}"): $(body_of "${answer}" | head -c 400)"
}

install_version() {
  local wanted="$1" answer

  answer="$(call "/Packages/Installed/${name// /%20}?assemblyGuid=${guid}&version=${wanted}&repositoryUrl=$(encoded "${address}")" POST "" "${token}")"
  [ "$(status_of "${answer}")" -lt 400 ] \
    || refuse "Installing ${wanted} from the published catalogue answered $(status_of "${answer}"): $(body_of "${answer}" | head -c 400). A server that cannot install from the address is the whole failure this run exists to find."
}

# The server writes the plugin to disk during the install and loads it on the next
# start, so a run that read the plugin listing without restarting would read the
# state from before the install every time.
restart_and_wait() {
  local answer

  answer="$(call "/System/Restart" POST "" "${token}")"
  [ "$(status_of "${answer}")" -lt 400 ] \
    || refuse "Restarting the server answered $(status_of "${answer}"), so what the install wrote could not be loaded."

  # The server is still answering for a moment after it accepts the restart, so a
  # poll that started immediately would pass against the process that is going away.
  sleep 10
  wait_for "/System/Info/Public" 240
}

loaded_as() {
  local wanted="$1" step="$2" plugins seen

  plugins="$(call "/Plugins" GET "" "${token}")"
  [ "$(status_of "${plugins}")" = "200" ] \
    || refuse "${step} The plugin listing answered $(status_of "${plugins}"), so what the server loaded cannot be read at all."

  seen="$(installed_as "$(body_of "${plugins}")" "${guid}")"
  case "${seen}" in
    "${wanted} Active")
      echo "${step} The server lists ${name} ${wanted} as active, installed from the catalogue and from nowhere else."
      ;;
    absent)
      refuse "${step} The server lists no plugin carrying ${guid} after installing ${wanted} from the catalogue. It lists: $(body_of "${plugins}" | jq -r 'map(((.Name // .name) // "?") + " " + ((.Version // .version) // "?")) | join("; ")' 2>/dev/null || echo unreadable)"
      ;;
    unreadable)
      refuse "${step} The plugin listing is not the array this route returns, so what the server loaded cannot be read at all."
      ;;
    *)
      refuse "${step} The server lists ${guid} as '${seen}' after installing ${wanted}, and '${wanted} Active' is what a working install reads as."
      ;;
  esac
}

# Generates the film and adds the library, because a stock server has no media and
# this plugin's add endpoint refuses an item the library cannot answer for.
#
# `MSYS_NO_PATHCONV=1` IS SET PER COMMAND AND MUST NOT BE EXPORTED, AND THAT IS
# MEASURED RATHER THAN CAUTIOUS. Git Bash rewrites an argument that looks like a
# POSIX path, which is wrong for a path INSIDE the container and right for the host
# side of a `--volume`. Unset, the encoder probe below is handed a Windows path and
# the run refuses saying the IMAGE has no encoder - a statement about the shell,
# pointing a reader at the wrong tree. Exported, the conversion also stops happening
# for `docker run --volume`, the media directory never reaches the container, and the
# run refuses one step later saying the film could not be generated. Both failures
# were produced on a workstation, in that order. Every other shell ignores the
# variable, so the prefixes cost nothing where nobody needs them.
build_the_library() {
  local ffmpeg="" candidate added found deadline

  for candidate in /usr/lib/jellyfin-ffmpeg/ffmpeg /usr/bin/ffmpeg ffmpeg; do
    if MSYS_NO_PATHCONV=1 docker exec "${container}" "${candidate}" -version >/dev/null 2>&1; then
      ffmpeg="${candidate}"
      break
    fi
  done

  [ -n "${ffmpeg}" ] \
    || refuse "No encoder answered inside ${image}. This harness generates its one film with the server's own rather than requiring one on the host, and there is nothing here to generate it with."

  MSYS_NO_PATHCONV=1 docker exec "${container}" "${ffmpeg}" -nostdin -loglevel error -y \
    -f lavfi -i "color=c=black:s=64x64:d=1:r=5" \
    -c:v libx264 -pix_fmt yuv420p -t 1 \
    "/media/Movies/${film}/${film}.mkv" \
    || refuse "The film could not be generated inside the container, so there is nothing for the library to hold and B2 cannot be reached."

  added="$(call "/Library/VirtualFolders?name=Movies&collectionType=movies&paths=%2Fmedia%2FMovies&refreshLibrary=true" POST '{}' "${token}")"
  [ "$(status_of "${added}")" -lt 400 ] \
    || refuse "Adding the movie library answered $(status_of "${added}"): $(body_of "${added}" | head -c 400)"

  item=""
  deadline=$(( $(date +%s) + 300 ))
  while [ "$(date +%s)" -lt "${deadline}" ]; do
    found="$(call "/Items?userId=${user}&recursive=true&includeItemTypes=Movie" GET "" "${token}")"

    if [ "$(status_of "${found}")" = "200" ]; then
      item="$(body_of "${found}" | jq -r '(((.Items // .items) // []) | if length == 0 then "" else ((.[0].Id // .[0].id) // "") end)')"
      [ -n "${item}" ] && break
    fi

    sleep 3
  done

  [ -n "${item}" ] \
    || refuse "The library scan produced no film within 300s, so there is no item to put on a list and B2 could not run."
}

echo ""
echo "Reading the published catalogue at ${address}."

if ! catalogue="$(curl --silent --show-error --location --max-time 60 "${address}" 2>&1)"; then
  refuse "The published address did not answer. For a user that is the plugin being unavailable, and it is the first thing this run has to establish: ${catalogue}"
fi

versions="$(catalogue_versions "${catalogue}" "${guid}")"
case "${versions}" in
  unreadable)
    refuse "The published address answered something that is not a catalogue, so no server could resolve this plugin from it. The first 200 bytes: $(printf '%s' "${catalogue}" | head -c 200)"
    ;;
  absent)
    refuse "The published catalogue carries no installable entry for ${guid}. Every install and every update of this plugin is a read of that file, so an absent entry is the plugin being unavailable."
    ;;
esac

newest="$(printf '%s' "${versions}" | sed -n '1p')"
previous="$(printf '%s' "${versions}" | sed -n '2p')"

echo "The catalogue lists: $(printf '%s' "${versions}" | tr '\n' ' ')"

[ -n "${previous}" ] \
  || refuse "The published catalogue lists one version of this plugin, so there is no upgrade to walk. This run needs two published versions and says so rather than reporting half a walk as a whole one."

echo "Newest published: ${newest}. The one before it: ${previous}."

# THE LAG IS REPORTED AND NEVER JUDGED, for the reason the header gives. It is read
# best-effort, and a reading that fails says so instead of standing in for one.
#
# WHICH REPOSITORY IT ASKS ABOUT IS DERIVED AND NOT TYPED. On a runner it is the one
# the run belongs to; on a workstation it is the origin this checkout was cloned
# from. A name written here would be a second copy of a fact the checkout already
# holds, and the copy is the one that survives a fork or a rename and then answers
# about somebody else's releases.
repository="${GITHUB_REPOSITORY:-}"
if [ -z "${repository}" ]; then
  repository="$(git remote get-url origin 2>/dev/null | sed -e 's|^git@github.com:|https://github.com/|' -e 's|^https://github.com/||' -e 's|[.]git$||')"
fi

released="$(curl --silent --show-error --location --max-time 30 \
  "https://api.github.com/repos/${repository}/releases/latest" 2>/dev/null \
  | jq -r '.tag_name // ""' 2>/dev/null || true)"

if [ -z "${released}" ]; then
  echo "Lag against the newest release: not read. Either this checkout names no origin on GitHub or the releases API did not answer, and the lag is a report here rather than a verdict, so this run carries on."
elif [ "${released%-stable}" = "${newest}" ]; then
  echo "Lag against the newest release: none. The catalogue's newest entry is the newest release, ${released}."
else
  echo "Lag against the newest release: the catalogue's newest entry is ${newest} and the newest release is ${released}."
  echo "  The catalogue is rebuilt on the hub and reaches the address through a merge there, so that wait is not this repository's to end and this run does not go red for it."
  echo "  What watches for it is #89. What this run judges is whether what IS published installs and upgrades, and it walks ${previous} to ${newest} below."
fi

echo ""
echo "=== Phase A: a clean install of ${newest}, on a server that was given nothing but the address ==="

boot "${container_a}" "${port_a}"
complete_setup
authenticate
set_repository true

# A1. The server can see this plugin in the catalogue it was given, at both versions.
#
# THE SERVER IS ASKED RATHER THAN THE ADDRESS, and the two are different questions.
# The address answering with a well-formed entry says the file is right; this says the
# server parsed it, which is the only form of it a user ever benefits from.
#
# It is polled rather than read once, because the server fetches the catalogue on its
# own schedule after the address is added and answers an empty listing until it has.
packages=""
deadline=$(( $(date +%s) + 180 ))
while [ "$(date +%s)" -lt "${deadline}" ]; do
  packages="$(call "/Packages" GET "" "${token}")"

  if [ "$(status_of "${packages}")" = "200" ] \
    && [ "$(server_offers "$(body_of "${packages}")" "${guid}")" != "absent" ]; then
    break
  fi

  sleep 5
done

[ "$(status_of "${packages}")" = "200" ] \
  || refuse "A1. The package listing answered $(status_of "${packages}"), so what the server can see in the catalogue cannot be read."

offered="$(server_offers "$(body_of "${packages}")" "${guid}")"
case "${offered}" in
  absent)
    refuse "A1. The server was given ${address} and offers no package carrying ${guid} within 180s. It offers: $(body_of "${packages}" | jq -r 'map((.name // .Name) // "?") | join("; ")' 2>/dev/null || echo unreadable)"
    ;;
  unreadable)
    refuse "A1. The package listing is not the array this route returns, so what the server can see cannot be read."
    ;;
esac

echo "A1. The server offers: $(printf '%s' "${offered}" | tr '\n' ' ')"

printf '%s\n' "${offered}" | grep -qx -- "${newest}" \
  || refuse "A1. The server does not offer ${newest}, which the catalogue lists, so the entry this run is about is one a server cannot act on."
printf '%s\n' "${offered}" | grep -qx -- "${previous}" \
  || refuse "A1. The server offers ${newest} and not ${previous}, so phase B has nothing to start from and would report the older release missing rather than the upgrade broken."

# A2 and A3.
install_version "${newest}"
restart_and_wait
loaded_as "${newest}" "A3."

echo ""
echo "=== Phase B: ${previous} from the catalogue, then the upgrade to ${newest} ==="

boot "${container_b}" "${port_b}"
complete_setup
authenticate
set_repository true

install_version "${previous}"

# THE REPOSITORY IS TURNED OFF BEFORE THE RESTART AND NOT AFTER IT. The server's own
# `Update Plugins` task runs at startup, and with the catalogue enabled it fetches
# the newer version seconds after loading the older one, which is what
# `docs/install-from-the-catalogue.md` records. Disabling it after the restart would
# be a race this run loses on a fast machine.
set_repository false

restart_and_wait
loaded_as "${previous}" "B1."

build_the_library
echo "B2. The library holds the film as ${item}."

put="$(call "/Watchlist/Items/${item}" POST "" "${token}")"
[ "$(status_of "${put}")" = "204" ] \
  || refuse "B2. POST /Watchlist/Items/${item} answered $(status_of "${put}") under ${previous}, and 204 is what an accepted add answers: $(body_of "${put}" | head -c 400)"

stored="$(call "/Watchlist/Items" GET "" "${token}")"
[ "$(status_of "${stored}")" = "200" ] \
  || refuse "B2. GET /Watchlist/Items answered $(status_of "${stored}") under ${previous}, so whether the add was stored cannot be read at all."

case "$(store_holds "$(body_of "${stored}")" "${item}")" in
  held)
    echo "B2. The stored list holds the film under ${previous}, which is what B4 below is a statement about."
    ;;
  absent)
    refuse "B2. The add answered 204 under ${previous} and the stored list does not hold the film, so B4 would pass on a list that was never written."
    ;;
  unreadable)
    refuse "B2. The stored list is not the array this endpoint returns, so B4 would take an unread server for a surviving list."
    ;;
esac

if [ "${drop_the_store}" = "yes" ]; then
  # The near miss. What is emptied is the directory this plugin writes its per-user
  # documents into, which the server itself never touches, so this is the upgrade
  # arriving at a lost store and not the server losing the plugin.
  MSYS_NO_PATHCONV=1 docker exec "${container_b}" sh -c 'rm -f /config/plugins/Jellyfin.Plugin.Watchlist/*.json' \
    || refuse "The near miss could not empty this plugin's store directory inside the container, so the run below would be the run that matters under another name."

  echo "The store directory is empty. Everything below is the same upgrade, and it must fail at B4."
fi

set_repository true
install_version "${newest}"
restart_and_wait
loaded_as "${newest}" "B3."

# B4. Read through the newer assembly, with no task run in between, so this is the
# upgrade's own answer rather than a projection's.
stored="$(call "/Watchlist/Items" GET "" "${token}")"
[ "$(status_of "${stored}")" = "200" ] \
  || refuse "${NO_SURVIVAL} B4. GET /Watchlist/Items answered $(status_of "${stored}") after the upgrade to ${newest}, so the list a user had before it cannot be read at all."

case "$(store_holds "$(body_of "${stored}")" "${item}")" in
  held)
    echo "B4. The stored list still holds the film after the upgrade from ${previous} to ${newest}."
    ;;
  absent)
    refuse "${NO_SURVIVAL} B4. The upgrade from ${previous} to ${newest} left the stored list without the film that was on it, so an upgrade published this way loses what a user had."
    ;;
  unreadable)
    refuse "${NO_SURVIVAL} B4. The stored list is not the array this endpoint returns after the upgrade, so whether the list survived cannot be read at all."
    ;;
esac

if [ "${drop_the_store}" = "yes" ]; then
  echo "" >&2
  echo "The store was emptied before the upgrade and B4 passed anyway, so nothing in this harness is watching the stored list and a green run of it would prove nothing." >&2
  exit 1
fi

echo ""
echo "${name} installs from ${address} on ${image} and upgrades there: a server given nothing but the address resolves ${newest} and loads it, ${previous} installs from the same address, and the list a user had under ${previous} is still there after the upgrade to ${newest}."
