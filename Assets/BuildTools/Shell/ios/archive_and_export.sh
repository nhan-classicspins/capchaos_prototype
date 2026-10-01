#!/usr/bin/env bash
#
# Packaging stage for iOS: archive the Xcode project Unity exported, then export
# one .ipa per requested distribution method and deliver them.
#
# Normally invoked by BuildTools/build.sh, but safe to run on its own to re-run
# only xcodebuild after hand-editing the exported project:
#
#   BuildTools/ios/archive_and_export.sh --export-dir Build/iOS/XcodeProject --method both
#
# The archive is signed once with the primary profile; exportArchive re-signs per
# method from the rendered ExportOptions plist, which is why `both` needs only
# one archive.

set -euo pipefail

SCRIPT_DIR=$(cd "$(dirname "$0")" && pwd -P)

# shellcheck source=../lib/common.sh
. "$SCRIPT_DIR/../lib/common.sh"

TEMPLATE_DIR="$SCRIPT_DIR"

usage() {
  cat <<'EOF'
Usage: BuildTools/ios/archive_and_export.sh --export-dir DIR [options]

  --export-dir DIR     Directory containing build-info.env, as written by the
                       Unity export stage. Required.
  --method M           appstore | adhoc | both. Default: from build-info.env
  --signing S          manual | auto. Default: from build-info.env, else manual
  --configuration C    Xcode configuration to archive. Default: Release
  -h, --help           Show this help and exit 0.

Environment:
  IOS_TEAM_ID              Apple Developer team id. Required either way.

  manual signing:
  IOS_PROFILE_APPSTORE     Provisioning profile name for app-store export.
  IOS_PROFILE_ADHOC        Provisioning profile name for ad-hoc export.
  IOS_CODE_SIGN_IDENTITY   Default: Apple Distribution

  auto signing: by default Xcode uses the signed-in Apple ID from the keychain
  -- which a headless agent does not have. To authenticate as an App Store
  Connect API key instead, set all four of:
  IOS_SIGNING_USE_ASC_KEY  1 to sign as the key below. The key needs the Admin
                              role: App Store Connect grants access to
                              cloud-managed distribution certificates to users,
                              not to keys, and without it export fails with
                              "No signing certificate iOS Distribution found".
                              Unset, the three variables below are upload
                              credentials only and do not affect signing.
  IOS_ASC_KEY_PATH         App Store Connect API key .p8 file.
  IOS_ASC_KEY_ID           Its key id.
  IOS_ASC_ISSUER_ID        Its issuer id.

  IOS_XCODEBUILD_EXTRA     Extra xcodebuild settings, whitespace separated.
                              Escape hatch when a target needs a signing
                              override this script does not model.
EOF
}

EXPORT_DIR=""
METHOD=""
SIGNING=""
CONFIGURATION="Release"

while [ "$#" -gt 0 ]; do
  case "$1" in
    --export-dir)
      if [ "$#" -lt 2 ] || [ -z "$2" ]; then die "$EXIT_USAGE" "--export-dir requires a value"; fi
      EXPORT_DIR="$2"; shift 2 ;;
    --method)
      if [ "$#" -lt 2 ] || [ -z "$2" ]; then die "$EXIT_USAGE" "--method requires a value"; fi
      METHOD="$2"; shift 2 ;;
    --signing)
      if [ "$#" -lt 2 ] || [ -z "$2" ]; then die "$EXIT_USAGE" "--signing requires a value"; fi
      SIGNING="$2"; shift 2 ;;
    --configuration)
      if [ "$#" -lt 2 ] || [ -z "$2" ]; then die "$EXIT_USAGE" "--configuration requires a value"; fi
      CONFIGURATION="$2"; shift 2 ;;
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

wg_require_cmd xcodebuild
wg_require_dir "$EXPORT_DIR" "export directory"
EXPORT_DIR=$(wg_abs_path "$EXPORT_DIR")

