# Build tools

Produces a signed iOS `.ipa` and Android `.aab` from this Unity project. One code path,
two front-ends: the Editor menu under **Tools > Build Tools**, and
`BuildTools/build.sh` for a terminal or a TeamCity step.

```
Assets/BuildTools/
  Editor/                         the Unity side: BuildCli, BuildRunner, BuildProfile,
                                  BuildNaming, BuildToolsWindow, IosExportCompliance
  Editor/Tests/                   EditMode tests for the pure logic
  Shell/
    build.sh                      entry point for both platforms
    lib/common.sh                 logging, TeamCity messages, validation helpers
    android/bundle_release.sh     Gradle -> .aab
    ios/archive_and_export.sh     xcodebuild archive/export -> .ipa
    ios/upload_testflight.sh      xcrun altool -> App Store Connect / TestFlight
    ios/ExportOptions.manual.template.plist
    ios/ExportOptions.automatic.template.plist
    tests/run_tests.sh            behaviour tests; no credentials or toolchain needed

BuildTools/build.sh               forwarder, so the documented command still works
```

Everything lives under `Assets/` so the whole tool exports as one `.unitypackage` --
see **Distribution** below. `BuildTools/build.sh` at the project root is a forwarder kept
for the commands and CI steps that already reference it; it is not part of the package.

## How a build runs

1. **Unity stage.** Unity applies the chosen profile, exports the native project
   (Xcode project for iOS, Gradle project for Android) and writes `build-info.env`
   into the export directory.
2. **Packaging stage.** A shell script reads `build-info.env`, runs `xcodebuild` or
   Gradle, and copies the artifact into `Build/Artifacts/`.
3. **Upload stage** — iOS only, opt-in with `--upload-testflight`. Another shell script
   reads the same `build-info.env`, finds the App Store `.ipa` the packaging stage
   delivered, and sends it to App Store Connect with `xcrun altool`. Off unless asked for.

`build-info.env` is the only channel between the stages. That is why the shell
scripts are static and reusable: nothing is text-substituted into a copy of them, and
no build metadata is smuggled through custom `Info.plist` keys.

Artifact names are `{prefix}_{platform}_{version}-{buildNumber}{suffix}[_{method}]_{yyMMddHHmm}`:

```
Build/Artifacts/WoolGather_Android_0.1.0-42_2608111530.aab
Build/Artifacts/WoolGather_iOS_0.1.0-42_appstore_2608111530.ipa
```

## One-time setup

1. **Build profile.** Open **Tools > Build Tools > Build Settings** and
   press *Create Default Profile*, or use **Assets > Create > Build Tools > Build Profile**.
   A default one ships at `Assets/_Project/Configs/BuildProfiles/Release.asset`, so this is
   only needed if you want a second profile (say a staging variant). Profiles hold non-secret
   settings only: artifact prefix and suffix, optional per-platform bundle id overrides, the
   development-build flag, extra scripting defines, the iOS provisioning profile type,
   Android architectures, and the export/artifact output folders.
2. **Android:** obtain the release keystore. It is deliberately **not** in the repo
   (`.gitignore` excludes `*.keystore` and `*.jks`). Note its path, store password, key
   alias and alias password.
3. **iOS:** note your Apple Developer **Team ID** — needed under either signing style.
   For the default **manual** signing, also note the exact **names** of the distribution
   provisioning profiles and install them on the build machine. For **automatic** signing
   (`--signing auto`) you need no profile names, but the machine must be able to
   authenticate with Apple: see below.

The profile's bundle id fields are **empty by default, meaning "use the project's own
identifier"** — the tool will not silently rewrite an identifier you set in Player Settings.
Fill one in only to override it for builds made with that profile, e.g. a staging
identifier; the override is applied for the build and then reverted, so `ProjectSettings/`
is left untouched either way. The identifier actually built with is recorded in
`build-info.env` as `BUNDLE_ID`, and the iOS ExportOptions profile mapping keys off it.

## Environment variables

Credentials are read from the environment, never from an asset or a committed file.
Nothing here is ever echoed to the log.

