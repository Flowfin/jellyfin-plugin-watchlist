#!/usr/bin/env bash
#
# Reads a running server and reports the ways two plugins on it collide (#104).
#
# WHY A SCAN AND NOT AN ARGUMENT. Two plugins collide in a small number of
# concrete ways, and every one of them is enumerable from a running server. This
# reads what the server reports about itself, so it says the same thing about any
# set of plugins and gains value as the set grows rather than having to be
# rewritten for each new sibling.
#
# WHAT IT REPORTS IS A LIST AND NEVER THE FIRST ENTRY. Somebody clearing
# collisions wants the whole set; a scan that stopped at the first would turn one
# afternoon into as many afternoons as there are collisions. The same reason the
# vulnerable dependency listing exists beside the restore audit on this board.
#
# THE KINDS IT REPORTS, and where each one is read from. They are not counted
# here, because a count in a comment drifts against the code that decides it, and
# the probe below prints the number it actually ran.
#
#   plugin-identifier            Two loaded plugins claiming one identifier. The
#                                server keys a plugin's configuration and its
#                                dashboard entry on this.
#   plugin-configuration-file    Two loaded plugins whose configuration file
#                                names collide. Both are written into the
#                                server's one configurations directory:
#
#                                  git show v10.11.11:Emby.Server.Implementations/AppBase/BaseApplicationPaths.cs | grep -n 'PluginConfigurationsPath'
#
#                                so two plugins reporting one name overwrite each
#                                other's settings with no error anywhere.
#   plugin-data-folder           Two loaded plugins whose installed directory
#                                under the server's plugin path is one directory.
#                                DERIVED rather than reported: no route on either
#                                supported line returns a loaded plugin's
#                                directory on disk, so it is built from the name
#                                and the version, which is the pair the server's
#                                installer builds it from. A plugin placed on disk
#                                by hand can sit in a directory that pair does not
#                                predict, and this kind cannot see that one. The
#                                entry says `derived` so a reader is not left to
#                                assume otherwise.
#   scheduled-task-key           Two tasks the server keys the same.
#   scheduled-task-name          Two tasks the dashboard cannot tell apart.
#   route                        Two paths in the server's OpenAPI document that
#                                the router answers as one. A JSON object cannot
#                                hold a duplicate key, so a literal repeat is not
#                                a thing a document can show. What it can show is
#                                two keys ASP.NET routing treats as the same
#                                route, which is a pair differing only in case or
#                                in a trailing slash.
#   identifier-not-the-manifest  A plugin the server lists under THIS board's name
#                                but not under the identifier `build.yaml`
#                                declares. This is the one kind that is not about
#                                two plugins, and it is here because a package on
#                                the server that is not the package this tree
#                                describes makes every other verdict a verdict
#                                about something else.
#
# WHAT IT DOES NOT COVER, said here rather than left to be discovered.
#
#   The identifier-against-manifest arm is checked for THIS plugin only. The
#   server reports no manifest, and this repository holds one, its own, so a
#   sibling shipping an identifier its own manifest does not declare is a
#   collision this scan is blind to.
#
#   It reads what the server reports. Two plugins that fight over a file, a
#   database row or a setting they both write are not in any of the lists below,
#   and no reading of those lists would find them.
#
#   A route this scan reports is a pair of paths in one document. Which plugin
#   claims which path is not in that document, so the report names the paths and
#   not the parties.
#
# AN UNREAD LIST AND A CLEAN ONE MUST NOT LOOK THE SAME. A route that answers
# anything but 200, and an answer that is not the shape this expects, END the run
# rather than being treated as an empty list. A scan that reported nothing would
# pass a colliding server exactly like one that is holding.
#
# WHAT PROVES IT BITES. `--prove-it-bites` runs every kind over a fabricated
# report that must produce exactly that collision and over a one-change
# neighbour of it that must produce none. It starts no container. The neighbour
# is the one-character difference somebody actually makes, not a report with
# nothing in it.
#
# THE MEANS. Bash with curl and jq, the same as the other gate scripts here and
# the same as the harness this runs beside. It adds no toolchain.
#
# It needs no display, no elevated rights and no machine trust store.
#
# Usage:
#
#   .github/scripts/scan-a-server-for-collisions.sh --base http://127.0.0.1:18096 --token <token>
#   .github/scripts/scan-a-server-for-collisions.sh --prove-it-bites

