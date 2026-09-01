#!/usr/bin/env bash
#
# Drives the whole watchlist loop against a stock server of one supported line,
# with the packaged plugin installed the way a server installs one (#52).
#
# WHY THIS EXISTS, AND WHY THE BOOT HARNESS BESIDE IT IS NOT IT. The unit suite
# covers the store, the projector and the reconciler with no server; the boot
# harness proves the packaged archive loads, is listed active and answers three
# routes. Neither of them adds an item, runs the projection or reads a playlist,
# so "a client sees the list" has been a claim about source on every green run
# this board has produced. The parts only a real server has - the scheduled task
# the projection is driven by, the playlist manager it writes through, the
# library the endpoints refuse an unknown item against - are exactly the parts
# nothing here has ever executed.
#
# WHAT IT ASSERTS, in the order the loop takes and with each one read the way a
# client reads it:
#
#   1. An item added through this plugin's own endpoint is on the stored list.
#   2. Before the projection has run there is no playlist carrying the configured
#      name.
#   3. After a run of this plugin's scheduled task, a playlist carrying that name
#      exists and holds the item, read with the two queries a client issues: the
#      item listing filtered to playlists, and the playlist's own contents route.
#   4. The stored list still holds the item at that point.
#   5. After the row is removed from the playlist the way a client removes one,
#      and the task is run again, the stored list no longer holds the item.
#
# ASSERTIONS 2 AND 4 ARE NEIGHBOURS RATHER THAN DECORATION. Without 2, a server
# that had held a matching playlist since before the run would pass 3 with the
# projection having done nothing. Without 4, a store that dropped the entry for
# any reason at all would pass 5. Both are read on the same server, in the same
# run, one step before the assertion they guard.
#
# THE PACKAGED ARCHIVE IS THE SUBJECT, NOT THE BUILD OUTPUT, for the reason the
# boot harness beside this one states: copying `bin/` into a container proves the
# code works and not that the thing a user installs works.
#
# THE LIBRARY IS BUILT HERE BECAUSE A STOCK SERVER HAS NO MEDIA. The add endpoint
# refuses an item the library cannot answer for, so a harness pointed at an empty
# server can only ever watch it refuse. One file is generated inside the container
# with the server's own encoder - so this needs nothing on the host the boot
# harness does not already need - a movie library is added over it, and the run
# waits for the scan rather than sleeping.
#
# THE TASK IS STARTED THROUGH THE SERVER'S OWN ROUTE AND WAITED FOR. The
# projection is driven by a scheduled task and by nothing else on the add path,
# so a harness that added an item and read a playlist immediately would be a race
# that passes on a fast runner and fails on a slow one. The task is looked up by
# the key the plugin declares, started, and polled until the server reports it
# idle again.
#
# WHAT IT DOES NOT ASSERT, said here rather than left to be discovered. It reads
# one user's private list. The shared list is off until an administrator turns it
# on and is a second projection target with rules of its own, and a harness
# covering both would be two loops sharing a boot rather than one loop. It reads
# one server line, because this tree carries one package set.
#
# WHAT PROVES IT BITES. Two probes, ahead of the run that matters, which is the
# ordering every other guard in this repository uses:
#
#   --prove-it-bites            Runs the four judgements this harness decides with
#                               over fabricated answers and their one-change
#                               neighbours. Starts no container.
#   --without-the-projection    Boots the same image with the same package, turns
#                               the projection off through the plugin's own
#                               configuration route, and drives the same loop.
#                               Exits zero ONLY where this refused it for the
#                               playlist never appearing, and non-zero for every
#                               other outcome including a harness that died before
#                               it asserted anything.
#
# THE NEAR MISS READS THE SHAPE OF THE FAILURE AND NEVER THE EXIT STATUS, which is
# the boot harness's rule and is here for its reason: a step written as "run it and
# expect a non-zero exit" accepts a harness that fell over at first-time setup
# having asserted nothing. The shape is carried by the marker below and compared
# by name.
#
# A FAILURE PRINTS WHAT IT COLLECTED. Every refusal goes through one function that
# dumps the transcript of the calls made and the container's log before it exits,
# so a red run is readable without re-running it. `--collect` writes the same two
# things to a directory instead of only to the log, which is what a job uploads
# when the run that produced them is gone: a log printed into a workflow run is
# readable while somebody is looking at that run, and a file is readable after it
# has scrolled past. Both are written on EVERY path, not only the failing one, so
# a green run leaves the transcript that says what it actually asked the server.
#
# THE MEANS. Bash, curl and jq, with docker and unzip, which is what
# `boot-a-line-with-this-plugin.sh` beside it is written in and what the workflows
# here already run. A .NET test cannot be the means: the suite is refused the
# network and a process launch by the headless rule in
# `Jellyfin.Plugin.Watchlist.Tests/HEADLESS.md`, and this has to start a container
# and speak HTTP. Node or Python would add a runtime this tree does not carry, for
# one script.
#
# It needs no display, no elevated rights and no machine trust store. The server
# answers plain HTTP on a port bound to the loopback address. It starts no daemon:
# a container runtime somebody else's session owns is not a thing to switch on in
# passing, and its absence ends the run with that sentence rather than with a
# verdict about the plugin.
#
# Usage:
#
#   .github/scripts/drive-the-whole-loop-on-a-line.sh \
#     --image jellyfin/jellyfin:10.11.11 --package <package.zip>
#   .github/scripts/drive-the-whole-loop-on-a-line.sh \
#     --image jellyfin/jellyfin:10.11.11 --package <package.zip> \
#     --collect <directory>
#   .github/scripts/drive-the-whole-loop-on-a-line.sh \
#     --image jellyfin/jellyfin:10.11.11 --package <package.zip> \
#     --without-the-projection
#   .github/scripts/drive-the-whole-loop-on-a-line.sh --prove-it-bites