| Variable | Platform | Required | Purpose |
| --- | --- | --- | --- |
| `ANDROID_KEYSTORE_PATH` | Android | yes | Path to the release keystore. Must exist. |
| `ANDROID_KEYSTORE_PASS` | Android | yes | Keystore password. |
| `ANDROID_KEY_ALIAS` | Android | yes | Signing key alias. |
| `ANDROID_KEY_ALIAS_PASS` | Android | yes | Alias password. |
| `IOS_TEAM_ID` | iOS | yes | Apple Developer team id, e.g. `2S5CQ8C2GL`. Required under **both** signing styles. For Editor builds it can be typed into Build Settings instead — see below. |
| `IOS_PROFILE_APPSTORE` | iOS | manual, for `appstore`/`both` | Provisioning profile **name**. |
| `IOS_PROFILE_ADHOC` | iOS | manual, for `adhoc`/`both` | Provisioning profile **name**. |
| `IOS_CODE_SIGN_IDENTITY` | iOS | no | Manual signing only. Defaults to `Apple Distribution`. |
| `IOS_ASC_KEY_PATH` | iOS | `--upload-testflight` / `--stage upload`: **required** | App Store Connect API key `.p8`. All three ASC variables together, or none. May live anywhere. Upload credentials only: they do not change how signing authenticates unless `IOS_SIGNING_USE_ASC_KEY=1`. For Editor builds it can be typed into Build Settings instead — see below. |
| `IOS_ASC_KEY_ID` | iOS | as above | The key's id. Typeable in Build Settings too. |
| `IOS_ASC_ISSUER_ID` | iOS | as above | The key's issuer id. Typeable in Build Settings too. |
| `IOS_SIGNING_USE_ASC_KEY` | iOS | auto signing on a headless agent | Set to `1` to make automatic signing authenticate as the ASC key above instead of the Apple ID in the keychain. Needs an **Admin** key; see the signing section. Off by default, so an upload flag cannot change how signing works. |
| `IOS_PROFILE_ID` | iOS | no | Profile **UUID**, if you want Unity's manual-signing field populated too. |
| `IOS_XCODEBUILD_EXTRA` | iOS | no | Extra `SETTING=value` pairs for `xcodebuild`. Escape hatch when a target needs a signing override the script does not model. |
| `ANDROID_GRADLE_EXTRA` | Android | no | Extra Gradle arguments, whitespace separated. `--no-daemon` is worth adding on CI agents so daemons do not accumulate. |
| `OVERWRITE_ARTIFACTS` | both | no | Set to `1` to replace an artifact that already exists. Off by default: names are unique only to the minute, and silently replacing a published artifact is worse than failing. |
| `UNITY_PATH` | both | no | Unity executable or `Unity.app`. Otherwise resolved from `ProjectSettings/ProjectVersion.txt` via the Hub install path. |

The Unity stage on Android needs all four keystore variables, because Unity bakes
`signingConfigs` into the exported Gradle project at export time. Running only
`--stage native` needs the keystore *file* but not the passwords.

> **The exported Gradle project contains your keystore passwords in cleartext.** That is a
> consequence of how Unity emits `signingConfigs`, not a choice this tool makes. Never
> publish, archive or attach `Build/Android/AndroidProject/` as a CI artifact — collect
> only `Build/Artifacts/`. `Build/` is gitignored, so it will not be committed.

## Command line

```sh
# Android app bundle
export ANDROID_KEYSTORE_PATH=~/keys/woolgather.keystore
export ANDROID_KEYSTORE_PASS=...
export ANDROID_KEY_ALIAS=woolgather
export ANDROID_KEY_ALIAS_PASS=...
BuildTools/build.sh --platform android --version 1.0.0 --build-number 42

# iOS, both distribution channels from a single archive
export IOS_TEAM_ID=2S5CQ8C2GL
export IOS_PROFILE_APPSTORE="Wool Gather AppStore"
export IOS_PROFILE_ADHOC="Wool Gather AdHoc"
BuildTools/build.sh --platform ios --export-method both --version 1.0.0 --build-number 42
```

`BuildTools/build.sh --help` lists every flag. The ones that matter most:

- `--profile NAME` — which `BuildProfile` asset to use. Default `Release`.
- `--version X.Y.Z` — marketing version. Omit to keep the project's value. See below.
- `--build-number N` — build number / `versionCode`. Omit to keep the project's value.
- `--export-method appstore|adhoc|both` — iOS only.
- `--signing manual|auto` — iOS only, default `manual`. See below.
- `--upload-testflight` — iOS only, off by default. Send the App Store `.ipa` to App Store
  Connect after packaging. See below.
- `--stage all|unity|native|upload` — split the stages, e.g. across TeamCity steps, or
  re-run packaging after hand-editing the exported project. With `--stage native` or
  `--stage upload` the artifact directory, version, build number and profile come from the
  `build-info.env` the Unity stage wrote, so passing those flags again has no effect and the
  script says so. `--stage upload` implies `--upload-testflight`.
- `--nographics` — for headless agents with no display.
- `--keep-export` — do not delete the previous export directory first.

Exit codes: **0** success, **1** a build stage ran and failed, **2** bad usage or a
missing prerequisite (nothing was built). Human-readable progress goes to stderr; stdout
carries TeamCity service messages and the delivered artifact paths, so the last line of
stdout is always the artifact.

## Version and build number

Two separate numbers, both optional, both overriding Player Settings for one build only:

| Flag | Player Settings field | iOS | Android |
| --- | --- | --- | --- |
| `--version 1.0.0` | `bundleVersion` | `CFBundleShortVersionString` | `versionName` |
| `--build-number 42` | `iOS.buildNumber` / `Android.bundleVersionCode` | `CFBundleVersion` | `versionCode` |

```sh
BuildTools/build.sh --platform ios --profile Release \
  --export-method appstore --signing auto --version 1.0.0 --build-number 42
```

`--version` takes **one to three dot-separated numbers**: `1`, `1.0` or `1.0.0`. Android's
`versionName` would accept anything, but iOS's `CFBundleShortVersionString` would not, and
App Store Connect only complains at upload — long after the build. So the stricter of the
two rules is enforced up front, as a usage error (exit 2) before Unity is even launched.
Leading zeros are rejected too (`1.2`, not `1.02`): the stores treat those as the same
version, but they would produce two different artifact names.

Omit either flag and the project's own value is used. Both are applied before the export —
Unity bakes them into the generated `Info.plist` and `build.gradle` — recorded in
`build-info.env` as `VERSION` and `BUILD_NUMBER`, used in the artifact name, and then
**restored**, so `ProjectSettings/` never shows up in a git diff after a build. Under
TeamCity the pair is reported as one build number, `1.0.0-42`, matching the artifact name.

The same two fields are in **Build Settings** in the Editor, where empty likewise means
"keep the project's value"; the *Version built* row shows which one a build would use.

## iOS signing: manual or automatic

Default is **manual**, and that is the recommendation for TeamCity.

| | manual (default) | auto (`--signing auto`) |
| --- | --- | --- |
| Needs | Team ID + profile **names** | Team ID + a way to authenticate with Apple |
| `xcodebuild` gets | `CODE_SIGN_STYLE=Manual`, pinned identity and `PROVISIONING_PROFILE_SPECIFIER` | `CODE_SIGN_STYLE=Automatic`, `-allowProvisioningUpdates` |
| ExportOptions | `signingStyle=manual` plus a `provisioningProfiles` map | `signingStyle=automatic`, no profile map |
| Deterministic | yes — the same profile every time | no — Xcode resolves, and may create |
| Touches the Apple account | no | **yes**: `-allowProvisioningUpdates` can create or modify profiles and certificates on the team account |

Two things to know before choosing `auto`:

- **It has to reach Apple.** Either an Apple ID signed in to Xcode (stored in the keychain),
  or an App Store Connect API key. The default is the keychain. A headless TeamCity agent has
  no keychain session, so on CI the API key is the only route — set
  **`IOS_SIGNING_USE_ASC_KEY=1`** alongside `IOS_ASC_KEY_PATH`, `IOS_ASC_KEY_ID` and
  `IOS_ASC_ISSUER_ID`, and the script passes
  `-authenticationKeyPath/-authenticationKeyID/-authenticationKeyIssuerID` to both the
  archive and the export. **That key needs the Admin role**: App Store Connect grants access
  to cloud-managed distribution certificates to users, not to keys, so a Developer or App
  Manager key gets `403 FORBIDDEN_ERROR` (resultCode 7495) and the export dies with
  `No signing certificate "iOS Distribution" found`.
