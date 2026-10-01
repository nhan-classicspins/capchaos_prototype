# Shared helpers for the Wool Gather build scripts.
#
# Source this file, do not execute it:
#   . "$(dirname "$0")/../lib/common.sh"
#
# Written for bash 3.2 (the system bash on macOS): no associative arrays,
# no mapfile/readarray, no ${var,,}, no globstar.

if [ -n "${COMMON_LOADED:-}" ]; then
  return 0
fi
COMMON_LOADED=1

# Exit codes, used consistently by every script in this tree.
EXIT_USAGE=2   # bad arguments, missing tool, missing credential, missing input file
EXIT_BUILD=1   # a build stage ran and failed

# ---------------------------------------------------------------- logging ----

wg_is_teamcity() {
  [ -n "${TEAMCITY_VERSION:-}" ]
}

# TeamCity service messages use their own escaping rules.
wg_tc_escape() {
  printf '%s' "$1" | sed -e "s/|/||/g" -e "s/'/|'/g" -e "s/\[/|[/g" -e "s/\]/|]/g" \
    | tr '\n\r' '  '
}

# Human-readable progress goes to stderr. stdout is reserved for machine-readable
# output: TeamCity service messages, usage text, and delivered artifact paths --
# which is what makes "the last line of stdout is the artifact path" hold.
log() {
  printf '[wg] %s\n' "$*" >&2
}

warn() {
  printf '[wg] WARNING: %s\n' "$*" >&2
}

# die <exit_code> <message...>
# Reports to stderr and exits. Does NOT emit a TeamCity buildProblem -- only the
# top-level entry point does that, via wg_install_failure_trap, so that a failure
# deep in a child script still produces exactly one buildProblem.
die() {
  code="$1"
  shift
  printf '[wg] ERROR: %s\n' "$*" >&2
  exit "$code"
}

wg_block_opened() {
  if wg_is_teamcity; then
    printf "##teamcity[blockOpened name='%s']\n" "$(wg_tc_escape "$1")"
  else
    printf '\n===== %s =====\n' "$1" >&2
  fi
}

wg_block_closed() {
  if wg_is_teamcity; then
    printf "##teamcity[blockClosed name='%s']\n" "$(wg_tc_escape "$1")"
  fi
}

wg_publish_artifact() {
  if wg_is_teamcity; then
    printf "##teamcity[publishArtifacts '%s']\n" "$(wg_tc_escape "$1")"
  fi
}

wg_set_build_number() {
  if wg_is_teamcity; then
    printf "##teamcity[buildNumber '%s']\n" "$(wg_tc_escape "$1")"
  fi
}

# Installs an EXIT trap that emits exactly one TeamCity buildProblem when the
# script exits non-zero. Call this once, from the top-level entry point only.
# $1 is a short label used in the problem description.
wg_install_failure_trap() {
  FAILURE_LABEL="$1"
  trap 'wg_report_exit $?' EXIT
}

wg_report_exit() {
  status="$1"
  if [ "$status" -ne 0 ]; then
    if wg_is_teamcity; then
      printf "##teamcity[buildProblem description='%s']\n" \
        "$(wg_tc_escape "${FAILURE_LABEL:-build} failed with exit code $status")"
    fi
  fi
}

# ------------------------------------------------------------ requirements ----

wg_require_cmd() {
  for _cmd in "$@"; do
    if ! command -v "$_cmd" >/dev/null 2>&1; then
      die "$EXIT_USAGE" "required command not found on PATH: $_cmd"
    fi
  done
}

# wg_require_env VAR...
# Checks every name before failing so the caller sees the full list at once.
# Values are never printed.
wg_require_env() {
  _missing=""
  for _name in "$@"; do
    eval "_value=\${$_name:-}"
    if [ -z "$_value" ]; then
      _missing="$_missing $_name"
    fi
  done
  if [ -n "$_missing" ]; then
    die "$EXIT_USAGE" "missing required environment variable(s):$_missing"
  fi
}

# wg_require_file <path> <description>
wg_require_file() {
  if [ ! -f "$1" ]; then
    die "$EXIT_USAGE" "$2 not found at: $1"
  fi
}

# wg_require_dir <path> <description>
wg_require_dir() {
  if [ ! -d "$1" ]; then
    die "$EXIT_USAGE" "$2 not found at: $1"
  fi
}

# ------------------------------------------------------------------ paths ----

# Absolute path without requiring GNU realpath (not present on stock macOS).
wg_abs_path() {
  if [ -d "$1" ]; then
    ( cd "$1" && pwd -P )
  else
    _dir=$( cd "$(dirname "$1")" && pwd -P )
    printf '%s/%s\n' "$_dir" "$(basename "$1")"
  fi
}

# ------------------------------------------------------------- build info ----

