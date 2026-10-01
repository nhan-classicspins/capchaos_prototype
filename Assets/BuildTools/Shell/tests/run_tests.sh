#!/usr/bin/env bash
#
# Behaviour tests for the BuildTools shell layer.
#
#   BuildTools/tests/run_tests.sh
#
# Everything runs against a throwaway copy of BuildTools inside a temporary
# "project", with stub `Unity`, `gradlew` and `xcodebuild` executables. That means
# the suite needs no signing credentials, no Xcode, no JDK and no Unity licence,
# and it never touches the real project's Build directory -- so it is safe to wire
# into a TeamCity step as a fast gate ahead of the real build.
#
# Exits 0 when every case passes, 1 otherwise.

set -uo pipefail   # deliberately not -e: most cases assert on failure exit codes

REPO_BUILD_TOOLS=$(cd "$(dirname "$0")/.." && pwd -P)

TESTS_RUN=0
TESTS_FAILED=0
CURRENT_CASE="<none>"

# Fully resolved: on macOS $TMPDIR lives under a /var -> /private/var symlink, and the
# scripts report real paths, so an unresolved WORK would make path assertions fail.
WORK=$(cd "$(mktemp -d "${TMPDIR:-/tmp}/wg-buildtools-tests.XXXXXX")" && pwd -P)
cleanup() { rm -rf "$WORK"; }
trap cleanup EXIT

# ------------------------------------------------------------------ harness ----

start_case() {
  CURRENT_CASE="$1"
  TESTS_RUN=$((TESTS_RUN + 1))
}

pass() { printf 'ok   %s\n' "$CURRENT_CASE"; }

fail() {
  printf 'FAIL %s\n     %s\n' "$CURRENT_CASE" "$1"
  TESTS_FAILED=$((TESTS_FAILED + 1))
}

assert_exit() {
  if [ "$1" -eq "$2" ]; then
    return 0
  fi
  fail "expected exit $2, got $1"
  return 1
}

assert_contains() {
  case "$1" in
    *"$2"*) return 0 ;;
  esac
  fail "expected output to contain: $2"
  return 1
}

assert_not_contains() {
  case "$1" in
    *"$2"*)
      fail "expected output NOT to contain: $2"
      return 1
      ;;
  esac
  return 0
}

assert_file() {
  if [ -f "$1" ]; then
    return 0
  fi
  fail "expected file to exist: $1"
  return 1
}

assert_no_file() {
  if [ ! -e "$1" ]; then
    return 0
  fi
  fail "expected path NOT to exist: $1"
  return 1
}

assert_equals() {
  if [ "$1" = "$2" ]; then
    return 0
  fi
  fail "expected '$2', got '$1'"
  return 1
}

# ------------------------------------------------------------- fake project ----

# A minimal project tree: a copy of BuildTools plus the one file build.sh reads to
# discover which Unity to use.
make_project() {
  _root="$WORK/$1"
  _editor_version="${2:-6000.3.12f1}"
  rm -rf "$_root"
  mkdir -p "$_root/ProjectSettings"
  cp -R "$REPO_BUILD_TOOLS" "$_root/BuildTools"
  printf 'm_EditorVersion: %s\nm_EditorVersionWithRevision: %s (abcdef123456)\n' \
    "$_editor_version" "$_editor_version" > "$_root/ProjectSettings/ProjectVersion.txt"
  printf '%s\n' "$_root"
}

# Stub Unity. Honours:
#   STUB_UNITY_EXIT          exit with this code instead of exporting (default 0)
#   STUB_UNITY_WRITE_INFO    0 to skip writing build-info.env (default 1)
#   STUB_UNITY_PLATFORM      value for PLATFORM (default: from --buildTarget)
#   STUB_UNITY_AAB_COUNT     .aab files the stub gradlew will produce (default 1)
#   STUB_UNITY_GRADLEW_EXIT  exit code for the stub gradlew (default 0)
#   STUB_UNITY_EMIT_GRADLEW  0 to omit gradlew, the shape real Unity exports (default 1)
#   STUB_UNITY_ARGV_FILE     write the received argv here, one per line
#   STUB_UNITY_BASENAMES     iOS artifact basenames to record: both (default, which is what
#                            real WriteBuildInfo does so a later run can pick either method)
#                            or matching, i.e. only those for the requested --export-method
write_stub_unity() {
  _path="$1"
  cat > "$_path" <<'STUB'
#!/usr/bin/env bash
set -uo pipefail
if [ -n "${STUB_UNITY_ARGV_FILE:-}" ]; then
  printf '%s\n' "$@" > "$STUB_UNITY_ARGV_FILE"
fi
EXPORT_DIR=""
ARTIFACT_DIR=""
BUILD_TARGET=""
EXPORT_METHOD="appstore"
SIGNING="manual"
while [ "$#" -gt 0 ]; do
  case "$1" in
    --export-dir)    EXPORT_DIR="$2"; shift 2 ;;
    --artifact-dir)  ARTIFACT_DIR="$2"; shift 2 ;;
    --export-method) EXPORT_METHOD="$2"; shift 2 ;;
    --signing)       SIGNING="$2"; shift 2 ;;
    -buildTarget)    BUILD_TARGET="$2"; shift 2 ;;
    *) shift ;;
  esac
done

if [ "${STUB_UNITY_EXIT:-0}" -ne 0 ]; then
  echo "stub Unity: pretending the export failed" >&2
  exit "${STUB_UNITY_EXIT:-0}"
fi
if [ "${STUB_UNITY_WRITE_INFO:-1}" != "1" ]; then
  exit 0
fi

if [ "$BUILD_TARGET" = "iOS" ]; then
  platform="ios"
  # Unity drops the Xcode project straight into the export directory.
  native_dir="$EXPORT_DIR"
  mkdir -p "$native_dir/Unity-iPhone.xcodeproj"
else
  platform="android"
  # Unity nests the Gradle project in a folder named after productName, which for
  # this project contains a space -- exercised here on purpose.
  native_dir="$EXPORT_DIR/Wool Gather"
  mkdir -p "$native_dir/launcher/build/outputs/bundle/release"
  printf "include ':launcher'\n" > "$native_dir/settings.gradle"
if [ "${STUB_UNITY_EMIT_GRADLEW:-1}" = "1" ]; then
  cat > "$native_dir/gradlew" <<'GRADLEW'
#!/usr/bin/env bash
set -uo pipefail
echo "stub gradlew: $*"
if [ "${STUB_UNITY_GRADLEW_EXIT:-0}" -ne 0 ]; then
  echo "stub gradlew: pretending the build failed" >&2
  exit "${STUB_UNITY_GRADLEW_EXIT:-0}"
fi
out="launcher/build/outputs/bundle/release"
mkdir -p "$out"
count="${STUB_UNITY_AAB_COUNT:-1}"
i=1
while [ "$i" -le "$count" ]; do
  printf 'fake bundle %s\n' "$i" > "$out/launcher-release-$i.aab"
  i=$((i + 1))
done
GRADLEW
  chmod +x "$native_dir/gradlew"
fi
fi

mkdir -p "$EXPORT_DIR"
{
  printf "PLATFORM='%s'\n" "${STUB_UNITY_PLATFORM:-$platform}"
  printf "PROFILE='Release'\n"
  printf "BUNDLE_ID='com.classicspins.woolgather'\n"
  printf "VERSION='0.1.0'\n"
  printf "BUILD_NUMBER='42'\n"
  printf "BUILD_TIMESTAMP='2608111530'\n"
  printf "EXPORT_DIR='%s'\n" "$EXPORT_DIR"
  printf "NATIVE_PROJECT_DIR='%s'\n" "$native_dir"
  printf "ARTIFACT_DIR='%s'\n" "$ARTIFACT_DIR"
  printf "ARTIFACT_BASENAME='WoolGather_%s_0.1.0-42_2608111530'\n" \
    "$([ "$platform" = "ios" ] && echo iOS || echo Android)"
  # Both basenames unless told otherwise, which is what real WriteBuildInfo writes: one per
  # method, so a later --stage native run can pick either without a second Unity launch.
  # 'matching' models a build info that names only what was actually exported.
  if [ "${STUB_UNITY_BASENAMES:-both}" = "both" ] || [ "$EXPORT_METHOD" != "adhoc" ]; then
    printf "ARTIFACT_BASENAME_APPSTORE='WoolGather_iOS_0.1.0-42_appstore_2608111530'\n"
  fi
  if [ "${STUB_UNITY_BASENAMES:-both}" = "both" ] || [ "$EXPORT_METHOD" != "appstore" ]; then
    printf "ARTIFACT_BASENAME_ADHOC='WoolGather_iOS_0.1.0-42_adhoc_2608111530'\n"
  fi
  # The iOS-only keys real WriteBuildInfo emits, so a later --stage native or --stage
  # upload run against this export sees what it would really see.
  if [ "$platform" = "ios" ]; then
    if [ "$EXPORT_METHOD" = "both" ]; then
      printf "IOS_EXPORT_METHODS='appstore adhoc'\n"
    else
      printf "IOS_EXPORT_METHODS='%s'\n" "$EXPORT_METHOD"
    fi
    printf "IOS_SIGNING='%s'\n" "$SIGNING"
  fi
} > "$EXPORT_DIR/build-info.env"
STUB
  chmod +x "$_path"
}

# Stub xcodebuild, placed on PATH. Honours:
#   STUB_XCODE_VERSION       version string reported by -version (default 26.3)
#   STUB_XCODE_ARCHIVE_EXIT  exit code for `archive` (default 0)
#   STUB_XCODE_EXPORT_EXIT   exit code for `-exportArchive` (default 0)
#   STUB_XCODE_IPA_COUNT     .ipa files produced per export (default 1)
#   STUB_XCODE_ARGV_FILE     append each non-version invocation's argv here
write_stub_xcodebuild() {
  _dir="$1"
  mkdir -p "$_dir"
  cat > "$_dir/xcodebuild" <<'STUB'
#!/usr/bin/env bash
set -uo pipefail
if [ "${1:-}" = "-version" ]; then
  printf 'Xcode %s\nBuild version 17C529\n' "${STUB_XCODE_VERSION:-26.3}"
  exit 0
fi
if [ -n "${STUB_XCODE_ARGV_FILE:-}" ]; then
  printf '%s\n' "$*" >> "$STUB_XCODE_ARGV_FILE"
fi

MODE="archive"
ARCHIVE_PATH=""
EXPORT_PATH=""
for arg in "$@"; do
  if [ "$arg" = "-exportArchive" ]; then MODE="export"; fi
done
while [ "$#" -gt 0 ]; do
  case "$1" in
    -archivePath) ARCHIVE_PATH="$2"; shift 2 ;;
    -exportPath)  EXPORT_PATH="$2"; shift 2 ;;
    *) shift ;;
  esac
