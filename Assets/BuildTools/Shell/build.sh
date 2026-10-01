#!/usr/bin/env bash
#
# Wool Gather mobile release build entry point.
#
# The same command works for a developer on a laptop and for a TeamCity build
# step. It drives three stages:
#
#   unity   Unity exports the native project (Xcode project / Gradle project)
#           and writes build-info.env describing the build.
#   native  xcodebuild or Gradle turns that project into a signed .ipa / .aab
#           and copies it into Build/Artifacts.
#   upload  iOS only, opt-in: xcrun altool sends the App Store .ipa to App Store
#           Connect, which is what puts it in front of TestFlight testers.
#
# See BuildTools/README.md for the environment variables and TeamCity setup.

set -euo pipefail

SCRIPT_DIR=$(cd "$(dirname "$0")" && pwd -P)

# shellcheck source=lib/common.sh
. "$SCRIPT_DIR/lib/common.sh"

CLI_NAMESPACE="ClassicSpins.BuildTools.BuildCli"

# Which project to build is deliberately independent of where these scripts live, so the
# same copy works from BuildTools/ at the root, from Assets/BuildTools/Shell/ inside a
# .unitypackage, or from Library/PackageCache/ as a UPM package. A Unity project is the
# nearest ancestor holding ProjectSettings/ProjectVersion.txt -- the file the Unity
# version lookup needs anyway, so finding it proves the root is real.
find_project_root() {
  _dir=$1
  while [ "$_dir" != "/" ]; do
    if [ -f "$_dir/ProjectSettings/ProjectVersion.txt" ]; then
      printf '%s\n' "$_dir"
      return 0
    fi
    _dir=$(dirname "$_dir")
  done
  return 1
}

resolve_project_root() {
  if [ -n "${UNITY_PROJECT_ROOT:-}" ]; then
    [ -d "$UNITY_PROJECT_ROOT" ] \
      || die "$EXIT_USAGE" "UNITY_PROJECT_ROOT is not a directory: $UNITY_PROJECT_ROOT"
    PROJECT_ROOT=$(cd "$UNITY_PROJECT_ROOT" && pwd -P)
    [ -f "$PROJECT_ROOT/ProjectSettings/ProjectVersion.txt" ] \
      || die "$EXIT_USAGE" \
           "UNITY_PROJECT_ROOT has no ProjectSettings/ProjectVersion.txt: $PROJECT_ROOT"
    return 0
  fi

  # Script location first: it is right in every layout above, and unlike $PWD it does not
  # change with where the run was launched from. $PWD is the fallback for the one case it
  # cannot cover -- these scripts kept outside the project they build.
  PROJECT_ROOT=$(find_project_root "$SCRIPT_DIR") && return 0
  PROJECT_ROOT=$(find_project_root "$PWD") && return 0

  die "$EXIT_USAGE" \
    "no Unity project found above $SCRIPT_DIR or $PWD -- set UNITY_PROJECT_ROOT to the project root"
}