- **The three `IOS_ASC_*` variables alone do not affect signing**, and that is deliberate.
  `-authenticationKey*` does not add a permission — it *replaces* the identity xcodebuild
  provisions as. Handing the upload's key to the archive therefore swaps a working Apple ID
  session for a key that may not see the team's cloud-managed distribution certificate, and
  `--upload-testflight` would break an export that succeeds without it. If auto signing works
  on your machine, adding the upload credentials leaves it working.
- **`adhoc` + `auto` is the weakest combination.** Xcode can only put *already registered*
  device UDIDs into a profile it generates, so an ad-hoc export fails on an account with no
  devices registered. `appstore` + `auto` is fine.

The signing style is recorded in `build-info.env` as `IOS_SIGNING`, so a later
`--stage native` run reproduces the same choice without being told again.

## Uploading to TestFlight

Off by default. Add `--upload-testflight` and the build continues past the artifact: the
App Store `.ipa` is handed to App Store Connect with `xcrun altool --upload-app`, which is
what puts it in front of TestFlight testers.

```sh
export IOS_TEAM_ID=2S5CQ8C2GL
export IOS_PROFILE_APPSTORE="Wool Gather AppStore"
export IOS_ASC_KEY_PATH=~/keys/AuthKey_ABC123.p8
export IOS_ASC_KEY_ID=ABC123
export IOS_ASC_ISSUER_ID=69a6de00-...
BuildTools/build.sh --platform ios --build-number 42 --upload-testflight
```

- **The same three `IOS_ASC_*` variables automatic signing uses**, and all three are
  required here. No new secret was invented: the key that authenticates `xcodebuild` is the
  key that authenticates `altool`. They are checked, along with the existence of the `.p8`
  file, **before Unity is launched** — the same rule the signing credentials follow.
- **The key stays where it is.** It reaches altool as `--p8-file-path`, so
  `IOS_ASC_KEY_PATH` may point anywhere. Without that flag altool searches only
  `./private_keys`, `~/private_keys`, `~/.private_keys` and
  `~/.appstoreconnect/private_keys` — the first of which is the checkout, so nothing has to
  be copied into the repo or into your home directory.
- **iOS and App Store only.** `--export-method adhoc` with `--upload-testflight` is a usage
  error (exit 2, before Unity), as is `--platform android`: TestFlight will not take an
  ad-hoc `.ipa`, and Play Console is out of scope. `--export-method both` still produces two
  artifacts and uploads the appstore one, saying so in the log.
- **Exactly the artifact that was delivered** is uploaded, located through `build-info.env`
  (`ARTIFACT_DIR` + `ARTIFACT_BASENAME_APPSTORE`). No file name is re-derived.
- **A failed upload is cheap to retry.** An upload fails for reasons that have nothing to
  do with the build — a build number already used, a network blip, an expired key — and
  re-archiving costs about twenty minutes. So it is its own stage, and the artifact is left
  untouched on disk:

  ```sh
  # re-send what is already there; no Unity, no xcodebuild, no signing credentials needed
  BuildTools/build.sh --platform ios --stage upload
  ```

  There is no automatic retry: a duplicate build number will fail identically however many
  times it is tried, and only a human can tell that apart from a network blip.
- **altool's own output goes to stderr**, so the upload adds nothing to stdout but the path it
  sent — and the last line of stdout is still the artifact. With
  `--export-method both --upload-testflight` the App Store path is therefore printed **twice**,
  once on delivery and once by the upload, which also means the last line is the App Store
  `.ipa` rather than whichever method happened to be exported last. (`xcodebuild`'s own stdout
  is inherited as it always was, so stdout is not *only* artifact paths; what holds is that
  the last line is one.)
- Uploading is **not** submitting. Nothing here assigns tester groups, submits for review,
  or touches any other App Store Connect API.

### Which stages upload