done

if [ "$MODE" = "archive" ]; then
  echo "stub xcodebuild: archive -> $ARCHIVE_PATH"
  if [ "${STUB_XCODE_ARCHIVE_EXIT:-0}" -ne 0 ]; then
    echo "stub xcodebuild: pretending the archive failed" >&2
    exit "${STUB_XCODE_ARCHIVE_EXIT:-0}"
  fi
  mkdir -p "$ARCHIVE_PATH/Products"
  exit 0
fi

echo "stub xcodebuild: exportArchive -> $EXPORT_PATH"
if [ "${STUB_XCODE_EXPORT_EXIT:-0}" -ne 0 ]; then
  echo "stub xcodebuild: pretending the export failed" >&2
  exit "${STUB_XCODE_EXPORT_EXIT:-0}"
fi
mkdir -p "$EXPORT_PATH"
count="${STUB_XCODE_IPA_COUNT:-1}"
i=1
while [ "$i" -le "$count" ]; do
  printf 'fake ipa %s\n' "$i" > "$EXPORT_PATH/Wool Gather-$i.ipa"
  i=$((i + 1))
done
STUB
  chmod +x "$_dir/xcodebuild"
}

# Stub xcrun, placed on PATH beside the stub xcodebuild. Only `altool` is modelled --
# it is the one tool the upload stage reaches for -- and anything else is refused loudly
# rather than silently succeeding. Honours:
#   STUB_ALTOOL_EXIT       exit code for altool (default 0)
#   STUB_XCRUN_ARGV_FILE   append each invocation's argv here, one line per call
write_stub_xcrun() {
  _dir="$1"
  mkdir -p "$_dir"
  cat > "$_dir/xcrun" <<'STUB'
#!/usr/bin/env bash
set -uo pipefail
if [ -n "${STUB_XCRUN_ARGV_FILE:-}" ]; then
  printf '%s\n' "$*" >> "$STUB_XCRUN_ARGV_FILE"
fi
if [ "${1:-}" != "altool" ]; then
  echo "stub xcrun: no such tool: ${1:-<none>}" >&2
  exit 1
fi
shift
# On stdout on purpose: the real altool writes its progress there, and the upload stage
# is supposed to redirect it so stdout stays reserved for the artifact path.
echo "stub altool: $*"
if [ "${STUB_ALTOOL_EXIT:-0}" -ne 0 ]; then
  echo "stub altool: *** Error: pretending the upload failed" >&2
  exit "${STUB_ALTOOL_EXIT:-0}"
fi
echo "No errors uploading."
STUB
  chmod +x "$_dir/xcrun"
}

ANDROID_ENV="ANDROID_KEYSTORE_PATH=$WORK/fake.keystore ANDROID_KEYSTORE_PASS=hunter2 ANDROID_KEY_ALIAS=alias ANDROID_KEY_ALIAS_PASS=hunter2"
printf 'not a real keystore\n' > "$WORK/fake.keystore"

# The .p8 the upload cases authenticate with. Contents are never read by the stub -- only
# its existence and the path it is passed under matter.
ASC_KEY="$WORK/AuthKey_STUB123.p8"
printf 'not a real p8 key\n' > "$ASC_KEY"
ASC_ENV="IOS_ASC_KEY_PATH=$ASC_KEY IOS_ASC_KEY_ID=KEYID1 IOS_ASC_ISSUER_ID=ISSUER1"

# ============================================================================
# build.sh: argument handling
# ============================================================================

start_case "--help exits 0 and prints usage on stdout"
out=$("$REPO_BUILD_TOOLS/build.sh" --help 2>/dev/null); status=$?
assert_exit "$status" 0 && assert_contains "$out" "Usage: BuildTools/build.sh" && pass

start_case "unknown flag exits 2"
out=$("$REPO_BUILD_TOOLS/build.sh" --platfrom ios 2>&1); status=$?
assert_exit "$status" 2 && assert_contains "$out" "unknown argument: --platfrom" && pass

start_case "missing --platform exits 2"
out=$("$REPO_BUILD_TOOLS/build.sh" 2>&1); status=$?
assert_exit "$status" 2 && assert_contains "$out" "--platform is required" && pass

start_case "invalid --platform exits 2"
out=$("$REPO_BUILD_TOOLS/build.sh" --platform windows 2>&1); status=$?
assert_exit "$status" 2 && assert_contains "$out" "must be ios or android" && pass

start_case "flag without a value exits 2"
out=$("$REPO_BUILD_TOOLS/build.sh" --platform 2>&1); status=$?
assert_exit "$status" 2 && assert_contains "$out" "--platform requires a value" && pass

start_case "non-numeric --build-number exits 2 before Unity starts"
out=$("$REPO_BUILD_TOOLS/build.sh" --platform android --build-number abc 2>&1); status=$?
assert_exit "$status" 2 \
  && assert_contains "$out" "--build-number must be a non-negative integer" \
  && assert_not_contains "$out" "Unity:" && pass

start_case "a malformed --version exits 2 before Unity starts"
version_case_ok=1
for bad_version in 1.0.0.1 1.0. .1.0 1..0 1.0-beta v1.0.0 "1 0 0"; do
  out=$("$REPO_BUILD_TOOLS/build.sh" --platform android --version "$bad_version" 2>&1); status=$?
  if [ "$status" -ne 2 ]; then
    fail "expected exit 2 for --version '$bad_version', got $status"
    version_case_ok=0
    break
  fi
  case "$out" in
    *"--version must"*) ;;
    *)
      fail "expected a --version message for '$bad_version', got: $out"
      version_case_ok=0
      break
      ;;
  esac
done
[ "$version_case_ok" -eq 1 ] && pass

start_case "a --version with leading zeros is rejected, naming the rule"
out=$("$REPO_BUILD_TOOLS/build.sh" --platform android --version 1.02 2>&1); status=$?
assert_exit "$status" 2 && assert_contains "$out" "must not have leading zeros" && pass

start_case "a --version part longer than 9 digits is rejected"
out=$("$REPO_BUILD_TOOLS/build.sh" --platform android --version 1.0.1234567890 2>&1); status=$?
assert_exit "$status" 2 && assert_contains "$out" "at most 9 digits per part" && pass

start_case "--version without a value exits 2"
out=$("$REPO_BUILD_TOOLS/build.sh" --platform android --version 2>&1); status=$?
assert_exit "$status" 2 && assert_contains "$out" "--version requires a value" && pass

start_case "invalid --stage exits 2"
out=$("$REPO_BUILD_TOOLS/build.sh" --platform ios --stage nope 2>&1); status=$?
assert_exit "$status" 2 && assert_contains "$out" "--stage must be all, unity, native or upload" && pass

start_case "invalid --export-method exits 2"
out=$("$REPO_BUILD_TOOLS/build.sh" --platform ios --export-method nope 2>&1); status=$?
assert_exit "$status" 2 && assert_contains "$out" "--export-method must be appstore, adhoc or both" && pass

start_case "--upload-testflight on Android exits 2 before Unity starts"
out=$(env $ANDROID_ENV "$REPO_BUILD_TOOLS/build.sh" --platform android --upload-testflight 2>&1); status=$?
assert_exit "$status" 2 \
  && assert_contains "$out" "TestFlight is iOS only" \
  && assert_not_contains "$out" "Unity:" && pass

start_case "--stage upload on Android exits 2: the stage implies the flag"
out=$(env $ANDROID_ENV "$REPO_BUILD_TOOLS/build.sh" --platform android --stage upload 2>&1); status=$?
assert_exit "$status" 2 && assert_contains "$out" "TestFlight is iOS only" && pass

start_case "--export-method adhoc with --upload-testflight exits 2 before Unity starts"
out=$("$REPO_BUILD_TOOLS/build.sh" --platform ios --export-method adhoc --upload-testflight 2>&1); status=$?
assert_exit "$status" 2 \
  && assert_contains "$out" "TestFlight needs an App Store build" \
  && assert_not_contains "$out" "Unity:" && pass

start_case "--upload-testflight with --stage unity warns and asks for no credentials"
out=$(env -u IOS_ASC_KEY_PATH -u IOS_ASC_KEY_ID -u IOS_ASC_ISSUER_ID \
        IOS_TEAM_ID=TEAM123 "$REPO_BUILD_TOOLS/build.sh" --platform ios --stage unity \
        --upload-testflight --unity "$WORK/no-such-unity" 2>&1); status=$?
assert_exit "$status" 2 \
  && assert_contains "$out" "--upload-testflight is ignored with --stage unity" \
  && assert_not_contains "$out" "IOS_ASC_KEY_ID" \
  && assert_contains "$out" "Unity executable not found" && pass

start_case "--help documents the flag and the stage"
out=$("$REPO_BUILD_TOOLS/build.sh" --help 2>/dev/null); status=$?
assert_exit "$status" 0 \
  && assert_contains "$out" "--upload-testflight" \
  && assert_contains "$out" "all | unity | native | upload" && pass

# ============================================================================
# build.sh: credentials, checked before Unity is launched
# ============================================================================

start_case "missing Android credentials are all listed, values never echoed"
out=$(env -u ANDROID_KEYSTORE_PATH -u ANDROID_KEYSTORE_PASS \
        -u ANDROID_KEY_ALIAS -u ANDROID_KEY_ALIAS_PASS \
        "$REPO_BUILD_TOOLS/build.sh" --platform android --build-number 1 2>&1); status=$?
assert_exit "$status" 2 \
  && assert_contains "$out" "ANDROID_KEYSTORE_PATH" \
  && assert_contains "$out" "ANDROID_KEYSTORE_PASS" \
  && assert_contains "$out" "ANDROID_KEY_ALIAS" \
  && assert_contains "$out" "ANDROID_KEY_ALIAS_PASS" \
  && assert_not_contains "$out" "Unity:" && pass