wg_load_build_info "$EXPORT_DIR" \
  NATIVE_PROJECT_DIR ARTIFACT_DIR ARTIFACT_BASENAME BUNDLE_ID

if [ "${PLATFORM:-}" != "ios" ]; then
  die "$EXIT_USAGE" "build-info.env at $EXPORT_DIR describes platform '${PLATFORM:-<unset>}', not ios"
fi

mkdir -p "$ARTIFACT_DIR"
ARTIFACT_DIR=$(wg_abs_path "$ARTIFACT_DIR")

# With no --method, fall back to what the Unity stage recorded, so a standalone
# `--stage native` re-run reproduces the same set of exports.
if [ -z "$METHOD" ]; then
  case "${IOS_EXPORT_METHODS:-}" in
    *appstore*adhoc*|*adhoc*appstore*) METHOD="both" ;;
    *adhoc*)                           METHOD="adhoc" ;;
    *appstore*)                        METHOD="appstore" ;;
    *)                                 METHOD="appstore" ;;
  esac
  log "no --method given; using '$METHOD' from build-info.env"
fi

case "$METHOD" in
  appstore|adhoc|both) ;;
  *) die "$EXIT_USAGE" "--method must be appstore, adhoc or both, got: $METHOD" ;;
esac

if [ -z "$SIGNING" ]; then
  SIGNING="${IOS_SIGNING:-manual}"
fi
case "$SIGNING" in
  manual|auto) ;;
  *) die "$EXIT_USAGE" "--signing must be manual or auto, got: $SIGNING" ;;
esac
log "signing style: $SIGNING"

TEMPLATE="$TEMPLATE_DIR/ExportOptions.$([ "$SIGNING" = auto ] && echo automatic || echo manual).template.plist"
wg_require_file "$TEMPLATE" "ExportOptions template for $SIGNING signing"

# A team id is needed either way: automatic signing removes the profile names, not the
# account Xcode signs on behalf of.
wg_require_env IOS_TEAM_ID
if [ "$SIGNING" = "manual" ]; then
  case "$METHOD" in
    appstore) wg_require_env IOS_PROFILE_APPSTORE ;;
    adhoc)    wg_require_env IOS_PROFILE_ADHOC ;;
    both)     wg_require_env IOS_PROFILE_APPSTORE IOS_PROFILE_ADHOC ;;
  esac
fi

# Signing flags differ per mode, so build them once here.
#   manual: pin the identity and profile so the result is deterministic.
#   auto:   let Xcode resolve, and allow it to create what is missing. That reaches out to
#           Apple, so it needs either a signed-in Apple ID in the keychain or an App Store
#           Connect API key -- and it can create or modify profiles on the team account.
# AUTH_ARGS is needed by exportArchive as well as archive, because automatic signing
# contacts Apple in both phases.
SIGNING_ARGS=()
AUTH_ARGS=()
if [ "$SIGNING" = "manual" ]; then
  SIGNING_ARGS=(
    CODE_SIGN_STYLE=Manual
    DEVELOPMENT_TEAM="$IOS_TEAM_ID"
    CODE_SIGN_IDENTITY="${IOS_CODE_SIGN_IDENTITY:-Apple Distribution}"
  )
