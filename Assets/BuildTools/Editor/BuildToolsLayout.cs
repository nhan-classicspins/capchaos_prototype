using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Compilation;
using UnityEngine;
using PackageInfo = UnityEditor.PackageManager.PackageInfo;

namespace ClassicSpins.BuildTools
{
    /// <summary>
    /// Finds the shell layer on disk.
    ///
    /// The two halves of this tool ship together but not at a fixed distance apart: the same
    /// tree is dragged in as a <c>.unitypackage</c> (<c>Assets/BuildTools/Shell</c>), embedded
    /// as a package (<c>Packages/…/Shell~</c>), or resolved from a git URL into
    /// <c>Library/PackageCache</c>. Hardcoding one of those breaks the other two silently —
    /// the Editor build would run all the way to the packaging stage before failing on a
    /// missing file. So the location is derived, and derived from the one thing that always
    /// travels with the scripts: this assembly.
    /// </summary>
    public static class BuildToolsLayout
    {
        /// <summary>Both spellings: `~` is stripped only in the UPM layout, where Unity must ignore the folder.</summary>
        private static readonly string[] ShellFolderNames = { "Shell", "Shell~" };

        /// <summary>Checked, in order, when the assembly lookup comes up empty.</summary>
        private static readonly string[] FallbackCandidates =
        {
            "Assets/BuildTools/Shell",
            "Packages/com.classicspins.buildtools/Shell~",
            "BuildTools", // the layout before the shell layer moved under Assets
        };

        private static string cachedShellRoot;

        /// <summary>
        /// Absolute path of the folder holding <c>build.sh</c>, <c>lib/</c>, <c>ios/</c> and
        /// <c>android/</c>. Throws with the paths it checked rather than returning something
        /// that will fail later as a confusing "no such file".
        /// </summary>
        public static string ShellRoot
        {
            get
            {
                if (cachedShellRoot != null && IsShellRoot(cachedShellRoot))
                    return cachedShellRoot;

                cachedShellRoot = FindShellRoot();
                return cachedShellRoot;
            }
        }

        public static string ScriptPath(params string[] relativeParts) =>
            Path.Combine(new[] { ShellRoot }.Concat(relativeParts).ToArray());

        /// <summary>
        /// How to spell the entry point in a command meant to be pasted into a terminal at the
        /// project root. A forwarder at <c>BuildTools/build.sh</c> wins when one exists: it is
        /// executable, it is what the CI steps already invoke, and it is shorter. Otherwise the
        /// real script is named and prefixed with <c>bash</c>, because a <c>.unitypackage</c>
        /// does not carry the executable bit and a freshly imported copy would not run.
        /// </summary>
        public static string CliEntryPoint()
        {
            string projectRoot = BuildRunner.ProjectRoot;
            string forwarder = Path.Combine(projectRoot, "BuildTools", "build.sh");
            if (File.Exists(forwarder))
                return "BuildTools/build.sh";

            string relative = ToProjectRelative(Path.Combine(ShellRoot, "build.sh"), projectRoot);
            return "bash " + (relative.Contains(' ') ? "'" + relative + "'" : relative);
        }

        private static string FindShellRoot()
        {
            string fromAssembly = FromThisAssembly();
            if (fromAssembly != null)
                return fromAssembly;

            string projectRoot = BuildRunner.ProjectRoot;
            foreach (string candidate in FallbackCandidates)
            {
                string absolute = Path.Combine(projectRoot, candidate.Replace('/', Path.DirectorySeparatorChar));
                if (IsShellRoot(absolute))
                    return absolute;
            }

            throw new BuildFailedException(
                "Could not find the BuildTools shell scripts. Looked beside this assembly and at: " +
                string.Join(", ", FallbackCandidates) +
                ". The Editor code and the Shell folder must ship together.");
        }

        /// <summary>
        /// The shell folder sits beside the folder holding this assembly's asmdef, so locating
        /// the asmdef locates the scripts — and keeps working if the whole tree is renamed or
        /// moved, which a hardcoded path would not.
        /// </summary>
        private static string FromThisAssembly()
        {
            System.Reflection.Assembly assembly = typeof(BuildToolsLayout).Assembly;

            // A package's assets live under Library/PackageCache, not under the `Packages/…`
            // path the AssetDatabase reports, so the package manager has to resolve it.
            PackageInfo package = PackageInfo.FindForAssembly(assembly);
            if (package != null)
                return FirstShellFolderUnder(package.resolvedPath);

            string asmdef = CompilationPipeline
                .GetAssemblyDefinitionFilePathFromAssemblyName(assembly.GetName().Name);
            if (string.IsNullOrEmpty(asmdef))
                return null;

            // asmdef is project-relative, e.g. Assets/BuildTools/Editor/CS.BuildTools.Editor.asmdef;
            // its grandparent is the folder the Shell folder is a sibling within.
            string editorFolder = Path.GetDirectoryName(Path.Combine(BuildRunner.ProjectRoot, asmdef));
            string toolRoot = Path.GetDirectoryName(editorFolder);
            return toolRoot == null ? null : FirstShellFolderUnder(toolRoot);
        }

        private static string FirstShellFolderUnder(string root) =>
            ShellFolderNames
                .Select(name => Path.Combine(root, name))
                .FirstOrDefault(IsShellRoot);

        /// <summary>
        /// A folder is the shell root when it holds the entry point. Checked rather than
        /// assumed, so a stale or half-copied folder is skipped instead of chosen.
        /// </summary>
        private static bool IsShellRoot(string path) =>
            !string.IsNullOrEmpty(path) && File.Exists(Path.Combine(path, "build.sh"));

        private static string ToProjectRelative(string absolute, string projectRoot)
        {
            string prefix = projectRoot.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal)
                ? projectRoot
                : projectRoot + Path.DirectorySeparatorChar;

            string relative = absolute.StartsWith(prefix, StringComparison.Ordinal)
                ? absolute.Substring(prefix.Length)
                : absolute;

            return relative.Replace('\\', '/');
        }
    }
}
