using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;
using Debug = UnityEngine.Debug;
using NamedBuildTarget = UnityEditor.Build.NamedBuildTarget;

namespace ClassicSpins.BuildTools
{
    /// <summary>A build stage ran and failed. Distinct from a usage error.</summary>
    public sealed class BuildFailedException : Exception
    {
        public BuildFailedException(string message) : base(message) { }
    }

    public sealed class BuildRequest
    {
        /// <summary>
        /// Only safe to read <em>before</em> the export. See <see cref="BuildRunner"/>'s
        /// ProfileData for why.
        /// </summary>
        public BuildProfile Profile;
        public MobilePlatform Platform;

        /// <summary>
        /// Marketing version — CFBundleShortVersionString on iOS, versionName on Android.
        /// Null or empty keeps whatever <see cref="PlayerSettings.bundleVersion"/> already
        /// holds; a value overrides it for this build only and is then restored.
        /// </summary>
        public string Version;

        /// <summary>Null or empty keeps whatever build number the project already has.</summary>
        public string BuildNumber;

        public IosExportMethod[] ExportMethods = { IosExportMethod.AppStore };
        public bool DevelopmentBuild;

        /// <summary>Absolute path. Receives the exported native project and build-info.env.</summary>
        public string ExportDir;

        /// <summary>Absolute path. Receives the finished .ipa / .aab.</summary>
        public string ArtifactDir;

        /// <summary>When false, stop after the Unity export (build.sh --stage unity).</summary>
        public bool RunNativeStage = true;

        /// <summary>
        /// iOS only, and only for a request that produces an App Store <c>.ipa</c>. After the
        /// packaging stage, upload that artifact to App Store Connect so it reaches TestFlight
        /// (build.sh --upload-testflight). Off by default: an upload is never something to do
        /// because nobody said not to.
        /// </summary>
        public bool UploadToTestFlight;

        /// <summary>Leave a previous export in place instead of deleting it first.</summary>
        public bool KeepExport;

        /// <summary>
        /// iOS only. Manual pins the profile by name and is deterministic, which is what CI
        /// wants. Automatic lets Xcode resolve and create profiles, which needs a signed-in
        /// Apple ID or an App Store Connect API key.
        /// </summary>
        public bool AutomaticSigning;

        /// <summary>
        /// iOS only. Apple Developer team id, overriding <see cref="BuildRunner.EnvIosTeamId"/>
        /// for this build. Exists because Unity launched from the Hub or Finder inherits none of
        /// the shell's environment, which would otherwise make an Editor build impossible; the
        /// team id is an identifier rather than a secret, so unlike the passwords it is safe to
        /// hand over this way. Null or empty reads the environment as before.
        /// </summary>
        public string TeamId;

        /// <summary>
        /// iOS only. App Store Connect API key details, overriding
        /// <see cref="BuildRunner.EnvIosAscKeyPath"/>, <see cref="BuildRunner.EnvIosAscKeyId"/>
        /// and <see cref="BuildRunner.EnvIosAscIssuerId"/> for this build, for the same reason
        /// <see cref="TeamId"/> exists: a Hub-launched Editor has no environment to read them
        /// from. Each is filled in independently — whichever is left empty falls back to the
        /// environment. Two ids and a file path, none of which is the secret; the <c>.p8</c>'s
        /// contents are, and are never handled here.
        /// </summary>
        public string AscKeyPath;

        public string AscKeyId;

        public string AscIssuerId;
    }

    /// <summary>
    /// The build core. Both front-ends — the Editor menu and BuildTools/build.sh via
    /// <see cref="BuildCli"/> — go through here, so they cannot drift apart.
    /// </summary>
    public static class BuildRunner
    {
        public const string BuildInfoFileName = "build-info.env";

        public const string EnvAndroidKeystorePath = "ANDROID_KEYSTORE_PATH";
        public const string EnvAndroidKeystorePass = "ANDROID_KEYSTORE_PASS";
        public const string EnvAndroidKeyAlias = "ANDROID_KEY_ALIAS";
        public const string EnvAndroidKeyAliasPass = "ANDROID_KEY_ALIAS_PASS";
        public const string EnvIosTeamId = "IOS_TEAM_ID";
        public const string EnvIosProfileAppStore = "IOS_PROFILE_APPSTORE";
        public const string EnvIosProfileAdHoc = "IOS_PROFILE_ADHOC";

        /// <summary>Optional: provisioning profile UUID for Unity's manual signing field.</summary>
        public const string EnvIosProfileId = "IOS_PROFILE_ID";

        /// <summary>
        /// App Store Connect API key. Optional for automatic signing, required for a
        /// TestFlight upload — the same three variables either way, since the key that
        /// authenticates xcodebuild is the key that authenticates altool.
        ///
        /// All three name an identifier or a path rather than a secret: two ids that are
        /// useless without the key, and the location of the <c>.p8</c>. So, like the team id,
        /// a caller may carry them itself instead of exporting them — see
        /// <see cref="BuildRequest.AscKeyPath"/>. The <c>.p8</c>'s <em>contents</em> are the
        /// secret, and are never read, stored or passed by this tool: only its path is, and
        /// only as far as altool's <c>--p8-file-path</c>. Protect the file itself with
        /// filesystem permissions. Never put any of this in a profile asset — that is a
        /// committed file, which none of these belong in.
        /// </summary>
        public const string EnvIosAscKeyPath = "IOS_ASC_KEY_PATH";
        public const string EnvIosAscKeyId = "IOS_ASC_KEY_ID";
        public const string EnvIosAscIssuerId = "IOS_ASC_ISSUER_ID";

        public static string ProjectRoot => Path.GetDirectoryName(Application.dataPath);