start_case "one missing Android credential is named, secret value not printed"
out=$(env -u ANDROID_KEYSTORE_PASS ANDROID_KEYSTORE_PATH="$WORK/fake.keystore" \
        ANDROID_KEY_ALIAS=alias ANDROID_KEY_ALIAS_PASS=hunter2 \
        "$REPO_BUILD_TOOLS/build.sh" --platform android 2>&1); status=$?
assert_exit "$status" 2 \
  && assert_contains "$out" "ANDROID_KEYSTORE_PASS" \
  && assert_not_contains "$out" "hunter2" && pass

start_case "absent keystore file exits 2 and shows the path checked"
out=$(env ANDROID_KEYSTORE_PATH="$WORK/definitely-absent.keystore" ANDROID_KEYSTORE_PASS=p \
        ANDROID_KEY_ALIAS=a ANDROID_KEY_ALIAS_PASS=p \
        "$REPO_BUILD_TOOLS/build.sh" --platform android 2>&1); status=$?
assert_exit "$status" 2 \
  && assert_contains "$out" "definitely-absent.keystore" \
  && assert_not_contains "$out" "Unity:" && pass

start_case "missing IOS_TEAM_ID exits 2"
out=$(env -u IOS_TEAM_ID "$REPO_BUILD_TOOLS/build.sh" --platform ios 2>&1); status=$?
assert_exit "$status" 2 && assert_contains "$out" "IOS_TEAM_ID" && pass

start_case "--export-method both requires both profile names"
out=$(env -u IOS_PROFILE_APPSTORE -u IOS_PROFILE_ADHOC IOS_TEAM_ID=TEAM1 \
        "$REPO_BUILD_TOOLS/build.sh" --platform ios --stage native --export-method both 2>&1); status=$?
assert_exit "$status" 2 \
  && assert_contains "$out" "IOS_PROFILE_APPSTORE" \
  && assert_contains "$out" "IOS_PROFILE_ADHOC" && pass

# ============================================================================
# build.sh: Unity resolution
# ============================================================================

start_case "--unity pointing nowhere exits 2"
out=$(env $ANDROID_ENV "$REPO_BUILD_TOOLS/build.sh" --platform android \
        --unity "$WORK/no-such-unity" 2>&1); status=$?
assert_exit "$status" 2 && assert_contains "$out" "Unity executable not found" && pass

start_case "uninstalled editor version exits 2, naming version and expected path"
project=$(make_project uninstalled-version 9999.1.0f1)
out=$(env -u UNITY_PATH $ANDROID_ENV "$project/BuildTools/build.sh" --platform android 2>&1); status=$?
assert_exit "$status" 2 \
  && assert_contains "$out" "no Unity 9999.1.0f1 install found" \
  && assert_contains "$out" "/Applications/Unity/Hub/Editor/9999.1.0f1/Unity.app/Contents/MacOS/Unity" && pass

# ============================================================================
# build.sh + stub Unity: orchestration
# ============================================================================

start_case "a failing Unity export exits 1 and the packaging stage never runs"
project=$(make_project unity-fails)
write_stub_unity "$WORK/stub-unity-fail"
out=$(env $ANDROID_ENV STUB_UNITY_EXIT=1 "$project/BuildTools/build.sh" \
        --platform android --unity "$WORK/stub-unity-fail" 2>&1); status=$?
assert_exit "$status" 1 \
  && assert_contains "$out" "Unity export stage failed" \
  && assert_not_contains "$out" "stub gradlew" && pass

start_case "a Unity stage that writes no build-info.env exits 2"
project=$(make_project no-build-info)
write_stub_unity "$WORK/stub-unity-noinfo"
out=$(env $ANDROID_ENV STUB_UNITY_WRITE_INFO=0 "$project/BuildTools/build.sh" \
        --platform android --unity "$WORK/stub-unity-noinfo" 2>&1); status=$?
assert_exit "$status" 2 && assert_contains "$out" "build-info.env" && pass

start_case "a stale export directory is removed before the export"
project=$(make_project stale-export)
write_stub_unity "$WORK/stub-unity-stale"
mkdir -p "$project/Build/Android/AndroidProject"
printf 'left over from a previous run\n' > "$project/Build/Android/AndroidProject/stale.txt"
out=$(env $ANDROID_ENV "$project/BuildTools/build.sh" \
        --platform android --unity "$WORK/stub-unity-stale" 2>&1); status=$?
assert_exit "$status" 0 && assert_no_file "$project/Build/Android/AndroidProject/stale.txt" && pass

start_case "--keep-export leaves the previous export directory alone"
project=$(make_project keep-export)
write_stub_unity "$WORK/stub-unity-keep"
mkdir -p "$project/Build/Android/AndroidProject"
printf 'kept\n' > "$project/Build/Android/AndroidProject/stale.txt"
out=$(env $ANDROID_ENV "$project/BuildTools/build.sh" --keep-export \
        --platform android --unity "$WORK/stub-unity-keep" 2>&1); status=$?
assert_exit "$status" 0 && assert_file "$project/Build/Android/AndroidProject/stale.txt" && pass

start_case "Android happy path delivers the .aab and prints its path last on stdout"
project=$(make_project android-ok)
write_stub_unity "$WORK/stub-unity-android"
out=$(env $ANDROID_ENV "$project/BuildTools/build.sh" \
        --platform android --build-number 42 --unity "$WORK/stub-unity-android" 2>/dev/null); status=$?
expected="$project/Build/Artifacts/WoolGather_Android_0.1.0-42_2608111530.aab"
last_line=$(printf '%s\n' "$out" | tail -1)
assert_exit "$status" 0 && assert_file "$expected" && assert_equals "$last_line" "$expected" && pass

start_case "a failing Gradle exits 1 and delivers nothing"
project=$(make_project gradle-fails)
write_stub_unity "$WORK/stub-unity-gradle-fail"
out=$(env $ANDROID_ENV STUB_UNITY_GRADLEW_EXIT=7 "$project/BuildTools/build.sh" \
        --platform android --unity "$WORK/stub-unity-gradle-fail" 2>&1); status=$?
assert_exit "$status" 1 \
  && assert_contains "$out" "pretending the build failed" \
  && assert_no_file "$project/Build/Artifacts/WoolGather_Android_0.1.0-42_2608111530.aab" && pass

start_case "zero .aab produced exits 1 rather than guessing a file name"
project=$(make_project no-aab)
write_stub_unity "$WORK/stub-unity-no-aab"
out=$(env $ANDROID_ENV STUB_UNITY_AAB_COUNT=0 "$project/BuildTools/build.sh" \
        --platform android --unity "$WORK/stub-unity-no-aab" 2>&1); status=$?
assert_exit "$status" 1 && assert_contains "$out" "no release app bundle" && pass

start_case "several .aab produced exits 1 and lists them"
project=$(make_project many-aab)
write_stub_unity "$WORK/stub-unity-many-aab"
out=$(env $ANDROID_ENV STUB_UNITY_AAB_COUNT=2 "$project/BuildTools/build.sh" \
        --platform android --unity "$WORK/stub-unity-many-aab" 2>&1); status=$?
assert_exit "$status" 1 \
  && assert_contains "$out" "expected exactly one release app bundle" \
  && assert_contains "$out" "launcher-release-2.aab" && pass

start_case "build-info.env for the wrong platform is rejected"
project=$(make_project platform-mismatch)
write_stub_unity "$WORK/stub-unity-mismatch"
out=$(env $ANDROID_ENV STUB_UNITY_PLATFORM=ios "$project/BuildTools/build.sh" \
        --platform android --unity "$WORK/stub-unity-mismatch" 2>&1); status=$?
assert_exit "$status" 2 && assert_contains "$out" "describes platform 'ios', not android" && pass

start_case "--stage unity stops before packaging"
project=$(make_project stage-unity)
write_stub_unity "$WORK/stub-unity-stage"
out=$(env $ANDROID_ENV "$project/BuildTools/build.sh" --stage unity \
        --platform android --unity "$WORK/stub-unity-stage" 2>&1); status=$?
assert_exit "$status" 0 \
  && assert_contains "$out" "Unity export stage complete" \
  && assert_not_contains "$out" "stub gradlew" && pass

# ============================================================================
# Shared stubs used from here on
# ============================================================================

STUB_BIN="$WORK/stubbin"
write_stub_xcodebuild "$STUB_BIN"
write_stub_xcrun "$STUB_BIN"
write_stub_unity "$WORK/stub-unity-ios"

# A PATH carrying the handful of tools the scripts need and deliberately no xcrun. /usr/bin
# cannot simply be dropped, because dirname and basename live there too -- and so does the
# real xcrun, which is the whole point.
NO_XCRUN_BIN="$WORK/noxcrunbin"
mkdir -p "$NO_XCRUN_BIN"
for tool in bash dirname basename cat sed tr; do
  tool_path=$(command -v "$tool" 2>/dev/null) && ln -sf "$tool_path" "$NO_XCRUN_BIN/$tool"
done

# ============================================================================
# Android without a gradlew wrapper -- the shape real Unity actually exports
# ============================================================================

# Stub java that records how it was invoked, then plays the part of Gradle.
write_stub_java() {
  mkdir -p "$1/bin"
  cat > "$1/bin/java" <<'STUB'
#!/usr/bin/env bash
set -uo pipefail
if [ "${1:-}" = "-version" ]; then
  echo 'openjdk version "17.0.9" 2023-10-17' >&2
  exit 0
fi
if [ -n "${STUB_JAVA_ARGV_FILE:-}" ]; then
  printf '%s\n' "$*" > "$STUB_JAVA_ARGV_FILE"
fi
if [ "${STUB_JAVA_EXIT:-0}" -ne 0 ]; then
  exit "${STUB_JAVA_EXIT:-0}"
fi
out="launcher/build/outputs/bundle/release"
mkdir -p "$out"
printf 'fake bundle\n' > "$out/launcher-release.aab"
STUB
  chmod +x "$1/bin/java"
}

FAKE_JDK="$WORK/fakejdk"
write_stub_java "$FAKE_JDK"
printf 'fake gradle launcher jar\n' > "$WORK/gradle-launcher-8.13.jar"