usage() {
  cat <<'EOF'
Usage: BuildTools/build.sh --platform ios|android [options]

Required:
  --platform ios|android        Which artifact to produce.

Options:
  --profile NAME                BuildProfile asset name. Default: Release
  --version X.Y.Z               Marketing version: CFBundleShortVersionString on
                                iOS, versionName on Android. One to three
                                dot-separated numbers, e.g. 1.0.0. Default:
                                whatever the project already has. Applied for
                                this build only; ProjectSettings is restored
                                afterwards.
  --build-number N              Integer build number / versionCode. Default:
                                whatever the project already has.
  --export-method M             iOS only: appstore | adhoc | both.
                                Default: appstore
  --signing S                   iOS only: manual | auto. Default: manual
                                manual pins the profile by name and is
                                deterministic, which is what CI wants. auto lets
                                Xcode resolve and create profiles, which needs a
                                signed-in Apple ID or an App Store Connect API
                                key, and can modify the team's profiles.
  --stage S                     all | unity | native | upload. Default: all
                                Use unity/native to split into two TeamCity
                                steps, or to re-run only packaging. upload
                                re-sends the .ipa a previous run delivered and
                                implies --upload-testflight; nothing is built.
  --unity PATH                  Unity executable or Unity.app. Default:
                                $UNITY_PATH, else the Hub install matching
                                ProjectSettings/ProjectVersion.txt
  --artifact-dir DIR            Where finished artifacts land.
                                Default: <project>/Build/Artifacts
  --development                 Produce a development build (Unity's
                                BuildOptions.Development).
  --nographics                  Add -nographics to the Unity invocation. Use on
                                headless agents with no display.
  --keep-export                 Do not delete the previous export directory
                                before the Unity stage.
  --upload-testflight           iOS only, and only with --export-method appstore
                                or both: after packaging, upload the App Store
                                .ipa to App Store Connect with xcrun altool, so
                                it reaches TestFlight. Off by default. Needs the
                                three IOS_ASC_* variables, which are checked
                                before Unity is launched.
  -h, --help                    Show this help and exit 0.

Environment (see BuildTools/README.md; values are never echoed):
  iOS       IOS_TEAM_ID (always)
            manual signing: IOS_PROFILE_APPSTORE, IOS_PROFILE_ADHOC
            auto signing:   the signed-in Apple ID from the keychain, or
                            IOS_SIGNING_USE_ASC_KEY=1 plus the three
                            IOS_ASC_* variables (needs an Admin key)
            --upload-testflight / --stage upload: IOS_ASC_KEY_PATH,
                            IOS_ASC_KEY_ID, IOS_ASC_ISSUER_ID, all three
                            required. On their own they do not affect
                            signing
            optional: IOS_CODE_SIGN_IDENTITY, IOS_PROFILE_ID,
                      IOS_XCODEBUILD_EXTRA
  Android   ANDROID_KEYSTORE_PATH, ANDROID_KEYSTORE_PASS,
            ANDROID_KEY_ALIAS, ANDROID_KEY_ALIAS_PASS

Exit codes: 0 success, 1 a build stage failed, 2 bad usage / missing
prerequisite (nothing was built).
EOF
}

# ------------------------------------------------------------- arguments -----

PLATFORM=""
PROFILE="Release"
VERSION=""
BUILD_NUMBER=""
EXPORT_METHOD="appstore"
EXPORT_METHOD_GIVEN=0
SIGNING="manual"
SIGNING_GIVEN=0
STAGE="all"
UNITY_BIN=""
ARTIFACT_DIR=""
DEVELOPMENT=0
NOGRAPHICS=0
KEEP_EXPORT=0
UPLOAD_TESTFLIGHT=0

# Every flag that takes a value verifies the value is present, so a trailing
# `--profile` cannot silently swallow the next flag.
need_value() {
  if [ "$#" -lt 2 ] || [ -z "$2" ]; then
    printf '[wg] ERROR: %s requires a value\n' "$1" >&2
    usage >&2
    exit "$EXIT_USAGE"
  fi
}

# Mirrors BuildNaming.IsValidVersion on the Unity side: one to three dot-separated
# non-negative integers, no leading zeros. Checked here too so a typo costs a second
# rather than a 20 minute export followed by an App Store Connect rejection.
validate_version() {
  _value="$1"
  _bad="--version must be one to three dot-separated numbers, e.g. 1.0.0, got: $_value"
  case "$_value" in
    ''|*[!0-9.]*|.*|*.|*..*) die "$EXIT_USAGE" "$_bad" ;;
  esac

  _count=0
  _rest="$_value"
  while [ -n "$_rest" ]; do
    _part="${_rest%%.*}"
    case "$_rest" in
      *.*) _rest="${_rest#*.}" ;;
      *)   _rest="" ;;
    esac
    _count=$((_count + 1))

    # Nine digits keeps every part inside an int, matching the Unity-side rule.
    if [ "${#_part}" -gt 9 ]; then
      die "$EXIT_USAGE" "--version must have at most 9 digits per part, got: $_value"
    fi
    # '1.02' and '1.2' are the same version to the stores but two different artifact names.
    if [ "${#_part}" -gt 1 ]; then
      case "$_part" in
        0*) die "$EXIT_USAGE" "--version must not have leading zeros, e.g. 1.2 rather than 1.02, got: $_value" ;;
      esac
    fi
  done

  if [ "$_count" -gt 3 ]; then
    die "$EXIT_USAGE" "--version must have at most 3 parts, e.g. 1.0.0, got: $_value"
  fi
}

