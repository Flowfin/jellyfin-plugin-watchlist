#!/usr/bin/env bash
#
# Boots a server on one supported line with the packaged plugin and nothing else,
# and asserts that the plugin loaded and answers (#104).
#
# WHY THIS EXISTS. Every check in this repository reads the tree, builds it,
# packages it or greps it. None of them has ever started a server, so "this
# plugin loads" has been a claim about source on every green run this board has
# produced. A package can compile, package and install cleanly and still fail at
# load time, and that failure arrives in front of a user with nothing in the gate
# having said a word.
#
# THE PACKAGED ARCHIVE IS THE SUBJECT, NOT THE BUILD OUTPUT. Copying `bin/` into
# a container proves the code works and not that the thing a user installs works.
# The two come apart at exactly the places this board already has checks for -
# the artifact list in build.yaml, the framework directory the packager reads
# from, the declared ABI - and each of those is a value the compiler never sees.
# So this takes an archive produced by the same packager and the same pin the
# release route runs, and unpacks it the way a server does.
#
# WHAT IT ASSERTS, and it is four things:
#
#   1. The startup log holds no error from this plugin.
#   2. The server lists this plugin, under the identifier build.yaml declares,
#      with status Active.
#   3. The plugin's surface answers an administrator and refuses an anonymous
#      caller. Three routes: the server's plugin list, this plugin's
#      configuration route keyed on its identifier, and `Watchlist/Items`, which
#      is this plugin's own controller and is the one of the three that says the
#      controller was registered by a real server rather than by a test host.
#   4. The running server holds no collision of any kind
#      `scan-a-server-for-collisions.sh` reads. That script carries what each
#      kind is, where it is read from and what it cannot see; it is run from here
#      rather than from a job of its own because the server, the token and the
#      whole population are already in hand at this point, and a second boot
#      would answer about a different server.
#
# WHAT IT DOES NOT ASSERT, said here rather than left to be discovered. It does
# not add an item, does not read a playlist and does not touch a library. The
# whole loop through a projection is #52 and there is no projection to drive:
#
#     git grep -c 'IPlaylistManager' -- Jellyfin.Plugin.Watchlist/ ; echo "exit=$?"
#
# It also boots this plugin ALONE. A green run means the server holds no problem
# with this plugin on it. It does not mean two plugins have been watched not
# colliding, which is the other half of #104 and is a matrix over the sibling
# boards rather than a second assertion here.
#
# WHAT PROVES IT BITES. Two probes, ahead of the run that matters, which is the
# ordering `say-which-server-lines-this-run-covers.sh` and the invariant lint in
# the suite already use:
#
#   --prove-it-bites        Runs the log scan and the listed-and-active
#                           judgement over fabricated inputs and their
#                           one-change neighbours. Starts no container.
#   --without-the-plugin    Boots the same image with an empty plugin directory.
#                           Exits zero ONLY where this refused it for being
#                           unlisted, and non-zero for every other outcome
#                           including a harness that died before it asserted
#                           anything.
#
# THE NEAR MISS READS THE SHAPE OF THE FAILURE AND NEVER THE EXIT STATUS. A step
# written as "run it and expect a non-zero exit" accepts a harness that fell over
# at first-time setup having asserted nothing, and records that as a proof. The
# shape is carried by the marker below and compared by name.
#
# THE ANONYMOUS ARM IS MEANINGLESS BEFORE FIRST-TIME SETUP IS COMPLETED. A fresh
# Jellyfin admits an unauthenticated caller to the wizard's own routes while no
# user exists. A harness that skipped the wizard would meet a server in that
# state and could read it as either arm of assertion 3. So setup is completed and
# an administrator is created before anything is asserted, and that ordering is a
# property of this harness rather than a convenience.
#
# WHAT IS NOT PROVED TO BITE is the anonymous arm of assertion 3. Making a server
# answer an anonymous caller on an administrator route means misconfiguring the
# server, and a harness that arranges the failure it then detects proves the
# arrangement. That arm is watched passing and has not been watched failing, and
# this sentence is the whole of what is claimed for it. The administrator arm is
# proved, by the near miss rather than by anything written for it: the
# configuration route and `Watchlist/Items` answer 404 to an administrator on a
# server without this package and 200 on the run with it, so both track this
# plugin's presence rather than answering the same thing either way.
#
# THE MEANS. Bash, curl and jq, which is what this repository's other gate
# scripts are written in and what its workflows already run. Node would add a
# runtime this tree does not carry for one script; a .NET test cannot be the
# means at all, because the suite here is refused the network and a process
# launch by the headless rule, and this has to start a container and speak HTTP.
# It shells out to `docker` and `unzip`: a container runtime is what the issue
# asks for, and `unzip` is already how the packaging checks read an archive.
# Neither is a dependency this tree installs.
#
# It needs no display, no elevated rights and no machine trust store. The server
# answers plain HTTP on a port bound to the loopback address, so nothing here
# trusts a certificate. It starts no daemon: a container runtime somebody else's
# session owns is not a thing to switch on in passing, and its absence ends the
# run with that sentence rather than with a verdict about the plugin.
#
# Usage:
#
#   .github/scripts/boot-a-line-with-only-this-plugin.sh \
#     --image jellyfin/jellyfin:10.11.11 --package <package.zip>
#   .github/scripts/boot-a-line-with-only-this-plugin.sh \
#     --image jellyfin/jellyfin:10.11.11 --without-the-plugin
#   .github/scripts/boot-a-line-with-only-this-plugin.sh --prove-it-bites

