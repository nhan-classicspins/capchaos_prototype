using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace ClassicSpins.BuildTools
{
    /// <summary>
    /// Packages this whole tool — the Editor C# and the shell layer beside it — into a
    /// single <c>.unitypackage</c> another project can drag in.
    ///
    /// The shell scripts are ordinary assets rather than living in a <c>~</c> folder,
    /// because a <c>.unitypackage</c> can only carry what the AssetDatabase can see: a
    /// folder Unity ignores is a folder <see cref="AssetDatabase.ExportPackage(string[],
    /// string, ExportPackageOptions)"/> cannot export. Unity imports <c>.sh</c> and
    /// <c>.plist</c> as inert <c>DefaultAsset</c>s, so nothing is compiled or shipped in
    /// a player build; they only gain the .meta GUID that makes them exportable.
    /// </summary>
    public static class BuildToolsExporter
    {
        /// <summary>Everything under here is the tool.</summary>
        private const string SourceFolder = "Assets/BuildTools";

        /// <summary>
        /// Excluded from the package. The tests reference the test framework, so a project
        /// without <c>com.unity.test-framework</c> would fail to compile the moment the
        /// package landed — and they test this tool, which is not the receiving project's
        /// job. They stay here, in the repo the tool is developed in.
        /// </summary>
        private const string TestsFolder = "Assets/BuildTools/Editor/Tests";

        private const string DefaultFileName = "BuildTools.unitypackage";

        [MenuItem(BuildToolsWindow.MenuRoot + "Export .unitypackage", false, 60)]
        private static void ExportInteractive()
        {
            string output = EditorUtility.SaveFilePanel(
                "Export Build Tools", "", DefaultFileName, "unitypackage");
            if (string.IsNullOrEmpty(output))
                return;

            string[] exported = Export(output);
            EditorUtility.RevealInFinder(output);
            EditorUtility.DisplayDialog(
                "Build Tools",
                $"Exported {exported.Length} asset(s) to:\n{output}",
                "OK");
        }

        /// <summary>
        /// Batch-mode entry point, so a release can be cut without opening the Editor:
        /// <code>
        /// Unity -batchmode -quit -projectPath . \
        ///       -executeMethod ClassicSpins.BuildTools.BuildToolsExporter.ExportFromCommandLine \
        ///       --output BuildTools.unitypackage
        /// </code>
        /// </summary>
        public static void ExportFromCommandLine()
        {
            string output = ReadOutputArgument() ?? DefaultFileName;
            string[] exported = Export(output);
            Debug.Log($"[buildtools] exported {exported.Length} asset(s) to {Path.GetFullPath(output)}");
        }

        /// <summary>
        /// Exports the tool to <paramref name="outputPath"/> and returns the asset paths
        /// that went in. Paths are listed explicitly rather than exporting the folder with
        /// <see cref="ExportPackageOptions.Recurse"/>, which is the only way to leave the
        /// tests out; <see cref="ExportPackageOptions.IncludeDependencies"/> is likewise
        /// off, or a BuildProfile asset elsewhere in the project would be dragged along.
        /// </summary>
        public static string[] Export(string outputPath)
        {
            if (string.IsNullOrEmpty(outputPath))
                throw new ArgumentException("Output path is required.", nameof(outputPath));

            AssetDatabase.Refresh();

            string[] paths = CollectAssetPaths();
            if (paths.Length == 0)
                throw new InvalidOperationException($"No assets found under {SourceFolder}.");

            string directory = Path.GetDirectoryName(Path.GetFullPath(outputPath));
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);

            AssetDatabase.ExportPackage(paths, outputPath, ExportPackageOptions.Default);
            return paths;
        }

        /// <summary>
        /// Every asset under <see cref="SourceFolder"/> except the tests. Walked on disk
        /// rather than through <c>AssetDatabase.FindAssets</c> so the extensionless and
        /// unrecognised files — the shell scripts, the plist templates — are all picked up;
        /// a `.meta` is proof Unity has imported one and can therefore export it.
        /// </summary>
        private static string[] CollectAssetPaths() =>
            Directory.EnumerateFiles(SourceFolder, "*", SearchOption.AllDirectories)
                .Select(path => path.Replace('\\', '/'))
                .Where(path => !path.EndsWith(".meta", StringComparison.Ordinal))
                .Where(path => !path.StartsWith(TestsFolder + "/", StringComparison.Ordinal))
                .Where(path => File.Exists(path + ".meta"))
                .OrderBy(path => path, StringComparer.Ordinal)
                .ToArray();

        private static string ReadOutputArgument()
        {
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (args[i] == "--output")
                    return args[i + 1];
            }

            return null;
        }
    }
}