while [ "$#" -gt 0 ]; do
  case "$1" in
    --platform)      need_value "$@"; PLATFORM="$2"; shift 2 ;;
    --profile)       need_value "$@"; PROFILE="$2"; shift 2 ;;
    --version)       need_value "$@"; VERSION="$2"; shift 2 ;;
    --build-number)  need_value "$@"; BUILD_NUMBER="$2"; shift 2 ;;
    --export-method) need_value "$@"; EXPORT_METHOD="$2"; EXPORT_METHOD_GIVEN=1; shift 2 ;;
    --signing)       need_value "$@"; SIGNING="$2"; SIGNING_GIVEN=1; shift 2 ;;
    --stage)         need_value "$@"; STAGE="$2"; shift 2 ;;
    --unity)         need_value "$@"; UNITY_BIN="$2"; shift 2 ;;
    --artifact-dir)  need_value "$@"; ARTIFACT_DIR="$2"; shift 2 ;;
    --development)   DEVELOPMENT=1; shift ;;
    --nographics)    NOGRAPHICS=1; shift ;;
    --keep-export)   KEEP_EXPORT=1; shift ;;
    --upload-testflight) UPLOAD_TESTFLIGHT=1; shift ;;
    -h|--help)       usage; exit 0 ;;
    *)
      printf '[wg] ERROR: unknown argument: %s\n' "$1" >&2
      usage >&2
      exit "$EXIT_USAGE"
      ;;
  esac
done

case "$PLATFORM" in
  ios|android) ;;
  "")
    printf '[wg] ERROR: --platform is required\n' >&2
    usage >&2
    exit "$EXIT_USAGE"
    ;;
  *)
    printf '[wg] ERROR: --platform must be ios or android, got: %s\n' "$PLATFORM" >&2
    exit "$EXIT_USAGE"
    ;;
esac

case "$STAGE" in
  all|unity|native|upload) ;;
  *) die "$EXIT_USAGE" "--stage must be all, unity, native or upload, got: $STAGE" ;;
esac

# Asking for the upload stage on its own is asking for the upload, so the flag is implied
# rather than also required -- there is nothing else --stage upload could mean.
if [ "$STAGE" = "upload" ]; then
  UPLOAD_TESTFLIGHT=1
fi

case "$EXPORT_METHOD" in
  appstore|adhoc|both) ;;
  *) die "$EXIT_USAGE" "--export-method must be appstore, adhoc or both, got: $EXPORT_METHOD" ;;
esac

case "$SIGNING" in
  manual|auto) ;;
  *) die "$EXIT_USAGE" "--signing must be manual or auto, got: $SIGNING" ;;
esac

# Usage errors rather than something discovered after a twenty minute export: neither
# combination has an outcome worth building towards.
if [ "$UPLOAD_TESTFLIGHT" -eq 1 ]; then
  if [ "$PLATFORM" != "ios" ]; then
    die "$EXIT_USAGE" \
      "TestFlight is iOS only; --upload-testflight and --stage upload need --platform ios, got: $PLATFORM"
  fi
  if [ "$EXPORT_METHOD" = "adhoc" ]; then
    die "$EXIT_USAGE" \
      "TestFlight needs an App Store build; --export-method adhoc produces an ad-hoc .ipa, which App Store Connect will not accept. Use appstore or both."
  fi
  # A warning rather than an error: splitting the work across TeamCity steps means passing
  # the same flag set to both, and the upload belongs to the second one. Turned off here so
  # the run is not made to produce credentials it will not use.
  if [ "$STAGE" = "unity" ]; then
    warn "--upload-testflight is ignored with --stage unity; nothing is packaged, so there is no .ipa to upload"
    UPLOAD_TESTFLIGHT=0
  fi
fi

if [ -n "$VERSION" ]; then
  validate_version "$VERSION"
fi

if [ -n "$BUILD_NUMBER" ]; then
  case "$BUILD_NUMBER" in
    ''|*[!0-9]*)
      die "$EXIT_USAGE" "--build-number must be a non-negative integer, got: $BUILD_NUMBER"
      ;;
  esac
  # Android's versionCode is a signed 32-bit int. Rejecting here keeps it a usage error
  # rather than an int overflow surfacing as a build failure after the export.
  if [ "${#BUILD_NUMBER}" -gt 10 ] || [ "$BUILD_NUMBER" -gt 2147483647 ] 2>/dev/null; then
    die "$EXIT_USAGE" "--build-number must be at most 2147483647, got: $BUILD_NUMBER"
  fi
