using System.Collections.Generic;
using System.IO;
using System.Linq;
using Game.Domain;
using NUnit.Framework;

namespace CapsChaos.SkuHeadlessTests.CapChaos
{
    /// <summary>
    /// The shipped level content (Assets/CapsChaos/Content/LevelConfig) passes V1–V6 and the play order is
    /// consistent. Derived from the folder, so a level added tomorrow is covered without editing this file.
    /// </summary>
    public sealed class ContentLevelsTests
    {
        private static string Dir => RepoLayout.Path("Assets", "CapsChaos", "Content", "LevelConfig");

        private static IEnumerable<string> LevelFiles() =>
            Directory.Exists(Dir)
                ? Directory.GetFiles(Dir, "level_*.json").OrderBy(f => f, System.StringComparer.Ordinal).Select(Path.GetFileName)!
                : Enumerable.Empty<string>();

        [Test]
        public void There_is_content()
        {
            Assert.That(LevelFiles(), Is.Not.Empty, "no level_*.json under " + Dir + " — run `dotnet run --project Tools/LevelTool -- generate`");
        }

        [TestCaseSource(nameof(LevelFiles))]
        public void Level_is_valid_and_provably_solvable(string file)
        {
            var parsed = LevelJson.Parse(File.ReadAllText(Path.Combine(Dir, file)));
            Assert.That(parsed.Errors, Is.Empty, "V1");
            Assert.That(parsed.Level!.Id, Is.EqualTo(Path.GetFileNameWithoutExtension(file)), "V1 id = file name");
            Assert.That(LevelValidator.Validate(parsed.Level), Is.Empty, "V2–V5");
            var proof = LevelSolver.Prove(parsed.Level);
            Assert.That(proof.Status, Is.EqualTo(SolveStatus.Solvable), $"V6 ({proof.NodesExplored} nodes)");
        }

        [Test]
        public void The_index_lists_every_level_exactly_once()
        {
            var index = JsonReader.Parse(File.ReadAllText(Path.Combine(Dir, "levels.index.json")));
            Assert.That(index.TryGet("order", out var order), Is.True);
            var listed = order.Items.Select(i => i.String).ToList();
            var onDisk = LevelFiles().Select(Path.GetFileNameWithoutExtension).ToList();
            Assert.That(listed, Is.Unique);
            Assert.That(listed, Is.EquivalentTo(onDisk));
        }
    }
}