# build-info.env as the real WriteBuildInfo emits it for Android: no gradlew in the
# exported project, so GRADLE_LAUNCHER and JAVA_HOME are the only way in.
make_gradle_export_without_wrapper() {
  _export="$1"
  _artifacts="$2"
  _native="$_export/Wool Gather"
  rm -rf "$_export"
  mkdir -p "$_native/launcher/build/outputs/bundle/release" "$_artifacts"
  printf "include ':launcher'\n" > "$_native/settings.gradle"
  {
    printf "PLATFORM='android'\n"
    printf "PROFILE='Release'\n"
    printf "BUNDLE_ID='com.classicspins.woolgather'\n"
    printf "VERSION='0.1.0'\n"
    printf "BUILD_NUMBER='42'\n"
    printf "BUILD_TIMESTAMP='2608111530'\n"
    printf "EXPORT_DIR='%s'\n" "$_export"
    printf "NATIVE_PROJECT_DIR='%s'\n" "$_native"
    printf "ARTIFACT_DIR='%s'\n" "$_artifacts"
    printf "ARTIFACT_BASENAME='WoolGather_Android_0.1.0-42_2608111530'\n"
    printf "GRADLE_LAUNCHER='%s'\n" "$WORK/gradle-launcher-8.13.jar"
    printf "JAVA_HOME='%s'\n" "$FAKE_JDK"
  } > "$_export/build-info.env"
}

start_case "with no gradlew, Gradle is launched via -cp and GradleMain, not -jar"
export_dir="$WORK/nowrapper/export"
artifact_dir="$WORK/nowrapper/artifacts"
make_gradle_export_without_wrapper "$export_dir" "$artifact_dir"
argv_file="$WORK/nowrapper/java-argv.txt"
out=$(env STUB_JAVA_ARGV_FILE="$argv_file" \
        "$REPO_BUILD_TOOLS/android/bundle_release.sh" --export-dir "$export_dir" 2>&1); status=$?
java_argv=$(cat "$argv_file" 2>/dev/null || echo "<no argv recorded>")
assert_exit "$status" 0 \
  && assert_contains "$java_argv" "-cp" \
  && assert_contains "$java_argv" "org.gradle.launcher.GradleMain" \
  && assert_contains "$java_argv" "bundleRelease" \
  && assert_not_contains "$java_argv" "-jar" \
  && assert_file "$artifact_dir/WoolGather_Android_0.1.0-42_2608111530.aab" && pass

start_case "a missing Gradle launcher jar exits 2 before opening a TeamCity block"
export_dir="$WORK/nolauncher/export"
make_gradle_export_without_wrapper "$export_dir" "$WORK/nolauncher/artifacts"
sed -i.bak "s|GRADLE_LAUNCHER=.*|GRADLE_LAUNCHER='$WORK/absent-launcher.jar'|" "$export_dir/build-info.env"
out=$(env TEAMCITY_VERSION=2024.12 "$REPO_BUILD_TOOLS/android/bundle_release.sh" \
        --export-dir "$export_dir" 2>&1); status=$?
assert_exit "$status" 2 \
  && assert_contains "$out" "Gradle launcher jar not found" \
  && assert_not_contains "$out" "blockOpened" && pass

start_case "an unusable java on PATH is rejected with a clear message"
export_dir="$WORK/badjava/export"
make_gradle_export_without_wrapper "$export_dir" "$WORK/badjava/artifacts"
mkdir -p "$WORK/badjava/bin"
printf '#!/bin/sh\necho "Unable to locate a Java Runtime" >&2\nexit 1\n' > "$WORK/badjava/bin/java"
chmod +x "$WORK/badjava/bin/java"
sed -i.bak "s|JAVA_HOME=.*|JAVA_HOME=''|" "$export_dir/build-info.env"
out=$(env -u JAVA_HOME PATH="$WORK/badjava/bin:/usr/bin:/bin" \
        "$REPO_BUILD_TOOLS/android/bundle_release.sh" --export-dir "$export_dir" 2>&1); status=$?
assert_exit "$status" 2 && assert_contains "$out" "no working Java runtime found" && pass

# build-info.env carries JAVA_HOME under its own name, so sourcing it overwrites the one
# this shell arrived with -- including with an empty value when Unity resolved no JDK. The
# agent's own JAVA_HOME has to survive that as the fallback it has always been. PATH holds
# the unusable java from the case above, so only the fallback can satisfy this.
start_case "an empty JAVA_HOME in build-info.env falls back to the shell's own"
export_dir="$WORK/ambientjava/export"
make_gradle_export_without_wrapper "$export_dir" "$WORK/ambientjava/artifacts"
sed -i.bak "s|JAVA_HOME=.*|JAVA_HOME=''|" "$export_dir/build-info.env"
out=$(env JAVA_HOME="$FAKE_JDK" PATH="$WORK/badjava/bin:/usr/bin:/bin" \
        "$REPO_BUILD_TOOLS/android/bundle_release.sh" --export-dir "$export_dir" 2>&1); status=$?
assert_exit "$status" 0 \
  && assert_file "$WORK/ambientjava/artifacts/WoolGather_Android_0.1.0-42_2608111530.aab" && pass

start_case "a relative artifact dir is resolved before Gradle changes directory"
export_dir="$WORK/relartifact/export"
make_gradle_export_without_wrapper "$export_dir" "$WORK/relartifact/artifacts"
( cd "$WORK/relartifact" && sed -i.bak "s|ARTIFACT_DIR=.*|ARTIFACT_DIR='artifacts'|" "$export_dir/build-info.env" )
out=$(cd "$WORK/relartifact" && "$REPO_BUILD_TOOLS/android/bundle_release.sh" \
        --export-dir "$export_dir" 2>&1); status=$?
assert_exit "$status" 0 \
  && assert_file "$WORK/relartifact/artifacts/WoolGather_Android_0.1.0-42_2608111530.aab" \
  && assert_no_file "$export_dir/Wool Gather/artifacts" && pass

start_case "delivering over an existing artifact is refused"
export_dir="$WORK/overwrite/export"
artifact_dir="$WORK/overwrite/artifacts"
make_gradle_export_without_wrapper "$export_dir" "$artifact_dir"
printf 'already published\n' > "$artifact_dir/WoolGather_Android_0.1.0-42_2608111530.aab"
out=$("$REPO_BUILD_TOOLS/android/bundle_release.sh" --export-dir "$export_dir" 2>&1); status=$?
kept=$(cat "$artifact_dir/WoolGather_Android_0.1.0-42_2608111530.aab")
assert_exit "$status" 1 \
  && assert_contains "$out" "refusing to overwrite" \
  && assert_equals "$kept" "already published" && pass

start_case "OVERWRITE_ARTIFACTS=1 replaces it on purpose"
export_dir="$WORK/overwrite-ok/export"
artifact_dir="$WORK/overwrite-ok/artifacts"
make_gradle_export_without_wrapper "$export_dir" "$artifact_dir"
printf 'stale\n' > "$artifact_dir/WoolGather_Android_0.1.0-42_2608111530.aab"
out=$(env OVERWRITE_ARTIFACTS=1 "$REPO_BUILD_TOOLS/android/bundle_release.sh" \
        --export-dir "$export_dir" 2>&1); status=$?
kept=$(cat "$artifact_dir/WoolGather_Android_0.1.0-42_2608111530.aab")
assert_exit "$status" 0 && assert_equals "$kept" "fake bundle" && pass

# ============================================================================
# build.sh forwards its flags to Unity
# ============================================================================

start_case "build.sh forwards every build flag to the Unity entry point"
project=$(make_project forwarding)
write_stub_unity "$WORK/stub-unity-fwd"
argv_file="$WORK/unity-argv.txt"
out=$(env $ANDROID_ENV STUB_UNITY_ARGV_FILE="$argv_file" "$project/BuildTools/build.sh" \
        --platform android --profile Staging --version 1.2.3 --build-number 77 --development --keep-export \
        --unity "$WORK/stub-unity-fwd" 2>&1); status=$?
argv=$(tr '\n' ' ' < "$argv_file" 2>/dev/null || echo "<none>")
assert_exit "$status" 0 \
  && assert_contains "$argv" "-executeMethod ClassicSpins.BuildTools.BuildCli.ExportAndroid" \
  && assert_contains "$argv" "--profile Staging" \
  && assert_contains "$argv" "--app-version 1.2.3" \
  && assert_contains "$argv" "--build-number 77" \
  && assert_contains "$argv" "--development" \
  && assert_contains "$argv" "--keep-export" \
  && assert_contains "$argv" "-buildTarget Android" && pass

start_case "iOS forwards the export method and the iOS entry point"
project=$(make_project forwarding-ios)
argv_file="$WORK/unity-argv-ios.txt"
out=$(env PATH="$STUB_BIN:$PATH" IOS_TEAM_ID=TEAM123 \
        IOS_PROFILE_APPSTORE="P1" IOS_PROFILE_ADHOC="P2" \
        STUB_UNITY_ARGV_FILE="$argv_file" \
        "$project/BuildTools/build.sh" --platform ios --export-method both --stage unity \
        --unity "$WORK/stub-unity-ios" 2>&1); status=$?
argv=$(tr '\n' ' ' < "$argv_file" 2>/dev/null || echo "<none>")
assert_exit "$status" 0 \
  && assert_contains "$argv" "BuildCli.ExportIos" \
  && assert_contains "$argv" "--export-method both" \
  && assert_contains "$argv" "-buildTarget iOS" && pass

start_case "a build number above int32 is a usage error, not a build failure"
out=$("$REPO_BUILD_TOOLS/build.sh" --platform android --build-number 99999999999 2>&1); status=$?
assert_exit "$status" 2 && assert_contains "$out" "at most 2147483647" && pass

start_case "a directory passed as --unity is rejected"
out=$(env $ANDROID_ENV "$REPO_BUILD_TOOLS/build.sh" --platform android --unity "$WORK" 2>&1); status=$?
assert_exit "$status" 2 && assert_contains "$out" "Unity executable not found" && pass

start_case "--stage native warns that flags it cannot honour are ignored"
project=$(make_project stage-native-warn)
out=$(env $ANDROID_ENV "$project/BuildTools/build.sh" --platform android --stage native \
        --artifact-dir "$WORK/elsewhere" --version 2.0.0 --build-number 5 2>&1); status=$?
assert_contains "$out" "--artifact-dir is ignored with --stage native" \
  && assert_contains "$out" "--version is ignored with --stage native" \
  && assert_contains "$out" "--build-number is ignored with --stage native" && pass

start_case "a build number without --version keeps the version out of the Unity arguments"
project=$(make_project no-version-flag)
write_stub_unity "$WORK/stub-unity-noversion"
argv_file="$WORK/unity-argv-noversion.txt"
out=$(env $ANDROID_ENV STUB_UNITY_ARGV_FILE="$argv_file" "$project/BuildTools/build.sh" \
        --platform android --build-number 5 --unity "$WORK/stub-unity-noversion" 2>&1); status=$?