# wg_load_build_info <export_dir> <required_key>...
# Sources the build-info.env that the Unity stage generated, then verifies the
# named keys are present and non-empty. This file is the only channel through
# which Unity hands data to these scripts.
#
# Sets EXPORT_DIR to <export_dir> afterwards, on purpose: the file records the
# EXPORT_DIR the Unity stage wrote to, and sourcing it would otherwise replace the
# directory the caller was actually pointed at. The one on the command line wins, so
# an export that was moved or copied still packages from where it now is.
wg_load_build_info() {
  _export_dir="$1"
  shift
  _info="$_export_dir/build-info.env"
  if [ ! -f "$_info" ]; then
    die "$EXIT_USAGE" "build-info.env not found at: $_info (run the Unity export stage first)"
  fi
  # shellcheck source=/dev/null
  . "$_info"
  EXPORT_DIR="$_export_dir"
  log "loaded build info from $_info"
  if [ "$#" -gt 0 ]; then
    wg_require_env "$@"
  fi
}

# wg_find_single <description> <dir> <predicate> <pattern>
# <predicate> is a find test, normally -name or -path.
# Prints the single matching file, or dies when there are zero or several.
# Never guesses a filename -- the reference scripts this replaces did, and
# silently produced the wrong artifact when the pattern stopped matching.
wg_find_single() {
  _desc="$1"
  _dir="$2"
  _predicate="$3"
  _pattern="$4"
  wg_require_dir "$_dir" "$_desc search directory"

  _matches=$(find "$_dir" -type f "$_predicate" "$_pattern" 2>/dev/null)
  _count=0
  if [ -n "$_matches" ]; then
    _count=$(printf '%s\n' "$_matches" | wc -l | tr -d ' ')
  fi

  if [ "$_count" -eq 0 ]; then
    die "$EXIT_BUILD" "no $_desc matching '$_pattern' under $_dir"
  fi
  if [ "$_count" -gt 1 ]; then
    printf '[wg] ERROR: expected exactly one %s matching %s under %s, found %s:\n' \
      "$_desc" "$_pattern" "$_dir" "$_count" >&2
    printf '%s\n' "$_matches" >&2
    exit "$EXIT_BUILD"
  fi
  printf '%s\n' "$_matches"
}

# wg_find_single_child_dir <description> <dir> <name_pattern>
# Directory equivalent of wg_find_single, limited to immediate children. Used for
# bundle-style directories such as *.xcodeproj, where a recursive search would
# also pick up nested projects (Pods, plugins).
wg_find_single_child_dir() {
  _desc="$1"
  _dir="$2"
  _pattern="$3"
  wg_require_dir "$_dir" "$_desc search directory"

  _matches=$(find "$_dir" -maxdepth 1 -type d -name "$_pattern" 2>/dev/null)
  _count=0
  if [ -n "$_matches" ]; then
    _count=$(printf '%s\n' "$_matches" | wc -l | tr -d ' ')
  fi

  if [ "$_count" -eq 0 ]; then
    die "$EXIT_BUILD" "no $_desc matching '$_pattern' directly under $_dir"
  fi
  if [ "$_count" -gt 1 ]; then
    printf '[wg] ERROR: expected exactly one %s matching %s under %s, found %s:\n' \
      "$_desc" "$_pattern" "$_dir" "$_count" >&2
    printf '%s\n' "$_matches" >&2
    exit "$EXIT_BUILD"
  fi
  printf '%s\n' "$_matches"
}

# wg_deliver_artifact <source_file> <artifact_dir> <final_basename_with_extension>
# Copies the artifact to its final home and reports it. Prints the absolute
# destination path as the last thing it does, so callers can tail it.
wg_deliver_artifact() {
  _src="$1"
  _dest_dir="$2"
  _name="$3"

  mkdir -p "$_dest_dir"
  _dest="$_dest_dir/$_name"

  # The name is unique only to the minute, so two runs in the same minute with the same
  # build number collide. Refusing beats silently replacing an artifact that may already
  # have been published. Set OVERWRITE_ARTIFACTS=1 to replace on purpose.
  if [ -e "$_dest" ] && [ "${OVERWRITE_ARTIFACTS:-0}" != "1" ]; then
    die "$EXIT_BUILD" "artifact already exists, refusing to overwrite: $_dest (set OVERWRITE_ARTIFACTS=1 to replace it)"
  fi

  cp "$_src" "$_dest"

  if [ ! -s "$_dest" ]; then
    die "$EXIT_BUILD" "delivered artifact is empty: $_dest"
  fi

  _size=$(wc -c < "$_dest" | tr -d ' ')
  log "artifact: $_dest ($_size bytes)"
  wg_publish_artifact "$_dest"
  printf '%s\n' "$_dest"
}