set -euo pipefail

manifest="build.yaml"
base=""
token=""
prove="no"

usage() {
  echo "Usage: scan-a-server-for-collisions.sh --base <address> --token <token>" >&2
  echo "       scan-a-server-for-collisions.sh --prove-it-bites" >&2
  exit 2
}

while [ "$#" -gt 0 ]; do
  case "$1" in
    --manifest) manifest="$2"; shift 2 ;;
    --base) base="$2"; shift 2 ;;
    --token) token="$2"; shift 2 ;;
    --prove-it-bites) prove="yes"; shift ;;
    *) echo "Unknown argument: $1" >&2; usage ;;
  esac
done

refuse() {
  echo "$1" >&2
  exit 1
}

# Reads one scalar written at column zero of the manifest. An absent key ends the
# run rather than falling back to a default, because a default would make the
# identifier arm below a statement about some other plugin.
scalar_of() {
  local key="$1" value

  value="$(sed -n "s/^${key}: *//p" "${manifest}" | head -n 1 | tr -d '\r')"
  value="${value%\"}"
  value="${value#\"}"

  [ -n "${value}" ] || refuse "${manifest} declares no ${key} at column zero. This scan refuses rather than asserting against a default."

  printf '%s' "${value}"
}

# Refuses an answer that is not a JSON array. An unreadable list and an empty one
# are opposite statements and this is where they are kept apart.
array_or_refuse() {
  local json="$1" what="$2"

  printf '%s' "${json}" | jq -e 'type == "array"' >/dev/null 2>&1 \
    || refuse "${what} is not a JSON array, so this scan cannot tell a server with no collisions from one it failed to read."
}

# Refuses an OpenAPI answer that carries no paths object.
paths_or_refuse() {
  local json="$1"

  printf '%s' "${json}" | jq -e '.paths | type == "object"' >/dev/null 2>&1 \
    || refuse "The OpenAPI answer carries no paths object, so the route kind would report a clean server without having read one."
}