fi

# Deferred until here so `--help` and usage errors still work outside a Unity project.
resolve_project_root

ARTIFACT_DIR_GIVEN=1
if [ -z "$ARTIFACT_DIR" ]; then
  ARTIFACT_DIR_GIVEN=0
  ARTIFACT_DIR="$PROJECT_ROOT/Build/Artifacts"
fi

# The packaging and upload stages read the artifact directory, version, build number and
# profile out of build-info.env, which only the Unity stage writes. Saying so beats letting
# a flag look like it applied. The upload stage additionally reads neither --export-method
# nor --signing: it signs nothing, and the only artifact it can send is the App Store one.
# Those two are warned about for `upload` alone, since packaging really does use them.
if [ "$STAGE" = "native" ] || [ "$STAGE" = "upload" ]; then
  for ignored_flag in \
    "$([ "$ARTIFACT_DIR_GIVEN" -eq 1 ] && echo --artifact-dir)" \
    "$([ -n "$VERSION" ] && echo --version)" \
    "$([ -n "$BUILD_NUMBER" ] && echo --build-number)" \
    "$([ "$DEVELOPMENT" -eq 1 ] && echo --development)" \
    "$([ "$STAGE" = "upload" ] && [ "$EXPORT_METHOD_GIVEN" -eq 1 ] && echo --export-method)" \
    "$([ "$STAGE" = "upload" ] && [ "$SIGNING_GIVEN" -eq 1 ] && echo --signing)"
  do
    if [ -n "$ignored_flag" ]; then
      warn "$ignored_flag is ignored with --stage $STAGE; that value comes from the build-info.env written by the Unity stage"
    fi
  done
fi

# Arguments are valid from here on, so a non-zero exit is now a real failure
# worth reporting to TeamCity. Installed after parsing so that --help and usage
# errors never produce a buildProblem.
wg_install_failure_trap "Wool Gather $PLATFORM build"

if [ "$PLATFORM" = "ios" ]; then
  EXPORT_DIR="$PROJECT_ROOT/Build/iOS/XcodeProject"
  UNITY_BUILD_TARGET="iOS"
  UNITY_METHOD="$CLI_NAMESPACE.ExportIos"
else
  EXPORT_DIR="$PROJECT_ROOT/Build/Android/AndroidProject"
  UNITY_BUILD_TARGET="Android"
  UNITY_METHOD="$CLI_NAMESPACE.ExportAndroid"
fi

# ----------------------------------------------------------- credentials -----

# Checked before Unity is launched: a 20 minute export that then fails to sign
# is the single most expensive failure mode this tool can have.
validate_credentials() {
  if [ "$PLATFORM" = "android" ]; then
    if [ "$STAGE" != "native" ]; then
      wg_require_env ANDROID_KEYSTORE_PATH ANDROID_KEYSTORE_PASS \
        ANDROID_KEY_ALIAS ANDROID_KEY_ALIAS_PASS
    fi
    # Needed by both stages: Unity bakes the keystore path into the exported
    # Gradle project, and Gradle reads the file at packaging time.
    if [ -n "${ANDROID_KEYSTORE_PATH:-}" ]; then
      wg_require_file "$ANDROID_KEYSTORE_PATH" "Android keystore"
    fi
    return 0
  fi

  # Collected into one list rather than checked group by group, so a machine that is set up
  # for nothing at all still sees every name it has to provide in a single message.
  _required=""

  # The team id is needed under both signing styles: automatic signing removes the profile
  # names, not the account Xcode signs on behalf of. An upload-only run signs nothing, so
  # it needs neither a team nor a profile -- which is what keeps the retry cheap.
  if [ "$STAGE" != "upload" ]; then
    _required="IOS_TEAM_ID"
    if [ "$STAGE" != "unity" ] && [ "$SIGNING" = "manual" ]; then
      case "$EXPORT_METHOD" in
        appstore) _required="$_required IOS_PROFILE_APPSTORE" ;;
        adhoc)    _required="$_required IOS_PROFILE_ADHOC" ;;
        both)     _required="$_required IOS_PROFILE_APPSTORE IOS_PROFILE_ADHOC" ;;
      esac
    fi
    # Signing as the API key is opt-in, and asked for here rather than discovered halfway
    # through the archive: the packaging stage checks it too, but only after Unity has run.
    # Skipped when the upload will demand the same three names below, so a machine that has
    # none of them is not told the same name twice.
    if [ "$STAGE" != "unity" ] && [ "$SIGNING" = "auto" ] \
       && [ "${IOS_SIGNING_USE_ASC_KEY:-0}" = "1" ] && [ "$UPLOAD_TESTFLIGHT" -ne 1 ]; then
      _required="$_required IOS_ASC_KEY_PATH IOS_ASC_KEY_ID IOS_ASC_ISSUER_ID"
    fi
  fi

  # The upload has its own credentials, needed whichever signing style is in play and in
  # every stage that includes it -- hence outside the conditions above. Same three
  # variables automatic signing uses; no new secret was invented for this.
  if [ "$UPLOAD_TESTFLIGHT" -eq 1 ]; then
    _required="$_required IOS_ASC_KEY_PATH IOS_ASC_KEY_ID IOS_ASC_ISSUER_ID"
  fi

  # Deliberately unquoted: the accumulated names are one argument each.
  # shellcheck disable=SC2086
  wg_require_env $_required

  if [ "$UPLOAD_TESTFLIGHT" -eq 1 ]; then
    wg_require_file "$IOS_ASC_KEY_PATH" "App Store Connect API key"
    # Also checked by the upload script itself, but that runs after the build. A machine with
    # no Xcode command line tools is exactly what this gate is here to catch early.
    wg_require_cmd xcrun
  fi
}

