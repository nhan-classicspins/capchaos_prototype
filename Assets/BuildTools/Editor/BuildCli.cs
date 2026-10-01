using System;
using System.Globalization;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace ClassicSpins.BuildTools
{
    /// <summary>The command line was wrong. Reported as exit code 2: nothing was built.</summary>
    public sealed class BuildArgumentException : Exception
    {
        public BuildArgumentException(string message) : base(message) { }
    }

    /// <summary>
    /// Parsed form of the arguments BuildTools/build.sh appends after <c>--</c>. Kept free of
    /// Unity API calls so the parsing rules can be unit tested.
    /// </summary>
    public sealed class BuildArguments
    {
        public const string Separator = "--";

        /// <summary>
        /// The version flag is spelled <c>--app-version</c>, not <c>--version</c>. Unity scans
        /// the whole command line no matter where <see cref="Separator"/> is, and reads
        /// <c>--version</c> as its own <c>-version</c>: it prints the editor version and exits 0
        /// without ever running the <c>-executeMethod</c>. The user-facing
        /// <c>build.sh --version</c> is translated to this on the way in.
        /// </summary>
        public const string VersionFlag = "--app-version";

        public string Profile = string.Empty;
        public string Version = string.Empty;
        public string BuildNumber = string.Empty;
        public string ExportDir = string.Empty;
        public string ArtifactDir = string.Empty;
        public IosExportMethod[] ExportMethods = { IosExportMethod.AppStore };
        public bool Development;
        public bool KeepExport;
        public bool AutomaticSigning;

        /// <summary>
        /// iOS only. Upload the App Store <c>.ipa</c> to App Store Connect after packaging.
        /// <c>build.sh</c> owns its own upload stage and deliberately does <em>not</em> forward
        /// this flag to the Unity stage it drives; the flag is parsed here for the single-launch
        /// <see cref="BuildCli.BuildIos"/> entry point, which does the packaging in-process and
        /// so has to do the upload too.
        /// </summary>
        public bool UploadToTestFlight;

        /// <summary>
        /// Reads the arguments that follow the first <c>--</c>. When no separator is present
        /// the whole command line is scanned instead, so a launcher that strips it still works;
        /// Unity's own single-dash arguments are ignored either way.
        /// </summary>
        public static BuildArguments Parse(string[] commandLine)
        {
            if (commandLine == null)
                throw new BuildArgumentException("No command line was supplied.");

            int separatorIndex = Array.IndexOf(commandLine, Separator);
            int start = separatorIndex >= 0 ? separatorIndex + 1 : 0;

            var arguments = new BuildArguments();
            for (int i = start; i < commandLine.Length; i++)
            {
                string token = commandLine[i];
                switch (token)
                {
                    case "--profile":
                        arguments.Profile = TakeValue(commandLine, ref i);
                        break;
                    // Not "--version": Unity claims that name itself, prints the editor version
                    // and exits before -executeMethod ever runs. See VersionFlag.
                    case VersionFlag:
                        arguments.Version = RequireVersion(TakeValue(commandLine, ref i));
                        break;
                    case "--build-number":
                        arguments.BuildNumber = RequireDigits(TakeValue(commandLine, ref i));
                        break;
                    case "--export-dir":
                        arguments.ExportDir = TakeValue(commandLine, ref i);
                        break;
                    case "--artifact-dir":
                        arguments.ArtifactDir = TakeValue(commandLine, ref i);
                        break;
                    case "--export-method":
                        arguments.ExportMethods = ParseExportMethods(TakeValue(commandLine, ref i));
                        break;
                    case "--signing":
                        arguments.AutomaticSigning = ParseSigning(TakeValue(commandLine, ref i));
                        break;
                    case "--development":
                        arguments.Development = true;
                        break;
                    case "--keep-export":
                        arguments.KeepExport = true;
                        break;
                    case "--upload-testflight":
                        arguments.UploadToTestFlight = true;
                        break;
                    default:
                        // Only our own long-form flags are validated. Anything else here is
                        // Unity's (-batchmode, -projectPath, ...) and is not ours to police.
                        if (token != null && token.StartsWith("--", StringComparison.Ordinal))
                            throw new BuildArgumentException($"Unknown argument: {token}");
                        break;
                }
            }

            return arguments;
        }

        private static string TakeValue(string[] commandLine, ref int index)
        {
            string flag = commandLine[index];
            int valueIndex = index + 1;
            if (valueIndex >= commandLine.Length)
                throw new BuildArgumentException($"{flag} requires a value.");

            string value = commandLine[valueIndex];
            if (string.IsNullOrEmpty(value) || value.StartsWith("--", StringComparison.Ordinal))
                throw new BuildArgumentException($"{flag} requires a value.");

            index = valueIndex;
            return value;
        }

        private static string RequireVersion(string value)
        {
            // Named after the flag actually typed: build.sh validates --version itself and
            // never reaches this, so anyone who does is driving Unity directly.
            if (!BuildNaming.IsValidVersion(value, out string error))
                throw new BuildArgumentException(VersionFlag + " " + error);

            return value;
        }

        private static string RequireDigits(string value)
        {
            // char.IsDigit accepts non-ASCII digits such as Arabic-Indic, which int.Parse then
            // rejects; and Android's versionCode is a signed 32-bit int. Catching both here
            // keeps them usage errors rather than build failures after a long export.
            if (value.Length == 0 || value.Any(c => c < '0' || c > '9'))
                throw new BuildArgumentException($"--build-number must be a non-negative integer, got '{value}'.");

            if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out int _))
                throw new BuildArgumentException($"--build-number must be at most {int.MaxValue}, got '{value}'.");

            return value;
        }

        /// <summary>Returns true for automatic signing.</summary>
        private static bool ParseSigning(string value)
        {
            switch (value)
            {
                case "manual": return false;
                case "auto": return true;
                default:
                    throw new BuildArgumentException($"--signing must be manual or auto, got '{value}'.");
            }
        }

        private static IosExportMethod[] ParseExportMethods(string value)
        {
            switch (value)
            {
                case "appstore": return new[] { IosExportMethod.AppStore };
                case "adhoc": return new[] { IosExportMethod.AdHoc };
                case "both": return new[] { IosExportMethod.AppStore, IosExportMethod.AdHoc };
                default:
                    throw new BuildArgumentException(
                        $"--export-method must be appstore, adhoc or both, got '{value}'.");
            }
        }
    }

    /// <summary>
    /// Batch-mode entry points, invoked by BuildTools/build.sh with -executeMethod. Exit codes
    /// are set explicitly (0 ok, 1 build failure, 2 bad usage) rather than relying on -quit,
    /// which always reports success.
    /// </summary>
    public static class BuildCli
    {
        private const int ExitOk = 0;
        private const int ExitBuildFailed = 1;
        private const int ExitUsage = 2;

        /// <summary>Unity export only. Packaging is left to build.sh.</summary>
        public static void ExportIos() => Execute(MobilePlatform.iOS, runNativeStage: false);

        public static void ExportAndroid() => Execute(MobilePlatform.Android, runNativeStage: false);

        /// <summary>Unity export plus packaging, for driving the whole build from one Unity launch.</summary>
        public static void BuildIos() => Execute(MobilePlatform.iOS, runNativeStage: true);

        public static void BuildAndroid() => Execute(MobilePlatform.Android, runNativeStage: true);

        /// <summary>
        /// Builds a request from parsed arguments, filling in the directories build.sh would
        /// otherwise pass. Shared with the settings window so both front-ends default alike.
        /// </summary>
        public static BuildRequest CreateRequest(BuildProfile profile, MobilePlatform platform,
            BuildArguments arguments, bool runNativeStage)
        {
            string projectRoot = BuildRunner.ProjectRoot;
            string exportDir = string.IsNullOrEmpty(arguments.ExportDir)
                ? Path.Combine(projectRoot, profile.exportRoot, DefaultExportLeaf(platform))
                : arguments.ExportDir;
            string artifactDir = string.IsNullOrEmpty(arguments.ArtifactDir)
                ? Path.Combine(projectRoot, profile.artifactRoot)
                : arguments.ArtifactDir;

            return new BuildRequest
            {
                Profile = profile,
                Platform = platform,
                Version = arguments.Version,
                BuildNumber = arguments.BuildNumber,
                ExportMethods = arguments.ExportMethods,
                DevelopmentBuild = arguments.Development || profile.developmentBuild,
                ExportDir = Path.GetFullPath(exportDir),
                ArtifactDir = Path.GetFullPath(artifactDir),
                RunNativeStage = runNativeStage,
                KeepExport = arguments.KeepExport,
                AutomaticSigning = arguments.AutomaticSigning,
                UploadToTestFlight = arguments.UploadToTestFlight
            };
        }

        /// <summary>
        /// Matches the directory layout BuildTools/build.sh uses, so a standalone
        /// -executeMethod run and a build.sh run touch the same paths.
        /// </summary>
        public static string DefaultExportLeaf(MobilePlatform platform) =>
            platform == MobilePlatform.iOS
                ? Path.Combine("iOS", "XcodeProject")
                : Path.Combine("Android", "AndroidProject");

        private static void Execute(MobilePlatform platform, bool runNativeStage)
        {
            int exitCode = ExitOk;
            try
            {
                BuildArguments arguments = BuildArguments.Parse(Environment.GetCommandLineArgs());

                if (!BuildProfile.TryResolve(arguments.Profile, out BuildProfile profile, out string error))
                {
                    // The repo ships Assets/_Project/Configs/BuildProfiles/Release.asset, so this
                    // is the case where it was removed or a CI checkout is missing it. Creating
                    // the default beats failing a build over a file of pure defaults — but only
                    // when no specific profile was asked for, since inventing a named profile
                    // would quietly build the wrong configuration.
                    if (!string.IsNullOrEmpty(arguments.Profile) || !Application.isBatchMode)
                        throw new BuildArgumentException(error);

                    Debug.LogWarning($"[BuildTools] {error} Creating the default profile and continuing.");
                    profile = BuildProfile.CreateDefaultAsset();
                }

                BuildRequest request = CreateRequest(profile, platform, arguments, runNativeStage);
                string[] artifacts = BuildRunner.Run(request);

                Debug.Log(artifacts.Length == 0
                    ? $"[BuildTools] {platform} export complete: {request.ExportDir}"
                    : $"[BuildTools] {platform} build complete: {string.Join(", ", artifacts)}");
            }
            catch (BuildArgumentException exception)
            {
                Debug.LogError($"[BuildTools] Usage error: {exception.Message}");
                exitCode = ExitUsage;
            }
            catch (Exception exception)
            {
                Debug.LogError($"[BuildTools] Build failed: {exception}");
                exitCode = ExitBuildFailed;
            }

            Finish(exitCode);
        }

        private static void Finish(int exitCode)
        {
            if (Application.isBatchMode)
            {
                EditorApplication.Exit(exitCode);
                return;
            }

            // Guard rail: these entry points are also reachable from the Editor, and quitting
            // it out from under a developer would be an unpleasant surprise.
            Debug.Log($"[BuildTools] Would exit with code {exitCode} (not batch mode, so staying open).");
        }
    }
}
