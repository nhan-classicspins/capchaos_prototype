#!/usr/bin/env bash
#
# Packaging stage for Android: turn the Gradle project Unity exported into a
# signed .aab and deliver it to the artifact directory.
#
# Normally invoked by BuildTools/build.sh, but safe to run on its own to re-run
# only Gradle after hand-editing the exported project:
#
#   BuildTools/android/bundle_release.sh --export-dir Build/Android/AndroidProject
#
# Signing is not configured here. Unity bakes signingConfigs into the exported
# project at export time (see the **SIGN** token in Unity's launcherTemplate.gradle),
# so the keystore must already have been applied on the Unity side.

set -euo pipefail

SCRIPT_DIR=$(cd "$(dirname "$0")" && pwd -P)

# shellcheck source=../lib/common.sh
. "$SCRIPT_DIR/../lib/common.sh"

usage() {
  cat <<'EOF'
Usage: BuildTools/android/bundle_release.sh --export-dir DIR [--task TASK]

  --export-dir DIR   Directory containing build-info.env, as written by the
                     Unity export stage. Required.
  --task TASK        Gradle task to run. Default: bundleRelease
  -h, --help         Show this help and exit 0.
EOF
}

EXPORT_DIR=""
GRADLE_TASK="bundleRelease"

while [ "$#" -gt 0 ]; do
  case "$1" in
    --export-dir)
      if [ "$#" -lt 2 ] || [ -z "$2" ]; then die "$EXIT_USAGE" "--export-dir requires a value"; fi
      EXPORT_DIR="$2"; shift 2 ;;
    --task)
      if [ "$#" -lt 2 ] || [ -z "$2" ]; then die "$EXIT_USAGE" "--task requires a value"; fi
      GRADLE_TASK="$2"; shift 2 ;;
    -h|--help) usage; exit 0 ;;
    *)
      printf '[wg] ERROR: unknown argument: %s\n' "$1" >&2
      usage >&2
      exit "$EXIT_USAGE"
      ;;
  esac
done

if [ -z "$EXPORT_DIR" ]; then
  printf '[wg] ERROR: --export-dir is required\n' >&2
  usage >&2
  exit "$EXIT_USAGE"
fi

wg_require_dir "$EXPORT_DIR" "export directory"
EXPORT_DIR=$(wg_abs_path "$EXPORT_DIR")

# build-info.env carries JAVA_HOME and ANDROID_SDK_ROOT -- the toolchain Unity exported
# with -- so sourcing it replaces whatever this shell already had, including with an empty
# value when Unity could not resolve one. Kept here first, so the agent's own settings stay
# available as the fallback they have always been.
AMBIENT_JAVA_HOME="${JAVA_HOME:-}"
AMBIENT_ANDROID_SDK_ROOT="${ANDROID_SDK_ROOT:-}"

wg_load_build_info "$EXPORT_DIR" \
  NATIVE_PROJECT_DIR ARTIFACT_DIR ARTIFACT_BASENAME

if [ "${PLATFORM:-}" != "android" ]; then
  die "$EXIT_USAGE" "build-info.env at $EXPORT_DIR describes platform '${PLATFORM:-<unset>}', not android"
fi

# Resolved now, because Gradle runs with the exported project as its working directory
# and a relative artifact dir would otherwise land inside the export.
mkdir -p "$ARTIFACT_DIR"
ARTIFACT_DIR=$(wg_abs_path "$ARTIFACT_DIR")

GRADLE_PROJECT="$NATIVE_PROJECT_DIR"
wg_require_dir "$GRADLE_PROJECT" "exported Gradle project"
if [ ! -f "$GRADLE_PROJECT/settings.gradle" ] && [ ! -f "$GRADLE_PROJECT/settings.gradle.kts" ]; then
  die "$EXIT_USAGE" "no settings.gradle(.kts) in $GRADLE_PROJECT -- that does not look like an exported Gradle project"
fi

# -------------------------------------------------------- gradle invocation ---

# Unity does not emit a gradlew wrapper, so the normal path is to run Unity's
# own bundled Gradle launcher with Unity's bundled JDK -- this machine has no
# system java at all. ./gradlew is still preferred if a future Unity emits one.
# macOS always has /usr/bin/java: it is a stub that prints "Unable to locate a Java
# Runtime" and fails. Existence therefore proves nothing -- run it before believing it.
java_is_usable() {
  [ -x "$1" ] && "$1" -version >/dev/null 2>&1
}