# ------------------------------------------------------------------ unity ----

resolve_unity() {
  if [ -z "$UNITY_BIN" ]; then
    UNITY_BIN="${UNITY_PATH:-}"
  fi

  # Accept either the binary or the .app bundle.
  if [ -n "$UNITY_BIN" ] && [ -d "$UNITY_BIN" ] && [ -x "$UNITY_BIN/Contents/MacOS/Unity" ]; then
    UNITY_BIN="$UNITY_BIN/Contents/MacOS/Unity"
  fi

  if [ -n "$UNITY_BIN" ]; then
    # -f as well as -x: a directory is executable, and would otherwise only fail later
    # as an exec error that looks like a build failure.
    if [ ! -f "$UNITY_BIN" ] || [ ! -x "$UNITY_BIN" ]; then
      die "$EXIT_USAGE" "Unity executable not found or not executable: $UNITY_BIN"
    fi
    return 0
  fi

  version_file="$PROJECT_ROOT/ProjectSettings/ProjectVersion.txt"
  wg_require_file "$version_file" "ProjectVersion.txt"
  editor_version=$(sed -n 's/^m_EditorVersion: *//p' "$version_file" | head -1 | tr -d '\r')
  if [ -z "$editor_version" ]; then
    die "$EXIT_USAGE" "could not read m_EditorVersion from $version_file"
  fi

  for candidate in \
    "/Applications/Unity/Hub/Editor/$editor_version/Unity.app/Contents/MacOS/Unity" \
    "$HOME/Applications/Unity/Hub/Editor/$editor_version/Unity.app/Contents/MacOS/Unity"
  do
    if [ -x "$candidate" ]; then
      UNITY_BIN="$candidate"
      return 0
    fi
  done

  printf '[wg] ERROR: no Unity %s install found. Pass --unity PATH or set UNITY_PATH.\n' \
    "$editor_version" >&2
  printf '[wg]        (project requires %s, per %s)\n' "$editor_version" "$version_file" >&2
  printf '[wg]        looked for: /Applications/Unity/Hub/Editor/%s/Unity.app/Contents/MacOS/Unity\n' \
    "$editor_version" >&2
  exit "$EXIT_USAGE"
}