else
  # No CODE_SIGN_IDENTITY here on purpose: pinning it would defeat the point of letting
  # Xcode resolve the identity.
  SIGNING_ARGS=(
    -allowProvisioningUpdates
    CODE_SIGN_STYLE=Automatic
    DEVELOPMENT_TEAM="$IOS_TEAM_ID"
  )
  # The three IOS_ASC_* variables are upload credentials, and reusing them for signing is
  # opt-in rather than automatic. Passing -authenticationKey* does not add a permission, it
  # *replaces* the identity xcodebuild provisions as: the API key instead of the Apple ID
  # signed in to Xcode. A key without access to the team's cloud-managed distribution
  # certificate then gets 403 from Apple, the certificate the Apple ID could see becomes
  # invisible, the App Store profile fails qualification, and exportArchive dies with
  # "No signing certificate iOS Distribution found" -- on a machine where the very same
  # build succeeded moments earlier without the key. App Store Connect grants that access
  # to users, not to keys, so in practice the key has to hold the Admin role. Making the
  # reuse explicit keeps `--upload-testflight` from silently changing how signing happens.
  if [ "${IOS_SIGNING_USE_ASC_KEY:-0}" = "1" ]; then
    wg_require_env IOS_ASC_KEY_PATH IOS_ASC_KEY_ID IOS_ASC_ISSUER_ID
    wg_require_file "$IOS_ASC_KEY_PATH" "App Store Connect API key"
    AUTH_ARGS=(
      -authenticationKeyPath "$IOS_ASC_KEY_PATH"
      -authenticationKeyID "$IOS_ASC_KEY_ID"
      -authenticationKeyIssuerID "$IOS_ASC_ISSUER_ID"
    )
  elif [ -n "${IOS_ASC_KEY_PATH:-}" ] || [ -n "${IOS_ASC_KEY_ID:-}" ] \
       || [ -n "${IOS_ASC_ISSUER_ID:-}" ]; then
    log "automatic signing is not using the App Store Connect API key in the environment: those three variables are upload credentials. Xcode will sign as the Apple ID in its keychain session. Set IOS_SIGNING_USE_ASC_KEY=1 to authenticate signing with the key instead"
  else
    warn "automatic signing with no App Store Connect API key: xcodebuild will fall back to the Apple ID in the keychain, which a headless agent does not have"
  fi
fi

XCODE_PROJECT_DIR="$NATIVE_PROJECT_DIR"
wg_require_dir "$XCODE_PROJECT_DIR" "exported Xcode project directory"

# ------------------------------------------------- xcode version / methods ----

# Xcode 15.3 renamed the export methods: app-store -> app-store-connect and
# ad-hoc -> release-testing. Older Xcode rejects the new spellings and newer
# Xcode only warns about the old ones, so pick by version instead of guessing.
# Captures only the leading numeric part, so "Xcode 26.4 beta 2" still parses.
XCODE_VERSION=$(xcodebuild -version 2>/dev/null \
  | sed -n '1s/^Xcode \([0-9][0-9.]*\).*/\1/p' | tr -d '\r')
if [ -z "$XCODE_VERSION" ]; then
  die "$EXIT_USAGE" "could not determine the Xcode version from 'xcodebuild -version'"
fi
XCODE_VERSION=${XCODE_VERSION%.}
XCODE_MAJOR=${XCODE_VERSION%%.*}
case "$XCODE_VERSION" in
  *.*) XCODE_MINOR=${XCODE_VERSION#*.}; XCODE_MINOR=${XCODE_MINOR%%.*} ;;
  *)   XCODE_MINOR=0 ;;
esac
case "$XCODE_MAJOR" in ''|*[!0-9]*) die "$EXIT_USAGE" "could not parse the Xcode version: $XCODE_VERSION" ;; esac
case "$XCODE_MINOR" in ''|*[!0-9]*) XCODE_MINOR=0 ;; esac

USE_MODERN_METHOD_NAMES=0
if [ "$XCODE_MAJOR" -gt 15 ]; then
  USE_MODERN_METHOD_NAMES=1
elif [ "$XCODE_MAJOR" -eq 15 ] && [ "$XCODE_MINOR" -ge 3 ]; then
  USE_MODERN_METHOD_NAMES=1
fi
log "Xcode $XCODE_VERSION detected"