set -euo pipefail

manifest="build.yaml"
image=""
package=""
port="18096"
without="no"
prove="no"

# The shape the near miss compares by name. A harness that died before asserting
# anything exits non-zero without ever printing this, which is the case the
# marker exists to separate from a real refusal.
NOT_LISTED="[not-listed]"

# This harness authenticates as a user it creates. The password is written here
# because the server it is given to lives for the length of one job on a
# loopback port and is destroyed at the end of it; nothing this value protects
# outlives the run.
admin="harness-administrator"
secret="9f2c41b7-alone-harness"
client='MediaBrowser Client="alone-harness", Device="ci", DeviceId="alone-harness", Version="1.0.0.0"'

usage() {
  echo "Usage: boot-a-line-with-only-this-plugin.sh --image <image> --package <package.zip> [--port <port>]" >&2
  echo "       boot-a-line-with-only-this-plugin.sh --image <image> --without-the-plugin [--port <port>]" >&2
  echo "       boot-a-line-with-only-this-plugin.sh --prove-it-bites" >&2
  exit 2
}

while [ "$#" -gt 0 ]; do
  case "$1" in
    --manifest) manifest="$2"; shift 2 ;;
    --image) image="$2"; shift 2 ;;
    --package) package="$2"; shift 2 ;;
    --port) port="$2"; shift 2 ;;
    --without-the-plugin) without="yes"; shift ;;
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

# Reads one scalar written at column zero of the manifest.
#
# It refuses an absent key rather than falling back to a default. A default would
# make every assertion below true of some other plugin, and a run asserting
# against the wrong identity is worse than one that did not run.
scalar_of() {
  local key="$1" value

  value="$(sed -n "s/^${key}: *//p" "${manifest}" | head -n 1 | tr -d '\r')"
  value="${value%\"}"
  value="${value#\"}"

  if [ -z "${value}" ]; then
    refuse "${manifest} declares no ${key} at column zero. This harness refuses rather than asserting against a default."
  fi

  printf '%s' "${value}"
}

# Decides whether a startup log carries an error from this plugin.
#
# The scan is deliberately narrow. A server log holds errors that have nothing to
# do with any plugin - a missing hardware encoder, an unreachable metadata
# provider - and a harness that refused the whole log would report this plugin
# broken for somebody else's reason and would be switched off within a week. So
# it refuses a line only where the level is ERR or FTL and the line names this
# plugin, by the name the manifest declares, that name with its spaces removed,
# or the assembly the manifest ships.
plugin_errors_in() {
  local file="$1" name="$2" squashed assembly

  squashed="${name// /}"
  assembly="Jellyfin.Plugin.Watchlist"

  tr -d '\r' < "${file}" \
    | grep -E '\[(ERR|FTL)\]' \
    | grep -F -e "${name}" -e "${squashed}" -e "${assembly}" \
    || true
}

# Compares two identifiers written in either of the forms a server returns: the
# hyphenated form the manifest declares and the bare hexadecimal one the API
# hands back.
same_identifier() {
  local left right

  left="$(printf '%s' "$1" | tr -d '-' | tr '[:upper:]' '[:lower:]')"
  right="$(printf '%s' "$2" | tr -d '-' | tr '[:upper:]' '[:lower:]')"

  [ "${left}" = "${right}" ]
}

