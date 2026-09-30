using System;
using System.IO;

namespace CapsChaos.SkuHeadlessTests
{
    /// <summary>
    /// Locates the repo root from inside a test run, so a test can read a SHIPPED file (an asmdef, an
    /// Addressables group, a manifest) rather than a copy of it.
    ///
    /// <para>Walks up from the test assembly's own directory looking for the same two sentinels the
    /// framework's scripts use — <c>AGENTS.md</c> + <c>Packages/manifest.json</c>. Failure throws and
    /// names what it looked for; a test that silently fell back to a guessed path would assert against
    /// nothing.</para>
    /// </summary>
    public static class RepoLayout
    {
        private static readonly Lazy<string> Root = new Lazy<string>(Locate);

        /// <summary>Absolute path of the repo root. Throws if it cannot be found.</summary>
        public static string RepoRoot => Root.Value;

        /// <summary>Repo-root-relative path, joined with the platform separator.</summary>
        public static string Path(params string[] segments)
            => System.IO.Path.Combine(RepoRoot, System.IO.Path.Combine(segments));

        private static string Locate()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null)
            {
                if (File.Exists(System.IO.Path.Combine(dir.FullName, "AGENTS.md")) &&
                    File.Exists(System.IO.Path.Combine(dir.FullName, "Packages", "manifest.json")))
                {
                    return dir.FullName;
                }

                dir = dir.Parent;
            }

            throw new InvalidOperationException(
                "Could not locate the repo root above '" + AppContext.BaseDirectory +
                "' (looked for a directory holding both AGENTS.md and Packages/manifest.json).");
        }
    }
}