        /// <summary>
        /// Environment variables this request needs. Used by the runner to fail early and
        /// by the settings window to show a readiness checklist without ever reading values.
        /// <paramref name="suppliedNames"/> drops the names the caller carries itself rather
        /// than reading from the environment — see <see cref="BuildRequest.TeamId"/> and
        /// <see cref="BuildRequest.AscKeyPath"/>. A collection rather than one flag per
        /// credential: the set of things the Editor can stand in for grows, and a row of
        /// interchangeable bools transposes silently.
        /// <paramref name="uploadToTestFlight"/> adds the three App Store Connect names.
        /// </summary>
        public static string[] RequiredEnvironmentVariables(MobilePlatform platform,
            IEnumerable<IosExportMethod> exportMethods, bool includeNativeStage,
            bool automaticSigning = false, bool uploadToTestFlight = false,
            IEnumerable<string> suppliedNames = null)
        {
            var supplied = new HashSet<string>(suppliedNames ?? Enumerable.Empty<string>());
            var names = new List<string>();
            if (platform == MobilePlatform.Android)
            {
                names.Add(EnvAndroidKeystorePath);
                names.Add(EnvAndroidKeystorePass);
                names.Add(EnvAndroidKeyAlias);
                names.Add(EnvAndroidKeyAliasPass);
                return StillNeededFromEnvironment(names, supplied);
            }

            // Needed under both signing styles: automatic signing removes the profile names,
            // not the account Xcode signs on behalf of.
            names.Add(EnvIosTeamId);

            // The upload needs its own credentials whichever signing style is in play, which
            // is why they are added above the early return rather than beside the profile names.
            if (uploadToTestFlight)
            {
                names.Add(EnvIosAscKeyPath);
                names.Add(EnvIosAscKeyId);
                names.Add(EnvIosAscIssuerId);
            }

            if (!includeNativeStage || automaticSigning)
                return StillNeededFromEnvironment(names, supplied);

            foreach (IosExportMethod method in exportMethods ?? Enumerable.Empty<IosExportMethod>())
                names.Add(EnvironmentVariableFor(method));

            return StillNeededFromEnvironment(names, supplied);
        }

        /// <summary>
        /// Deduplicates, then drops whatever the caller is carrying itself. In one place so
        /// that no return path above can forget either step — an earlier version applied
        /// <c>Distinct</c> to some of them and not others.
        /// </summary>
        private static string[] StillNeededFromEnvironment(IEnumerable<string> names,
            ICollection<string> supplied) =>
            names.Distinct().Where(name => !supplied.Contains(name)).ToArray();

        /// <summary>
        /// The credentials this request carries itself, keyed by the environment variable each
        /// stands in for. Both the readiness checklist and the child-process environment are
        /// built from this, so a value typed into Build Settings cannot satisfy one and not
        /// the other. Empty for Android: none of these apply there.
        /// </summary>
        public static Dictionary<string, string> SuppliedCredentials(BuildRequest request)
        {
            var supplied = new Dictionary<string, string>();
            if (request == null || request.Platform != MobilePlatform.iOS)
                return supplied;

            AddIfPresent(supplied, EnvIosTeamId, request.TeamId);
            AddIfPresent(supplied, EnvIosAscKeyPath, request.AscKeyPath);
            AddIfPresent(supplied, EnvIosAscKeyId, request.AscKeyId);
            AddIfPresent(supplied, EnvIosAscIssuerId, request.AscIssuerId);
            return supplied;
        }

        private static void AddIfPresent(IDictionary<string, string> into, string name, string value)
        {
            if (!string.IsNullOrEmpty(value))
                into[name] = value;
        }

        /// <summary>
        /// The value a build will actually use for <paramref name="name"/>: the one the request
        /// carries, else the environment. Resolved in one place because both the Unity stage
        /// and the shell stages have to see the same value.
        /// </summary>
        public static string ResolveCredential(BuildRequest request, string name) =>
            SuppliedCredentials(request).TryGetValue(name, out string typed)
                ? typed
                : Environment.GetEnvironmentVariable(name);

        public static string EnvironmentVariableFor(IosExportMethod method) =>
            method == IosExportMethod.AppStore ? EnvIosProfileAppStore : EnvIosProfileAdHoc;

        /// <summary>
        /// True when <paramref name="teamId"/> has the shape Apple issues: ASCII letters and
        /// digits only, ten characters, e.g. <c>2S5CQ8C2GL</c>. Worth checking because the value
        /// reaches xcodebuild as <c>DEVELOPMENT_TEAM=</c> and is substituted into
        /// ExportOptions.plist, so a pasted quote or a trailing space would surface much later as
        /// a signing failure that says nothing about where it came from. The length is
        /// deliberately not pinned: a wrong-but-well-formed id fails at xcodebuild with Apple's
        /// own message, while rejecting an unexpected length here would block a build over a rule
        /// this code cannot verify. <paramref name="error"/> reads as a sentence after "Team ID".
        /// </summary>
        public static bool IsValidTeamId(string teamId, out string error)
        {
            if (string.IsNullOrEmpty(teamId))
            {
                error = "must not be empty.";
                return false;
            }

            foreach (char c in teamId)
            {
                if (c >= 128 || !char.IsLetterOrDigit(c))
                {
                    error = "must be letters and digits only, e.g. 2S5CQ8C2GL, got " +
                            $"'{teamId}'. Quotes and spaces are not part of the id.";
                    return false;
                }
            }

            error = null;
            return true;
        }

        /// <summary>The token used for this method in artifact names and script arguments.</summary>
        public static string MethodKey(IosExportMethod method) =>
            method == IosExportMethod.AppStore ? "appstore" : "adhoc";