run_unity_stage() {
  resolve_unity
  log "Unity:    $UNITY_BIN"
  log "project:  $PROJECT_ROOT"
  log "profile:  $PROFILE"
  log "version:  ${VERSION:-(project value)}"
  log "build no: ${BUILD_NUMBER:-(project value)}"
  log "export:   $EXPORT_DIR"
  log "artifact: $ARTIFACT_DIR"

  if [ "$KEEP_EXPORT" -eq 0 ] && [ -d "$EXPORT_DIR" ]; then
    log "removing stale export directory"
    rm -rf "$EXPORT_DIR"
  fi
  mkdir -p "$EXPORT_DIR" "$ARTIFACT_DIR"

  set -- -batchmode -logFile - -projectPath "$PROJECT_ROOT" \
         -buildTarget "$UNITY_BUILD_TARGET" -executeMethod "$UNITY_METHOD"
  if [ "$NOGRAPHICS" -eq 1 ]; then
    set -- "$@" -nographics
  fi
  # Everything after `--` is ours and BuildCli parses it. Unity ignores flags it does not
  # know -- but it still *scans* the whole command line, so a name Unity does claim is
  # acted on even here. `--version` is exactly that: Unity reads it as its own -version,
  # prints the editor version and exits 0 without running -executeMethod. Hence the
  # rename to --app-version. Check any new flag name against Unity's CLI before adding it.
  set -- "$@" -- \
    --profile "$PROFILE" \
    --export-dir "$EXPORT_DIR" \
    --artifact-dir "$ARTIFACT_DIR" \
    --export-method "$EXPORT_METHOD" \
    --signing "$SIGNING"
  if [ -n "$VERSION" ]; then
    set -- "$@" --app-version "$VERSION"
  fi
  if [ -n "$BUILD_NUMBER" ]; then
    set -- "$@" --build-number "$BUILD_NUMBER"
  fi
  if [ "$DEVELOPMENT" -eq 1 ]; then
    set -- "$@" --development
  fi
  if [ "$KEEP_EXPORT" -eq 1 ]; then
    set -- "$@" --keep-export
  fi

  wg_block_opened "Unity export ($PLATFORM)"
  set +e
  "$UNITY_BIN" "$@"
  unity_status=$?
  set -e
  wg_block_closed "Unity export ($PLATFORM)"

  if [ "$unity_status" -ne 0 ]; then
    # Normalised to the documented 0/1/2 contract. Unity can exit with anything -- a
    # licensing failure, a crash, or 128+signal -- and callers should not have to know
    # which of those means "the build failed".
    if [ "$unity_status" -eq "$EXIT_USAGE" ]; then
      die "$EXIT_USAGE" "Unity rejected the build arguments (exit 2). See the Unity log above."
    fi
    die "$EXIT_BUILD" "Unity export stage failed (Unity exit $unity_status). See the Unity log above."
  fi

  wg_require_file "$EXPORT_DIR/build-info.env" "build-info.env written by the Unity stage"
  log "Unity export stage complete"
}

# ----------------------------------------------------------------- native ----

run_native_stage() {
  if [ "$PLATFORM" = "ios" ]; then
    bash "$SCRIPT_DIR/ios/archive_and_export.sh" \
      --export-dir "$EXPORT_DIR" --method "$EXPORT_METHOD" --signing "$SIGNING"
  else
    bash "$SCRIPT_DIR/android/bundle_release.sh" --export-dir "$EXPORT_DIR"
  fi
}

# ----------------------------------------------------------------- upload ----

# A no-op unless asked for, so it can sit unconditionally in the stage dispatch below and
# an existing build command keeps behaving exactly as it did.
run_upload_stage() {
  if [ "$UPLOAD_TESTFLIGHT" -ne 1 ]; then
    return 0
  fi
  bash "$SCRIPT_DIR/ios/upload_testflight.sh" --export-dir "$EXPORT_DIR"
}

# ------------------------------------------------------------------ main -----

validate_credentials

# Reported to TeamCity as version-buildNumber, the same pair the artifact name carries, so
# the build in the UI and the file on disk can be matched by eye. Whichever half was not
# given is left out rather than guessed at: only the Unity stage knows the project's own.
if [ -n "$VERSION" ] && [ -n "$BUILD_NUMBER" ]; then
  wg_set_build_number "$VERSION-$BUILD_NUMBER"
elif [ -n "$BUILD_NUMBER" ]; then
  wg_set_build_number "$BUILD_NUMBER"
elif [ -n "$VERSION" ]; then
  wg_set_build_number "$VERSION"
fi

case "$STAGE" in
  unity)  run_unity_stage ;;
  native) run_native_stage; run_upload_stage ;;
  upload) run_upload_stage ;;
  all)    run_unity_stage; run_native_stage; run_upload_stage ;;
esac