# xcodebuild_method <appstore|adhoc>
xcodebuild_method() {
  if [ "$USE_MODERN_METHOD_NAMES" -eq 1 ]; then
    case "$1" in
      appstore) printf 'app-store-connect\n' ;;
      adhoc)    printf 'release-testing\n' ;;
    esac
  else
    case "$1" in
      appstore) printf 'app-store\n' ;;
      adhoc)    printf 'ad-hoc\n' ;;
    esac
  fi
}

# profile_name_for <appstore|adhoc>
profile_name_for() {
  case "$1" in
    appstore) printf '%s\n' "${IOS_PROFILE_APPSTORE:-}" ;;
    adhoc)    printf '%s\n' "${IOS_PROFILE_ADHOC:-}" ;;
  esac
}

# artifact_basename_for <appstore|adhoc>
# The Unity stage precomputes one basename per method so the naming rule lives in
# exactly one place (BuildNaming) instead of being re-implemented here.
artifact_basename_for() {
  _upper=$(printf '%s' "$1" | tr 'a-z' 'A-Z')
  eval "_base=\${ARTIFACT_BASENAME_$_upper:-}"
  if [ -z "$_base" ]; then
    die "$EXIT_USAGE" "build-info.env is missing ARTIFACT_BASENAME_$_upper"
  fi
  printf '%s\n' "$_base"
}

if [ "$METHOD" = "both" ]; then
  METHODS="appstore adhoc"
else
  METHODS="$METHOD"
fi

# The archive carries one signature; exportArchive re-signs per method. Prefer
# the app-store profile for the archive when it is in play. Plain expansion rather
# than `| head -1`: under `set -o pipefail` a SIGPIPE from the closed pipe would
# fail the whole script.
PRIMARY_METHOD=${METHODS%% *}

# ---------------------------------------------------------------- archive -----