| Command | Uploads |
| --- | --- |
| `--stage all --upload-testflight` (the default stage) | yes, after packaging |
| `--stage native --upload-testflight` | yes — one step that packages an existing export and then uploads |
| `--stage upload` | yes, and nothing else; implies `--upload-testflight` |
| `--stage unity --upload-testflight` | **no** — nothing is packaged, so the flag is reported as ignored and switched off, and the run does not demand credentials it will not use. That is what lets a two-step TeamCity setup pass the same flag set to both steps. |
| anything without the flag | no, and no ASC variable is required |

The Editor toggle and the `-executeMethod BuildCli.BuildIos` entry point both follow the same
rule: they package in-process, so they upload in-process too.

### `--ipa PATH`, and what it does not bypass

`Assets/BuildTools/Shell/ios/upload_testflight.sh` takes an `--ipa` escape hatch — `build.sh` deliberately
does not, so the one-entry-point contract keeps holding. It replaces the **artifact
resolution** (the `ARTIFACT_DIR` + `ARTIFACT_BASENAME_APPSTORE` lookup) and nothing
else. Still enforced: `xcrun` on `PATH`, all three credentials, the key file existing,
`build-info.env` existing and describing an `ios` build whose recorded export methods include
`appstore`, and the named file existing and being non-empty. It is for the case where the
artifact was renamed or moved by hand — not a way to upload an ad-hoc build or to skip the
build info.

### What is visible on a shared machine

The key id and the issuer id are passed to altool as command-line arguments, so on a shared
build agent they are readable with `ps` for as long as the upload runs. The `.p8` itself never
is — only its path. Nothing is written to a log by these scripts either way.

That is worth knowing rather than worrying about: they are identifiers for a key, useless
without the `.p8`, which is why marking them `password`-typed in TeamCity is still the right
default (it keeps them out of the build log and the UI) rather than a guarantee about process
listings. Protect the `.p8` file itself with filesystem permissions; that is the secret.

### Verified against the real altool

The test suite stubs `xcrun`, so it cannot prove the flag spellings. Those were checked by
hand against **Xcode 26.3 / altool 26.10.1**, which accepts
`--upload-app -f <ipa> -t ios --apiKey <id> --apiIssuer <id> --p8-file-path <path>` and loads
the key from the path given. Re-check them after a major Xcode upgrade: altool also accepts
`--api-key`/`--api-issuer` as aliases, and Apple has renamed export-method spellings before
(see the Xcode 15.3 note below).

## iOS export compliance

Every iOS export gets `ITSAppUsesNonExemptEncryption = false` written into `Info.plist`
by `IosExportCompliance`, a `PostProcessBuild` callback on the Unity side. Without it
App Store Connect flags each uploaded build **"Missing Compliance"** and blocks
submission until someone answers the encryption question by hand in the web UI — per
build, every time. Unity neither writes the key nor exposes a setting for it, so it is
added after the export.

`false` is a declaration to a regulator, not a build switch, which is why it is a
constant in the source rather than a profile field. It says the app contains no
cryptography of its own and uses only the operating system's HTTPS/TLS, which Apple
exempts. **If that ever stops being true** — a bundled crypto library, save files the
game encrypts itself, a proprietary algorithm — change
`IosExportCompliance.UsesNonExemptEncryption` and expect Apple's self-classification
paperwork to apply.

```sh
# quick local build with automatic signing, using the Apple ID in Xcode
BuildTools/build.sh --platform ios --signing auto --build-number 42

# automatic signing on CI, authenticating with an App Store Connect API key
export IOS_ASC_KEY_PATH=~/keys/AuthKey_ABC123.p8
export IOS_ASC_KEY_ID=ABC123
export IOS_ASC_ISSUER_ID=69a6de00-...
BuildTools/build.sh --platform ios --signing auto --build-number %build.counter%
```

## Building from the Editor

**Tools > Build Tools >**

- **Build Settings** — profile picker, version, build number, export method, signing style,
  iOS **Team ID**, an **Upload TestFlight** toggle, and a read-only checklist showing which
  credential variables are set — the checklist follows the settings, so switching to
  automatic signing drops the profile-name rows and ticking *Upload TestFlight* adds the
  three `IOS_ASC_*` rows. Also *Copy CLI Command*.