# Judges a plugin listing. Prints one word and nothing else, so the probe below
# can run it over fabricated listings without a server.
#
#   not-listed     no entry carries this identifier
#   inactive:<s>   the entry is there and its status is not Active
#   active         the entry is there and active
#   unreadable     the listing is not a JSON array
listed_verdict() {
  local listing="$1" guid="$2" count index id status

  if ! count="$(printf '%s' "${listing}" | jq -e 'if type == "array" then length else empty end' 2>/dev/null)"; then
    printf 'unreadable'
    return 0
  fi

  index=0
  while [ "${index}" -lt "${count}" ]; do
    id="$(printf '%s' "${listing}" | jq -r ".[${index}].Id // \"\"")"

    if same_identifier "${id}" "${guid}"; then
      status="$(printf '%s' "${listing}" | jq -r ".[${index}].Status // \"\"")"

      if [ "${status}" = "Active" ]; then
        printf 'active'
      else
        printf 'inactive:%s' "${status}"
      fi

      return 0
    fi

    index=$((index + 1))
  done

  printf 'not-listed'
}

if [ "${prove}" = "yes" ]; then
  name="$(scalar_of name)"
  guid="$(scalar_of guid)"
  probe="$(mktemp)"
  trap 'rm -f "${probe}"' EXIT

  # The log scan refuses an error naming this plugin.
  printf '%s\n' '[19:12:33] [ERR] [1] Emby.Server.Implementations.Plugins.PluginManager: Failed to load assembly Jellyfin.Plugin.Watchlist.dll' > "${probe}"
  [ "$(plugin_errors_in "${probe}" "${name}" | wc -l)" -eq 1 ] \
    || refuse "The log scan passed a line naming this plugin at error level, so assertion 1 would pass on a server where the plugin failed to load."

  # ... and passes an error naming something else, which is the one-change
  # neighbour that matters: a harness refusing the whole log reports this plugin
  # broken for somebody else's reason.
  printf '%s\n' '[19:12:33] [ERR] [1] Emby.Server.Implementations.Library.LibraryManager: Error reading a library path' > "${probe}"
  [ -z "$(plugin_errors_in "${probe}" "${name}")" ] \
    || refuse "The log scan refused a line naming something other than this plugin, so assertion 1 would report this plugin broken for somebody else's reason."

  # ... and passes a line naming this plugin below error level, which is the
  # other one-change neighbour: the level is the only difference.
  printf '%s\n' '[19:12:33] [INF] [1] Emby.Server.Implementations.Plugins.PluginManager: Loaded plugin Jellyfin.Plugin.Watchlist' > "${probe}"
  [ -z "$(plugin_errors_in "${probe}" "${name}")" ] \
    || refuse "The log scan refused a line naming this plugin below error level, so assertion 1 would refuse a server that loaded the plugin and said so."

  active="[{\"Id\":\"${guid}\",\"Name\":\"${name}\",\"Version\":\"0.1.0.0\",\"Status\":\"Active\"}]"
  # The identifier the server hands back has no hyphens. A judgement that
  # compared the two literally would report every healthy server as unlisted.
  bare="[{\"Id\":\"$(printf '%s' "${guid}" | tr -d '-')\",\"Name\":\"${name}\",\"Version\":\"0.1.0.0\",\"Status\":\"Active\"}]"
  # One character of the identifier moved, which is the mistake somebody makes
  # when a guid is retyped rather than copied.
  wrong="[{\"Id\":\"$(printf '%s' "${guid}" | sed 's/^6/7/')\",\"Name\":\"${name}\",\"Version\":\"0.1.0.0\",\"Status\":\"Active\"}]"

  [ "$(listed_verdict "${active}" "${guid}")" = "active" ] \
    || refuse "The listing judgement refused a server listing this plugin as active, so the run that matters could never pass."
  [ "$(listed_verdict "${bare}" "${guid}")" = "active" ] \
    || refuse "The listing judgement refused the unhyphenated identifier a server returns, so every healthy server would report as unlisted."
  [ "$(listed_verdict "${wrong}" "${guid}")" = "not-listed" ] \
    || refuse "The listing judgement accepted an identifier one character away from this plugin's, so assertion 2 would pass for another plugin."
  [ "$(listed_verdict '[]' "${guid}")" = "not-listed" ] \
    || refuse "The listing judgement passed an empty plugin list, so assertion 2 would pass on a server where nothing loaded."
  [ "$(listed_verdict "${active//Active/Malfunctioned}" "${guid}")" = "inactive:Malfunctioned" ] \
    || refuse "The listing judgement passed a plugin the server loaded and disabled, so assertion 2 would pass on a server that refused the package."
  [ "$(listed_verdict 'not json at all' "${guid}")" = "unreadable" ] \
    || refuse "The listing judgement read an unreadable answer as a list, so an unread server and an empty one would look the same."

  echo "The log scan refuses an error naming this plugin and passes an error from elsewhere and a line below error level."
  echo "The listing judgement refuses an empty list, a disabled plugin, an identifier one character away and an unreadable answer, and passes both spellings of this plugin's identifier."
  exit 0
fi

if [ -z "${image}" ]; then
  usage
fi

if [ "${without}" = "no" ] && [ -z "${package}" ]; then
  usage
fi

for tool in docker unzip curl jq; do
  command -v "${tool}" >/dev/null 2>&1 \
    || refuse "This harness needs ${tool} and it is not on the path. It installs nothing."
done

if ! runtime="$(docker version --format '{{.Server.Version}}' 2>&1)"; then
  echo "No container runtime answered. This harness needs one and does not start one: a daemon somebody else's session owns is not a thing to switch on in passing." >&2
  echo "${runtime}" >&2
  exit 1
fi

name="$(scalar_of name)"
guid="$(scalar_of guid)"
version="$(scalar_of version)"

echo "Container runtime: ${runtime}"
echo "Plugin:            ${name} ${version} ${guid}"
echo "Image:             ${image}"
if [ "${without}" = "yes" ]; then
  echo "Package:           none, this is the near miss"
else
  echo "Package:           ${package}"
fi

root="$(mktemp -d)"
config="${root}/config"
plugins="${config}/plugins"
container="alone-harness-$$"
base="http://127.0.0.1:${port}"

mkdir -p "${plugins}" "${root}/cache"

if [ "${without}" = "no" ]; then
  into="${plugins}/${name// /}_${version}"
  mkdir -p "${into}"
  unzip -q -o "${package}" -d "${into}"

  unpacked="$(find "${into}" -mindepth 1 -maxdepth 1 | wc -l)"
  echo "Unpacked ${unpacked} entr(ies) into the server's plugin directory: $(find "${into}" -mindepth 1 -maxdepth 1 -printf '%f ' 2>/dev/null || true)"

  if [ "${unpacked}" -eq 0 ]; then
    refuse "The package unpacked to nothing, so the run below would boot a server with no plugin and could not tell that from a plugin that failed to load."
  fi
fi

# The container and the temporary directory go whichever way the run ends. The
# server writes into the mounted directories as the user inside the container,
# which is not the user running this, so removing them can fail on a permission
# error. That is tidying rather than a verdict: it is reported and it does not
# decide the exit status, because a harness that reported a plugin broken over a
# leftover temporary file would be switched off within a week.
cleanup() {
  docker rm --force "${container}" >/dev/null 2>&1 || true
  rm -rf "${root}" 2>/dev/null || echo "Left behind ${root}."
}
trap cleanup EXIT

# Calls the server. Prints the body on standard output and the status code on
# the last line, so one function serves both the routes that are read and the
# routes that are only asked for a status.
call() {
  local route="$1" method="${2:-GET}" body="${3:-}" token="${4:-}"
  local authorization="${client}"

  if [ -n "${token}" ]; then
    authorization="${client}, Token=\"${token}\""
  fi

  if [ -n "${body}" ]; then
    curl --silent --show-error --write-out '\n%{http_code}' \
      --request "${method}" \
      --header "Authorization: ${authorization}" \
      --header 'Content-Type: application/json' \
      --data "${body}" \
      "${base}${route}"
  else
    curl --silent --show-error --write-out '\n%{http_code}' \
      --request "${method}" \
      --header "Authorization: ${authorization}" \
      "${base}${route}"
  fi
}

status_of() { printf '%s' "$1" | tail -n 1; }
body_of() { printf '%s' "$1" | sed '$d'; }

# Waits for a route to answer 200.
#
# TWO ROUTES ARE WAITED ON RATHER THAN ONE. `/System/Info/Public` answers while
# the rest of the server is still starting, and a first-time setup step posted at
# that moment is refused by the middleware that holds requests until startup
# finishes. The public route says the server is listening; the wizard route says
# it is ready. Waiting on only the first is a harness that fails on every run for
# a reason that has nothing to do with the plugin.
wait_for() {
  local route="$1" seconds="$2" deadline answer status
  deadline=$(( $(date +%s) + seconds ))

  while [ "$(date +%s)" -lt "${deadline}" ]; do
    if answer="$(call "${route}" 2>/dev/null)"; then
      status="$(status_of "${answer}")"

      if [ "${status}" = "200" ]; then
        echo "${route} answers: $(body_of "${answer}" | head -c 240)"
        return 0
      fi
    fi

    sleep 2
  done

  echo "The server did not answer ${base}${route} with 200 within ${seconds}s. Container output follows." >&2
  docker logs "${container}" 2>&1 | tail -n 120 >&2
  refuse "The server never answered ${route}."
}

docker run --detach --name "${container}" \
  --publish "127.0.0.1:${port}:8096" \
  --volume "${config}:/config" \
  --volume "${root}/cache:/cache" \
  "${image}" >/dev/null

wait_for "/System/Info/Public" 240
wait_for "/Startup/Configuration" 240

echo "Completing first-time setup, so that an anonymous caller is refused by authorisation rather than admitted by the setup policy."

# THE GET ON /Startup/User IS NOT A READ. It is what creates the first user, and
# posting without it answers 404. The server's own source is the authority:
#
#     gh api "repos/jellyfin/jellyfin/contents/Jellyfin.Api/Controllers/StartupController.cs?ref=v10.11.11" \
#       -H "Accept: application/vnd.github.raw" | grep -n 'GetFirstUser\|UpdateStartupUser'
#
# so the order is a property of the server rather than a preference here.
created="$(call "/Startup/User")"
echo "  GET /Startup/User -> $(status_of "${created}")"
[ "$(status_of "${created}")" = "200" ] \
  || refuse "The server did not create a first user: /Startup/User answered $(status_of "${created}"). Every step below would run against a server with no user, and the assertion that an anonymous caller is refused would pass for want of anyone to authenticate."

post_setup() {
  local route="$1" body="$2" answer status

  answer="$(call "${route}" POST "${body}")"
  status="$(status_of "${answer}")"
  echo "  POST ${route} -> ${status}"

  [ "${status}" -lt 400 ] \
    || refuse "First-time setup failed at ${route} with status ${status}: $(body_of "${answer}" | head -c 400)"
}

post_setup "/Startup/Configuration" '{"UICulture":"en-US","MetadataCountryCode":"US","PreferredMetadataLanguage":"en"}'
post_setup "/Startup/User" "$(jq -n --arg n "${admin}" --arg p "${secret}" '{Name:$n,Password:$p}')"
post_setup "/Startup/RemoteAccess" '{"EnableRemoteAccess":true,"EnableAutomaticPortMapping":false}'
post_setup "/Startup/Complete" '{}'

authenticated="$(call "/Users/AuthenticateByName" POST "$(jq -n --arg u "${admin}" --arg p "${secret}" '{Username:$u,Pw:$p}')")"
[ "$(status_of "${authenticated}")" = "200" ] \
  || refuse "Authenticating the administrator this harness created returned $(status_of "${authenticated}"): $(body_of "${authenticated}" | head -c 400)"

token="$(body_of "${authenticated}" | jq -r '.AccessToken // ""')"
[ -n "${token}" ] \
  || refuse "The authentication response carried no access token, so every assertion below would run unauthenticated and pass for the wrong reason."

failures=()

# 1. The startup log holds no error from this plugin.
log="${root}/server.log"
docker logs "${container}" > "${log}" 2>&1 || true
errors="$(plugin_errors_in "${log}" "${name}")"
echo "1. Startup log: $(wc -l < "${log}") lines, $([ -z "${errors}" ] && echo 0 || printf '%s' "${errors}" | wc -l) naming this plugin at error level."

if [ -n "${errors}" ]; then
  while IFS= read -r line; do
    failures+=("The startup log holds an error from this plugin: ${line}")
  done <<< "${errors}"
fi

# 2. The server lists this plugin, under the declared identifier, as active.
listing="$(call "/Plugins" GET "" "${token}")"
echo "2. GET /Plugins as an administrator -> $(status_of "${listing}")"

if [ "$(status_of "${listing}")" != "200" ]; then
  failures+=("GET /Plugins as an administrator returned $(status_of "${listing}"), so whether the plugin loaded cannot be read at all.")
else
  echo "   The server lists: $(body_of "${listing}" | jq -r 'map("\(.Name) \(.Version) \(.Status)") | join("; ")' 2>/dev/null || echo "unreadable")"

  case "$(listed_verdict "$(body_of "${listing}")" "${guid}")" in
    active) ;;
    not-listed) failures+=("${NOT_LISTED} No plugin with identifier ${guid} is listed, so the packaged archive did not load.") ;;
    unreadable) failures+=("The plugin listing is not a JSON array, so whether the plugin loaded cannot be read at all.") ;;
    inactive:*) failures+=("The plugin is listed with status $(listed_verdict "$(body_of "${listing}")" "${guid}" | cut -d: -f2) rather than Active.") ;;
  esac
