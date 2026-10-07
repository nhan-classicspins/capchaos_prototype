using System.Collections.Generic;
using System.IO;
using System.Linq;
using Game.Domain;
using NUnit.Framework;

namespace CapsChaos.SkuHeadlessTests.CapChaos
{
    /// <summary>
    /// The shipped level content (Assets/CapsChaos/Content/Configs/LevelConfig) passes V1–V9 and the play order is
    /// consistent; every shared conveyor in <c>Conveyors/</c> is valid and named by its file. Derived from the folders,
    /// so a level or conveyor added tomorrow is covered without editing this file.
    /// </summary>
    public sealed class ContentLevelsTests
    {
        private static string Dir => RepoLayout.Path("Assets", "CapsChaos", "Content", "Configs", "LevelConfig");

        private static IEnumerable<string> LevelFiles() =>
            Directory.Exists(Dir)
                ? Directory.GetFiles(Dir, "level_*.json").OrderBy(f => f, System.StringComparer.Ordinal).Select(Path.GetFileName)!
                : Enumerable.Empty<string>();

        private static string ConveyorDir => Path.Combine(Dir, "..", ConveyorJson.Folder);

        private static IEnumerable<string> ConveyorFiles() =>
            Directory.Exists(ConveyorDir)
                ? Directory.GetFiles(ConveyorDir, "*.json").OrderBy(f => f, System.StringComparer.Ordinal).Select(Path.GetFileName)!
                : Enumerable.Empty<string>();

        private static ConveyorLibrary Library() =>
            new ConveyorLibrary(ConveyorFiles().Select(f => ConveyorJson.Parse(File.ReadAllText(Path.Combine(ConveyorDir, f))).Conveyor)
                                               .Where(c => c != null)!);

        [TestCaseSource(nameof(ConveyorFiles))]
        public void Conveyor_is_valid_and_named_by_its_file(string file)
        {
            var parsed = ConveyorJson.Parse(File.ReadAllText(Path.Combine(ConveyorDir, file)));
            Assert.That(parsed.Errors, Is.Empty, "V1");
            Assert.That(parsed.Conveyor!.Id, Is.EqualTo(Path.GetFileNameWithoutExtension(file)), "V1 id = file name");
            Assert.That(ConveyorValidator.Validate(parsed.Conveyor), Is.Empty, "V8–V9");
        }

        [Test]
        public void There_is_content()
        {
            Assert.That(LevelFiles(), Is.Not.Empty, "no level_*.json under " + Dir + " — run `dotnet run --project Tools/LevelTool -- generate`");
        }

        [TestCaseSource(nameof(LevelFiles))]
        public void Level_is_valid_and_provably_solvable(string file)
        {
            var parsed = LevelJson.Parse(File.ReadAllText(Path.Combine(Dir, file)), Library());
            Assert.That(parsed.Errors, Is.Empty, "V1");
            Assert.That(parsed.Level!.Id, Is.EqualTo(Path.GetFileNameWithoutExtension(file)), "V1 id = file name");
            Assert.That(LevelValidator.Validate(parsed.Level), Is.Empty, "V2–V5, V7–V9");
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