- **Build iOS (IPA)** / **Build Android (AAB)** — build straight away using the settings
  last used in the window.
- **Open Artifacts Folder**.

Two caveats. A build started here blocks the Editor until `xcodebuild`/Gradle finishes —
*Copy CLI Command* exists so you can run the identical build in a terminal instead. And
the Editor only sees environment variables from the shell that launched it, so if you
start Unity from the Hub or Finder the credential checklist will show everything as
missing; either launch Unity from a configured shell or use the CLI.

### Team ID in Build Settings

That last caveat is worst for the team id, which is needed under **both** signing styles — so
Build Settings takes one directly. Leave it empty and `IOS_TEAM_ID` is read from the
environment exactly as before; fill it in and it is used for the build instead, which is what
makes **automatic signing + Team ID enough to build from a Hub-launched Editor with no
environment at all**. Under manual signing the profile names are still needed, so the
checklist keeps asking for those.

The value is used in both stages: Player Settings' `appleDeveloperTeamID` for the export, and
injected into the packaging stage's environment as `IOS_TEAM_ID`, since
`archive_and_export.sh` reads it there. It is stored per machine in `EditorPrefs`, never in
the project — the team id is an identifier rather than a secret, which is why it can be
handled this way at all while the passwords cannot. *Copy CLI Command* prefixes it as a plain
assignment, so the copied line works in a shell that has nothing set:

```sh
IOS_TEAM_ID=2S5CQ8C2GL BuildTools/build.sh --platform ios --export-method appstore --signing auto
```

`build.sh` itself has no `--team-id` flag on purpose: on the command line the environment
stays the one way credentials arrive.

### Upload TestFlight in Build Settings

iOS only, off by default, remembered per machine in `EditorPrefs` — and honoured by the
**Build iOS (IPA)** menu item, which builds with whatever the window last had. It greys out,
with the reason spelled out, when *Export method* is **AdHoc** or *Run packaging stage* is
off; a greyed-out toggle never uploads, whatever was stored. Ticking it adds the three
`IOS_ASC_*` rows to the credential checklist.

Those three can be **typed straight into the window**, like the Team ID: ticking the toggle
reveals *Key file (.p8)* (with a **Browse…** picker), *Key ID* and *Issuer ID*, and automatic
signing shows the same rows as optional. Leave one empty and that variable is read from the
environment exactly as before, so the two sources mix freely — fill in two fields and the
checklist asks only for the third. A path that names no file disables the Build button rather
than failing twenty minutes later. All three are remembered per machine in `EditorPrefs`, so
they survive closing Unity, and *Copy CLI Command* prefixes whatever was typed as plain
assignments so the copied line runs in a shell that has nothing set.

This is safe for the same reason the Team ID is, and for no stronger reason: they are two
identifiers and a path. **The `.p8` itself is never opened, read or copied by Unity** — only
its path is stored, and only altool opens the file. Keep all of it out of `BuildProfile`
assets and the repo, which are committed; `EditorPrefs` is a per-user `0600` plist outside
the project. If your machine is shared by accounts you would not hand the key to, leave the
fields empty and use the environment.

The upload itself is the same `Assets/BuildTools/Shell/ios/upload_testflight.sh` the CLI runs, started
as a child process exactly as the packaging stage is, so the Editor and the command line
cannot drift apart.

## TeamCity

Add a **Command Line** build step:

```
Working directory: <checkout>
Custom script:     BuildTools/build.sh --platform android --version %wg.version% --build-number %build.counter%
```

Define the credential variables as build parameters, marking the passwords as
`password`-typed so TeamCity masks them. A configuration parameter such as `wg.version`
lets whoever triggers the build set the release version without a commit; drop `--version`
altogether to ship whatever is in Player Settings. The scripts detect `TEAMCITY_VERSION`
and then:

- wrap each stage in `blockOpened`/`blockClosed`,
- report `##teamcity[buildNumber '1.0.0-42']` — the version and build number as one string,
  the same pair the artifact name carries, or just the half that was given,