# The whole judgement, as one function over three documents and one identifier,
# so the probe can run it without a server.
#
# Prints one line per collision, `<kind><tab><detail>`, and nothing where there
# are none.
collisions_in() {
  local plugins="$1" tasks="$2" openapi="$3" guid="$4" name="$5"

  array_or_refuse "${plugins}" "The plugin list"
  array_or_refuse "${tasks}" "The scheduled task list"
  paths_or_refuse "${openapi}"

  # Two loaded plugins claiming one identifier. Compared with hyphens stripped
  # and case folded, because the two spellings are the same identifier and a
  # literal comparison would miss the collision it exists to find.
  printf '%s' "${plugins}" | jq -r '
    [ .[] | (.Id // "") | ascii_downcase | gsub("-"; "") | select(. != "") ]
    | group_by(.) | map(select(length > 1))
    | .[] | "plugin-identifier\tidentifier " + .[0] + " is claimed by " + (length | tostring) + " loaded plugins"'

  # Two loaded plugins whose configuration file names collide. Case folded,
  # because the server writes both into one directory and a case-insensitive
  # filesystem answers for both.
  printf '%s' "${plugins}" | jq -r '
    [ .[] | (.ConfigurationFileName // "") | ascii_downcase | select(. != "") ]
    | group_by(.) | map(select(length > 1))
    | .[] | "plugin-configuration-file\tconfiguration file " + .[0] + " is written by " + (length | tostring) + " loaded plugins"'

  # Two loaded plugins whose installed directory is one directory. Derived from
  # the name and the version, which is the pair the installer builds it from.
  printf '%s' "${plugins}" | jq -r '
    [ .[] | ((.Name // "") | gsub(" "; "")) + "_" + (.Version // "") | ascii_downcase | select(. != "_") ]
    | group_by(.) | map(select(length > 1))
    | .[] | "plugin-data-folder\tderived directory " + .[0] + " is claimed by " + (length | tostring) + " loaded plugins"'

  # Two tasks the server keys the same, and two the dashboard cannot tell apart.
  printf '%s' "${tasks}" | jq -r '
    [ .[] | (.Key // "") | select(. != "") ]
    | group_by(.) | map(select(length > 1))
    | .[] | "scheduled-task-key\ttask key " + .[0] + " is declared by " + (length | tostring) + " tasks"'

  printf '%s' "${tasks}" | jq -r '
    [ .[] | (.Name // "") | select(. != "") ]
    | group_by(.) | map(select(length > 1))
    | .[] | "scheduled-task-name\ttask name " + .[0] + " is declared by " + (length | tostring) + " tasks"'

  # Two paths the router answers as one. Case folded and the trailing slash
  # removed, which are the two differences ASP.NET routing does not keep apart.
  printf '%s' "${openapi}" | jq -r '
    [ .paths | keys[] | { raw: ., key: (ascii_downcase | sub("/$"; "")) } ]
    | group_by(.key) | map(select(length > 1))
    | .[] | "route\tthe router answers " + ([ .[].raw ] | join(" and ")) + " as one route"'

  # This plugin listed under an identifier the manifest does not declare. Read by
  # name, because the identifier is exactly what is in doubt.
  printf '%s' "${plugins}" | jq -r --arg guid "${guid}" --arg name "${name}" '
    [ .[] | select((.Name // "") == $name) ]
    | map(select(((.Id // "") | ascii_downcase | gsub("-"; "")) != ($guid | ascii_downcase | gsub("-"; ""))))
    | .[] | "identifier-not-the-manifest\t" + $name + " is loaded as " + (.Id // "none") + " and build.yaml declares " + $guid'
}

if [ "${prove}" = "yes" ]; then
  guid="$(scalar_of guid)"
  name="$(scalar_of name)"
  other="11111111-2222-3333-4444-555555555555"

  no_tasks='[]'
  one_path='{"paths":{"/Watchlist/Items":{}}}'
  ran=0
  failures=0

  # Runs the judgement and asserts the kinds it reported. `expected` is the kinds
  # in order, space separated, and empty means the report must be clean.
  expect() {
    local what="$1" expected="$2" plugins="$3" tasks="$4" openapi="$5" got

    ran=$((ran + 1))
    got="$(collisions_in "${plugins}" "${tasks}" "${openapi}" "${guid}" "${name}" | cut -f1 | sort | tr '\n' ' ')"
    got="${got% }"

    if [ "${got}" = "${expected}" ]; then
      if [ -z "${expected}" ]; then
        printf 'clean, as it must be: %s\n' "${what}"
      else
        printf 'reported %s, as it must: %s\n' "${expected}" "${what}"
      fi
      return
    fi

    echo "WRONG VERDICT on ${what}. Expected [${expected}] and got [${got}]." >&2
    failures=$((failures + 1))
  }

  mine="{\"Id\":\"${guid}\",\"Name\":\"${name}\",\"Version\":\"0.1.0.0\",\"ConfigurationFileName\":\"Jellyfin.Plugin.Watchlist.xml\",\"Status\":\"Active\"}"
  theirs="{\"Id\":\"${other}\",\"Name\":\"Something Else\",\"Version\":\"2.0.0.0\",\"ConfigurationFileName\":\"Something.Else.xml\",\"Status\":\"Active\"}"

  expect "the pair as a healthy server reports it" "" "[${mine},${theirs}]" "${no_tasks}" "${one_path}"

  # The identifier, with the neighbour being the same value one character along.
  # A report with nothing in it would prove nothing here.
  expect "a sibling claiming this plugin's identifier" "plugin-identifier" \
    "[${mine},$(printf '%s' "${theirs}" | sed "s/${other}/${guid}/")]" "${no_tasks}" "${one_path}"
  expect "the same sibling with one character of that identifier moved" "" \
    "[${mine},$(printf '%s' "${theirs}" | sed "s/${other}/$(printf '%s' "${guid}" | sed 's/^6/7/')/")]" "${no_tasks}" "${one_path}"

  # The identifier written the other way round, which is the spelling the API
  # returns and the one a literal comparison would miss.
  expect "a sibling claiming it with the hyphens dropped" "plugin-identifier" \
    "[${mine},$(printf '%s' "${theirs}" | sed "s/${other}/$(printf '%s' "${guid}" | tr -d '-')/")]" "${no_tasks}" "${one_path}"

  expect "a sibling writing this plugin's configuration file" "plugin-configuration-file" \
    "[${mine},$(printf '%s' "${theirs}" | sed 's/Something\.Else\.xml/Jellyfin.Plugin.Watchlist.xml/')]" "${no_tasks}" "${one_path}"
  expect "the same file name with one letter changed" "" \
    "[${mine},$(printf '%s' "${theirs}" | sed 's/Something\.Else\.xml/Jellyfin.Plugin.Watchlists.xml/')]" "${no_tasks}" "${one_path}"

  # The name is folded to lower case rather than repeated, so this fixture trips
  # the directory kind ALONE. A sibling carrying this plugin's name exactly also
  # trips identifier-not-the-manifest, which is the judgement working rather than
  # a defect, and would make this probe assert two things at once.
  lower="$(printf '%s' "${name}" | tr '[:upper:]' '[:lower:]')"
  expect "a sibling installing into this plugin's directory" "plugin-data-folder" \
    "[${mine},$(printf '%s' "${theirs}" | sed "s/Something Else/${lower}/; s/2\.0\.0\.0/0.1.0.0/")]" "${no_tasks}" "${one_path}"
  expect "the same directory name one letter longer" "" \
    "[${mine},$(printf '%s' "${theirs}" | sed "s/Something Else/${lower}s/; s/2\.0\.0\.0/0.1.0.0/")]" "${no_tasks}" "${one_path}"

  expect "two tasks the server keys the same" "scheduled-task-key" \
    "[${mine}]" '[{"Key":"WatchlistReconcile","Name":"Reconcile"},{"Key":"WatchlistReconcile","Name":"Reconcile again"}]' "${one_path}"
  expect "the same pair with one key changed" "" \
    "[${mine}]" '[{"Key":"WatchlistReconcile","Name":"Reconcile"},{"Key":"WatchlistReconciles","Name":"Reconcile again"}]' "${one_path}"

  expect "two tasks the dashboard cannot tell apart" "scheduled-task-name" \
    "[${mine}]" '[{"Key":"One","Name":"Reconcile the watchlist"},{"Key":"Two","Name":"Reconcile the watchlist"}]' "${one_path}"
  expect "the same pair with one name changed" "" \
    "[${mine}]" '[{"Key":"One","Name":"Reconcile the watchlist"},{"Key":"Two","Name":"Reconcile the watchlists"}]' "${one_path}"

  expect "two paths differing only in case" "route" \
    "[${mine}]" "${no_tasks}" '{"paths":{"/Watchlist/Items":{},"/watchlist/items":{}}}'
  expect "two paths differing in a segment as well as in case" "" \
    "[${mine}]" "${no_tasks}" '{"paths":{"/Watchlist/Items":{},"/watchlist/item":{}}}'
  expect "two paths differing only in a trailing slash" "route" \
    "[${mine}]" "${no_tasks}" '{"paths":{"/Watchlist/Items":{},"/Watchlist/Items/":{}}}'

  expect "this plugin loaded under an identifier the manifest does not declare" "identifier-not-the-manifest" \
    "[$(printf '%s' "${mine}" | sed "s/${guid}/${other}/")]" "${no_tasks}" "${one_path}"
  expect "the same plugin loaded under the identifier it declares" "" \
    "[${mine}]" "${no_tasks}" "${one_path}"

  echo ""
  echo "${ran} probe(s) ran, ${failures} of them wrong."

  [ "${failures}" -eq 0 ] \
    || refuse "The judgement above does not do what this script claims it does, so no reading it takes is worth having."

  exit 0
fi

[ -n "${base}" ] || usage
[ -n "${token}" ] || usage

for tool in curl jq; do
  command -v "${tool}" >/dev/null 2>&1 \
    || refuse "This scan needs ${tool} and it is not on the path. It installs nothing."
done

guid="$(scalar_of guid)"
name="$(scalar_of name)"
client='MediaBrowser Client="collision-scan", Device="ci", DeviceId="collision-scan", Version="1.0.0.0"'

# Reads one route as an administrator. A status other than 200 ends the run:
# an unread list and a clean one must not look the same.
read_route() {
  local route="$1" answer status

  answer="$(curl --silent --show-error --write-out '\n%{http_code}' \
    --header "Authorization: ${client}, Token=\"${token}\"" \
    "${base}${route}")"
  status="$(printf '%s' "${answer}" | tail -n 1)"

  [ "${status}" = "200" ] \
    || refuse "${route} answered ${status}. This scan refuses to report a clean server it did not read."

  printf '%s' "${answer}" | sed '$d'
}

# The OpenAPI document moved between the supported lines, so both spellings are
# tried and the one that answers is the one read. Neither answering ends the run
# for the same reason a bad status does.
read_openapi() {
  local route answer status

  for route in "/api-docs/openapi.json" "/openapi.json"; do
    answer="$(curl --silent --show-error --write-out '\n%{http_code}' \
      --header "Authorization: ${client}, Token=\"${token}\"" \
      "${base}${route}")"
    status="$(printf '%s' "${answer}" | tail -n 1)"

    if [ "${status}" = "200" ]; then
      echo "OpenAPI document: ${route}" >&2
      printf '%s' "${answer}" | sed '$d'
      return 0
    fi
  done

  refuse "Neither /api-docs/openapi.json nor /openapi.json answered 200, so the route kind has nothing to read and a clean report would be a report about nothing."
}

plugins="$(read_route "/Plugins")"
tasks="$(read_route "/ScheduledTasks")"
openapi="$(read_openapi)"

array_or_refuse "${plugins}" "The plugin list"
array_or_refuse "${tasks}" "The scheduled task list"
paths_or_refuse "${openapi}"

echo "Read from ${base}:"
echo "  /Plugins:        $(printf '%s' "${plugins}" | jq 'length') loaded - $(printf '%s' "${plugins}" | jq -r 'map("\(.Name) \(.Version)") | join("; ")')"
echo "  /ScheduledTasks: $(printf '%s' "${tasks}" | jq 'length') declared"
echo "  OpenAPI paths:   $(printf '%s' "${openapi}" | jq '.paths | length')"

found="$(collisions_in "${plugins}" "${tasks}" "${openapi}" "${guid}" "${name}")"

if [ -n "${found}" ]; then
  echo "" >&2
  echo "The server holds $(printf '%s' "${found}" | wc -l) collision(s):" >&2
  printf '%s\n' "${found}" | while IFS=$'\t' read -r kind detail; do
    echo "  ${kind}: ${detail}" >&2
  done
  exit 1
fi

echo ""
echo "No collision of any kind this scan reads, on the set this server is holding."