        /// <summary>
        /// Runs the requested stages. Returns every delivered artifact path — plural, because
        /// <c>--export-method both</c> produces two — or an empty array when only the Unity
        /// stage ran. Throws <see cref="BuildFailedException"/> on failure.
        /// </summary>
        public static string[] Run(BuildRequest request)
        {
            ValidateRequest(request);
            ValidateEnvironment(request);

            // Captured up front, because the export destroys the asset. See ProfileData.
            ProfileData profileData = ProfileData.Capture(request.Profile);

            NamedBuildTarget namedTarget = request.Platform == MobilePlatform.iOS
                ? NamedBuildTarget.iOS
                : NamedBuildTarget.Android;
            BuildTarget target = request.Platform == MobilePlatform.iOS ? BuildTarget.iOS : BuildTarget.Android;

            string version = ResolveVersion(request);
            string buildNumber = ResolveBuildNumber(request);
            DateTime timestampUtc = DateTime.UtcNow;

            var snapshot = SettingsSnapshot.Capture(request.Platform, namedTarget);
            string[] artifactPaths = new string[0];
            try
            {
                SwitchTarget(namedTarget, target);
                ApplySettings(request, namedTarget, version, buildNumber);

                string nativeProjectDir = ExportPlayer(request, profileData, namedTarget, target);
                WriteBuildInfo(request, profileData, namedTarget, nativeProjectDir, version,
                    buildNumber, timestampUtc);

                if (request.RunNativeStage)
                {
                    artifactPaths = RunNativeStage(request);

                    // After packaging, never instead of it: the upload sends the artifact that
                    // stage just delivered. ValidateRequest has already refused the combinations
                    // where no App Store .ipa would exist.
                    if (request.UploadToTestFlight)
                        RunUploadStage(request);
                }
            }
            finally
            {
                // Restoring matters even on the happy path: a build must not leave the
                // developer's ProjectSettings carrying CI credentials or a CI build number.
                snapshot.Restore();
                AssetDatabase.SaveAssets();
            }

            return artifactPaths;
        }

        // ------------------------------------------------------------- validation ----

        private static void ValidateRequest(BuildRequest request)
        {
            if (request == null)
                throw new BuildFailedException("No build request was supplied.");
            if (request.Profile == null)
                throw new BuildFailedException("The build request has no BuildProfile.");
            if (string.IsNullOrEmpty(request.ExportDir))
                throw new BuildFailedException("The build request has no export directory.");
            if (string.IsNullOrEmpty(request.ArtifactDir))
                throw new BuildFailedException("The build request has no artifact directory.");
            if (string.IsNullOrEmpty(request.Version) && string.IsNullOrEmpty(PlayerSettings.bundleVersion))
            {
                throw new BuildFailedException(
                    "PlayerSettings.bundleVersion is empty; set a version before building, or pass one " +
                    "(build.sh --version 1.0.0).");
            }

            if (request.Platform == MobilePlatform.iOS &&
                (request.ExportMethods == null || request.ExportMethods.Length == 0))
                throw new BuildFailedException("An iOS build needs at least one export method.");

            if (request.UploadToTestFlight)
            {
                if (request.Platform != MobilePlatform.iOS)
                {
                    throw new BuildFailedException(
                        "TestFlight is iOS only; a " + request.Platform + " build cannot be uploaded there.");
                }

                if (!request.RunNativeStage)
                {
                    throw new BuildFailedException(
                        "A TestFlight upload needs the packaging stage: without it there is no .ipa to upload.");
                }

                if (request.ExportMethods == null || !request.ExportMethods.Contains(IosExportMethod.AppStore))
                {
                    throw new BuildFailedException(
                        "TestFlight needs an App Store build; the ad-hoc export method produces an ad-hoc " +
                        ".ipa, which App Store Connect will not accept.");
                }
            }

            if (!string.IsNullOrEmpty(request.TeamId) && !IsValidTeamId(request.TeamId, out string teamIdError))
                throw new BuildFailedException("Team ID " + teamIdError);

            // The profile's bundle id is an optional override; the project's own value is what
            // matters, and it must exist.
            NamedBuildTarget namedTarget = request.Platform == MobilePlatform.iOS
                ? NamedBuildTarget.iOS
                : NamedBuildTarget.Android;
            string effectiveBundleId = request.Profile.BundleIdFor(request.Platform);
            if (string.IsNullOrEmpty(effectiveBundleId))
                effectiveBundleId = PlayerSettings.GetApplicationIdentifier(namedTarget);
            if (string.IsNullOrEmpty(effectiveBundleId))
            {
                throw new BuildFailedException(
                    $"No {request.Platform} application identifier: it is empty in the project and " +
                    $"profile '{request.Profile.name}' does not override it.");
            }
        }

        private static void ValidateEnvironment(BuildRequest request)
        {
            // Named rather than positional: a row of interchangeable bools transposes silently.
            string[] required = RequiredEnvironmentVariables(
                request.Platform, request.ExportMethods,
                includeNativeStage: request.RunNativeStage,
                automaticSigning: request.AutomaticSigning,
                uploadToTestFlight: request.UploadToTestFlight,
                suppliedNames: SuppliedCredentials(request).Keys);

            var missing = required
                .Where(name => string.IsNullOrEmpty(Environment.GetEnvironmentVariable(name)))
                .ToArray();
            if (missing.Length > 0)
            {
                throw new BuildFailedException(
                    "Missing required environment variable(s): " + string.Join(", ", missing) +
                    ". See the BuildTools README. When building from the Editor, these must be set in the " +
                    "environment Unity itself was launched from.");
            }

            // Checked here rather than left to altool at the end of the build, for the same
            // reason the Android keystore is: a twenty minute export followed by "no such key
            // file" is the most expensive way to learn about a typo.
            if (request.UploadToTestFlight)
            {
                // Resolved, not read straight from the environment: the path may have been typed
                // into Build Settings, and checking the wrong one would either pass a build that
                // cannot upload or fail one that can.
                string ascKey = ResolveCredential(request, EnvIosAscKeyPath);
                if (!File.Exists(ascKey))
                    throw new BuildFailedException($"App Store Connect API key not found at: {ascKey}");
            }

            if (request.Platform != MobilePlatform.Android)
                return;

            string keystore = Environment.GetEnvironmentVariable(EnvAndroidKeystorePath);
            if (!File.Exists(keystore))
                throw new BuildFailedException($"Android keystore not found at: {keystore}");
        }

        /// <summary>
        /// The version this build ships as. Validated here as well as in the CLI parser, because
        /// the Editor window and a direct <see cref="Run"/> caller do not go through that parser.
        /// </summary>
        private static string ResolveVersion(BuildRequest request)
        {
            if (string.IsNullOrEmpty(request.Version))
                return PlayerSettings.bundleVersion;

            if (!BuildNaming.IsValidVersion(request.Version, out string error))
                throw new BuildFailedException("Version " + error);

            return request.Version;
        }