set -euo pipefail

manifest="build.yaml"
image=""
package=""
port="18098"
prove="no"
without_projection="no"
collect_into=""

# The shape the near miss compares by name, for the reason the header gives.
NO_PLAYLIST="[no-playlist]"

# This harness authenticates as a user it creates. The password is written here
# because the server it is given to lives for the length of one job on a loopback
# port and is destroyed at the end of it; nothing this value protects outlives the
# run.
admin="loop-administrator"
secret="4d1e77a0-loop-harness"
client='MediaBrowser Client="loop-harness", Device="ci", DeviceId="loop-harness", Version="1.0.0.0"'

# The one film the loop is driven with. The year is part of the directory and the
# file name because that is the shape the server's own movie resolver reads.
film="Loop Probe (2020)"

usage() {
  echo "Usage: drive-the-whole-loop-on-a-line.sh --image <image> --package <package.zip> [--port <port>] [--collect <directory>] [--without-the-projection]" >&2
  echo "       drive-the-whole-loop-on-a-line.sh --prove-it-bites" >&2
  exit 2
}

while [ "$#" -gt 0 ]; do
  case "$1" in
    --manifest) manifest="$2"; shift 2 ;;
    --image) image="$2"; shift 2 ;;
    --package) package="$2"; shift 2 ;;
    --port) port="$2"; shift 2 ;;
    --collect) collect_into="$2"; shift 2 ;;
    --without-the-projection) without_projection="yes"; shift ;;
    --prove-it-bites) prove="yes"; shift ;;
    *)
      echo "Unknown argument: $1" >&2
      usage
      ;;
  esac
done

# Reads one scalar written at column zero of the manifest, refusing an absent key
# rather than falling back to a default, for the reason the boot harness beside
# this one gives: a run asserting against the wrong identity is worse than one
# that did not run.
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

# Compares two identifiers written in either of the forms a server returns: the
# hyphenated form and the bare hexadecimal one the API hands back.
same_identifier() {
  local left right

  left="$(printf '%s' "$1" | tr -d '-' | tr '[:upper:]' '[:lower:]')"
  right="$(printf '%s' "$2" | tr -d '-' | tr '[:upper:]' '[:lower:]')"

  [ "${left}" = "${right}" ]
}