argv=$(tr '\n' ' ' < "$argv_file" 2>/dev/null || echo "<none>")
assert_exit "$status" 0 && assert_not_contains "$argv" "app-version" && pass

# Real Unity reads a bare --version anywhere on the command line as its own -version: it
# prints the editor version, exits 0, and never runs -executeMethod, so the export silently
# produces nothing and the build fails later on a missing build-info.env. The stub Unity
# cannot reproduce that, so pin the argument name instead.
start_case "the version reaches Unity under a name Unity does not claim for itself"
project=$(make_project version-flag-name)
write_stub_unity "$WORK/stub-unity-flagname"
argv_file="$WORK/unity-argv-flagname.txt"
out=$(env $ANDROID_ENV STUB_UNITY_ARGV_FILE="$argv_file" "$project/BuildTools/build.sh" \
        --platform android --version 1.2.3 --unity "$WORK/stub-unity-flagname" 2>&1); status=$?
argv=$(tr '\n' ' ' < "$argv_file" 2>/dev/null || echo "<none>")
assert_exit "$status" 0 \
  && assert_contains "$argv" "--app-version 1.2.3" \
  && assert_not_contains "$argv" "--version" && pass

start_case "a Unity crash exit code is normalised to the documented 1"
project=$(make_project unity-crash)
write_stub_unity "$WORK/stub-unity-crash"
out=$(env $ANDROID_ENV STUB_UNITY_EXIT=139 "$project/BuildTools/build.sh" \
        --platform android --unity "$WORK/stub-unity-crash" 2>&1); status=$?
assert_exit "$status" 1 && assert_contains "$out" "Unity exit 139" && pass

# ============================================================================
# iOS packaging with a stub xcodebuild
# ============================================================================

# run_ios_build <project> <extra_env> [build.sh flags...]
# extra_env is a whitespace-separated list of NAME=value stub knobs, kept separate
# from the flags because env(1) stops reading assignments at the first non-assignment.
run_ios_build() {
  _project="$1"
  _extra_env="$2"
  shift 2
  env PATH="$STUB_BIN:$PATH" \
    IOS_TEAM_ID=TEAM123 \
    IOS_PROFILE_APPSTORE="WG AppStore Profile" \
    IOS_PROFILE_ADHOC="WG AdHoc Profile" \
    $_extra_env \
    "$_project/BuildTools/build.sh" --platform ios --unity "$WORK/stub-unity-ios" "$@" 2>&1
}

write_stub_unity "$WORK/stub-unity-ios"

start_case "iOS appstore export delivers one .ipa with the method in its name"
project=$(make_project ios-appstore)
out=$(run_ios_build "$project" "" --export-method appstore); status=$?
assert_exit "$status" 0 \
  && assert_file "$project/Build/Artifacts/WoolGather_iOS_0.1.0-42_appstore_2608111530.ipa" && pass

start_case "Xcode 26 gets the modern export method name"
project=$(make_project ios-modern-method)
out=$(run_ios_build "$project" "STUB_XCODE_VERSION=26.3" --export-method appstore); status=$?
plist="$project/Build/iOS/XcodeProject/build/ExportOptions.appstore.plist"
assert_exit "$status" 0 && assert_file "$plist" \
  && assert_contains "$(cat "$plist")" "<string>app-store-connect</string>" && pass

start_case "Xcode 14 gets the legacy export method name"
project=$(make_project ios-legacy-method)
out=$(run_ios_build "$project" "STUB_XCODE_VERSION=14.3" --export-method appstore); status=$?
plist="$project/Build/iOS/XcodeProject/build/ExportOptions.appstore.plist"
assert_exit "$status" 0 && assert_file "$plist" \
  && assert_contains "$(cat "$plist")" "<string>app-store</string>" \
  && assert_not_contains "$(cat "$plist")" "app-store-connect" && pass

start_case "Xcode 15.3 is the boundary for the modern names"
project=$(make_project ios-boundary)
out=$(run_ios_build "$project" "STUB_XCODE_VERSION=15.3" --export-method adhoc); status=$?
plist="$project/Build/iOS/XcodeProject/build/ExportOptions.adhoc.plist"
assert_exit "$status" 0 && assert_file "$plist" \
  && assert_contains "$(cat "$plist")" "<string>release-testing</string>" && pass

start_case "the rendered plist carries team, bundle id and profile, with no placeholders left"
project=$(make_project ios-plist-content)
out=$(run_ios_build "$project" "" --export-method appstore); status=$?
plist=$(cat "$project/Build/iOS/XcodeProject/build/ExportOptions.appstore.plist")
assert_exit "$status" 0 \
  && assert_contains "$plist" "<string>TEAM123</string>" \
  && assert_contains "$plist" "<key>com.classicspins.woolgather</key>" \
  && assert_contains "$plist" "<string>WG AppStore Profile</string>" \
  && assert_not_contains "$plist" "__" && pass

start_case "--export-method both delivers one .ipa per method from a single archive"
project=$(make_project ios-both)
out=$(run_ios_build "$project" "" --export-method both); status=$?
assert_exit "$status" 0 \
  && assert_file "$project/Build/Artifacts/WoolGather_iOS_0.1.0-42_appstore_2608111530.ipa" \
  && assert_file "$project/Build/Artifacts/WoolGather_iOS_0.1.0-42_adhoc_2608111530.ipa" && pass

start_case "a failing archive exits 1 and delivers nothing"
project=$(make_project ios-archive-fails)
out=$(run_ios_build "$project" "STUB_XCODE_ARCHIVE_EXIT=65" --export-method appstore); status=$?
assert_exit "$status" 1 \
  && assert_contains "$out" "xcodebuild archive failed" \
  && assert_no_file "$project/Build/Artifacts/WoolGather_iOS_0.1.0-42_appstore_2608111530.ipa" && pass

start_case "a failing export names the method that failed"
project=$(make_project ios-export-fails)
out=$(run_ios_build "$project" "STUB_XCODE_EXPORT_EXIT=70" --export-method adhoc); status=$?
assert_exit "$status" 1 && assert_contains "$out" "exportArchive failed for method 'adhoc'" && pass

start_case "zero .ipa produced exits 1"
project=$(make_project ios-no-ipa)
out=$(run_ios_build "$project" "STUB_XCODE_IPA_COUNT=0" --export-method appstore); status=$?
assert_exit "$status" 1 && assert_contains "$out" "no exported ipa" && pass

start_case "several .ipa produced exits 1 and lists them"
project=$(make_project ios-many-ipa)
out=$(run_ios_build "$project" "STUB_XCODE_IPA_COUNT=2" --export-method appstore); status=$?
assert_exit "$status" 1 && assert_contains "$out" "expected exactly one exported ipa" && pass

# build-info.env records EXPORT_DIR under the same name the packaging scripts use for the
# directory they were pointed at, so sourcing it must not displace the one on the command
# line -- otherwise the xcarchive lands wherever the Unity stage happened to run.
start_case "the EXPORT_DIR in build-info.env does not displace --export-dir"
project=$(make_project ios-export-dir-wins)
out=$(run_ios_build "$project" "" --export-method appstore); status=$?
export_dir="$project/Build/iOS/XcodeProject"
sed -i.bak "s|^EXPORT_DIR=.*|EXPORT_DIR='$WORK/somewhere-else'|" "$export_dir/build-info.env"
rm -rf "$export_dir/build"
out2=$(env PATH="$STUB_BIN:$PATH" IOS_TEAM_ID=TEAM123 \
        IOS_PROFILE_APPSTORE="WG AppStore Profile" OVERWRITE_ARTIFACTS=1 \
        "$project/BuildTools/ios/archive_and_export.sh" \
        --export-dir "$export_dir" --method appstore 2>&1); status2=$?
assert_exit "$status" 0 && assert_exit "$status2" 0 \
  && assert_file "$export_dir/build/ExportOptions.appstore.plist" \
  && assert_no_file "$WORK/somewhere-else" && pass

# ============================================================================
# iOS automatic signing
# ============================================================================

start_case "auto signing needs no profile names, only a team id"
project=$(make_project ios-auto-noprofiles)
out=$(env -u IOS_PROFILE_APPSTORE -u IOS_PROFILE_ADHOC PATH="$STUB_BIN:$PATH" \
        IOS_TEAM_ID=TEAM123 \
        "$project/BuildTools/build.sh" --platform ios --signing auto \
        --unity "$WORK/stub-unity-ios" 2>&1); status=$?
assert_exit "$status" 0 \
  && assert_file "$project/Build/Artifacts/WoolGather_iOS_0.1.0-42_appstore_2608111530.ipa" && pass

start_case "auto signing still requires the team id"
project=$(make_project ios-auto-noteam)
out=$(env -u IOS_TEAM_ID PATH="$STUB_BIN:$PATH" \
        "$project/BuildTools/build.sh" --platform ios --signing auto \
        --unity "$WORK/stub-unity-ios" 2>&1); status=$?
assert_exit "$status" 2 && assert_contains "$out" "IOS_TEAM_ID" && pass

start_case "manual signing still demands the profile name"
project=$(make_project ios-manual-noprofile)
out=$(env -u IOS_PROFILE_APPSTORE PATH="$STUB_BIN:$PATH" IOS_TEAM_ID=TEAM123 \
        "$project/BuildTools/build.sh" --platform ios --signing manual \
        --unity "$WORK/stub-unity-ios" 2>&1); status=$?
assert_exit "$status" 2 && assert_contains "$out" "IOS_PROFILE_APPSTORE" && pass

start_case "auto signing renders signingStyle automatic with no provisioningProfiles"
project=$(make_project ios-auto-plist)
out=$(run_ios_build "$project" "" --signing auto --export-method appstore); status=$?
plist=$(cat "$project/Build/iOS/XcodeProject/build/ExportOptions.appstore.plist" 2>/dev/null || echo "")
assert_exit "$status" 0 \
  && assert_contains "$plist" "<string>automatic</string>" \
  && assert_not_contains "$plist" "<key>provisioningProfiles</key>" \
  && assert_contains "$plist" "<string>TEAM123</string>" \
  && assert_not_contains "$plist" "__" && pass