# Unity names the project Unity-iPhone.xcodeproj, and adds a workspace only when
# CocoaPods is involved. Discover it instead of hard-coding either name.
# Globbed rather than `find | head -1` for the same pipefail/SIGPIPE reason.
WORKSPACE=""
for candidate in "$XCODE_PROJECT_DIR"/*.xcworkspace; do
  if [ -d "$candidate" ]; then
    WORKSPACE="$candidate"
    break
  fi
done

if [ -n "$WORKSPACE" ]; then
  CONTAINER_FLAG="-workspace"
  CONTAINER_PATH="$WORKSPACE"
  SCHEME=$(basename "$WORKSPACE" .xcworkspace)
  log "using workspace $WORKSPACE (scheme $SCHEME)"
else
  CONTAINER_FLAG="-project"
  CONTAINER_PATH=$(wg_find_single_child_dir "Xcode project" "$XCODE_PROJECT_DIR" '*.xcodeproj')
  SCHEME=$(basename "$CONTAINER_PATH" .xcodeproj)
  log "using project $CONTAINER_PATH (scheme $SCHEME)"
fi

BUILD_DIR="$EXPORT_DIR/build"
ARCHIVE_PATH="$BUILD_DIR/$ARTIFACT_BASENAME.xcarchive"
rm -rf "$BUILD_DIR"
mkdir -p "$BUILD_DIR"

# Under manual signing the archive is pinned to one profile; exportArchive re-signs per
# method afterwards. Under automatic signing Xcode picks, so there is nothing to pin.
PROFILE_ARGS=()
if [ "$SIGNING" = "manual" ]; then
  PROFILE_ARGS=(PROVISIONING_PROFILE_SPECIFIER="$(profile_name_for "$PRIMARY_METHOD")")
fi

wg_block_opened "xcodebuild archive"
set +e
# IOS_XCODEBUILD_EXTRA is intentionally word-split: it carries additional
# `SETTING=value` pairs.
# shellcheck disable=SC2086
xcodebuild \
  "$CONTAINER_FLAG" "$CONTAINER_PATH" \
  -scheme "$SCHEME" \
  -configuration "$CONFIGURATION" \
  -destination 'generic/platform=iOS' \
  -archivePath "$ARCHIVE_PATH" \
  "${SIGNING_ARGS[@]}" \
  ${AUTH_ARGS[@]+"${AUTH_ARGS[@]}"} \
  ${PROFILE_ARGS[@]+"${PROFILE_ARGS[@]}"} \
  ${IOS_XCODEBUILD_EXTRA:-} \
  clean archive
archive_status=$?
set -e
wg_block_closed "xcodebuild archive"

if [ "$archive_status" -ne 0 ]; then
  die "$EXIT_BUILD" "xcodebuild archive failed (exit $archive_status)"
fi
wg_require_dir "$ARCHIVE_PATH" "produced xcarchive"

# ----------------------------------------------------------------- export -----

# Escapes the three characters that would otherwise break the plist. Profile names are
# free text chosen in the Apple developer portal and routinely contain "&".
xml_escape() {
  printf '%s' "$1" | sed -e 's/&/\&amp;/g' -e 's/</\&lt;/g' -e 's/>/\&gt;/g'
}

# Renders the selected ExportOptions template for one method. Substitutes literally, so
# characters that are special to sed or to the shell survive intact.
render_export_options() {
  _method_key="$1"
  _out="$2"
  _xc_method=$(xcodebuild_method "$_method_key")
  _profile=$(profile_name_for "$_method_key")

  METHOD_VALUE="$_xc_method" \
  TEAM_VALUE="$(xml_escape "$IOS_TEAM_ID")" \
  BUNDLE_VALUE="$(xml_escape "$BUNDLE_ID")" \
  PROFILE_VALUE="$(xml_escape "$_profile")" \
  awk '
    { line = $0
      gsub(/__METHOD__/,    ENVIRON["METHOD_VALUE"],  line)
      gsub(/__TEAM_ID__/,   ENVIRON["TEAM_VALUE"],    line)
      gsub(/__BUNDLE_ID__/, ENVIRON["BUNDLE_VALUE"],  line)
      gsub(/__PROFILE__/,   ENVIRON["PROFILE_VALUE"], line)
      print line }
  ' "$TEMPLATE" > "$_out"

  if grep -q '__[A-Z_]*__' "$_out"; then
    die "$EXIT_BUILD" "ExportOptions placeholders left unresolved in $_out"
  fi
}

for method_key in $METHODS; do
  xc_method=$(xcodebuild_method "$method_key")
  options_plist="$BUILD_DIR/ExportOptions.$method_key.plist"
  export_path="$BUILD_DIR/export-$method_key"

  render_export_options "$method_key" "$options_plist"
  rm -rf "$export_path"

  wg_block_opened "xcodebuild exportArchive ($xc_method)"
  set +e
  # Automatic signing needs -allowProvisioningUpdates and the API key here too: export
  # re-signs, and can need a profile Xcode has not fetched yet.
  EXPORT_SIGNING_ARGS=()
  if [ "$SIGNING" = "auto" ]; then
    EXPORT_SIGNING_ARGS=(-allowProvisioningUpdates)
  fi
  xcodebuild -exportArchive \
    -archivePath "$ARCHIVE_PATH" \
    -exportPath "$export_path" \
    -exportOptionsPlist "$options_plist" \
    ${EXPORT_SIGNING_ARGS[@]+"${EXPORT_SIGNING_ARGS[@]}"} \
    ${AUTH_ARGS[@]+"${AUTH_ARGS[@]}"}
  export_status=$?
  set -e
  wg_block_closed "xcodebuild exportArchive ($xc_method)"

  if [ "$export_status" -ne 0 ]; then
    die "$EXIT_BUILD" "xcodebuild exportArchive failed for method '$method_key' ($xc_method), exit $export_status"
  fi

  ipa_path=$(wg_find_single "exported ipa" "$export_path" -name '*.ipa')
  wg_deliver_artifact "$ipa_path" "$ARTIFACT_DIR" "$(artifact_basename_for "$method_key").ipa"
done
