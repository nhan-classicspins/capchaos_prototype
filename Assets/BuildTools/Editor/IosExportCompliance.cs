// Guarded because UnityEditor.iOS.Xcode ships with the iOS build support module. Left
// unguarded, a machine without that module installed would fail to compile this file --
// and that would break the Android build too, which has nothing to do with it.
#if UNITY_IOS
using System.IO;
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEditor.iOS.Xcode;
using UnityEngine;

namespace ClassicSpins.BuildTools
{
    /// <summary>
    /// Answers Apple's export-compliance question in the exported Info.plist.
    ///
    /// Without <c>ITSAppUsesNonExemptEncryption</c>, every build that reaches App Store
    /// Connect arrives flagged "Missing Compliance" and cannot be submitted until someone
    /// answers the encryption question by hand in the web UI — once per build, forever.
    /// Unity never writes the key and exposes no setting for it, so it has to be added
    /// after the export.
    ///
    /// The answer is <c>false</c>: the game ships no cryptography of its own, and the only
    /// encryption involved is HTTPS/TLS provided by the operating system, which Apple
    /// exempts. <strong>That answer stops being true</strong> the moment the app bundles a
    /// crypto library, encrypts save data itself, or implements a proprietary algorithm —
    /// at which point <see cref="UsesNonExemptEncryption"/> has to change and Apple's
    /// year-end self-classification report applies. It is a declaration to a regulator,
    /// not a build flag, which is why it is a constant here rather than a profile field
    /// someone could flip per build.
    /// </summary>
    public static class IosExportCompliance
    {
        /// <summary>The Info.plist key App Store Connect reads instead of asking.</summary>
        public const string PlistKey = "ITSAppUsesNonExemptEncryption";

        /// <summary>Read the class summary before changing this.</summary>
        public const bool UsesNonExemptEncryption = false;

        /// <summary>
        /// Runs late (high callback order) so that any other post-processor which rewrites
        /// Info.plist wholesale has already had its turn.
        /// </summary>
        [PostProcessBuild(1000)]
        public static void OnPostProcessBuild(BuildTarget target, string pathToBuiltProject)
        {
            if (target != BuildTarget.iOS)
                return;

            string plistPath = Path.Combine(pathToBuiltProject, "Info.plist");
            Apply(plistPath);

            Debug.Log($"[BuildTools] Set {PlistKey}=" +
                      $"{UsesNonExemptEncryption.ToString().ToLowerInvariant()} in {plistPath}.");
        }

        /// <summary>
        /// Writes the key into the plist at <paramref name="plistPath"/>, leaving every
        /// other entry untouched. Separate from the callback so it can be tested without a
        /// real build, and so a missing plist fails here — loudly, before the archive — for
        /// the same reason the packaging scripts refuse to guess a file name.
        /// </summary>
        public static void Apply(string plistPath)
        {
            if (!File.Exists(plistPath))
            {
                throw new BuildFailedException(
                    $"No Info.plist at {plistPath}, so {PlistKey} could not be written. The build " +
                    "would upload with a Missing Compliance warning, so it is failed here instead.");
            }

            var plist = new PlistDocument();
            plist.ReadFromFile(plistPath);
            plist.root.SetBoolean(PlistKey, UsesNonExemptEncryption);
            plist.WriteToFile(plistPath);
        }
    }
}
#endif