fi

# 3. The plugin's surface answers an administrator and refuses an anonymous
#    caller. Both arms over the same routes, so a route that answers nobody
#    cannot pass the second arm by being broken.
for route in "/Plugins" "/Plugins/${guid}/Configuration" "/Watchlist/Items"; do
  as_administrator="$(status_of "$(call "${route}" GET "" "${token}")")"
  as_anonymous="$(curl --silent --output /dev/null --write-out '%{http_code}' "${base}${route}")"

  echo "3. ${route}: administrator ${as_administrator}, anonymous ${as_anonymous}"

  if [ "${as_administrator}" != "200" ]; then
    failures+=("${route} returned ${as_administrator} to an administrator, and 200 is what a reachable plugin surface answers.")
  fi

  if [ "${as_anonymous}" != "401" ] && [ "${as_anonymous}" != "403" ]; then
    failures+=("${route} returned ${as_anonymous} to an anonymous caller, and every route asserted here is behind authorisation.")
  fi
done

# 4. The server holds no collision of any kind the scan reads. It is run here
#    rather than in a job of its own because the server, the administrator token
#    and the whole population are already in hand, and a second boot would prove
#    the same thing about a different server. It is skipped on the near miss,
#    which is about assertion 2 and would pay another boot for a reading its own
#    probe already covers.
if [ "${without}" = "no" ]; then
  scan="$(dirname "$0")/scan-a-server-for-collisions.sh"

  [ -f "${scan}" ] \
    || refuse "The collision scan is not beside this harness at ${scan}. A run that skipped it would report a server as clean that nothing had read for a collision."

  echo "4. Scanning the running server for collisions."

  if ! scanned="$(bash "${scan}" --manifest "${manifest}" --base "${base}" --token "${token}" 2>&1)"; then
    printf '%s\n' "${scanned}" | sed 's/^/   /'
    failures+=("The collision scan refused the running server. Its report is above.")
  else
    printf '%s\n' "${scanned}" | sed 's/^/   /'
  fi
fi

if [ "${without}" = "yes" ]; then
  # The near miss asserts the SHAPE of the failure rather than that there was
  # one, for the reason the marker at the top of this file gives.
  refused=0

  for failure in "${failures[@]:-}"; do
    [ -n "${failure}" ] && echo "  ${failure}"
    case "${failure}" in "${NOT_LISTED}"*) refused=$((refused + 1)) ;; esac
  done

  if [ "${refused}" -ne 1 ]; then
    echo "" >&2
    echo "The harness was pointed at a server with no plugin installed and did not refuse it for being unlisted. Whatever it reported, the assertion that the plugin is loaded and active has not been shown to bite, so the run that matters would prove nothing." >&2
    exit 1
  fi

  echo ""
  echo "The harness refuses ${image} with no plugin installed, for being unlisted."
  exit 0
fi

if [ "${#failures[@]}" -gt 0 ]; then
  echo "" >&2
  echo "The plugin does not work alone on ${image}:" >&2

  for failure in "${failures[@]}"; do
    echo "  ${failure}" >&2
  done

  exit 1
fi

echo ""
echo "The packaged plugin loads and answers on ${image}, alone."