start_case "manual signing keeps signingStyle manual with the profile mapping"
project=$(make_project ios-manual-plist)
out=$(run_ios_build "$project" "" --signing manual --export-method appstore); status=$?
plist=$(cat "$project/Build/iOS/XcodeProject/build/ExportOptions.appstore.plist" 2>/dev/null || echo "")
assert_exit "$status" 0 \
  && assert_contains "$plist" "<string>manual</string>" \
  && assert_contains "$plist" "<key>provisioningProfiles</key>" \
  && assert_contains "$plist" "<string>WG AppStore Profile</string>" && pass

start_case "auto signing passes -allowProvisioningUpdates and pins no identity"
project=$(make_project ios-auto-flags)
argv_file="$WORK/xcode-argv-auto.txt"
: > "$argv_file"
out=$(run_ios_build "$project" "STUB_XCODE_ARGV_FILE=$argv_file" --signing auto --export-method appstore); status=$?
argv=$(cat "$argv_file" 2>/dev/null || echo "")
assert_exit "$status" 0 \
  && assert_contains "$argv" "-allowProvisioningUpdates" \
  && assert_contains "$argv" "CODE_SIGN_STYLE=Automatic" \
  && assert_contains "$argv" "DEVELOPMENT_TEAM=TEAM123" \
  && assert_not_contains "$argv" "PROVISIONING_PROFILE_SPECIFIER" \
  && assert_not_contains "$argv" "CODE_SIGN_IDENTITY" && pass

start_case "manual signing pins style, identity and profile, and never allows updates"
project=$(make_project ios-manual-flags)
argv_file="$WORK/xcode-argv-manual.txt"
: > "$argv_file"
out=$(run_ios_build "$project" "STUB_XCODE_ARGV_FILE=$argv_file" --signing manual --export-method appstore); status=$?
argv=$(cat "$argv_file" 2>/dev/null || echo "")
assert_exit "$status" 0 \
  && assert_contains "$argv" "CODE_SIGN_STYLE=Manual" \
  && assert_contains "$argv" "PROVISIONING_PROFILE_SPECIFIER=WG AppStore Profile" \
  && assert_contains "$argv" "CODE_SIGN_IDENTITY=Apple Distribution" \
  && assert_not_contains "$argv" "-allowProvisioningUpdates" && pass

start_case "IOS_SIGNING_USE_ASC_KEY=1 forwards the API key to both archive and export"
project=$(make_project ios-asc-key)
printf 'not a real p8 key\n' > "$WORK/asc.p8"
argv_file="$WORK/xcode-argv-asc.txt"
: > "$argv_file"
out=$(env PATH="$STUB_BIN:$PATH" IOS_TEAM_ID=TEAM123 IOS_SIGNING_USE_ASC_KEY=1 \
        IOS_ASC_KEY_PATH="$WORK/asc.p8" IOS_ASC_KEY_ID=KEY1 IOS_ASC_ISSUER_ID=ISS1 \
        STUB_XCODE_ARGV_FILE="$argv_file" \
        "$project/BuildTools/build.sh" --platform ios --signing auto \
        --unity "$WORK/stub-unity-ios" 2>&1); status=$?
lines_with_key=$(grep -c -- "-authenticationKeyID KEY1" "$argv_file" 2>/dev/null || echo 0)
assert_exit "$status" 0 && assert_equals "$lines_with_key" "2" && pass

# The regression this guards: an upload flag used to drag the key into the signing session,
# where a key without cloud-managed distribution certificate access fails the export that
# the same machine had just completed without it.
start_case "without the opt-in the API key reaches neither archive nor export"
project=$(make_project ios-asc-key-unused)
printf 'not a real p8 key\n' > "$WORK/asc-unused.p8"
argv_file="$WORK/xcode-argv-asc-unused.txt"
: > "$argv_file"
out=$(env PATH="$STUB_BIN:$PATH" IOS_TEAM_ID=TEAM123 \
        IOS_ASC_KEY_PATH="$WORK/asc-unused.p8" IOS_ASC_KEY_ID=KEY1 IOS_ASC_ISSUER_ID=ISS1 \
        STUB_XCODE_ARGV_FILE="$argv_file" \
        "$project/BuildTools/build.sh" --platform ios --signing auto \
        --unity "$WORK/stub-unity-ios" 2>&1); status=$?
argv=$(cat "$argv_file" 2>/dev/null || echo "")
assert_exit "$status" 0 \
  && assert_not_contains "$argv" "-authenticationKeyID" \
  && assert_not_contains "$argv" "-authenticationKeyPath" \
  && assert_contains "$out" "not using the App Store Connect API key" && pass

start_case "a partial App Store Connect API key is rejected, naming what is missing"
project=$(make_project ios-asc-partial)
out=$(env PATH="$STUB_BIN:$PATH" IOS_TEAM_ID=TEAM123 IOS_SIGNING_USE_ASC_KEY=1 \
        IOS_ASC_KEY_ID=KEY1 \
        "$project/BuildTools/build.sh" --platform ios --signing auto \
        --unity "$WORK/stub-unity-ios" 2>&1); status=$?
assert_exit "$status" 2 \
  && assert_contains "$out" "IOS_ASC_KEY_PATH" \
  && assert_contains "$out" "IOS_ASC_ISSUER_ID" && pass

start_case "auto signing without an API key warns about the keychain"
project=$(make_project ios-auto-warn)
out=$(run_ios_build "$project" "" --signing auto --export-method appstore); status=$?
assert_exit "$status" 0 && assert_contains "$out" "no App Store Connect API key" && pass

start_case "an invalid --signing value exits 2"
out=$("$REPO_BUILD_TOOLS/build.sh" --platform ios --signing sometimes 2>&1); status=$?
assert_exit "$status" 2 && assert_contains "$out" "--signing must be manual or auto" && pass

start_case "build.sh forwards --signing to the Unity entry point"
project=$(make_project forwarding-signing)
argv_file="$WORK/unity-argv-signing.txt"
out=$(env PATH="$STUB_BIN:$PATH" IOS_TEAM_ID=TEAM123 STUB_UNITY_ARGV_FILE="$argv_file" \
        "$project/BuildTools/build.sh" --platform ios --signing auto --stage unity \
        --unity "$WORK/stub-unity-ios" 2>&1); status=$?
argv=$(tr '\n' ' ' < "$argv_file" 2>/dev/null || echo "<none>")
assert_exit "$status" 0 && assert_contains "$argv" "--signing auto" && pass

# ============================================================================
# iOS TestFlight upload with a stub xcrun / altool
# ============================================================================

# run_ios_upload_build <project> <extra_env> [build.sh flags...]
# run_ios_build plus the three App Store Connect variables an upload needs.
run_ios_upload_build() {
  _project="$1"
  _extra_env="$2"
  shift 2
  env PATH="$STUB_BIN:$PATH" \
    IOS_TEAM_ID=TEAM123 \
    IOS_PROFILE_APPSTORE="WG AppStore Profile" \
    IOS_PROFILE_ADHOC="WG AdHoc Profile" \
    $ASC_ENV \
    $_extra_env \
    "$_project/BuildTools/build.sh" --platform ios --unity "$WORK/stub-unity-ios" "$@" 2>&1
}

# make_ios_export <name> [export-method] [extra_env]
# A stub Unity export and nothing else: build-info.env plus an Xcode project, no packaging
# and no artifact. Prints the project root. For cases that only need a build info to point
# the upload script at.
make_ios_export() {
  _export_project=$(make_project "$1")
  env PATH="$STUB_BIN:$PATH" IOS_TEAM_ID=TEAM123 ${3:-} \
    "$_export_project/BuildTools/build.sh" --platform ios --stage unity \
    --export-method "${2:-appstore}" --unity "$WORK/stub-unity-ios" >/dev/null 2>&1
  printf '%s\n' "$_export_project"
}

start_case "--upload-testflight hands altool the .ipa the packaging stage delivered"
project=$(make_project ios-upload-ok)
argv_file="$WORK/xcrun-argv-ok.txt"
: > "$argv_file"
out=$(run_ios_upload_build "$project" "STUB_XCRUN_ARGV_FILE=$argv_file" \
        --export-method appstore --upload-testflight); status=$?
argv=$(cat "$argv_file" 2>/dev/null || echo "")
ipa="$project/Build/Artifacts/WoolGather_iOS_0.1.0-42_appstore_2608111530.ipa"
assert_exit "$status" 0 \
  && assert_file "$ipa" \
  && assert_contains "$argv" "altool --upload-app" \
  && assert_contains "$argv" "-f $ipa" \
  && assert_contains "$argv" "-t ios" \
  && assert_contains "$argv" "--apiKey KEYID1" \
  && assert_contains "$argv" "--apiIssuer ISSUER1" \
  && assert_contains "$argv" "--p8-file-path $ASC_KEY" && pass

start_case "after an upload the last line of stdout is still the artifact path"
project=$(make_project ios-upload-stdout)
out=$(env PATH="$STUB_BIN:$PATH" IOS_TEAM_ID=TEAM123 \
        IOS_PROFILE_APPSTORE="WG AppStore Profile" $ASC_ENV \
        "$project/BuildTools/build.sh" --platform ios --export-method appstore \
        --upload-testflight --unity "$WORK/stub-unity-ios" 2>/dev/null); status=$?
expected="$project/Build/Artifacts/WoolGather_iOS_0.1.0-42_appstore_2608111530.ipa"
last_line=$(printf '%s\n' "$out" | tail -1)
# altool's own chatter must be on stderr, or it would be the last thing on stdout.
assert_exit "$status" 0 \
  && assert_equals "$last_line" "$expected" \
  && assert_not_contains "$out" "stub altool" && pass

start_case "a build without --upload-testflight never invokes altool and needs no ASC variable"
project=$(make_project ios-upload-off)
argv_file="$WORK/xcrun-argv-off.txt"
: > "$argv_file"
out=$(env -u IOS_ASC_KEY_PATH -u IOS_ASC_KEY_ID -u IOS_ASC_ISSUER_ID \
        PATH="$STUB_BIN:$PATH" IOS_TEAM_ID=TEAM123 \
        IOS_PROFILE_APPSTORE="WG AppStore Profile" STUB_XCRUN_ARGV_FILE="$argv_file" \
        "$project/BuildTools/build.sh" --platform ios --export-method appstore \
        --unity "$WORK/stub-unity-ios" 2>&1); status=$?
recorded=$(cat "$argv_file" 2>/dev/null || echo "")
assert_exit "$status" 0 \
  && assert_equals "$recorded" "" \
  && assert_not_contains "$out" "TestFlight" && pass