        /// <summary>
        /// iOS team id: the one the request carries, else the environment. Both the Unity stage
        /// (Player Settings) and the packaging stage (xcodebuild, ExportOptions.plist) have to
        /// see the same value, so it is resolved once here.
        /// </summary>
        private static string ResolveTeamId(BuildRequest request) =>
            ResolveCredential(request, EnvIosTeamId);

        private static string ResolveBuildNumber(BuildRequest request)
        {
            if (!string.IsNullOrEmpty(request.BuildNumber))
            {
                if (request.Platform == MobilePlatform.Android &&
                    !int.TryParse(request.BuildNumber, out _))
                {
                    throw new BuildFailedException(
                        $"Android versionCode must be an integer, got '{request.BuildNumber}'.");
                }

                return request.BuildNumber;
            }

            return request.Platform == MobilePlatform.iOS
                ? PlayerSettings.iOS.buildNumber
                : PlayerSettings.Android.bundleVersionCode.ToString();
        }

        // ------------------------------------------------------------- apply ---------

        private static void SwitchTarget(NamedBuildTarget namedTarget, BuildTarget target)
        {
            if (EditorUserBuildSettings.activeBuildTarget == target)
                return;

            // Not restored afterwards on purpose: the active target lives in
            // Library/EditorUserBuildSettings.asset, so it never shows up in a git diff, and
            // switching back would cost a second full platform reimport.
            if (!EditorUserBuildSettings.SwitchActiveBuildTarget(namedTarget, target))
            {
                throw new BuildFailedException(
                    $"Could not switch the active build target to {target}. Is the {target} build support module installed?");
            }
        }

        private static void ApplySettings(BuildRequest request, NamedBuildTarget namedTarget,
            string version, string buildNumber)
        {
            BuildProfile profile = request.Profile;

            // Only when the profile actually overrides it, so a build cannot quietly rewrite an
            // identifier the developer set in Player Settings.
            string bundleIdOverride = profile.BundleIdFor(request.Platform);
            if (!string.IsNullOrEmpty(bundleIdOverride))
                PlayerSettings.SetApplicationIdentifier(namedTarget, bundleIdOverride);

            // Same rule for the version, and it has to happen before the export: Unity writes
            // bundleVersion into the generated Info.plist and build.gradle, so setting it
            // afterwards would leave the native project on the old version.
            if (!string.IsNullOrEmpty(request.Version))
                PlayerSettings.bundleVersion = version;

            if (request.Platform == MobilePlatform.iOS)
            {
                PlayerSettings.iOS.buildNumber = buildNumber;
                PlayerSettings.iOS.appleDeveloperTeamID = ResolveTeamId(request);
                PlayerSettings.iOS.appleEnableAutomaticSigning = request.AutomaticSigning;

                if (request.AutomaticSigning)
                {
                    // Nothing else to set: Xcode resolves the profile, and leaving the manual
                    // fields alone keeps them out of the generated pbxproj.
                    return;
                }

                PlayerSettings.iOS.iOSManualProvisioningProfileType = profile.iosProvisioningProfileType;

                // Optional: Unity's field wants the profile UUID, which most CI setups do not
                // have to hand. When it is absent, xcodebuild is driven by profile *name*
                // instead (PROVISIONING_PROFILE_SPECIFIER in archive_and_export.sh).
                string profileId = Environment.GetEnvironmentVariable(EnvIosProfileId);
                if (!string.IsNullOrEmpty(profileId))
                    PlayerSettings.iOS.iOSManualProvisioningProfileID = profileId;

                return;
            }

            PlayerSettings.Android.bundleVersionCode = int.Parse(buildNumber);
            PlayerSettings.Android.targetArchitectures = profile.androidArchitectures;

            // Unity bakes signingConfigs into the exported Gradle project at export time
            // (the **SIGN** token in Unity's launcherTemplate.gradle), so this has to happen
            // before BuildPlayer or the release bundle comes out unsigned.
            PlayerSettings.Android.useCustomKeystore = true;
            PlayerSettings.Android.keystoreName = Path.GetFullPath(
                Environment.GetEnvironmentVariable(EnvAndroidKeystorePath));
            PlayerSettings.Android.keystorePass = Environment.GetEnvironmentVariable(EnvAndroidKeystorePass);
            PlayerSettings.Android.keyaliasName = Environment.GetEnvironmentVariable(EnvAndroidKeyAlias);
            PlayerSettings.Android.keyaliasPass = Environment.GetEnvironmentVariable(EnvAndroidKeyAliasPass);

            EditorUserBuildSettings.exportAsGoogleAndroidProject = true;
            EditorUserBuildSettings.buildAppBundle = true;
        }

        // ------------------------------------------------------------- export --------

        private static string ExportPlayer(BuildRequest request, ProfileData profileData,
            NamedBuildTarget namedTarget, BuildTarget target)
        {
            string[] scenes = EditorBuildSettings.scenes
                .Where(scene => scene.enabled)
                .Select(scene => scene.path)
                .ToArray();
            if (scenes.Length == 0)
                throw new BuildFailedException("No enabled scenes in Build Settings; there is nothing to build.");

            if (!request.KeepExport && Directory.Exists(request.ExportDir))
            {
                GuardExportDirIsSafeToDelete(request);
                Directory.Delete(request.ExportDir, true);
            }

            Directory.CreateDirectory(request.ExportDir);
            Directory.CreateDirectory(request.ArtifactDir);

            BuildOptions options = BuildOptions.None;
            if (request.DevelopmentBuild)
                options |= BuildOptions.Development;

            var playerOptions = new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = request.ExportDir,
                target = target,
                targetGroup = namedTarget.ToBuildTargetGroup(),
                options = options,
                // Per-build defines, so the project's own define list is never mutated.
                extraScriptingDefines = profileData.ExtraScriptingDefines
            };

            Debug.Log($"[BuildTools] Exporting {request.Platform} to {request.ExportDir} " +
                      $"(profile '{profileData.Name}', {scenes.Length} scene(s)).");

            BuildReport report = BuildPipeline.BuildPlayer(playerOptions);
            if (report == null)
                throw new BuildFailedException("BuildPipeline.BuildPlayer returned no BuildReport.");