- publish each artifact with `##teamcity[publishArtifacts ...]`,
- emit exactly one `##teamcity[buildProblem ...]` on any failure past argument parsing,
  including a missing credential. Only malformed command lines — an unknown flag, a bad
  `--version` or `--build-number`, `--help` — exit without one, because nothing was
  attempted.

You can also collect artifacts with a path of `Build/Artifacts/*.aab` or
`Build/Artifacts/*.ipa`.

To split the work across steps — useful when the Unity licence and the signing identity
live on different agents — run `--stage unity` in one step and `--stage native` in a
later one against the same checkout.

Splitting **build** from **upload** is worth doing for a different reason: an upload fails
for reasons the build cannot control, and this way the retry is one click on one step rather
than a fresh twenty-minute archive.

```
Step 1  BuildTools/build.sh --platform ios --build-number %build.counter%
Step 2  BuildTools/build.sh --platform ios --stage upload
```

Each stage gets its own `blockOpened`/`blockClosed`, the upload included, and a failed
upload still produces exactly one `buildProblem` and exit code 1. Step 2 needs only the
three `IOS_ASC_*` parameters — no team id, no profile names, since nothing is signed.
Mark `IOS_ASC_KEY_ID` and `IOS_ASC_ISSUER_ID` as `password`-typed parameters and keep
the `.p8` itself off the checkout; point `IOS_ASC_KEY_PATH` at a file on the agent, and
protect that file with filesystem permissions — see *What is visible on a shared machine*
above for what `password`-typed does and does not cover.

Packaging and uploading in one step is `--stage native --upload-testflight`, if you want the
Unity licence split off but not the upload.

If you would rather drive everything from a single Unity launch and skip `build.sh`, the
`-executeMethod` entry points `BuildCli.BuildIos` and `BuildCli.BuildAndroid` run the export
*and* the packaging stage in-process — and `BuildCli.BuildIos` accepts `--upload-testflight`
after `--`, so it can do all three. `BuildCli.ExportIos`/`ExportAndroid` do the export only,
which is what `build.sh` uses.

`build.sh` never forwards `--upload-testflight` to the Unity stage it drives: it owns the
upload stage itself, and Unity scans the whole command line even past `--`, so every flag name
has to be checked against Unity's own before being passed through.

## Distribution

The tool is self-contained: the Editor C# references nothing but `UnityEditor`, and the
shell layer talks to it only through `build-info.env` and one `-executeMethod` name. So it
moves to another project as a unit.

### As a `.unitypackage`

**Tools > Build Tools > Export .unitypackage**, or without opening the Editor:

```sh
Unity -batchmode -quit -projectPath . \
      -executeMethod ClassicSpins.BuildTools.BuildToolsExporter.ExportFromCommandLine \
      --output BuildTools.unitypackage
```

The receiving project drags the file in and is done -- no `manifest.json` edit, no git
access. Three things follow from the format:

- **The shell scripts are ordinary assets, not a `~` folder.** A `.unitypackage` carries
  only what the AssetDatabase can see, and Unity ignores `~` folders entirely. `.sh` and
  `.plist` import as inert `DefaultAsset`s: no compilation, nothing in a player build.
- **The executable bit is not carried.** Hence `bash <path>/build.sh`, and hence the
  forwarder execs with `bash` too. The scripts already invoke each other that way.
- **There is no uninstall.** Re-importing overwrites by GUID, so an update lands on the
  same files -- but a file dropped in a later version stays behind on the receiving side.

The tests under `Editor/Tests/` are deliberately left out of the package: they reference
the test framework, so they would break a project that does not have it, and they test
this tool rather than the project importing it.

### As a UPM package

The same tree works as a package: add a `package.json` and put it in `Packages/`, or point
a git URL at it. Rename `Shell/` to `Shell~` in that layout so Unity stops importing the
scripts as assets -- nothing else changes, because `build.sh` finds the project by walking
up from its own location.

### What a receiving project has to do

1. Create a profile: **Tools > Build Tools > Build Settings > Create Default Profile**.
   The artifact prefix is seeded from `productName`; the bundle id fields stay empty,
   meaning "use the project's own identifier".