# Judges a client's playlist listing. Prints one word or one identifier and
# nothing else, so the probe can run it over fabricated answers with no server.
#
#   unreadable      the answer is not the item envelope a client receives
#   absent          no playlist carries the configured name
#   <identifier>    the identifier of the playlist carrying that name
playlist_named() {
  local listing="$1" wanted="$2" found

  if ! found="$(printf '%s' "${listing}" \
    | jq -er --arg wanted "${wanted}" '
        if (type == "object" and ((.Items? // .items?) != null)) then
          ((.Items // .items)
            | map(select(((.Name // .name) // "") == $wanted))
            | if length == 0 then "absent" else (.[0].Id // .[0].id // "absent") end)
        else
          empty
        end' 2>/dev/null)"; then
    printf 'unreadable'
    return 0
  fi

  printf '%s' "${found}"
}

# Judges a playlist's own contents route. Prints one word or the row identifier
# the removal route takes, and nothing else.
#
#   unreadable      the answer is not the item envelope a client receives, or the
#                   row carries no identifier a client could remove it by
#   absent          no row carries this item
#   <identifier>    the PlaylistItemId of the row carrying it, which is what a
#                   client removes by and is NOT the item's own identifier
row_holding() {
  local contents="$1" item="$2" rows count index id row

  if ! rows="$(printf '%s' "${contents}" \
    | jq -er 'if (type == "object" and ((.Items? // .items?) != null)) then (.Items // .items) else empty end' 2>/dev/null)"; then
    printf 'unreadable'
    return 0
  fi

  count="$(printf '%s' "${rows}" | jq -r 'length')"
  index=0

  while [ "${index}" -lt "${count}" ]; do
    id="$(printf '%s' "${rows}" | jq -r ".[${index}] | (.Id // .id // \"\")")"

    if same_identifier "${id}" "${item}"; then
      row="$(printf '%s' "${rows}" | jq -r ".[${index}] | (.PlaylistItemId // .playlistItemId // \"\")")"

      if [ -z "${row}" ]; then
        printf 'unreadable'
      else
        printf '%s' "${row}"
      fi

      return 0
    fi

    index=$((index + 1))
  done

  printf 'absent'
}

# Judges this plugin's own reading of the stored list.
#
#   unreadable      the answer is not the array the endpoint returns
#   absent          no entry carries this item
#   held            an entry carries it
store_holds() {
  local listing="$1" item="$2" count index id

  if ! count="$(printf '%s' "${listing}" | jq -er 'if type == "array" then length else empty end' 2>/dev/null)"; then
    printf 'unreadable'
    return 0
  fi

  index=0
  while [ "${index}" -lt "${count}" ]; do
    id="$(printf '%s' "${listing}" | jq -r ".[${index}] | (.ItemId // .itemId // \"\")")"

    if same_identifier "${id}" "${item}"; then
      printf 'held'
      return 0
    fi

    index=$((index + 1))
  done

  printf 'absent'
}

# Judges the server's scheduled task listing, which is how this harness finds the
# task to start. Prints one word or the task identifier.
#
#   unreadable      the answer is not a JSON array
#   absent          no task carries this plugin's key
#   <identifier>    the identifier the run route takes
task_keyed() {
  local listing="$1" key="$2" found

  if ! found="$(printf '%s' "${listing}" \
    | jq -er --arg key "${key}" '
        if type == "array" then
          (map(select(((.Key // .key) // "") == $key))
            | if length == 0 then "absent" else (.[0].Id // .[0].id // "absent") end)
        else
          empty
        end' 2>/dev/null)"; then
    printf 'unreadable'
    return 0
  fi

  printf '%s' "${found}"
}

# The key the plugin declares for its task. It is read out of the source rather
# than typed twice, so a rename that moved it cannot leave this harness looking
# for a task no server registers.
task_key() {
  sed -n 's/.*public string Key => "\(.*\)";/\1/p' \
    Jellyfin.Plugin.Watchlist/Projection/WatchlistReconciliationTask.cs | head -n 1
}

if [ "${prove}" = "yes" ]; then
  list_name="$(sed -n 's/.*DefaultProjectedListName = "\(.*\)";/\1/p' \
    Jellyfin.Plugin.Watchlist/Configuration/PluginConfiguration.cs | head -n 1)"

  refuse_probe() { echo "$1" >&2; exit 1; }

  [ -n "${list_name}" ] \
    || refuse_probe "The default projected list name could not be read out of the configuration, so the probes below would judge against a name nothing declares."

  item="6f9619ff-8b86-d011-b42d-00c04fc964ff"
  other="7f9619ff-8b86-d011-b42d-00c04fc964ff"
  playlist="11112222-3333-4444-5555-666677778888"
  row="aaaabbbb-cccc-dddd-eeee-ffff00001111"

  # The playlist judgement finds the configured name and refuses everything a
  # server that never projected would answer.
  present="{\"Items\":[{\"Id\":\"${playlist}\",\"Name\":\"${list_name}\"}],\"TotalRecordCount\":1}"
  [ "$(playlist_named "${present}" "${list_name}")" = "${playlist}" ] \
    || refuse_probe "The playlist judgement did not find a playlist carrying the configured name, so assertion 3 could never pass."
  [ "$(playlist_named '{"Items":[],"TotalRecordCount":0}' "${list_name}")" = "absent" ] \
    || refuse_probe "The playlist judgement read an empty listing as a hit, so assertion 3 would pass on a server that projected nothing."
  # One character of the name moved, which is the mistake a rename makes.
  [ "$(playlist_named "${present}" "${list_name} ")" = "absent" ] \
    || refuse_probe "The playlist judgement accepted a name one character away from the configured one, so assertion 3 would pass for somebody else's playlist."
  [ "$(playlist_named 'not json at all' "${list_name}")" = "unreadable" ] \
    || refuse_probe "The playlist judgement read an unreadable answer as a listing, so an unread server and one holding no playlist would look the same."

  # The contents judgement returns the row identifier a client removes by, and
  # never the item's own identifier, which is the mistake that makes the removal
  # answer nothing and leaves the loop green until assertion 5.
  holding="{\"Items\":[{\"Id\":\"${item}\",\"PlaylistItemId\":\"${row}\",\"Name\":\"Loop Probe\"}],\"TotalRecordCount\":1}"
  [ "$(row_holding "${holding}" "${item}")" = "${row}" ] \
    || refuse_probe "The contents judgement did not return the row identifier for an item the playlist holds, so the removal in assertion 5 would name nothing."
  [ "$(row_holding "${holding}" "${other}")" = "absent" ] \
    || refuse_probe "The contents judgement found a row for an item the playlist does not hold, so assertion 3 would pass for the wrong film."
  [ "$(row_holding '{"Items":[],"TotalRecordCount":0}' "${item}")" = "absent" ] \
    || refuse_probe "The contents judgement read an empty playlist as holding the item, so assertion 3 would pass on a playlist the projection never filled."
  [ "$(row_holding "${holding//PlaylistItemId/playlistEntryId}" "${item}")" = "unreadable" ] \
    || refuse_probe "The contents judgement accepted a row carrying no identifier a client could remove it by, so assertion 5 would remove nothing and read that as a pass."
  [ "$(row_holding 'not json at all' "${item}")" = "unreadable" ] \
    || refuse_probe "The contents judgement read an unreadable answer as a playlist, so an unread server and an empty playlist would look the same."

  # The store judgement is what assertion 1 and assertion 5 rest on, in opposite
  # directions, so it is proved in both.
  stored="[{\"ItemId\":\"${item}\",\"Kind\":\"Movie\",\"Name\":\"Loop Probe\"}]"
  [ "$(store_holds "${stored}" "${item}")" = "held" ] \
    || refuse_probe "The store judgement did not find an entry the list holds, so assertion 1 could never pass."
  [ "$(store_holds "${stored}" "${other}")" = "absent" ] \
    || refuse_probe "The store judgement found an entry for an item the list does not hold, so assertion 1 would pass for the wrong film."
  [ "$(store_holds '[]' "${item}")" = "absent" ] \
    || refuse_probe "The store judgement read an empty list as holding the item, so assertion 1 would pass on a server where the add did nothing."
  [ "$(store_holds 'not json at all' "${item}")" = "unreadable" ] \
    || refuse_probe "The store judgement read an unreadable answer as a list, so assertion 5 would take an unread server for an emptied list."

  # The task lookup is what makes the projection run at all. A harness that could
  # not find the task would read a server that was never asked to project, and
  # report the projection broken.
  key="$(task_key)"
  [ -n "${key}" ] \
    || refuse_probe "The scheduled task key could not be read out of the task, so the probes below would judge against a key nothing declares."

  tasks="[{\"Id\":\"abc123\",\"Key\":\"${key}\",\"Name\":\"Reconcile watchlist playlists\",\"State\":\"Idle\"}]"
  [ "$(task_keyed "${tasks}" "${key}")" = "abc123" ] \
    || refuse_probe "The task lookup did not find the task this plugin declares, so no run of it could ever be started."
  [ "$(task_keyed "${tasks}" "${key}X")" = "absent" ] \
    || refuse_probe "The task lookup accepted a key one character away from this plugin's, so it would start somebody else's task and read the result as this plugin's."
  [ "$(task_keyed '[]' "${key}")" = "absent" ] \
    || refuse_probe "The task lookup read an empty task list as a hit, so a server that never registered the task would look like one that did."
  [ "$(task_keyed 'not json at all' "${key}")" = "unreadable" ] \
    || refuse_probe "The task lookup read an unreadable answer as a task list, so an unread server and one without the task would look the same."

  echo "The playlist judgement finds the configured name, and refuses an empty listing, a name one character away and an unreadable answer."
  echo "The contents judgement returns the row identifier a client removes by, and refuses a row carrying none, another film and an empty playlist."
  echo "The store judgement finds a held entry, and refuses another film, an emptied list and an unreadable answer."
  echo "The task lookup finds the key this plugin declares, and refuses a key one character away, an empty list and an unreadable answer."
  exit 0
fi

if [ -z "${image}" ] || [ -z "${package}" ]; then
  usage
fi

for tool in docker unzip curl jq; do
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
version="$(scalar_of version)"
key="$(task_key)"

[ -n "${key}" ] \
  || { echo "The scheduled task key could not be read out of the task, so this harness has no task to start." >&2; exit 1; }

echo "Container runtime: ${runtime}"
echo "Plugin:            ${name} ${version} ${guid}"
echo "Image:             ${image}"
echo "Package:           ${package}"
echo "Task key:          ${key}"
if [ "${without_projection}" = "yes" ]; then
  echo "Mode:              the near miss, with the projection turned off"
fi

root="$(mktemp -d)"
config="${root}/config"
plugins="${config}/plugins"
media="${root}/media/Movies/${film}"
transcript="${root}/transcript.txt"
container="loop-harness-$$"
base="http://127.0.0.1:${port}"

mkdir -p "${plugins}" "${root}/cache" "${media}"
: > "${transcript}"
# The server writes into the mounted media directory as the user inside the
# container, which is not the user running this.
chmod -R 0777 "${root}/media"

into="${plugins}/${name// /}_${version}"
mkdir -p "${into}"
unzip -q -o "${package}" -d "${into}"

unpacked="$(find "${into}" -mindepth 1 -maxdepth 1 | wc -l)"
echo "Unpacked ${unpacked} entr(ies) into the server's plugin directory: $(find "${into}" -mindepth 1 -maxdepth 1 -printf '%f ' 2>/dev/null || true)"

if [ "${unpacked}" -eq 0 ]; then
  echo "The package unpacked to nothing, so the run below would drive a server with no plugin and would report the loop broken for the packaging's reason." >&2
  exit 1
fi

# The container and the temporary directory go whichever way the run ends.
# Removing them can fail on a permission error, because the server writes into
# the mounted directories as the user inside the container. That is tidying
# rather than a verdict.
#
# IT IS ALSO WHERE THE COLLECTION HAPPENS, AND THAT IS WHY IT IS NOT AT THE END OF
# THE RUN. A failing run leaves through a refusal rather than through the last
# line of this file, so anything written after the assertions is written only on
# the runs that did not need it. The trap fires on every path, and it is the only
# place that can say that.
collect() {
  [ -n "${collect_into}" ] || return 0

  mkdir -p "${collect_into}" || return 0
  docker logs "${container}" > "${collect_into}/server.log" 2>&1 || true
  cp "${transcript}" "${collect_into}/transcript.txt" 2>/dev/null || true

  echo "Collected the server log and the request transcript into ${collect_into}."
}

cleanup() {
  collect
  docker rm --force "${container}" >/dev/null 2>&1 || true
  rm -rf "${root}" 2>/dev/null || echo "Left behind ${root}."
}
trap cleanup EXIT

# Every refusal goes through here, so a red run carries the transcript and the log
# without anybody re-running it. That is the fifth done-condition of #52, and it is
# one function rather than a habit at each call site.
#
# IT IS ALSO WHERE THE NEAR MISS IS DECIDED, AND IT COMPARES THE SHAPE BY NAME. A
# near-miss run that ended non-zero anywhere else - a container that never came
# up, a library that never scanned, a token that was never issued - asserted
# nothing about the projection, and a step reading only the exit status would
# record that as a proof.
refuse() {
  if [ "${without_projection}" = "yes" ]; then
    case "$1" in
      "${NO_PLAYLIST}"*)
        echo ""
        echo "$1"
        echo ""
        echo "The harness refuses a server whose projection is off, for the playlist never appearing. Assertion 3 bites."
        exit 0
        ;;
    esac
  fi

  echo "" >&2
  echo "$1" >&2
  echo "" >&2
  echo "--- the calls this harness made, in order ---" >&2
  cat "${transcript}" >&2
  echo "" >&2
  echo "--- the last 200 lines the server logged ---" >&2
  docker logs "${container}" 2>&1 | tail -n 200 >&2
  exit 1
}

# Calls the server. Prints the body on standard output and the status code on the
# last line, and records the route and the status in the transcript the refusal
# above prints.
call() {
  local route="$1" method="${2:-GET}" body="${3:-}" token="${4:-}"
  local authorization="${client}" answer

  if [ -n "${token}" ]; then
    authorization="${client}, Token=\"${token}\""
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

  printf '%s %s -> %s\n' "${method}" "${route}" "$(printf '%s' "${answer}" | tail -n 1)" >> "${transcript}"
  printf '%s' "${answer}"
}

status_of() { printf '%s' "$1" | tail -n 1; }
body_of() { printf '%s' "$1" | sed '$d'; }

# Waits for a route to answer 200. Two routes are waited on rather than one, for
# the reason the boot harness beside this one states: the public route says the
# server is listening, the wizard route says it is ready.
wait_for() {
  local route="$1" seconds="$2" deadline answer status
  deadline=$(( $(date +%s) + seconds ))

  while [ "$(date +%s)" -lt "${deadline}" ]; do
    if answer="$(call "${route}" 2>/dev/null)"; then
      status="$(status_of "${answer}")"

      if [ "${status}" = "200" ]; then
        echo "${route} answers: $(body_of "${answer}" | head -c 200)"
        return 0
      fi
    fi

    sleep 2
  done

  refuse "The server did not answer ${base}${route} with 200 within ${seconds}s."
}

docker run --detach --name "${container}" \
  --publish "127.0.0.1:${port}:8096" \
  --volume "${config}:/config" \
  --volume "${root}/cache:/cache" \
  --volume "${root}/media:/media" \
  "${image}" >/dev/null

wait_for "/System/Info/Public" 240
wait_for "/Startup/Configuration" 240

echo "Completing first-time setup, so the loop below runs as a real user rather than against the wizard's own policy."

# THE GET ON /Startup/User IS NOT A READ. It is what creates the first user, and
# posting without it answers 404. The server's own source is the authority:
#
#     gh api "repos/jellyfin/jellyfin/contents/Jellyfin.Api/Controllers/StartupController.cs?ref=v10.11.11" \
#       -H "Accept: application/vnd.github.raw" | grep -n 'GetFirstUser\|UpdateStartupUser'
created="$(call "/Startup/User")"
[ "$(status_of "${created}")" = "200" ] \
  || refuse "The server did not create a first user: /Startup/User answered $(status_of "${created}")."

post_setup() {
  local route="$1" body="$2" answer status

  answer="$(call "${route}" POST "${body}")"
  status="$(status_of "${answer}")"

  [ "${status}" -lt 400 ] \
    || refuse "First-time setup failed at ${route} with status ${status}: $(body_of "${answer}" | head -c 400)"
}

post_setup "/Startup/Configuration" '{"UICulture":"en-US","MetadataCountryCode":"US","PreferredMetadataLanguage":"en"}'
post_setup "/Startup/User" "$(jq -n --arg n "${admin}" --arg p "${secret}" '{Name:$n,Password:$p}')"
post_setup "/Startup/RemoteAccess" '{"EnableRemoteAccess":true,"EnableAutomaticPortMapping":false}'
post_setup "/Startup/Complete" '{}'

authenticated="$(call "/Users/AuthenticateByName" POST "$(jq -n --arg u "${admin}" --arg p "${secret}" '{Username:$u,Pw:$p}')")"
[ "$(status_of "${authenticated}")" = "200" ] \
  || refuse "Authenticating the administrator this harness created returned $(status_of "${authenticated}")."

token="$(body_of "${authenticated}" | jq -r '.AccessToken // .accessToken // ""')"
user="$(body_of "${authenticated}" | jq -r '.User.Id // .user.id // ""')"
[ -n "${token}" ] && [ -n "${user}" ] \
  || refuse "The authentication response carried no access token or no user identifier, so every step below would run as nobody and pass for the wrong reason."

echo "Administrator ${user} authenticated."

# The plugin's settings, read from its own configuration route rather than written
# blind. The near-miss mode turns the projection off through the same route a
# person uses, which is what makes its refusal a statement about the projection
# rather than about a harness pointed somewhere else.
settings="$(call "/Plugins/${guid}/Configuration" GET "" "${token}")"
[ "$(status_of "${settings}")" = "200" ] \
  || refuse "This plugin's configuration route answered $(status_of "${settings}") to an administrator, so its settings could not be read and the projection's state is unknown."

list_name="$(body_of "${settings}" | jq -r '.ProjectedListName // .projectedListName // ""')"
[ -n "${list_name}" ] \
  || refuse "This plugin's configuration declares no projected list name, so the playlist below could not be looked for by name."

echo "Projected list name, read from the running server: ${list_name}"

if [ "${without_projection}" = "yes" ]; then
  turned_off="$(body_of "${settings}" | jq -c '.ProjectionEnabled = false')"
  answer="$(call "/Plugins/${guid}/Configuration" POST "${turned_off}" "${token}")"
  [ "$(status_of "${answer}")" -lt 400 ] \
    || refuse "Turning the projection off through this plugin's configuration route answered $(status_of "${answer}"), so the near miss could not be arranged."

  # `//` in jq takes its right-hand side when the left is null OR FALSE, so the
  # spelling `.ProjectionEnabled // .projectionEnabled` reads a setting that IS
  # off as the other spelling and then as null. That is the one place in this
  # file where the value being read is a boolean, and it is why the pair is
  # written out rather than defaulted through.
  again="$(call "/Plugins/${guid}/Configuration" GET "" "${token}")"
  state="$(body_of "${again}" | jq -r '[.ProjectionEnabled, .projectionEnabled] | map(select(. != null)) | if length == 0 then "absent" else (.[0] | tostring) end')"

  [ "${state}" = "false" ] \
    || refuse "The projection reads as ${state} after this harness turned it off, so the near miss would be the run that matters under another name."

  echo "The projection is off. Everything below is the same loop, and it must fail at the playlist."
fi

echo "Generating one film inside the container, because a stock server has no media and the add endpoint refuses an item the library cannot answer for."

ffmpeg=""
for candidate in /usr/lib/jellyfin-ffmpeg/ffmpeg /usr/bin/ffmpeg ffmpeg; do
  if docker exec "${container}" "${candidate}" -version >/dev/null 2>&1; then
    ffmpeg="${candidate}"
    break
  fi
done

[ -n "${ffmpeg}" ] \
  || refuse "No encoder answered inside ${image}. This harness generates its one film with the server's own rather than requiring one on the host, and there is nothing here to generate it with."

docker exec "${container}" "${ffmpeg}" -nostdin -loglevel error -y \
  -f lavfi -i "color=c=black:s=64x64:d=1:r=5" \
  -c:v libx264 -pix_fmt yuv420p -t 1 \
  "/media/Movies/${film}/${film}.mkv" \
  || refuse "The film could not be generated inside the container, so there is nothing for the library to hold and the loop cannot start."

docker exec "${container}" ls -l "/media/Movies/${film}/" || true

echo "Adding the library, and waiting for the scan rather than sleeping."

added="$(call "/Library/VirtualFolders?name=Movies&collectionType=movies&paths=%2Fmedia%2FMovies&refreshLibrary=true" POST '{}' "${token}")"
[ "$(status_of "${added}")" -lt 400 ] \
  || refuse "Adding the movie library answered $(status_of "${added}"): $(body_of "${added}" | head -c 400)"

item=""
deadline=$(( $(date +%s) + 300 ))
while [ "$(date +%s)" -lt "${deadline}" ]; do
  found="$(call "/Items?userId=${user}&recursive=true&includeItemTypes=Movie" GET "" "${token}")"

  if [ "$(status_of "${found}")" = "200" ]; then
    item="$(body_of "${found}" | jq -r '(((.Items // .items) // []) | if length == 0 then "" else (.[0].Id // .[0].id // "") end)')"
    [ -n "${item}" ] && break
  fi

  sleep 3
done

[ -n "${item}" ] \
  || refuse "The library scan produced no film within 300s, so there is no item to put on a list and nothing below could run."

echo "The library holds the film as ${item}."

# 1. An item added through this plugin's own endpoint is on the stored list.
put="$(call "/Watchlist/Items/${item}" POST "" "${token}")"
[ "$(status_of "${put}")" = "204" ] \
  || refuse "1. POST /Watchlist/Items/${item} answered $(status_of "${put}"), and 204 is what an accepted add answers: $(body_of "${put}" | head -c 400)"

stored="$(call "/Watchlist/Items" GET "" "${token}")"
[ "$(status_of "${stored}")" = "200" ] \
  || refuse "1. GET /Watchlist/Items answered $(status_of "${stored}"), so whether the add was stored cannot be read at all."

case "$(store_holds "$(body_of "${stored}")" "${item}")" in
  held) echo "1. The stored list holds the film the endpoint was given." ;;
  absent) refuse "1. The add answered 204 and the stored list does not hold the film, so nothing was written." ;;
  unreadable) refuse "1. The stored list is not the array this endpoint returns, so whether the add was stored cannot be read at all." ;;
esac

# 2. Before the projection has run there is no playlist carrying the configured
#    name. This is assertion 3's one-change neighbour, read one step before it.
playlists="$(call "/Items?userId=${user}&recursive=true&includeItemTypes=Playlist" GET "" "${token}")"
[ "$(status_of "${playlists}")" = "200" ] \
  || refuse "2. The client's playlist query answered $(status_of "${playlists}"), so what the server held before the projection cannot be read."

before="$(playlist_named "$(body_of "${playlists}")" "${list_name}")"
[ "${before}" = "absent" ] \
  || refuse "2. A playlist named ${list_name} already existed before the projection ran (${before}), so assertion 3 below would pass with the projection having done nothing."

echo "2. No playlist carries that name before the projection runs."

tasks="$(call "/ScheduledTasks" GET "" "${token}")"
[ "$(status_of "${tasks}")" = "200" ] \
  || refuse "The scheduled task listing answered $(status_of "${tasks}"), so the projection could not be started."

task="$(task_keyed "$(body_of "${tasks}")" "${key}")"
case "${task}" in
  absent) refuse "The running server registers no scheduled task with key ${key}, so this plugin's projection is driven by nothing a server would run. The server lists: $(body_of "${tasks}" | jq -r 'map(.Key // .key) | join("; ")' 2>/dev/null || echo unreadable)" ;;
  unreadable) refuse "The scheduled task listing is not a JSON array, so the projection could not be started." ;;
esac

echo "The server registers this plugin's task as ${task}."

# Runs the task and waits for the server to report it finished, rather than
# sleeping. A sleep is a race that passes on a fast runner.
run_the_task() {
  local started deadline state answer

  started="$(call "/ScheduledTasks/Running/${task}" POST "" "${token}")"
  [ "$(status_of "${started}")" -lt 400 ] \
    || refuse "Starting this plugin's task answered $(status_of "${started}"), so nothing projected and every assertion below would be about a server that was never asked."

  # The server reports Idle until it has picked the run up, so a poll that read
  # the state immediately would see the state it had before the start.
  sleep 3

  deadline=$(( $(date +%s) + 180 ))
  while [ "$(date +%s)" -lt "${deadline}" ]; do
    answer="$(call "/ScheduledTasks/${task}" GET "" "${token}")"

    if [ "$(status_of "${answer}")" = "200" ]; then
      state="$(body_of "${answer}" | jq -r '.State // .state // ""')"

      if [ "${state}" = "Idle" ]; then
        echo "   The task is idle again; its last run says $(body_of "${answer}" | jq -r '.LastExecutionResult.Status // .lastExecutionResult.status // "nothing"')."
        return 0
      fi
    fi

    sleep 2
  done

  refuse "This plugin's task did not return to idle within 180s, so what the projection wrote cannot be read as finished."
}

echo "Running this plugin's projection task."
run_the_task

# 3. A playlist carrying the configured name exists and holds the item, read with
#    the two queries a client issues.
playlists="$(call "/Items?userId=${user}&recursive=true&includeItemTypes=Playlist" GET "" "${token}")"
[ "$(status_of "${playlists}")" = "200" ] \
  || refuse "3. The client's playlist query answered $(status_of "${playlists}"), so whether a client would see the list cannot be read."

projected="$(playlist_named "$(body_of "${playlists}")" "${list_name}")"
case "${projected}" in
  absent) refuse "${NO_PLAYLIST} 3. After the projection ran, no playlist named ${list_name} is visible to the client query that lists playlists. The server lists: $(body_of "${playlists}" | jq -r '((.Items // .items) // []) | map(.Name // .name) | join("; ")' 2>/dev/null || echo unreadable)" ;;
  unreadable) refuse "3. The client's playlist listing is not the envelope a client receives, so whether a client would see the list cannot be read." ;;
esac

echo "3. The client's playlist query sees ${list_name} as ${projected}."

contents="$(call "/Playlists/${projected}/Items?userId=${user}" GET "" "${token}")"
[ "$(status_of "${contents}")" = "200" ] \
  || refuse "3. The playlist's own contents route answered $(status_of "${contents}"), so what a client would see inside the list cannot be read."

row="$(row_holding "$(body_of "${contents}")" "${item}")"
case "${row}" in
  absent) refuse "3. The projected playlist exists and does not hold the film. The client sees: $(body_of "${contents}" | jq -r '((.Items // .items) // []) | map(.Name // .name) | join("; ")' 2>/dev/null || echo unreadable)" ;;
  unreadable) refuse "3. The playlist's contents are not the envelope a client receives, or the row carries no identifier a client could remove it by." ;;
esac

echo "3. The playlist holds the film, as row ${row}."

# 4. The stored list still holds the item. Assertion 5's one-change neighbour.
stored="$(call "/Watchlist/Items" GET "" "${token}")"
[ "$(store_holds "$(body_of "${stored}")" "${item}")" = "held" ] \
  || refuse "4. The projection emptied the stored list, so assertion 5 below would pass with no client having removed anything."

echo "4. The stored list still holds the film after the projection."

# 5. The row is removed the way a client removes one, and the stored list drops
#    the entry on the next run.
removed="$(call "/Playlists/${projected}/Items?entryIds=${row}" DELETE "" "${token}")"
[ "$(status_of "${removed}")" -lt 400 ] \
  || refuse "5. Removing the row the way a client removes one answered $(status_of "${removed}"): $(body_of "${removed}" | head -c 400)"

contents="$(call "/Playlists/${projected}/Items?userId=${user}" GET "" "${token}")"
[ "$(row_holding "$(body_of "${contents}")" "${item}")" = "absent" ] \
  || refuse "5. The row is still in the playlist after the removal, so nothing was taken away and the store has nothing to notice."

echo "5. The client's removal took the row out of the playlist."

echo "Running this plugin's projection task again, which is what takes the client's edit."
run_the_task

stored="$(call "/Watchlist/Items" GET "" "${token}")"
[ "$(status_of "${stored}")" = "200" ] \
  || refuse "5. GET /Watchlist/Items answered $(status_of "${stored}"), so whether the store dropped the entry cannot be read at all."

case "$(store_holds "$(body_of "${stored}")" "${item}")" in
  absent) echo "5. The stored list dropped the entry the client removed." ;;
  held) refuse "5. The client removed the row and the stored list still holds the entry, so a removal made on a client does not reach the store." ;;
  unreadable) refuse "5. The stored list is not the array this endpoint returns, so whether the store dropped the entry cannot be read at all." ;;
esac

if [ "${without_projection}" = "yes" ]; then
  echo "" >&2
  echo "The projection was off and the whole loop passed anyway, so nothing in this harness is watching the projection and a green run of it would prove nothing." >&2
  exit 1
fi

echo ""
echo "The packaged plugin drives the whole loop on ${image}: an item added through the endpoint reaches the stored list, a run of this plugin's task puts it in a playlist a client can see, and a removal made the way a client makes one is taken back into the store."