# Sets JAVA_BIN and exports JAVA_HOME when a working runtime can be found. Returns 1
# when none can, so the caller decides whether that is fatal: ./gradlew locates its
# own JVM, but launching Gradle ourselves obviously cannot.
# Unity's JDK -- the JAVA_HOME build-info.env just supplied -- is preferred over the one
# this shell arrived with, and PATH is the last resort.
try_resolve_java() {
  JAVA_BIN=""
  for _home in "${JAVA_HOME:-}" "$AMBIENT_JAVA_HOME"; do
    if [ -n "$_home" ] && java_is_usable "$_home/bin/java"; then
      JAVA_BIN="$_home/bin/java"
      export JAVA_HOME="$_home"
      return 0
    fi
  done
  _path_java=$(command -v java 2>/dev/null || true)
  if [ -n "$_path_java" ] && java_is_usable "$_path_java"; then
    JAVA_BIN="$_path_java"
    return 0
  fi
  return 1
}

require_java() {
  if ! try_resolve_java; then
    die "$EXIT_USAGE" "no working Java runtime found (checked JAVA_HOME from build-info.env, this shell's own JAVA_HOME, and PATH; note that macOS ships a /usr/bin/java stub that is not a runtime)"
  fi
}

if [ -z "${ANDROID_SDK_ROOT:-}" ]; then
  ANDROID_SDK_ROOT="$AMBIENT_ANDROID_SDK_ROOT"
fi
if [ -n "$ANDROID_SDK_ROOT" ]; then
  export ANDROID_HOME="$ANDROID_SDK_ROOT"
  export ANDROID_SDK_ROOT
fi

# Decide how to launch Gradle, and fail before opening a TeamCity block so a failure
# here cannot leave the block unclosed and swallow the rest of the build log.
USE_WRAPPER=0
if [ -x "$GRADLE_PROJECT/gradlew" ]; then
  USE_WRAPPER=1
  try_resolve_java || log "no JDK resolved; leaving ./gradlew to find its own"
else
  if [ -z "${GRADLE_LAUNCHER:-}" ]; then
    die "$EXIT_USAGE" "GRADLE_LAUNCHER is not set in build-info.env and the exported project has no ./gradlew"
  fi
  wg_require_file "$GRADLE_LAUNCHER" "Gradle launcher jar"
  require_java
fi

wg_block_opened "Gradle $GRADLE_TASK"
cd "$GRADLE_PROJECT"

if [ "$USE_WRAPPER" -eq 1 ]; then
  log "using ./gradlew from the exported project"
  set +e
  # ANDROID_GRADLE_EXTRA is intentionally word-split.
  # shellcheck disable=SC2086
  ./gradlew "$GRADLE_TASK" ${ANDROID_GRADLE_EXTRA:-}
  gradle_status=$?
  set -e
else
  # NOT `java -jar`: Unity's gradle-launcher jar declares Class-Path but no Main-Class,
  # so -jar fails with "no main manifest attribute". Gradle's documented entry point is
  # org.gradle.launcher.GradleMain on the classpath.
  log "using $JAVA_BIN with $GRADLE_LAUNCHER"
  set +e
  # shellcheck disable=SC2086
  "$JAVA_BIN" -cp "$GRADLE_LAUNCHER" org.gradle.launcher.GradleMain \
    "$GRADLE_TASK" ${ANDROID_GRADLE_EXTRA:-}
  gradle_status=$?
  set -e
fi
wg_block_closed "Gradle $GRADLE_TASK"

if [ "$gradle_status" -ne 0 ]; then
  die "$EXIT_BUILD" "Gradle $GRADLE_TASK failed (exit $gradle_status)"
fi

# ------------------------------------------------------------- delivery -------

# Match on the canonical AGP output path rather than assuming the module is
# called `launcher`, and skip the intermediate .aab files AGP leaves under
# build/intermediates. Exactly one match is required, so a layout change fails
# loudly instead of delivering a stale or wrong bundle.
AAB_PATH=$(wg_find_single "release app bundle" "$GRADLE_PROJECT" \
  -path "*/build/outputs/bundle/release/*.aab")
log "gradle produced $AAB_PATH"

wg_deliver_artifact "$AAB_PATH" "$ARTIFACT_DIR" "$ARTIFACT_BASENAME.aab"