            BuildSummary summary = report.summary;
            if (summary.result != BuildResult.Succeeded)
            {
                throw new BuildFailedException(
                    $"Unity export finished with result '{summary.result}' and {summary.totalErrors} error(s).");
            }

            string searchRoot = !string.IsNullOrEmpty(summary.outputPath) && Directory.Exists(summary.outputPath)
                ? summary.outputPath
                : request.ExportDir;
            return ResolveNativeProjectDir(searchRoot, request.ExportDir, request.Platform);
        }

        /// <summary>
        /// The export directory is deleted recursively, and it can come straight from a
        /// hand-written <c>-executeMethod</c> command line, so refuse the paths where a typo
        /// would be destructive rather than merely wrong.
        /// </summary>
        private static void GuardExportDirIsSafeToDelete(BuildRequest request)
        {
            string exportDir = WithTrailingSeparator(request.ExportDir);
            string projectRoot = WithTrailingSeparator(ProjectRoot);
            string artifactDir = WithTrailingSeparator(request.ArtifactDir);

            if (projectRoot.StartsWith(exportDir, StringComparison.Ordinal))
            {
                throw new BuildFailedException(
                    $"Refusing to delete the export directory '{request.ExportDir}': it contains the project itself.");
            }

            if (artifactDir.StartsWith(exportDir, StringComparison.Ordinal))
            {
                throw new BuildFailedException(
                    $"Refusing to delete the export directory '{request.ExportDir}': the artifact directory " +
                    $"'{request.ArtifactDir}' is inside it and would be destroyed.");
            }
        }