start_case "--export-method both uploads only the appstore .ipa and says which"
project=$(make_project ios-upload-both)
argv_file="$WORK/xcrun-argv-both.txt"
: > "$argv_file"
out=$(run_ios_upload_build "$project" "STUB_XCRUN_ARGV_FILE=$argv_file" \
        --export-method both --upload-testflight); status=$?
argv=$(cat "$argv_file" 2>/dev/null || echo "")
uploads=$(grep -c 'upload-app' "$argv_file" 2>/dev/null || echo 0)
assert_exit "$status" 0 \
  && assert_file "$project/Build/Artifacts/WoolGather_iOS_0.1.0-42_appstore_2608111530.ipa" \
  && assert_file "$project/Build/Artifacts/WoolGather_iOS_0.1.0-42_adhoc_2608111530.ipa" \
  && assert_equals "$uploads" "1" \
  && assert_contains "$argv" "WoolGather_iOS_0.1.0-42_appstore_2608111530.ipa" \
  && assert_not_contains "$argv" "adhoc_2608111530.ipa" \
  && assert_contains "$out" "only the App Store one is uploaded" && pass

start_case "with --export-method both the last line of stdout is the uploaded App Store artifact"
project=$(make_project ios-upload-both-stdout)
out=$(env PATH="$STUB_BIN:$PATH" IOS_TEAM_ID=TEAM123 \
        IOS_PROFILE_APPSTORE="WG AppStore Profile" IOS_PROFILE_ADHOC="WG AdHoc Profile" \
        $ASC_ENV "$project/BuildTools/build.sh" --platform ios --export-method both \
        --upload-testflight --unity "$WORK/stub-unity-ios" 2>/dev/null); status=$?
appstore="$project/Build/Artifacts/WoolGather_iOS_0.1.0-42_appstore_2608111530.ipa"
adhoc="$project/Build/Artifacts/WoolGather_iOS_0.1.0-42_adhoc_2608111530.ipa"
last_line=$(printf '%s\n' "$out" | tail -1)
# Both delivered paths are on stdout, then the upload names what it sent -- so the App Store
# path appears twice and is last, rather than whichever method happened to be exported last.
# Not a line count: xcodebuild's own stdout is inherited and is not ours to predict.
appstore_lines=$(printf '%s\n' "$out" | grep -c -F "$appstore")
assert_exit "$status" 0 \
  && assert_equals "$last_line" "$appstore" \
  && assert_equals "$appstore_lines" "2" \
  && assert_contains "$out" "$adhoc" && pass

start_case "--upload-testflight never reaches the Unity command line"
# Unity scans the whole command line even past `--` and acts on names it claims, so a
# plausible "forward every flag" edit would break every TestFlight build. Same guard the
# --app-version case applies. The project name deliberately contains no 'upload', so the
# paths in argv cannot mask the assertion.
project=$(make_project ios-tf-not-forwarded)
argv_file="$WORK/unity-argv-testflight.txt"
out=$(run_ios_upload_build "$project" "STUB_UNITY_ARGV_FILE=$argv_file" \
        --export-method appstore --upload-testflight); status=$?
argv=$(tr '\n' ' ' < "$argv_file" 2>/dev/null || echo "<none>")
assert_exit "$status" 0 \
  && assert_contains "$argv" "BuildCli.ExportIos" \
  && assert_not_contains "$argv" "--upload-testflight" \
  && assert_not_contains "$argv" "upload" && pass

start_case "--stage native --upload-testflight packages and uploads in one step"
project=$(make_project ios-native-upload)
out=$(run_ios_build "$project" "" --export-method appstore --stage unity); status=$?
argv_file="$WORK/xcrun-argv-native.txt"
: > "$argv_file"
out2=$(run_ios_upload_build "$project" "STUB_XCRUN_ARGV_FILE=$argv_file" \
        --export-method appstore --stage native --upload-testflight); status2=$?
argv=$(cat "$argv_file" 2>/dev/null || echo "")
assert_exit "$status" 0 && assert_exit "$status2" 0 \
  && assert_contains "$out2" "stub xcodebuild" \
  && assert_file "$project/Build/Artifacts/WoolGather_iOS_0.1.0-42_appstore_2608111530.ipa" \
  && assert_contains "$argv" "altool --upload-app" && pass

start_case "an ad-hoc-only export is refused by naming what it produced"
# The cause, not the symptom: WriteBuildInfo writes both artifact basenames whatever was
# exported, so the appstore name resolves fine and only the recorded export methods can say
# there is nothing here to upload.
project=$(make_ios_export ios-upload-adhoc-only adhoc)
out=$(env PATH="$STUB_BIN:$PATH" $ASC_ENV \
        "$project/BuildTools/build.sh" --platform ios --stage upload 2>&1); status=$?
assert_exit "$status" 2 \
  && assert_contains "$out" "produced export method(s) 'adhoc'" \
  && assert_contains "$out" "TestFlight needs an App Store build" \
  && assert_not_contains "$out" "not found at" && pass

start_case "an ad-hoc-only export is refused even when its build info names no appstore artifact"
project=$(make_ios_export ios-upload-adhoc-lean adhoc "STUB_UNITY_BASENAMES=matching")
out=$(env PATH="$STUB_BIN:$PATH" $ASC_ENV \
        "$project/BuildTools/build.sh" --platform ios --stage upload 2>&1); status=$?
assert_exit "$status" 2 \
  && assert_contains "$out" "produced export method(s) 'adhoc'" \
  && assert_not_contains "$out" "ARTIFACT_BASENAME_APPSTORE" && pass

start_case "--ipa uploads the named file instead of the one build-info.env points at"
project=$(make_ios_export ios-upload-ipa-override)
override="$WORK/hand-picked.ipa"
printf 'hand-picked ipa\n' > "$override"
argv_file="$WORK/xcrun-argv-ipa.txt"
: > "$argv_file"
# No packaging stage ran, so the build-info basename resolves to a file that does not exist:
# succeeding here proves the override replaces that resolution rather than adding to it.
out=$(env PATH="$STUB_BIN:$PATH" $ASC_ENV STUB_XCRUN_ARGV_FILE="$argv_file" \
        "$REPO_BUILD_TOOLS/ios/upload_testflight.sh" \
        --export-dir "$project/Build/iOS/XcodeProject" --ipa "$override" 2>&1); status=$?
argv=$(cat "$argv_file" 2>/dev/null || echo "")
assert_exit "$status" 0 \
  && assert_contains "$argv" "-f $override" \
  && assert_not_contains "$argv" "WoolGather_iOS" && pass

start_case "--ipa pointing at an empty file is refused"
project=$(make_ios_export ios-upload-ipa-empty)
empty="$WORK/empty.ipa"
: > "$empty"
out=$(env PATH="$STUB_BIN:$PATH" $ASC_ENV "$REPO_BUILD_TOOLS/ios/upload_testflight.sh" \
        --export-dir "$project/Build/iOS/XcodeProject" --ipa "$empty" 2>&1); status=$?
assert_exit "$status" 2 && assert_contains "$out" "is empty: $empty" && pass

start_case "--ipa pointing at nothing is refused, naming the path"
project=$(make_ios_export ios-upload-ipa-absent)
out=$(env PATH="$STUB_BIN:$PATH" $ASC_ENV "$REPO_BUILD_TOOLS/ios/upload_testflight.sh" \
        --export-dir "$project/Build/iOS/XcodeProject" --ipa "$WORK/no-such.ipa" 2>&1); status=$?
assert_exit "$status" 2 && assert_contains "$out" "not found at: $WORK/no-such.ipa" && pass

start_case "a failing altool exits 1, surfaces its output and leaves the artifact on disk"
project=$(make_project ios-upload-fails)
out=$(run_ios_upload_build "$project" "STUB_ALTOOL_EXIT=1" \
        --export-method appstore --upload-testflight); status=$?
assert_exit "$status" 1 \
  && assert_contains "$out" "pretending the upload failed" \
  && assert_contains "$out" "altool --upload-app failed" \
  && assert_file "$project/Build/Artifacts/WoolGather_iOS_0.1.0-42_appstore_2608111530.ipa" && pass

start_case "--stage upload re-sends what a failed upload left behind, with no signing credentials"
project=$(make_project ios-upload-resend)
out=$(run_ios_upload_build "$project" "STUB_ALTOOL_EXIT=1" \
        --export-method appstore --upload-testflight); status=$?
argv_file="$WORK/xcrun-argv-resend.txt"
: > "$argv_file"
out2=$(env -u IOS_TEAM_ID -u IOS_PROFILE_APPSTORE -u IOS_PROFILE_ADHOC \
        PATH="$STUB_BIN:$PATH" $ASC_ENV STUB_XCRUN_ARGV_FILE="$argv_file" \
        "$project/BuildTools/build.sh" --platform ios --stage upload 2>&1); status2=$?
argv=$(cat "$argv_file" 2>/dev/null || echo "")
assert_exit "$status" 1 && assert_exit "$status2" 0 \
  && assert_contains "$argv" "WoolGather_iOS_0.1.0-42_appstore_2608111530.ipa" \
  && assert_not_contains "$out2" "stub xcodebuild" \
  && assert_not_contains "$out2" "Unity:" && pass

start_case "--stage upload with no previous export exits 2, naming the directory checked"
project=$(make_project ios-upload-noexport)
out=$(env PATH="$STUB_BIN:$PATH" $ASC_ENV \
        "$project/BuildTools/build.sh" --platform ios --stage upload 2>&1); status=$?
assert_exit "$status" 2 \
  && assert_contains "$out" "export directory not found at: $project/Build/iOS/XcodeProject" && pass

start_case "--stage upload with no build-info.env exits 2, naming the file checked"
project=$(make_project ios-upload-noinfo)
mkdir -p "$project/Build/iOS/XcodeProject"
out=$(env PATH="$STUB_BIN:$PATH" $ASC_ENV \
        "$project/BuildTools/build.sh" --platform ios --stage upload 2>&1); status=$?
assert_exit "$status" 2 \
  && assert_contains "$out" "build-info.env not found at: $project/Build/iOS/XcodeProject/build-info.env" && pass

