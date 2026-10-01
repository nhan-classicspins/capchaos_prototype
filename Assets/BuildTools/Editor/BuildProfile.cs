using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace ClassicSpins.BuildTools
{
    public enum MobilePlatform
    {
        iOS,
        Android
    }

    public enum IosExportMethod
    {
        AppStore,
        AdHoc
    }

    /// <summary>
    /// Non-secret build configuration, stored as an asset so it is reviewable and
    /// versioned. Credentials never live here — the runner reads those from the
    /// environment. See BuildTools/README.md.
    /// </summary>
    [CreateAssetMenu(menuName = "Build Tools/Build Profile", fileName = "BuildProfile")]
    public sealed class BuildProfile : ScriptableObject
    {
        public const string DefaultProfileName = "Release";
        public const string DefaultAssetFolder = "Assets/BuildProfiles";

        [Header("Artifact naming")]
        [Tooltip("Leading token of every artifact file name.")]
        public string artifactPrefix = "App";

        [Tooltip("Appended directly after the build number, e.g. \"s\" for staging. Optional.")]
        public string artifactSuffix = "";

        [Header("Application identifiers")]
        [Tooltip("Leave empty to keep whatever the project already has. Fill in only to override " +
                 "it for builds made with this profile, e.g. a separate staging identifier.")]
        public string iosBundleId = "";

        [Tooltip("Leave empty to keep whatever the project already has.")]
        public string androidBundleId = "";

        [Header("Build")]
        [Tooltip("Adds Unity's BuildOptions.Development to the export.")]
        public bool developmentBuild;

        [Tooltip("Merged into the platform's scripting define symbols for this build, then restored.")]
        public string[] extraScriptingDefines = new string[0];

        [Header("iOS")]
        public ProvisioningProfileType iosProvisioningProfileType = ProvisioningProfileType.Distribution;

        [Header("Android")]
        public AndroidArchitecture androidArchitectures = AndroidArchitecture.ARM64;

        [Header("Output (relative to the project root)")]
        public string exportRoot = "Build";
        public string artifactRoot = "Build/Artifacts";

        /// <summary>
        /// The override for this platform, or empty to leave the project's own identifier alone.
        /// Empty is the shipped default: the project settings are committed and correct, and a
        /// build tool silently rewriting the identifier a developer just set is a trap.
        /// </summary>
        public string BundleIdFor(MobilePlatform platform) =>
            platform == MobilePlatform.iOS ? iosBundleId : androidBundleId;

        /// <summary>Every profile asset in the project, ordered by name.</summary>
        public static BuildProfile[] FindAll() =>
            AssetDatabase.FindAssets("t:" + nameof(BuildProfile))
                .Select(AssetDatabase.GUIDToAssetPath)
                .Select(AssetDatabase.LoadAssetAtPath<BuildProfile>)
                .Where(profile => profile != null)
                .OrderBy(profile => profile.name)
                .ToArray();

        /// <summary>
        /// Resolves a profile by asset name. An empty <paramref name="profileName"/> falls
        /// back to the one called <see cref="DefaultProfileName"/>, or to the only profile
        /// in the project when there is exactly one. Returns false with a message the
        /// caller can show or log verbatim.
        /// </summary>
        public static bool TryResolve(string profileName, out BuildProfile profile, out string error)
        {
            profile = null;
            error = null;

            BuildProfile[] all = FindAll();
            if (all.Length == 0)
            {
                error = $"No {nameof(BuildProfile)} asset exists. Create one at {DefaultAssetFolder} " +
                        $"(Assets > Create > Build Tools > Build Profile), or use the " +
                        $"\"Create Default Profile\" button in Tools > Build Tools > Build Settings.";
                return false;
            }

            // Case-insensitive here too, so `--profile release` and the no-name default agree
            // about which asset "Release" means.
            string wanted = string.IsNullOrEmpty(profileName) ? DefaultProfileName : profileName;
            BuildProfile[] matches = all
                .Where(candidate => string.Equals(candidate.name, wanted, System.StringComparison.OrdinalIgnoreCase))
                .ToArray();

            if (matches.Length > 1)
            {
                error = $"More than one {nameof(BuildProfile)} is called '{wanted}': " +
                        string.Join(", ", matches.Select(AssetDatabase.GetAssetPath)) +
                        ". Rename one so the build cannot pick the wrong configuration.";
                return false;
            }

            if (matches.Length == 1)
            {
                profile = matches[0];
                return true;
            }

            // A single profile under any name is unambiguous, so use it rather than insisting
            // on the default name.
            if (string.IsNullOrEmpty(profileName) && all.Length == 1)
            {
                profile = all[0];
                return true;
            }

            error = string.IsNullOrEmpty(profileName)
                ? $"No profile name given and no profile called '{DefaultProfileName}' exists. Available: {DescribeNames(all)}"
                : $"No {nameof(BuildProfile)} called '{profileName}'. Available: {DescribeNames(all)}";
            return false;
        }

        /// <summary>Creates the default profile asset and returns it.</summary>
        public static BuildProfile CreateDefaultAsset()
        {
            Directory.CreateDirectory(DefaultAssetFolder);
            AssetDatabase.Refresh();

            var profile = CreateInstance<BuildProfile>();

            // Seed the prefix from the product name rather than a literal, so a fresh
            // profile in any project names its artifacts after that project.
            string fromProduct = BuildNaming.Sanitize(PlayerSettings.productName);
            if (fromProduct.Length > 0)
                profile.artifactPrefix = fromProduct;

            string path = AssetDatabase.GenerateUniqueAssetPath($"{DefaultAssetFolder}/{DefaultProfileName}.asset");
            AssetDatabase.CreateAsset(profile, path);
            AssetDatabase.SaveAssets();
            Debug.Log($"Created build profile at {path}.", profile);
            return profile;
        }

        private static string DescribeNames(IEnumerable<BuildProfile> profiles) =>
            string.Join(", ", profiles.Select(profile => profile.name));
    }
}
