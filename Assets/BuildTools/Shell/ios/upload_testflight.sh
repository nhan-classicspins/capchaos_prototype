#!/usr/bin/env bash
#
# Upload stage for iOS: send the App Store .ipa the packaging stage delivered to App
# Store Connect, which is what puts a build in front of TestFlight testers.
#
# Normally invoked by BuildTools/build.sh. It is a separate script on purpose: an upload
# fails for reasons that have nothing to do with the build -- a build number already
# used, a network blip, an expired key -- and re-archiving costs about twenty minutes.
# So the retry is free:
#
#   BuildTools/ios/upload_testflight.sh --export-dir Build/iOS/XcodeProject
#   BuildTools/build.sh --platform ios --stage upload
#
# The file uploaded is the exact one build-info.env says the packaging stage delivered.
# No artifact name is ever re-derived here.

set -euo pipefail

SCRIPT_DIR=$(cd "$(dirname "$0")" && pwd -P)

# shellcheck source=../lib/common.sh
. "$SCRIPT_DIR/../lib/common.sh"

usage() {
  cat <<'EOF'
Usage: BuildTools/ios/upload_testflight.sh --export-dir DIR [options]

  --export-dir DIR     Directory containing build-info.env, as written by the
                       Unity export stage. Required.
  --ipa PATH           Upload this file instead of the App Store .ipa named by
                       build-info.env. Escape hatch; the default is whatever the
                       packaging stage actually delivered.
  -h, --help           Show this help and exit 0.

Environment (all three required; values are never echoed):
  IOS_ASC_KEY_PATH   App Store Connect API key .p8 file. Passed to altool as
                        --p8-file-path, so it may live anywhere. Without that
                        flag altool searches only ./private_keys, ~/private_keys,
                        ~/.private_keys and ~/.appstoreconnect/private_keys --
                        one of which is the checkout, so nothing is copied.
  IOS_ASC_KEY_ID     Its key id.
  IOS_ASC_ISSUER_ID  Its issuer id.

App Store builds only: TestFlight will not take an ad-hoc .ipa, and an
--export-method both build uploads the appstore one.

altool's own output is sent to stderr, so stdout stays reserved for TeamCity
service messages and the artifact path.

Exit codes: 0 uploaded, 1 altool ran and failed (the artifact is still on disk,
so re-run this), 2 bad usage / missing prerequisite (nothing was sent).
EOF
}

EXPORT_DIR=""
IPA=""

while [ "$#" -gt 0 ]; do
  case "$1" in
    --export-dir)
      if [ "$#" -lt 2 ] || [ -z "$2" ]; then die "$EXIT_USAGE" "--export-dir requires a value"; fi
      EXPORT_DIR="$2"; shift 2 ;;
    --ipa)
      if [ "$#" -lt 2 ] || [ -z "$2" ]; then die "$EXIT_USAGE" "--ipa requires a value"; fi
      IPA="$2"; shift 2 ;;
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

# First, because without the Xcode command line tools there is no upload to attempt and
# every other check would only be reporting a second-order problem.
wg_require_cmd xcrun

# Checked here as well as in build.sh, since this script is also a standalone re-run
# entry point. Values are never printed; the key *path* is, because pointing at the wrong
# file is the likeliest mistake and the path is not itself the secret.
wg_require_env IOS_ASC_KEY_PATH IOS_ASC_KEY_ID IOS_ASC_ISSUER_ID
wg_require_file "$IOS_ASC_KEY_PATH" "App Store Connect API key"

wg_require_dir "$EXPORT_DIR" "export directory"
EXPORT_DIR=$(wg_abs_path "$EXPORT_DIR")

wg_load_build_info "$EXPORT_DIR"

if [ "${PLATFORM:-}" != "ios" ]; then
  die "$EXIT_USAGE" "build-info.env at $EXPORT_DIR describes platform '${PLATFORM:-<unset>}', not ios"
fi

# What the export actually produced decides whether there is anything here to upload, and
# it has to be asked before the artifact is resolved. WriteBuildInfo always writes *both*
# artifact basenames -- one per method, so a later run can pick either without a second
# Unity launch -- so the appstore basename existing proves nothing. Left to the file check
# below, an ad-hoc-only export would fail as "App Store .ipa to upload not found", which
# reports the symptom and hides the cause. Padded with spaces so the space-separated list
# is matched a whole token at a time.
case " ${IOS_EXPORT_METHODS:-} " in
  *" appstore "*) ;;
  *)
    die "$EXIT_USAGE" \
      "the export at $EXPORT_DIR produced export method(s) '${IOS_EXPORT_METHODS:-<unset>}', which do not include appstore; TestFlight needs an App Store build, so there is nothing here to upload"
    ;;
esac

# Said out loud, because 'both' is the one case where someone could reasonably expect two
# uploads. Only the App Store build can go to TestFlight.
case " ${IOS_EXPORT_METHODS:-} " in
  *" adhoc "*)
    log "this build also produced an ad-hoc .ipa; only the App Store one is uploaded"
    ;;
esac

# The one file the packaging stage delivered, named by build-info.env. wg_deliver_artifact
# refuses to overwrite, so this path identifies one specific build rather than a guess.
if [ -z "$IPA" ]; then
  wg_require_env ARTIFACT_DIR ARTIFACT_BASENAME_APPSTORE
  IPA="$ARTIFACT_DIR/$ARTIFACT_BASENAME_APPSTORE.ipa"
fi

wg_require_file "$IPA" "App Store .ipa to upload"
if [ ! -s "$IPA" ]; then
  die "$EXIT_USAGE" "the .ipa to upload is empty: $IPA"
fi
IPA=$(wg_abs_path "$IPA")

log "uploading to App Store Connect (TestFlight): $IPA"

wg_block_opened "altool upload to TestFlight"
set +e
# altool's stdout is redirected to stderr along with everything else this script says:
# stdout is reserved for TeamCity service messages and the artifact path, which is what
# makes "the last line of stdout is the artifact" hold through the upload as well.
xcrun altool --upload-app \
  -f "$IPA" \
  -t ios \
  --apiKey "$IOS_ASC_KEY_ID" \
  --apiIssuer "$IOS_ASC_ISSUER_ID" \
  --p8-file-path "$IOS_ASC_KEY_PATH" >&2
upload_status=$?
set -e
wg_block_closed "altool upload to TestFlight"

if [ "$upload_status" -ne 0 ]; then
  # EXIT_BUILD rather than EXIT_USAGE: every prerequisite was there, so this is a
  # stage that ran and failed. No automatic retry -- a duplicate build number will fail
  # identically however many times it is tried, and only a human can tell that apart from
  # a network blip.
  printf '[wg] ERROR: xcrun altool --upload-app failed (exit %s). See altool'\''s output above.\n' \
    "$upload_status" >&2
  printf '[wg]        The artifact is untouched at: %s\n' "$IPA" >&2
  printf '[wg]        Re-send it with: BuildTools/build.sh --platform ios --stage upload\n' >&2
  exit "$EXIT_BUILD"
fi

log "App Store Connect accepted the upload; it appears in TestFlight once Apple has processed it"

# Same contract the packaging stage follows, so the last line of stdout is still the
# artifact -- here the one that was uploaded.
printf '%s\n' "$IPA"
