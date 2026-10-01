using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace ClassicSpins.BuildTools
{
    /// <summary>
    /// Editor front-end for the build tool. Drives the same <see cref="BuildRunner"/> the
    /// command line uses, and can hand you the equivalent build.sh invocation instead of
    /// running the build in-process.
    /// </summary>
    public sealed class BuildToolsWindow : EditorWindow
    {
        internal const string MenuRoot = "Tools/Build Tools/";
        private const string PrefPrefix = "ClassicSpins.BuildTools.";

        /// <summary>Named once: the window and the menu item both read and write it.</summary>
        private const string UploadPrefKey = PrefPrefix + "UploadTestFlight";

        /// <summary>
        /// App Store Connect fields, stored per machine in EditorPrefs beside the team id and
        /// for the same reason: a Hub-launched Editor has no environment to read them from.
        /// Two ids and a path — none of them the secret. The <c>.p8</c>'s contents are never
        /// read here, so what lands in EditorPrefs cannot authenticate anything on its own.
        /// </summary>
        private const string AscKeyPathPrefKey = PrefPrefix + "AscKeyPath";

        private const string AscKeyIdPrefKey = PrefPrefix + "AscKeyId";
        private const string AscIssuerIdPrefKey = PrefPrefix + "AscIssuerId";

        private enum ExportMethodChoice
        {
            AppStore,
            AdHoc,
            Both
        }

        private enum SigningChoice
        {
            Manual,
            Automatic
        }

        private BuildProfile[] profiles = new BuildProfile[0];
        private string profileError;
        private int profileIndex;
        private MobilePlatform platform;
        private string version = string.Empty;
        private string buildNumber = string.Empty;
        private ExportMethodChoice exportMethod = ExportMethodChoice.AppStore;
        private SigningChoice signing = SigningChoice.Manual;
        private string teamId = string.Empty;
        private string ascKeyPath = string.Empty;
        private string ascKeyId = string.Empty;
        private string ascIssuerId = string.Empty;
        private bool runNativeStage = true;
        private bool development;
        private bool uploadToTestFlight;
        private Vector2 scroll;

        [MenuItem(MenuRoot + "Build Settings…", false, 0)]
        private static void Open()
        {
            BuildToolsWindow window = GetWindow<BuildToolsWindow>("Build Settings");
            window.minSize = new Vector2(460f, 460f);
        }

        [MenuItem(MenuRoot + "Build iOS (IPA)", false, 20)]
        private static void BuildIos() => RunFromMenu(MobilePlatform.iOS);

        [MenuItem(MenuRoot + "Build Android (AAB)", false, 21)]
        private static void BuildAndroid() => RunFromMenu(MobilePlatform.Android);

        [MenuItem(MenuRoot + "Open Artifacts Folder", false, 40)]
        private static void OpenArtifactsFolder()
        {
            string artifactDir = ResolveArtifactDir();
            Directory.CreateDirectory(artifactDir);
            EditorUtility.RevealInFinder(artifactDir);
        }

        private void OnEnable()
        {
            LoadPreferences();
            RefreshProfiles();
        }

        /// <summary>Picks up profile assets created or deleted while the window was open.</summary>
        private void OnFocus() => RefreshProfiles();

        private void OnGUI()
        {
            using var scrollScope = new EditorGUILayout.ScrollViewScope(scroll);
            scroll = scrollScope.scrollPosition;

            // A profile asset can be deleted or reimported while this window stays open, which
            // would otherwise leave destroyed objects in the array and throw on every repaint.
            if (Array.Exists(profiles, profile => profile == null))
                RefreshProfiles();

            if (profiles.Length == 0)
            {
                DrawNoProfileState();
                return;
            }

            EditorGUI.BeginChangeCheck();
            DrawProfileSection();
            DrawBuildSection();
            if (EditorGUI.EndChangeCheck())
                SavePreferences();

            EditorGUILayout.Space();
            DrawEnvironmentSection();
            EditorGUILayout.Space();
            DrawActions();
        }

        // ------------------------------------------------------------------ ui -------

        private void DrawNoProfileState()
        {
            EditorGUILayout.LabelField("Build Settings", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                profileError ?? "No build profile found.", MessageType.Warning);

            if (GUILayout.Button("Create Default Profile"))
            {
                BuildProfile created = BuildProfile.CreateDefaultAsset();
                RefreshProfiles();
                Selection.activeObject = created;
            }

            if (GUILayout.Button("Refresh"))
                RefreshProfiles();
        }

        private void DrawProfileSection()
        {
            EditorGUILayout.LabelField("Profile", EditorStyles.boldLabel);

            string[] names = profiles.Select(profile => profile.name).ToArray();
            profileIndex = EditorGUILayout.Popup("Build profile", Mathf.Clamp(profileIndex, 0, names.Length - 1), names);

            using (new EditorGUI.DisabledScope(true))
                EditorGUILayout.ObjectField("Asset", profiles[profileIndex], typeof(BuildProfile), false);
        }

        private void DrawBuildSection()
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Build", EditorStyles.boldLabel);

            platform = (MobilePlatform)EditorGUILayout.EnumPopup("Platform", platform);
            version = EditorGUILayout.TextField(
                new GUIContent("Version",
                    "Marketing version, e.g. 1.0.0. Leave empty to keep the version already in the " +
                    "project. Applied for this build only; Player Settings is restored afterwards."),
                version);
            buildNumber = EditorGUILayout.TextField(
                new GUIContent("Build number", "Leave empty to keep the number already in the project."),
                buildNumber);

            if (platform == MobilePlatform.iOS)
            {
                exportMethod = (ExportMethodChoice)EditorGUILayout.EnumPopup("Export method", exportMethod);
                signing = (SigningChoice)EditorGUILayout.EnumPopup(
                    new GUIContent("Signing",
                        "Manual pins the profile by name and is deterministic. Automatic lets Xcode " +
                        "resolve and create profiles, which needs a signed-in Apple ID or an App Store " +
                        "Connect API key, and can modify the team's profiles."),
                    signing);
                teamId = EditorGUILayout.TextField(
                    new GUIContent("Team ID",
                        "Apple Developer team id, ten letters and digits, e.g. 2S5CQ8C2GL. Fill this " +
                        "in when Unity was launched from the Hub or Finder and so has no " +
                        $"{BuildRunner.EnvIosTeamId} in its environment. Empty reads the environment " +
                        "as usual. Stored per machine in EditorPrefs, not in the project — it is an " +
                        "identifier, not a secret, but it is also nobody else's business."),
                    teamId);
            }

            development = EditorGUILayout.Toggle(
                new GUIContent("Development build", "Adds Unity's BuildOptions.Development."), development);
            runNativeStage = EditorGUILayout.Toggle(
                new GUIContent("Run packaging stage",
                    "On: also run xcodebuild / Gradle and deliver the artifact. Off: stop after the Unity export."),
                runNativeStage);

            // Drawn after "Run packaging stage" rather than up with the other iOS rows, because
            // it greys out based on that toggle's value and reading it before it is drawn would
            // leave this control a repaint behind.
            if (platform == MobilePlatform.iOS)
            {
                DrawUploadToggle();
                DrawAppStoreConnectFields();
            }

            // The version actually built with, alongside the identifier below it: both are the
            // project's own value unless something overrides it for this build.
            EditorGUILayout.LabelField("Version built", EffectiveVersion() +
                (version.Trim().Length == 0 ? "  (from Player Settings)" : "  (override)"));

            // The effective identifier, which is the project's unless the profile overrides it.
            string bundleIdOverride = profiles[profileIndex].BundleIdFor(platform);
            string effectiveBundleId = string.IsNullOrEmpty(bundleIdOverride)
                ? PlayerSettings.GetApplicationIdentifier(
                      platform == MobilePlatform.iOS
                          ? UnityEditor.Build.NamedBuildTarget.iOS
                          : UnityEditor.Build.NamedBuildTarget.Android) + "  (from Player Settings)"
                : bundleIdOverride + "  (profile override)";
            EditorGUILayout.LabelField("Bundle id", effectiveBundleId);
        }

        /// <summary>
        /// The <strong>Upload TestFlight</strong> row. Shown unticked and disabled, with the
        /// reason visible rather than only on hover, whenever this configuration could not
        /// upload — a greyed-out row with no explanation is the thing this avoids. The stored
        /// preference is left alone while disabled, so switching the export method back to
        /// AppStore restores the choice instead of silently having forgotten it.
        /// </summary>
        private void DrawUploadToggle()
        {
            string blockedReason = UploadBlockedReason();
            using (new EditorGUI.DisabledScope(blockedReason != null))
            {
                bool shown = EditorGUILayout.Toggle(
                    new GUIContent("Upload TestFlight",
                        blockedReason ??
                        "On: after packaging, upload the App Store .ipa to App Store Connect with " +
                        "xcrun altool, which is what puts it in front of TestFlight testers. Needs " +
                        $"{BuildRunner.EnvIosAscKeyPath}, {BuildRunner.EnvIosAscKeyId} and " +
                        $"{BuildRunner.EnvIosAscIssuerId} — from Unity's environment, or typed " +
                        "into the fields below."),
                    blockedReason == null && uploadToTestFlight);
                if (blockedReason == null)
                    uploadToTestFlight = shown;
            }

            if (blockedReason != null)
                EditorGUILayout.HelpBox("No TestFlight upload. " + blockedReason, MessageType.Info);
        }

        /// <summary>
        /// The App Store Connect API key rows. Drawn only when something actually uses them —
        /// the upload, which requires all three, and automatic signing, where they are optional
        /// — rather than sitting in front of every build. Each field empty means "read the
        /// environment", exactly like Team ID above.
        ///
        /// These are storable for the same reason the team id is: two identifiers and a path,
        /// none of which authenticates anything on its own. The <c>.p8</c> is never opened,
        /// read or copied here; altool opens it, from the path given.
        /// </summary>
        private void DrawAppStoreConnectFields()
        {
            bool required = UploadRequested;
            if (!required && signing != SigningChoice.Automatic)
                return;

            EditorGUILayout.Space();
            EditorGUILayout.LabelField(
                required
                    ? "App Store Connect API key  (required for the upload)"
                    : "App Store Connect API key  (optional for automatic signing)",
                EditorStyles.boldLabel);

            using (new EditorGUILayout.HorizontalScope())
            {
                ascKeyPath = EditorGUILayout.TextField(
                    new GUIContent("Key file (.p8)",
                        "Path to the AuthKey_*.p8 downloaded from App Store Connect, standing in " +
                        $"for {BuildRunner.EnvIosAscKeyPath}. Only the path is stored; the file " +
                        "stays where it is and is opened by altool, not by Unity."),
                    ascKeyPath);

                if (GUILayout.Button("Browse…", GUILayout.Width(70f)))
                {
                    string picked = EditorUtility.OpenFilePanel("App Store Connect API key", "", "p8");
                    if (!string.IsNullOrEmpty(picked))
                    {
                        ascKeyPath = picked;
                        // Otherwise the text field keeps showing the old value until it loses focus.
                        GUI.FocusControl(null);
                    }
                }
            }

            ascKeyId = EditorGUILayout.TextField(
                new GUIContent("Key ID",
                    "The Key ID column in App Store Connect > Users and Access > Integrations, " +
                    $"e.g. ABC123XYZ9. Stands in for {BuildRunner.EnvIosAscKeyId}."),
                ascKeyId);

            ascIssuerId = EditorGUILayout.TextField(
                new GUIContent("Issuer ID",
                    "The Issuer ID shown above that same key list — one per team, shared by every " +
                    $"key. Stands in for {BuildRunner.EnvIosAscIssuerId}."),
                ascIssuerId);
        }

        private string UploadBlockedReason() =>
            UploadBlockedReason(platform, exportMethod, runNativeStage);

        /// <summary>
        /// Why this configuration cannot upload, or null when it can. Static and taking every
        /// input explicitly because the <c>Build iOS (IPA)</c> menu item has to apply the very
        /// same rule to the stored preferences, and two copies of it would drift.
        /// </summary>
        private static string UploadBlockedReason(MobilePlatform platform,
            ExportMethodChoice exportMethod, bool runNativeStage)
        {
            if (platform != MobilePlatform.iOS)
                return "TestFlight is iOS only.";
            if (exportMethod == ExportMethodChoice.AdHoc)
            {
                return "TestFlight needs an App Store build, and export method AdHoc produces an " +
                       "ad-hoc .ipa. Choose AppStore or Both.";
            }

            if (!runNativeStage)
                return "With the packaging stage off there is no .ipa to upload.";

            return null;
        }

        /// <summary>
        /// True when this configuration both wants and can upload. Every caller goes through
        /// this rather than the raw field, so a disabled toggle cannot leak into a build.
        /// </summary>
        private bool UploadRequested => uploadToTestFlight && UploadBlockedReason() == null;

        private void DrawEnvironmentSection()
        {
            EditorGUILayout.LabelField("Credentials", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Anything not filled in above is read from the environment Unity was launched " +
                "from; those values are never shown or stored here. The passwords and profile " +
                "names can only come that way. See BuildTools/README.md.", MessageType.None);

            foreach (string name in RequiredEnvironmentVariables())
            {
                bool isSet = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable(name));
                EditorGUILayout.LabelField(name, isSet ? "set" : "MISSING");
            }

            // Still listed rather than silently dropped: the build does need these, and where
            // each one comes from is exactly what a developer is checking here. Never reported as
            // set when it would be rejected, which would read as a green light.
            if (HasTeamIdOverride)
            {
                EditorGUILayout.LabelField(BuildRunner.EnvIosTeamId,
                    BuildRunner.IsValidTeamId(teamId.Trim(), out _)
                        ? "set above, in Team ID"
                        : "INVALID, see Team ID above");
            }

            foreach (KeyValuePair<string, string> supplied in SuppliedCredentials())
            {
                if (supplied.Key == BuildRunner.EnvIosTeamId)
                    continue;

                string state = "set above";
                if (supplied.Key == BuildRunner.EnvIosAscKeyPath && !File.Exists(supplied.Value))
                    state = "NO FILE at that path, see above";

                EditorGUILayout.LabelField(supplied.Key, state);
            }
        }

        private void DrawActions()
        {
            string[] missing = RequiredEnvironmentVariables()
                .Where(name => string.IsNullOrEmpty(Environment.GetEnvironmentVariable(name)))
                .ToArray();
            bool buildNumberValid = buildNumber.Trim().Length == 0 || buildNumber.Trim().All(char.IsDigit);
            string versionError = null;
            bool versionValid = version.Trim().Length == 0 ||
                                BuildNaming.IsValidVersion(version.Trim(), out versionError);
            string teamIdError = null;
            bool teamIdValid = !HasTeamIdOverride ||
                               BuildRunner.IsValidTeamId(teamId.Trim(), out teamIdError);

            // A typed path that names nothing is caught here rather than after a twenty-minute
            // export, which is the same reason BuildRunner checks it before launching Unity.
            string typedKeyPath = ascKeyPath.Trim();
            bool ascKeyPathValid = typedKeyPath.Length == 0 || File.Exists(typedKeyPath);

            if (!versionValid)
                EditorGUILayout.HelpBox("Version " + versionError, MessageType.Error);
            if (!teamIdValid)
                EditorGUILayout.HelpBox("Team ID " + teamIdError, MessageType.Error);
            if (!ascKeyPathValid)
            {
                EditorGUILayout.HelpBox(
                    $"No file at {typedKeyPath}. Key file (.p8) must name the key downloaded from " +
                    "App Store Connect, or be empty to read the environment.", MessageType.Error);
            }
            if (!buildNumberValid)
                EditorGUILayout.HelpBox("Build number must be a whole number, or empty.", MessageType.Error);
            if (missing.Length > 0)
            {
                // Several of these can be settled right here, so point at the fields rather than
                // let the only advice be "go and use a terminal".
                string[] fillable = missing
                    .Where(name => name == BuildRunner.EnvIosTeamId ||
                                   name == BuildRunner.EnvIosAscKeyPath ||
                                   name == BuildRunner.EnvIosAscKeyId ||
                                   name == BuildRunner.EnvIosAscIssuerId)
                    .ToArray();
                string hint = fillable.Length > 0
                    ? $" {string.Join(", ", fillable)} can be filled in above instead."
                    : string.Empty;
                EditorGUILayout.HelpBox(
                    "Cannot build from the Editor until these are set in Unity's environment: " +
                    string.Join(", ", missing) + "." + hint +
                    " Copy the CLI command and run it from a shell instead.",
                    MessageType.Warning);
            }

            EditorGUILayout.HelpBox(
                "Building here blocks the Editor until xcodebuild / Gradle finishes. " +
                "Copy CLI Command runs the same build in a terminal instead.", MessageType.Info);

            using (new EditorGUI.DisabledScope(
                       missing.Length > 0 || !buildNumberValid || !versionValid || !teamIdValid ||
                       !ascKeyPathValid))
            {
                if (GUILayout.Button($"Build {platform}", GUILayout.Height(28f)))
                    ConfirmAndRun(platform);
            }

            if (GUILayout.Button("Copy CLI Command"))
            {
                string command = ComposeCliCommand();
                EditorGUIUtility.systemCopyBuffer = command;
                Debug.Log($"[BuildTools] Copied to clipboard: {command}");
            }

            if (GUILayout.Button("Open Artifacts Folder"))
                OpenArtifactsFolder();
        }

        // ------------------------------------------------------------- behaviour -----

        private void RefreshProfiles()
        {
            profiles = BuildProfile.FindAll();
            BuildProfile.TryResolve(string.Empty, out _, out profileError);

            string remembered = EditorPrefs.GetString(PrefPrefix + "Profile", BuildProfile.DefaultProfileName);
            int index = Array.FindIndex(profiles, profile => profile.name == remembered);
            profileIndex = index >= 0 ? index : 0;
        }

        /// <summary>True when the typed team id stands in for the environment variable.</summary>
        private bool HasTeamIdOverride =>
            platform == MobilePlatform.iOS && teamId.Trim().Length > 0;

        /// <summary>
        /// What this window is carrying itself, keyed by the variable each stands in for.
        /// Built by handing the typed values to <see cref="BuildRunner.SuppliedCredentials"/>
        /// through a throwaway request, so the checklist, the CLI command and an actual build
        /// can never disagree about which values the environment still has to provide.
        /// </summary>
        private Dictionary<string, string> SuppliedCredentials() =>
            SuppliedCredentials(platform, teamId, ascKeyPath, ascKeyId, ascIssuerId);

        /// <summary>
        /// The same rule over values that came from somewhere else — the menu item's stored
        /// preferences. Static so that path cannot grow its own copy.
        /// </summary>
        private static Dictionary<string, string> SuppliedCredentials(MobilePlatform platform,
            string teamId, string ascKeyPath, string ascKeyId, string ascIssuerId) =>
            BuildRunner.SuppliedCredentials(new BuildRequest
            {
                Platform = platform,
                TeamId = (teamId ?? string.Empty).Trim(),
                AscKeyPath = (ascKeyPath ?? string.Empty).Trim(),
                AscKeyId = (ascKeyId ?? string.Empty).Trim(),
                AscIssuerId = (ascIssuerId ?? string.Empty).Trim()
            });

        // Named rather than positional: a row of interchangeable bools transposes silently.
        private string[] RequiredEnvironmentVariables() =>
            BuildRunner.RequiredEnvironmentVariables(platform, MethodsFor(exportMethod),
                includeNativeStage: runNativeStage,
                automaticSigning: signing == SigningChoice.Automatic,
                uploadToTestFlight: UploadRequested,
                suppliedNames: SuppliedCredentials().Keys);

        private static IosExportMethod[] MethodsFor(ExportMethodChoice choice)
        {
            switch (choice)
            {
                case ExportMethodChoice.AdHoc: return new[] { IosExportMethod.AdHoc };
                case ExportMethodChoice.Both:
                    return new[] { IosExportMethod.AppStore, IosExportMethod.AdHoc };
                default: return new[] { IosExportMethod.AppStore };
            }
        }

        private static string MethodArgument(ExportMethodChoice choice)
        {
            switch (choice)
            {
                case ExportMethodChoice.AdHoc: return "adhoc";
                case ExportMethodChoice.Both: return "both";
                default: return "appstore";
            }
        }

        private string ComposeCliCommand()
        {
            var parts = new List<string>();

            // build.sh has no flags for any of these on purpose: on the command line the
            // environment is the one way credentials arrive. Leading assignments keep the copied
            // command self-contained anyway, so it also works in a shell that has none set.
            // Ordered by variable name so the copied line does not reshuffle between presses.
            //
            // These do reach the clipboard, and the Console log line below records them. That is
            // the trade for a paste-and-run command: they are identifiers and a path, useless
            // without the .p8, which is never copied here.
            foreach (KeyValuePair<string, string> supplied in SuppliedCredentials().OrderBy(entry => entry.Key))
                parts.Add($"{supplied.Key}={ShellQuoteIfNeeded(supplied.Value)}");

            parts.AddRange(new[]
            {
                // Not a literal: the entry point differs between the .unitypackage layout, a
                // UPM package and a checkout with the root forwarder, and a command that does
                // not run is worse than no button.
                BuildToolsLayout.CliEntryPoint(),
                "--platform", platform == MobilePlatform.iOS ? "ios" : "android",
                "--profile", ShellQuoteIfNeeded(profiles[profileIndex].name)
            });

            if (platform == MobilePlatform.iOS)
            {
                parts.Add("--export-method");
                parts.Add(MethodArgument(exportMethod));
                if (signing == SigningChoice.Automatic)
                {
                    parts.Add("--signing");
                    parts.Add("auto");
                }

                if (UploadRequested)
                    parts.Add("--upload-testflight");
            }

            if (version.Trim().Length > 0)
            {
                parts.Add("--version");
                parts.Add(version.Trim());
            }

            if (buildNumber.Trim().Length > 0)
            {
                parts.Add("--build-number");
                parts.Add(buildNumber.Trim());
            }

            string artifactDir = ResolveArtifactDir();
            if (artifactDir != Path.GetFullPath(Path.Combine(BuildRunner.ProjectRoot, "Build/Artifacts")))
            {
                parts.Add("--artifact-dir");
                parts.Add(ShellQuoteIfNeeded(artifactDir));
            }

            if (development)
                parts.Add("--development");
            if (!runNativeStage)
            {
                parts.Add("--stage");
                parts.Add("unity");
            }

            return string.Join(" ", parts);
        }

        /// <summary>
        /// Profile names are asset names and may contain spaces, which would otherwise make the
        /// copied command exit 2 with "unknown argument" the moment it is pasted into a shell.
        /// </summary>
        private static string ShellQuoteIfNeeded(string value)
        {
            if (!string.IsNullOrEmpty(value) && value.IndexOfAny(new[] { ' ', '\t', '\'', '"', '$', '&', '(', ')' }) < 0)
                return value;

            return "'" + (value ?? string.Empty).Replace("'", "'\\''") + "'";
        }

        /// <summary>The version this build would ship as: the typed override, else the project's.</summary>
        private string EffectiveVersion() =>
            version.Trim().Length == 0 ? PlayerSettings.bundleVersion : version.Trim();

        private void ConfirmAndRun(MobilePlatform target)
        {
            BuildProfile profile = profiles[profileIndex];
            string summary = $"Profile: {profile.name}\nPlatform: {target}\n" +
                             $"Version: {EffectiveVersion()}\n" +
                             $"Build number: {(buildNumber.Trim().Length == 0 ? "(project value)" : buildNumber.Trim())}";
            if (HasTeamIdOverride)
                summary += $"\nTeam ID: {teamId.Trim()}";
            // Named explicitly: this build leaves the machine, which is not something to
            // discover from the log afterwards.
            if (UploadRequested)
                summary += "\nUpload to TestFlight: yes";
            if (!EditorUtility.DisplayDialog($"Build {target}", summary + "\n\nStart the build?", "Build", "Cancel"))
                return;

            Run(profile, target,
                requestedVersion: version.Trim(),
                requestedBuildNumber: buildNumber.Trim(),
                credentials: SuppliedCredentials(),
                methods: MethodsFor(exportMethod),
                developmentBuild: development,
                runNative: runNativeStage,
                automaticSigning: signing == SigningChoice.Automatic,
                uploadToTestFlight: UploadRequested);
        }

        private static void RunFromMenu(MobilePlatform target)
        {
            if (!BuildProfile.TryResolve(EditorPrefs.GetString(PrefPrefix + "Profile", string.Empty),
                    out BuildProfile profile, out string error))
            {
                EditorUtility.DisplayDialog("Build", error, "OK");
                return;
            }

            string storedVersion = EditorPrefs.GetString(PrefPrefix + "Version", string.Empty).Trim();
            if (storedVersion.Length > 0 && !BuildNaming.IsValidVersion(storedVersion, out string versionError))
            {
                EditorUtility.DisplayDialog("Build",
                    "Version " + versionError +
                    " Fix it in Tools > Build Tools > Build Settings.", "OK");
                return;
            }

            string storedBuildNumber = EditorPrefs.GetString(PrefPrefix + "BuildNumber", string.Empty).Trim();
            if (storedBuildNumber.Length > 0 && !storedBuildNumber.All(char.IsDigit))
            {
                EditorUtility.DisplayDialog("Build",
                    $"The stored build number '{storedBuildNumber}' is not a whole number. " +
                    "Fix it in Tools > Build Tools > Build Settings.", "OK");
                return;
            }

            var choice = (ExportMethodChoice)EditorPrefs.GetInt(
                PrefPrefix + "ExportMethod", (int)ExportMethodChoice.AppStore);
            bool storedDevelopment = EditorPrefs.GetBool(PrefPrefix + "Development", false);
            bool storedRunNative = EditorPrefs.GetBool(PrefPrefix + "RunNativeStage", true);
            bool storedAutomaticSigning =
                (SigningChoice)EditorPrefs.GetInt(PrefPrefix + "Signing", (int)SigningChoice.Manual)
                == SigningChoice.Automatic;

            // iOS only, matching the window: an Android build has no use for any of these, and
            // carrying them over would put stale values in front of the wrong platform.
            string storedTeamId = target == MobilePlatform.iOS
                ? EditorPrefs.GetString(PrefPrefix + "TeamId", string.Empty).Trim()
                : string.Empty;
            Dictionary<string, string> storedCredentials = SuppliedCredentials(target,
                storedTeamId,
                EditorPrefs.GetString(AscKeyPathPrefKey, string.Empty),
                EditorPrefs.GetString(AscKeyIdPrefKey, string.Empty),
                EditorPrefs.GetString(AscIssuerIdPrefKey, string.Empty));
            if (storedTeamId.Length > 0 && !BuildRunner.IsValidTeamId(storedTeamId, out string teamIdError))
            {
                EditorUtility.DisplayDialog("Build",
                    "Team ID " + teamIdError +
                    " Fix it in Tools > Build Tools > Build Settings.", "OK");
                return;
            }

            // The window's rule, not a second copy of it, applied to the stored values.
            bool uploadWanted = EditorPrefs.GetBool(UploadPrefKey, false);
            string uploadBlockedReason = UploadBlockedReason(target, choice, storedRunNative);
            bool storedUpload = uploadWanted && uploadBlockedReason == null;

            string summary = $"Profile: {profile.name}\nPlatform: {target}\n" +
                             $"Version: {(storedVersion.Length == 0 ? PlayerSettings.bundleVersion : storedVersion)}\n" +
                             $"Build number: {(storedBuildNumber.Length == 0 ? "(project value)" : storedBuildNumber)}";
            if (storedTeamId.Length > 0)
                summary += $"\nTeam ID: {storedTeamId}";
            // A stored preference that this configuration blocks is said out loud rather than
            // silently skipped: the menu item shows no settings, so the dialog is the only
            // place someone could notice that the upload they asked for is not happening.
            if (uploadWanted)
            {
                summary += storedUpload
                    ? "\nUpload to TestFlight: yes"
                    : "\nUpload to TestFlight: NO — " + uploadBlockedReason;
            }
            if (!EditorUtility.DisplayDialog($"Build {target}",
                    summary + "\n\nStart the build? The Editor is blocked until it finishes.", "Build", "Cancel"))
                return;

            Run(profile, target,
                requestedVersion: storedVersion,
                requestedBuildNumber: storedBuildNumber,
                credentials: storedCredentials,
                methods: MethodsFor(choice),
                developmentBuild: storedDevelopment,
                runNative: storedRunNative,
                automaticSigning: storedAutomaticSigning,
                uploadToTestFlight: storedUpload);
        }

        private static void Run(BuildProfile profile, MobilePlatform target, string requestedVersion,
            string requestedBuildNumber, Dictionary<string, string> credentials,
            IosExportMethod[] methods, bool developmentBuild, bool runNative,
            bool automaticSigning, bool uploadToTestFlight)
        {
            var arguments = new BuildArguments
            {
                Profile = profile.name,
                Version = requestedVersion,
                BuildNumber = requestedBuildNumber,
                ExportMethods = methods,
                Development = developmentBuild,
                AutomaticSigning = automaticSigning,
                UploadToTestFlight = uploadToTestFlight
            };
            BuildRequest request = BuildCli.CreateRequest(profile, target, arguments, runNative);

            // Set on the request rather than on BuildArguments: that type mirrors the build.sh
            // command line, and there are no flags for these to mirror. They are the Editor's
            // own stand-ins for environment variables Unity never received. Anything absent
            // stays empty, which BuildRunner reads as "use the environment".
            request.TeamId = Supplied(credentials, BuildRunner.EnvIosTeamId);
            request.AscKeyPath = Supplied(credentials, BuildRunner.EnvIosAscKeyPath);
            request.AscKeyId = Supplied(credentials, BuildRunner.EnvIosAscKeyId);
            request.AscIssuerId = Supplied(credentials, BuildRunner.EnvIosAscIssuerId);

            try
            {
                string[] artifacts = BuildRunner.Run(request);
                string message = artifacts.Length == 0
                    ? $"Unity export complete:\n{request.ExportDir}"
                    : "Build complete:\n" + string.Join("\n", artifacts);
                Debug.Log($"[BuildTools] {message}");
                EditorUtility.DisplayDialog($"Build {target}", message, "OK");
            }
            catch (Exception exception)
            {
                Debug.LogError($"[BuildTools] Build failed: {exception}");
                EditorUtility.DisplayDialog($"Build {target}",
                    "Build failed:\n\n" + exception.Message + "\n\nSee the Console for the full log.", "OK");
            }
        }

        private static string Supplied(Dictionary<string, string> credentials, string name) =>
            credentials != null && credentials.TryGetValue(name, out string value)
                ? value
                : string.Empty;

        private static string ResolveArtifactDir()
        {
            string artifactRoot = BuildProfile.TryResolve(string.Empty, out BuildProfile profile, out _)
                ? profile.artifactRoot
                : "Build/Artifacts";
            return Path.GetFullPath(Path.Combine(BuildRunner.ProjectRoot, artifactRoot));
        }

        // ----------------------------------------------------------- preferences -----

        private void LoadPreferences()
        {
            platform = (MobilePlatform)EditorPrefs.GetInt(PrefPrefix + "Platform", (int)MobilePlatform.Android);
            version = EditorPrefs.GetString(PrefPrefix + "Version", string.Empty);
            buildNumber = EditorPrefs.GetString(PrefPrefix + "BuildNumber", string.Empty);
            exportMethod = (ExportMethodChoice)EditorPrefs.GetInt(
                PrefPrefix + "ExportMethod", (int)ExportMethodChoice.AppStore);
            signing = (SigningChoice)EditorPrefs.GetInt(PrefPrefix + "Signing", (int)SigningChoice.Manual);
            teamId = EditorPrefs.GetString(PrefPrefix + "TeamId", string.Empty);
            ascKeyPath = EditorPrefs.GetString(AscKeyPathPrefKey, string.Empty);
            ascKeyId = EditorPrefs.GetString(AscKeyIdPrefKey, string.Empty);
            ascIssuerId = EditorPrefs.GetString(AscIssuerIdPrefKey, string.Empty);
            development = EditorPrefs.GetBool(PrefPrefix + "Development", false);
            runNativeStage = EditorPrefs.GetBool(PrefPrefix + "RunNativeStage", true);
            // Default false: nothing is ever uploaded because nobody said not to.
            uploadToTestFlight = EditorPrefs.GetBool(UploadPrefKey, false);
        }

        private void SavePreferences()
        {
            if (profiles.Length > 0)
                EditorPrefs.SetString(PrefPrefix + "Profile", profiles[Mathf.Clamp(profileIndex, 0, profiles.Length - 1)].name);

            EditorPrefs.SetInt(PrefPrefix + "Platform", (int)platform);
            EditorPrefs.SetString(PrefPrefix + "Version", version);
            EditorPrefs.SetString(PrefPrefix + "BuildNumber", buildNumber);
            EditorPrefs.SetInt(PrefPrefix + "ExportMethod", (int)exportMethod);
            EditorPrefs.SetInt(PrefPrefix + "Signing", (int)signing);
            EditorPrefs.SetString(PrefPrefix + "TeamId", teamId);
            EditorPrefs.SetString(AscKeyPathPrefKey, ascKeyPath);
            EditorPrefs.SetString(AscKeyIdPrefKey, ascKeyId);
            EditorPrefs.SetString(AscIssuerIdPrefKey, ascIssuerId);
            EditorPrefs.SetBool(PrefPrefix + "Development", development);
            EditorPrefs.SetBool(PrefPrefix + "RunNativeStage", runNativeStage);
            EditorPrefs.SetBool(UploadPrefKey, uploadToTestFlight);
        }
    }
}