start_case "--stage upload with the delivered .ipa gone exits 2, naming the path checked"
project=$(make_project ios-upload-noipa)
out=$(run_ios_upload_build "$project" "" --export-method appstore); status=$?
gone="$project/Build/Artifacts/WoolGather_iOS_0.1.0-42_appstore_2608111530.ipa"
rm -f "$gone"
out=$(env PATH="$STUB_BIN:$PATH" $ASC_ENV \
        "$project/BuildTools/build.sh" --platform ios --stage upload 2>&1); status2=$?
assert_exit "$status" 0 && assert_exit "$status2" 2 \
  && assert_contains "$out" "App Store .ipa to upload not found at: $gone" && pass

start_case "--upload-testflight with the ASC variables unset lists all three before Unity starts"
project=$(make_project ios-upload-nocreds)
out=$(env -u IOS_ASC_KEY_PATH -u IOS_ASC_KEY_ID -u IOS_ASC_ISSUER_ID \
        PATH="$STUB_BIN:$PATH" IOS_TEAM_ID=TEAM123 \
        IOS_PROFILE_APPSTORE="WG AppStore Profile" \
        "$project/BuildTools/build.sh" --platform ios --upload-testflight \
        --unity "$WORK/stub-unity-ios" 2>&1); status=$?
assert_exit "$status" 2 \
  && assert_contains "$out" "IOS_ASC_KEY_PATH" \
  && assert_contains "$out" "IOS_ASC_KEY_ID" \
  && assert_contains "$out" "IOS_ASC_ISSUER_ID" \
  && assert_not_contains "$out" "Unity:" && pass

start_case "one missing ASC variable is named without echoing what the others hold"
project=$(make_project ios-upload-onecred)
out=$(env -u IOS_ASC_KEY_ID PATH="$STUB_BIN:$PATH" IOS_TEAM_ID=TEAM123 \
        IOS_PROFILE_APPSTORE="WG AppStore Profile" IOS_ASC_KEY_PATH="$ASC_KEY" \
        IOS_ASC_ISSUER_ID=issuer-value-that-must-not-be-echoed \
        "$project/BuildTools/build.sh" --platform ios --upload-testflight \
        --unity "$WORK/stub-unity-ios" 2>&1); status=$?
assert_exit "$status" 2 \
  && assert_contains "$out" "IOS_ASC_KEY_ID" \
  && assert_not_contains "$out" "issuer-value-that-must-not-be-echoed" && pass

start_case "an absent App Store Connect API key exits 2 before Unity, showing the path"
project=$(make_project ios-upload-nokey)
out=$(env PATH="$STUB_BIN:$PATH" IOS_TEAM_ID=TEAM123 \
        IOS_PROFILE_APPSTORE="WG AppStore Profile" \
        IOS_ASC_KEY_PATH="$WORK/definitely-absent.p8" \
        IOS_ASC_KEY_ID=KEYID1 IOS_ASC_ISSUER_ID=ISSUER1 \
        "$project/BuildTools/build.sh" --platform ios --upload-testflight \
        --unity "$WORK/stub-unity-ios" 2>&1); status=$?
assert_exit "$status" 2 \
  && assert_contains "$out" "definitely-absent.p8" \
  && assert_not_contains "$out" "Unity:" && pass

start_case "no xcrun on PATH exits 2, naming the tool"
export_dir="$WORK/noxcrun/export"
mkdir -p "$export_dir"
printf "PLATFORM='ios'\n" > "$export_dir/build-info.env"
out=$(env PATH="$NO_XCRUN_BIN" $ASC_ENV \
        "$REPO_BUILD_TOOLS/ios/upload_testflight.sh" --export-dir "$export_dir" 2>&1); status=$?
assert_exit "$status" 2 && assert_contains "$out" "xcrun" && pass

start_case "--upload-testflight with no xcrun on PATH exits 2 before Unity starts"
# The whole point of the fail-early gate: learning that the machine has no command line
# tools after a twenty minute export is the expensive way to find out.
project=$(make_project ios-upload-noxcrun)
out=$(env PATH="$NO_XCRUN_BIN" $ASC_ENV IOS_TEAM_ID=TEAM123 \
        IOS_PROFILE_APPSTORE="WG AppStore Profile" \
        "$project/BuildTools/build.sh" --platform ios --upload-testflight \
        --unity "$WORK/stub-unity-ios" 2>&1); status=$?
assert_exit "$status" 2 \
  && assert_contains "$out" "xcrun" \
  && assert_not_contains "$out" "Unity:" && pass

start_case "an upload against an Android export exits 2, naming the platform found"
export_dir="$WORK/upload-wrong-platform/export"
mkdir -p "$export_dir"
printf "PLATFORM='android'\nARTIFACT_DIR='%s'\nARTIFACT_BASENAME_APPSTORE='nope'\n" \
  "$WORK/upload-wrong-platform" > "$export_dir/build-info.env"
out=$(env PATH="$STUB_BIN:$PATH" $ASC_ENV \
        "$REPO_BUILD_TOOLS/ios/upload_testflight.sh" --export-dir "$export_dir" 2>&1); status=$?
assert_exit "$status" 2 && assert_contains "$out" "describes platform 'android', not ios" && pass

start_case "--stage upload warns that flags it cannot honour are ignored"
# Against a real previous run, so the warnings are the only thing this case turns on and the
# upload itself still succeeds. --export-method and --signing are ignored by the upload stage
# specifically -- the packaging stage does read them, so --stage native must not warn.
project=$(make_project ios-upload-ignored-flags)
out=$(run_ios_upload_build "$project" "" --export-method appstore --upload-testflight); status=$?
out2=$(env PATH="$STUB_BIN:$PATH" $ASC_ENV "$project/BuildTools/build.sh" --platform ios \
        --stage upload --build-number 5 --version 2.0.0 --export-method both --signing auto 2>&1); status2=$?
assert_exit "$status" 0 && assert_exit "$status2" 0 \
  && assert_contains "$out2" "--build-number is ignored with --stage upload" \
  && assert_contains "$out2" "--version is ignored with --stage upload" \
  && assert_contains "$out2" "--export-method is ignored with --stage upload" \
  && assert_contains "$out2" "--signing is ignored with --stage upload" && pass

start_case "--stage native does not claim --export-method or --signing are ignored"
project=$(make_project ios-native-not-ignored)
out=$(run_ios_build "$project" "" --export-method appstore --stage unity); status=$?
out2=$(run_ios_build "$project" "" --stage native --export-method appstore --signing manual); status2=$?
assert_exit "$status" 0 && assert_exit "$status2" 0 \
  && assert_not_contains "$out2" "--export-method is ignored" \
  && assert_not_contains "$out2" "--signing is ignored" && pass

start_case "upload_testflight.sh --help exits 0"
out=$("$REPO_BUILD_TOOLS/ios/upload_testflight.sh" --help 2>/dev/null); status=$?
assert_exit "$status" 0 \
  && assert_contains "$out" "Usage: BuildTools/ios/upload_testflight.sh" && pass

start_case "upload_testflight.sh without --export-dir exits 2"
out=$("$REPO_BUILD_TOOLS/ios/upload_testflight.sh" 2>&1); status=$?
assert_exit "$status" 2 && assert_contains "$out" "--export-dir is required" && pass

# ============================================================================
# TeamCity integration
# ============================================================================

start_case "a failure under TeamCity emits exactly one buildProblem"
project=$(make_project teamcity-failure)
write_stub_unity "$WORK/stub-unity-tc"
out=$(env $ANDROID_ENV TEAMCITY_VERSION=2024.12 STUB_UNITY_EXIT=1 \
        "$project/BuildTools/build.sh" --platform android --unity "$WORK/stub-unity-tc" 2>&1); status=$?
problems=$(printf '%s\n' "$out" | grep -c 'buildProblem')
assert_exit "$status" 1 && assert_equals "$problems" "1" && pass

start_case "a usage error under TeamCity emits no buildProblem"
out=$(env TEAMCITY_VERSION=2024.12 "$REPO_BUILD_TOOLS/build.sh" --platfrom ios 2>&1); status=$?
problems=$(printf '%s\n' "$out" | grep -c 'buildProblem')
assert_exit "$status" 2 && assert_equals "$problems" "0" && pass

start_case "success under TeamCity publishes the artifact and reports the build number"
project=$(make_project teamcity-success)
write_stub_unity "$WORK/stub-unity-tc-ok"
out=$(env $ANDROID_ENV TEAMCITY_VERSION=2024.12 "$project/BuildTools/build.sh" \
        --platform android --build-number 42 --unity "$WORK/stub-unity-tc-ok" 2>/dev/null); status=$?
assert_exit "$status" 0 \
  && assert_contains "$out" "##teamcity[buildNumber '42']" \
  && assert_contains "$out" "##teamcity[publishArtifacts" \
  && assert_not_contains "$out" "buildProblem" && pass

start_case "a failing upload under TeamCity emits exactly one buildProblem"
project=$(make_project teamcity-upload-failure)
out=$(run_ios_upload_build "$project" "TEAMCITY_VERSION=2024.12 STUB_ALTOOL_EXIT=1" \
        --export-method appstore --upload-testflight); status=$?
problems=$(printf '%s\n' "$out" | grep -c 'buildProblem')
assert_exit "$status" 1 && assert_equals "$problems" "1" && pass

start_case "the upload gets its own TeamCity block"
project=$(make_project teamcity-upload-block)
out=$(run_ios_upload_build "$project" "TEAMCITY_VERSION=2024.12" \
        --export-method appstore --upload-testflight); status=$?
assert_exit "$status" 0 \
  && assert_contains "$out" "blockOpened name='altool upload to TestFlight'" \
  && assert_contains "$out" "blockClosed name='altool upload to TestFlight'" \
  && assert_not_contains "$out" "buildProblem" && pass

start_case "--version and --build-number together are reported as one TeamCity build number"
project=$(make_project teamcity-version)
write_stub_unity "$WORK/stub-unity-tc-version"
out=$(env $ANDROID_ENV TEAMCITY_VERSION=2024.12 "$project/BuildTools/build.sh" \
        --platform android --version 1.0.0 --build-number 42 \
        --unity "$WORK/stub-unity-tc-version" 2>/dev/null); status=$?
assert_exit "$status" 0 && assert_contains "$out" "##teamcity[buildNumber '1.0.0-42']" && pass

# ============================================================================

printf '\n%s test(s) run, %s failed\n' "$TESTS_RUN" "$TESTS_FAILED"
if [ "$TESTS_FAILED" -ne 0 ]; then
  exit 1
fi