2. Have at least one enabled scene in Build Settings.
3. Add `[Bb]uild/`, `*.keystore` and `*.jks` to `.gitignore`.
4. Install the iOS and Android build support modules. `IosExportCompliance.cs` is behind
   `#if UNITY_IOS`, so a machine without the iOS module still compiles.
5. Set the credentials from the environment-variable table above. Nothing is baked into
   an asset.

Optionally copy `BuildTools/build.sh` -- the root forwarder -- so `BuildTools/build.sh`
keeps being the command. Otherwise call
`bash Assets/BuildTools/Shell/build.sh` directly.

## Tests

```sh
bash Assets/BuildTools/Shell/tests/run_tests.sh
```

Runs the shell layer against a throwaway copy of `BuildTools` inside a temporary
project, with stub `Unity`, `gradlew`, `xcodebuild` and `xcrun altool` executables. It needs
no credentials, no Xcode, no JDK, no App Store Connect key and no Unity licence, and never
touches the real `Build/` directory — so it is worth wiring in as a fast TeamCity gate ahead
of the real build. Nothing is ever uploaded: the stub records how altool was invoked and
exits with whatever code the case asks for.

The Unity-side pure logic (artifact naming, CLI argument parsing) is covered by EditMode
tests in `Assets/BuildTools/Editor/Tests/`:

```sh
/Applications/Unity/Hub/Editor/6000.3.12f1/Unity.app/Contents/MacOS/Unity \
  -batchmode -runTests -testPlatform EditMode -projectPath "$PWD" \
  -logFile - -testResults /tmp/wg-tests.xml
```

Close the Editor first — batch mode cannot open a project another instance has locked.

## Notes on the design

- **No `say`, no `open -a iTerm`, no AppleScript** anywhere in a build path. A CI agent
  has no GUI session, and the earlier scripts these replace hung or failed there.
- **`set -euo pipefail` everywhere**, so a failing step cannot be reported as success.
- **Never guess a file name.** The scripts require exactly one matching `.aab`/`.ipa` and
  fail loudly on zero or several, rather than assuming a fixed export name. Delivery also
  refuses to overwrite an artifact that already exists.
- **Gradle is launched with `-cp` and `org.gradle.launcher.GradleMain`, not `java -jar`.**
  Unity's `gradle-launcher-*.jar` declares `Class-Path` but no `Main-Class`, so `-jar` fails
  with "no main manifest attribute". `./gradlew` is preferred when a project has one, but
  Unity does not emit a wrapper.
- **`java` on `PATH` is run before being believed.** macOS always has `/usr/bin/java`; it is
  a stub that fails, so mere existence proves nothing.
- **Our flags are passed to Unity after `--`, but Unity still scans the whole command line.**
  It ignores names it does not know, so most flags pass through untouched — but a name Unity
  *does* claim is acted on even there. `--version` is one: Unity reads it as its own
  `-version`, prints the editor version and exits 0 without running `-executeMethod`, so the
  export silently produces nothing. That is why the user-facing `build.sh --version` reaches
  Unity as `--app-version` (`BuildArguments.VersionFlag`). Check any new flag name against
  Unity's command-line arguments before adding it.
- **Export method spelling follows the installed Xcode.** Xcode 15.3 renamed `app-store`
  to `app-store-connect` and `ad-hoc` to `release-testing`; older Xcode rejects the new
  names. The script reads `xcodebuild -version` and picks accordingly.
- **The exported project is located, not assumed.** Unity nests the Android Gradle
  project in a folder named after `productName` ("Wool Gather", with a space) while the
  iOS project lands directly in the export directory, so both are found by marker file.
- **The upload is a third stage, not a step inside packaging.** Uploads fail for reasons
  archiving cannot fix, and re-archiving costs twenty minutes; separating them makes the
  retry free and lets TeamCity own it as its own step. The key is passed to altool with
  `--p8-file-path` rather than being copied into one of the four fixed directories altool
  would otherwise search — one of which is the working directory.
- **`ProjectSettings/` is restored** after every build, successful or not.
- Written for **bash 3.2**, the system bash on macOS — no associative arrays, no
  `mapfile`, no `${var,,}`.