        private static string WithTrailingSeparator(string path)
        {
            string full = Path.GetFullPath(path);
            return full.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal)
                ? full
                : full + Path.DirectorySeparatorChar;
        }

        /// <summary>
        /// Finds the exported native project. Unity puts the Android Gradle project in a
        /// subdirectory named after productName ("Wool Gather" here, spaces and all), while
        /// the iOS project lands directly in the export directory — so detect by marker file
        /// rather than by assuming either layout.
        /// </summary>
        private static string ResolveNativeProjectDir(string searchRoot, string exportDir, MobilePlatform platform)
        {
            foreach (string candidate in CandidateProjectDirs(searchRoot, exportDir))
            {
                if (IsNativeProjectRoot(candidate, platform))
                    return candidate;
            }

            string marker = platform == MobilePlatform.Android ? "settings.gradle" : "*.xcodeproj";
            throw new BuildFailedException(
                $"Could not find the exported {platform} project (no {marker}) in {exportDir} or its immediate subdirectories.");
        }

        private static IEnumerable<string> CandidateProjectDirs(string searchRoot, string exportDir)
        {
            var roots = new List<string> { searchRoot };
            if (!string.Equals(searchRoot, exportDir, StringComparison.Ordinal))
                roots.Add(exportDir);

            foreach (string root in roots)
            {
                if (!Directory.Exists(root))
                    continue;

                yield return root;
                foreach (string child in Directory.GetDirectories(root))
                    yield return child;
            }
        }

        private static bool IsNativeProjectRoot(string dir, MobilePlatform platform)
        {
            if (!Directory.Exists(dir))
                return false;

            if (platform == MobilePlatform.Android)
            {
                return File.Exists(Path.Combine(dir, "settings.gradle"))
                       || File.Exists(Path.Combine(dir, "settings.gradle.kts"));
            }

            return Directory.GetDirectories(dir, "*.xcodeproj").Length > 0;
        }

        // ------------------------------------------------------------- build info ----

        private static void WriteBuildInfo(BuildRequest request, ProfileData profileData,
            NamedBuildTarget namedTarget, string nativeProjectDir, string version,
            string buildNumber, DateTime timestampUtc)
        {
            string platformKey = request.Platform == MobilePlatform.iOS ? "ios" : "android";
            string platformTag = request.Platform == MobilePlatform.iOS ? "iOS" : "Android";

            var lines = new List<string>
            {
                "# Generated by ClassicSpins.BuildTools.BuildRunner. Do not edit.",
                "# Sourced by the BuildTools shell scripts; it is the only channel between them and Unity.",
                Entry("PLATFORM", platformKey),
                Entry("PROFILE", profileData.Name),
                // Read back from Player Settings, so it is the identifier actually built with
                // whether the profile overrode it or not. The iOS ExportOptions profile mapping
                // keys off this, so it has to be the real one.
                Entry("BUNDLE_ID", PlayerSettings.GetApplicationIdentifier(namedTarget)),
                Entry("VERSION", version),
                Entry("BUILD_NUMBER", buildNumber),
                Entry("BUILD_TIMESTAMP", BuildNaming.ToBuildTimestamp(timestampUtc)),
                Entry("EXPORT_DIR", request.ExportDir),
                Entry("NATIVE_PROJECT_DIR", nativeProjectDir),
                Entry("ARTIFACT_DIR", request.ArtifactDir),
                Entry("ARTIFACT_BASENAME", BuildNaming.ComposeArtifactName(
                    profileData.ArtifactPrefix, platformTag, version, buildNumber,
                    profileData.ArtifactSuffix, null, timestampUtc))
            };

            if (request.Platform == MobilePlatform.iOS)
            {
                // Both are always written so a later `--stage native` run can pick either
                // method without a second Unity launch.
                foreach (IosExportMethod method in new[] { IosExportMethod.AppStore, IosExportMethod.AdHoc })
                {
                    string key = MethodKey(method);
                    lines.Add(Entry("ARTIFACT_BASENAME_" + key.ToUpperInvariant(),
                        BuildNaming.ComposeArtifactName(profileData.ArtifactPrefix, platformTag,
                            version, buildNumber, profileData.ArtifactSuffix, key, timestampUtc)));
                }

                lines.Add(Entry("IOS_EXPORT_METHODS",
                    string.Join(" ", request.ExportMethods.Select(MethodKey))));
                lines.Add(Entry("IOS_SIGNING", request.AutomaticSigning ? "auto" : "manual"));
            }
            else
            {
                AndroidToolPaths tools = AndroidToolPaths.Resolve();
                lines.Add(Entry("GRADLE_LAUNCHER", tools.GradleLauncherJar));
                lines.Add(Entry("JAVA_HOME", tools.JdkRoot));
                lines.Add(Entry("ANDROID_SDK_ROOT", tools.SdkRoot));
            }

            string path = Path.Combine(request.ExportDir, BuildInfoFileName);
            File.WriteAllText(path, string.Join("\n", lines) + "\n");
            Debug.Log($"[BuildTools] Wrote {path}.");
        }

        private static string Entry(string key, string value) => key + "=" + ShellQuote(value);

        /// <summary>
        /// Single-quotes a value for build-info.env, so bash sees it verbatim, spaces and all.
        /// This is for file contents that bash will <c>source</c> — not for process arguments,
        /// which follow a different rule; see <see cref="AppendArgument"/>.
        /// </summary>
        private static string ShellQuote(string value) =>
            "'" + (value ?? string.Empty).Replace("'", "'\\''") + "'";

        /// <summary>
        /// Encodes arguments into a single <see cref="ProcessStartInfo.Arguments"/> string using
        /// the backslash-and-double-quote rule that .NET and Mono parse it back with. Hand-rolled
        /// because Unity 6's .NET Standard 2.1 surface has no <c>ProcessStartInfo.ArgumentList</c>,
        /// and single-quoting — the obvious-looking choice for a bash command — is not the rule
        /// this string is read with, so it would arrive at bash with the quotes still attached.
        /// Public so the rule can be unit tested; the interesting cases (spaces, embedded quotes)
        /// never occur on a checkout whose path happens to be plain.
        /// </summary>
        public static string ComposeProcessArguments(params string[] arguments)
        {
            var builder = new StringBuilder();
            foreach (string argument in arguments)
                AppendArgument(builder, argument);

            return builder.ToString();
        }

        private static void AppendArgument(StringBuilder builder, string argument)
        {
            argument ??= string.Empty;
            if (builder.Length != 0)
                builder.Append(' ');

            if (argument.Length != 0 && argument.IndexOfAny(new[] { ' ', '\t', '"' }) < 0)
            {
                builder.Append(argument);
                return;
            }

            builder.Append('"');
            for (int i = 0; i < argument.Length; i++)
            {
                int backslashes = 0;
                while (i < argument.Length && argument[i] == '\\')
                {
                    backslashes++;
                    i++;
                }

                if (i == argument.Length)
                {
                    // Trailing backslashes would otherwise escape the closing quote.
                    builder.Append('\\', backslashes * 2);
                    break;
                }

                if (argument[i] == '"')
                    builder.Append('\\', backslashes * 2 + 1).Append('"');
                else
                    builder.Append('\\', backslashes).Append(argument[i]);
            }

            builder.Append('"');
        }

        // ------------------------------------------------------------- native stage --

        private static string[] RunNativeStage(BuildRequest request)
        {
            string script = request.Platform == MobilePlatform.iOS
                ? BuildToolsLayout.ScriptPath("ios", "archive_and_export.sh")
                : BuildToolsLayout.ScriptPath("android", "bundle_release.sh");

            string arguments = request.Platform == MobilePlatform.iOS
                ? ComposeProcessArguments(script, "--export-dir", request.ExportDir,
                    "--method", DescribeMethods(request.ExportMethods),
                    "--signing", request.AutomaticSigning ? "auto" : "manual")
                : ComposeProcessArguments(script, "--export-dir", request.ExportDir);

            List<string> stdout = RunBuildScript("packaging", script, arguments, request);

            // The scripts reserve stdout for delivered artifact paths, one per line, so an
            // export producing two IPAs reports both rather than only the last.
            string[] artifactPaths = ReportedPaths(stdout);

            foreach (string path in artifactPaths)
                Debug.Log($"[BuildTools] Artifact: {path}");

            if (artifactPaths.Length == 0)
            {
                throw new BuildFailedException(
                    $"The packaging stage reported success but named no artifact on stdout: {script}");
            }

            return artifactPaths;
        }

        // ------------------------------------------------------------- upload stage --

        /// <summary>
        /// Hands the delivered App Store <c>.ipa</c> to App Store Connect by running the same
        /// script <c>build.sh --stage upload</c> runs, through the same child-process code the
        /// packaging stage uses — which is what stops the Editor and the CLI drifting apart.
        /// The script locates the artifact itself, from <c>build-info.env</c>.
        /// </summary>
        private static void RunUploadStage(BuildRequest request)
        {
            string script = BuildToolsLayout.ScriptPath("ios", "upload_testflight.sh");
            string arguments = ComposeProcessArguments(script, "--export-dir", request.ExportDir);

            RunBuildScript("TestFlight upload", script, arguments, request);
        }

        // ------------------------------------------------------------- script runner -

        /// <summary>
        /// Runs one of the BuildTools shell scripts as a child process and returns the lines it
        /// wrote to stdout. Shared by every stage that leaves Unity, so they all inherit the
        /// same pipe handling, the same environment injection and the same failure reporting.
        /// </summary>
        private static List<string> RunBuildScript(string label, string script, string arguments,
            BuildRequest request)
        {
            if (!File.Exists(script))
                throw new BuildFailedException($"{Capitalized(label)} script not found at: {script}");

            var startInfo = new ProcessStartInfo("/bin/bash", arguments)
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                WorkingDirectory = ProjectRoot
            };

            // The child otherwise inherits Unity's environment, which is the whole problem when
            // Unity was launched from the Hub or Finder: archive_and_export.sh and
            // upload_testflight.sh read these from the environment themselves and would fail at
            // their own credential checks. Injected rather than passed as flags so each script
            // keeps one way of learning them. Only what the caller actually typed is injected;
            // anything left empty falls through to whatever Unity inherited.
            foreach (KeyValuePair<string, string> credential in SuppliedCredentials(request))
                startInfo.EnvironmentVariables[credential.Key] = credential.Value;

            var stdout = new List<string>();
            var stderr = new List<string>();
            var sync = new object();

            Debug.Log($"[BuildTools] Running {label} stage: /bin/bash {arguments}");
            using (var process = new Process { StartInfo = startInfo })
            {
                // Both streams are drained asynchronously: xcodebuild, Gradle and altool each
                // produce enough output to fill a pipe buffer, and reading them one after the
                // other would deadlock.
                process.OutputDataReceived += (_, e) =>
                {
                    if (e.Data != null) lock (sync) stdout.Add(e.Data);
                };
                process.ErrorDataReceived += (_, e) =>
                {
                    if (e.Data != null) lock (sync) stderr.Add(e.Data);
                };

                process.Start();
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();
                process.WaitForExit();

                LogBatched(label + " stderr", stderr);
                LogBatched(label + " stdout", stdout);

                if (process.ExitCode != 0)
                {
                    throw new BuildFailedException(
                        $"{Capitalized(label)} stage failed with exit code {process.ExitCode}: {script}");
                }
            }

            return stdout;
        }

        /// <summary>
        /// The stage label at the start of a sentence. Exists so the shared runner's messages
        /// read the same as the packaging-only ones it replaced, rather than being reworded by
        /// the refactor.
        /// </summary>
        private static string Capitalized(string label) =>
            char.ToUpperInvariant(label[0]) + label.Substring(1);

        /// <summary>Existing files a script named on stdout, in the order it named them.</summary>
        private static string[] ReportedPaths(List<string> stdout) =>
            stdout
                .Select(line => line.Trim())
                .Where(line => line.Length > 0 && File.Exists(line))
                .Distinct()
                .ToArray();

        private static string DescribeMethods(IosExportMethod[] methods)
        {
            bool appStore = methods.Contains(IosExportMethod.AppStore);
            bool adHoc = methods.Contains(IosExportMethod.AdHoc);
            if (appStore && adHoc)
                return "both";

            return MethodKey(methods[0]);
        }

        private static void LogBatched(string label, List<string> lines)
        {
            const int batchSize = 200;
            if (lines.Count == 0)
                return;

            for (int start = 0; start < lines.Count; start += batchSize)
            {
                int count = Math.Min(batchSize, lines.Count - start);
                Debug.Log($"[BuildTools] {label} ({start + 1}-{start + count} of {lines.Count}):\n" +
                          string.Join("\n", lines.GetRange(start, count)));
            }
        }

        /// <summary>
        /// Plain copy of the profile fields needed after the export.
        /// <see cref="BuildPipeline.BuildPlayer"/> runs an asset garbage collection — the
        /// "Unloading N unused Assets" line in the build log — which destroys a
        /// <see cref="BuildProfile"/> that no persistent asset references. Reading the live
        /// object once the export has finished therefore throws MissingReferenceException, and
        /// it does so only during a real build, where no stub can reproduce it. Copying the
        /// fields out first makes that structurally impossible.
        /// </summary>
        private readonly struct ProfileData
        {
            public readonly string Name;
            public readonly string ArtifactPrefix;
            public readonly string ArtifactSuffix;
            public readonly string[] ExtraScriptingDefines;

            private ProfileData(string name, string artifactPrefix, string artifactSuffix,
                string[] extraScriptingDefines)
            {
                Name = name;
                ArtifactPrefix = artifactPrefix;
                ArtifactSuffix = artifactSuffix;
                ExtraScriptingDefines = extraScriptingDefines;
            }

            public static ProfileData Capture(BuildProfile profile) =>
                new ProfileData(
                    profile.name,
                    profile.artifactPrefix,
                    profile.artifactSuffix,
                    (string[])(profile.extraScriptingDefines ?? new string[0]).Clone());
        }

        // ------------------------------------------------------------- snapshot -----

        /// <summary>
        /// Captures every setting <see cref="ApplySettings"/> writes, so a build leaves the
        /// project exactly as it found it. Fields in Library/ (the active build target) are
        /// deliberately excluded — see SwitchTarget.
        /// </summary>
        private sealed class SettingsSnapshot
        {
            private MobilePlatform platform;
            private NamedBuildTarget namedTarget;
            private string applicationIdentifier;

            /// <summary>Shared by both platforms, unlike everything below it.</summary>
            private string bundleVersion;

            private string iosBuildNumber;
            private string iosTeamId;
            private bool iosAutomaticSigning;
            private string iosProvisioningProfileId;
            private ProvisioningProfileType iosProvisioningProfileType;

            private int androidVersionCode;
            private bool androidUseCustomKeystore;
            private string androidKeystoreName;
            private string androidKeystorePass;
            private string androidKeyAliasName;
            private string androidKeyAliasPass;
            private AndroidArchitecture androidArchitectures;
            private bool androidExportProject;
            private bool androidBuildAppBundle;

            public static SettingsSnapshot Capture(MobilePlatform platform, NamedBuildTarget namedTarget)
            {
                var snapshot = new SettingsSnapshot
                {
                    platform = platform,
                    namedTarget = namedTarget,
                    applicationIdentifier = PlayerSettings.GetApplicationIdentifier(namedTarget),
                    bundleVersion = PlayerSettings.bundleVersion
                };

                if (platform == MobilePlatform.iOS)
                {
                    snapshot.iosBuildNumber = PlayerSettings.iOS.buildNumber;
                    snapshot.iosTeamId = PlayerSettings.iOS.appleDeveloperTeamID;
                    snapshot.iosAutomaticSigning = PlayerSettings.iOS.appleEnableAutomaticSigning;
                    snapshot.iosProvisioningProfileId = PlayerSettings.iOS.iOSManualProvisioningProfileID;
                    snapshot.iosProvisioningProfileType = PlayerSettings.iOS.iOSManualProvisioningProfileType;
                    return snapshot;
                }

                snapshot.androidVersionCode = PlayerSettings.Android.bundleVersionCode;
                snapshot.androidUseCustomKeystore = PlayerSettings.Android.useCustomKeystore;
                snapshot.androidKeystoreName = PlayerSettings.Android.keystoreName;
                snapshot.androidKeystorePass = PlayerSettings.Android.keystorePass;
                snapshot.androidKeyAliasName = PlayerSettings.Android.keyaliasName;
                snapshot.androidKeyAliasPass = PlayerSettings.Android.keyaliasPass;
                snapshot.androidArchitectures = PlayerSettings.Android.targetArchitectures;
                snapshot.androidExportProject = EditorUserBuildSettings.exportAsGoogleAndroidProject;
                snapshot.androidBuildAppBundle = EditorUserBuildSettings.buildAppBundle;
                return snapshot;
            }

            public void Restore()
            {
                try
                {
                    PlayerSettings.SetApplicationIdentifier(namedTarget, applicationIdentifier);
                    PlayerSettings.bundleVersion = bundleVersion;

                    if (platform == MobilePlatform.iOS)
                    {
                        PlayerSettings.iOS.buildNumber = iosBuildNumber;
                        PlayerSettings.iOS.appleDeveloperTeamID = iosTeamId;
                        PlayerSettings.iOS.appleEnableAutomaticSigning = iosAutomaticSigning;
                        PlayerSettings.iOS.iOSManualProvisioningProfileID = iosProvisioningProfileId;
                        PlayerSettings.iOS.iOSManualProvisioningProfileType = iosProvisioningProfileType;
                        return;
                    }

                    PlayerSettings.Android.bundleVersionCode = androidVersionCode;
                    PlayerSettings.Android.useCustomKeystore = androidUseCustomKeystore;
                    PlayerSettings.Android.keystoreName = androidKeystoreName;
                    PlayerSettings.Android.keystorePass = androidKeystorePass;
                    PlayerSettings.Android.keyaliasName = androidKeyAliasName;
                    PlayerSettings.Android.keyaliasPass = androidKeyAliasPass;
                    PlayerSettings.Android.targetArchitectures = androidArchitectures;
                    EditorUserBuildSettings.exportAsGoogleAndroidProject = androidExportProject;
                    EditorUserBuildSettings.buildAppBundle = androidBuildAppBundle;
                }
                catch (Exception exception)
                {
                    // Never mask the original build failure with a restore failure, but do say so.
                    Debug.LogError($"[BuildTools] Failed to restore player settings: {exception}");
                }
            }
        }

        // ------------------------------------------------------- android toolchain --

        /// <summary>
        /// Where Unity's bundled Gradle, JDK and SDK live. Resolved through
        /// AndroidExternalToolsSettings by reflection so it works no matter which platform
        /// is active — a <c>#if UNITY_ANDROID</c> guard here would compile the lookup away
        /// whenever a developer happens to be on the iOS target.
        /// </summary>
        private struct AndroidToolPaths
        {
            public string GradleLauncherJar;
            public string JdkRoot;
            public string SdkRoot;

            public static AndroidToolPaths Resolve()
            {
                string playerDir = FindAndroidPlayerDir();

                string gradleHome = FirstExistingDirectory(
                    ExternalToolSetting("gradlePath"),
                    playerDir == null ? null : Path.Combine(playerDir, "Tools", "gradle"));

                return new AndroidToolPaths
                {
                    GradleLauncherJar = FindGradleLauncherJar(gradleHome),
                    JdkRoot = FirstExistingDirectory(
                        ExternalToolSetting("jdkRootPath"),
                        playerDir == null ? null : Path.Combine(playerDir, "OpenJDK")),
                    SdkRoot = FirstExistingDirectory(
                        ExternalToolSetting("sdkRootPath"),
                        playerDir == null ? null : Path.Combine(playerDir, "SDK"))
                };
            }

            private static string FindGradleLauncherJar(string gradleHome)
            {
                if (string.IsNullOrEmpty(gradleHome))
                    return string.Empty;

                string lib = Path.Combine(gradleHome, "lib");
                if (!Directory.Exists(lib))
                    return string.Empty;

                // Ordered by parsed version, not by string: "8.9" sorts after "8.13"
                // lexicographically, which would pick the older Gradle.
                return Directory.GetFiles(lib, "gradle-launcher-*.jar")
                           .OrderBy(LauncherVersionKey)
                           .LastOrDefault()
                       ?? string.Empty;
            }

            /// <summary>
            /// Comparable key for a <c>gradle-launcher-&lt;version&gt;.jar</c> path. Unparseable
            /// segments sort lowest so a well-formed name always wins.
            /// </summary>
            private static IComparable LauncherVersionKey(string path)
            {
                string name = Path.GetFileNameWithoutExtension(path) ?? string.Empty;
                const string prefix = "gradle-launcher-";
                string version = name.StartsWith(prefix, StringComparison.Ordinal)
                    ? name.Substring(prefix.Length)
                    : string.Empty;

                long key = 0;
                foreach (string segment in version.Split('.'))
                {
                    key *= 100000;
                    if (int.TryParse(segment, out int number) && number >= 0)
                        key += number;
                }

                return key;
            }

            private static string ExternalToolSetting(string propertyName)
            {
                Type type = Type.GetType(
                                "UnityEditor.Android.AndroidExternalToolsSettings, UnityEditor.Android.Extensions")
                            ?? AppDomain.CurrentDomain.GetAssemblies()
                                .Select(assembly => assembly.GetType("UnityEditor.Android.AndroidExternalToolsSettings"))
                                .FirstOrDefault(candidate => candidate != null);
                PropertyInfo property = type?.GetProperty(propertyName, BindingFlags.Public | BindingFlags.Static);
                return property?.GetValue(null) as string;
            }

            private static string FindAndroidPlayerDir()
            {
                string contents = EditorApplication.applicationContentsPath;
                string editorRoot = Directory.GetParent(contents)?.Parent?.FullName;

                return FirstExistingDirectory(
                    Path.Combine(contents, "PlaybackEngines", "AndroidPlayer"),
                    editorRoot == null ? null : Path.Combine(editorRoot, "PlaybackEngines", "AndroidPlayer"));
            }

            private static string FirstExistingDirectory(params string[] candidates)
            {
                foreach (string candidate in candidates)
                {
                    if (!string.IsNullOrEmpty(candidate) && Directory.Exists(candidate))
                        return candidate;
                }

                return string.Empty;
            }
        }
    }
}
