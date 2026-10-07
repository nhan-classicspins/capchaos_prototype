using System.Collections.Generic;
using System.IO;
using System.Linq;
using Game.Application;
using Game.Domain;
using NUnit.Framework;

namespace CapsChaos.SkuHeadlessTests.CapChaos
{
    public sealed class LevelCatalogTests
    {
        private static string Dir => RepoLayout.Path("Assets", "CapsChaos", "Content", "Configs", "LevelConfig");

        /// <summary>What LevelConfigNode does at boot, with the disk standing in for Addressables.</summary>
        private static LevelCatalog FromDisk()
        {
            var order = LevelCatalog.ParseOrder(File.ReadAllText(Path.Combine(Dir, LevelCatalog.IndexFile)));
            var texts = order.Where(id => File.Exists(Path.Combine(Dir, id + ".json")))
                             .ToDictionary(id => id, id => File.ReadAllText(Path.Combine(Dir, id + ".json")));
            var conveyors = texts.Values.Select(LevelJson.ConveyorIdOf).Distinct()
                                 .ToDictionary(id => id!, id => File.ReadAllText(Path.Combine(Dir, "..", ConveyorJson.FileOf(id!))));
            var c = new LevelCatalog();
            c.Populate(order, texts, conveyors);
            return c;
        }

        private static Dictionary<string, string> NoConveyors => new Dictionary<string, string>();

        private const string Index = "{ \"order\": [\"level_0001\"] }";

        [Test]
        public void Loads_every_shipped_level_up_front_in_index_order_and_wraps_past_the_end()
        {
            var c = FromDisk();
            Assert.That(c.IsLoaded, Is.True);
            Assert.That(c.Count, Is.EqualTo(c.Order.Count).And.GreaterThan(0));
            Assert.That(c.Get(0).Id, Is.EqualTo(c.Order[0]));
            Assert.That(c.Get(c.Count).Id, Is.EqualTo(c.Order[0]), "next after the last level wraps");
            Assert.That(c.Normalize(-1), Is.EqualTo(c.Count - 1));
        }

        [Test]
        public void Get_before_the_boot_load_is_refused()
        {
            Assert.Throws<LevelLoadException>(() => new LevelCatalog().Get(0));
        }

        [Test]
        public void A_missing_level_is_refused_by_name()
        {
            var e = Assert.Throws<LevelLoadException>(() =>
                new LevelCatalog().Populate(LevelCatalog.ParseOrder(Index), new Dictionary<string, string>(), NoConveyors));
            Assert.That(e!.Message, Does.Contain("level_0001: listed in the index but not found"));
        }

        [Test]
        public void An_invalid_level_is_refused_with_every_problem_and_the_catalog_stays_empty()
        {
            var texts = new Dictionary<string, string>
            {
                ["level_0001"] = "{ \"formatVersion\": 4, \"id\": \"level_0001\", \"conveyor\": \"bad_8\", \"colors\": [1], " +
                                 "\"feeders\": [ { \"bottles\": [1, 1] } ], \"lanes\": [[{ \"color\": 1 }]] }",
            };
            var conveyors = new Dictionary<string, string>
            {
                ["bad_8"] = ConveyorJsonTests.Text("bad_8", 8, 1, new[] { 0, 5, 6 }),
            };
            var c = new LevelCatalog();
            var e = Assert.Throws<LevelLoadException>(() => c.Populate(LevelCatalog.ParseOrder(Index), texts, conveyors));
            Assert.That(e!.Message, Does.Contain("V8").And.Contains("V4"));
            Assert.That(c.IsLoaded, Is.False, "all-or-nothing");
        }

        [Test]
        public void A_missing_or_broken_conveyor_is_refused_by_file_and_by_the_levels_naming_it()
        {
            var texts = new Dictionary<string, string> { ["level_0001"] = LevelJsonTests.Minimal.Replace("level_0007", "level_0001") };
            var missing = Assert.Throws<LevelLoadException>(() => new LevelCatalog().Populate(LevelCatalog.ParseOrder(Index), texts, NoConveyors));
            Assert.That(missing!.Message, Does.Contain("level_0001: $.conveyor: 'test_8_1f' is not a conveyor"));

            var broken = new Dictionary<string, string> { ["test_8_1f"] = ConveyorJsonTests.Text("other", 8, 1, new[] { 6, 5, 4 }) };
            var e = Assert.Throws<LevelLoadException>(() => new LevelCatalog().Populate(LevelCatalog.ParseOrder(Index), texts, broken));
            Assert.That(e!.Message, Does.Contain("ConveyorConfig/test_8_1f.json: file declares id 'other'"));
        }

        [TestCase("{ \"order\": [] }", "lists no levels")]
        [TestCase("{ \"levels\": [] }", "'order' array missing")]
        [TestCase("{ \"order\": [1] }", "must be strings")]
        public void A_bad_index_is_refused(string json, string expected)
        {
            var e = Assert.Throws<LevelLoadException>(() => LevelCatalog.ParseOrder(json));
            Assert.That(e!.Message, Does.Contain(expected));
        }
    
        // ── resync from the sheet (LevelCatalog.Override) ───────────────────────────────────
        private static string Shipped(string id) => File.ReadAllText(Path.Combine(Dir, id + ".json"));

        [Test]
        public void A_resync_replaces_a_level_with_its_newer_text()
        {
            var c = FromDisk();
            string id = c.Order[0];
            string text = Shipped(id).Replace("\"difficulty\": \"" + c.Get(0).Difficulty + "\"", "\"difficulty\": \"breather\"");
            var report = c.Override(new Dictionary<string, string> { [id] = text });
            Assert.That(report.Changed, Is.EqualTo(new[] { id }));
            Assert.That(report.Problems, Is.Empty);
            Assert.That(c.Get(0).Difficulty, Is.EqualTo("breather"));
        }

        [Test]
        public void A_resync_with_the_same_text_changes_nothing()
        {
            var c = FromDisk();
            string id = c.Order[0];
            var before = c.Get(0);
            var report = c.Override(new Dictionary<string, string> { [id] = Shipped(id).Replace("\n", "\r\n") + "\n" });
            Assert.That(report.Changed, Is.Empty);
            Assert.That(report.Unchanged, Is.EqualTo(1));
            Assert.That(c.Get(0), Is.SameAs(before));
        }

        [Test]
        public void A_bad_cell_is_refused_and_keeps_its_level_while_the_others_still_sync()
        {
            var c = FromDisk();
            string a = c.Order[0], b = c.Order[1];
            var before = c.Get(0);
            string good = Shipped(b).Replace("\"difficulty\": \"" + c.Get(1).Difficulty + "\"", "\"difficulty\": \"hard\"");
            var report = c.Override(new Dictionary<string, string>
            {
                [a] = "{ not json",
                [b] = good,
                ["level_9999"] = Shipped(a),
                [c.Order[2]] = Shipped(a),
            });
            Assert.That(c.Get(0), Is.SameAs(before), "the broken cell's level keeps what it had");
            Assert.That(report.Changed, Is.EqualTo(new[] { b }));
            Assert.That(c.Get(1).Difficulty, Is.EqualTo("hard"));
            Assert.That(report.Problems, Has.Some.StartsWith(a + ":"));
            Assert.That(report.Problems, Has.Some.Contains("level_9999: not in levels.index.json"));
            Assert.That(report.Problems, Has.Some.Contains($"{c.Order[2]}: the text declares id '{a}'"));
            Assert.That(c.Order, Is.EqualTo(FromDisk().Order), "the play order never changes");
        }

        private static string ShippedConveyor(string id) => File.ReadAllText(Path.Combine(Dir, "..", ConveyorJson.FileOf(id)));
        private static readonly Dictionary<string, string> NoLevels = new Dictionary<string, string>();

        [Test]
        public void A_resynced_conveyor_relays_every_level_on_it()
        {
            var c = FromDisk();
            string id = c.Get(0).Conveyor.Id!;
            double scale = c.Get(0).Conveyor.Scale;
            string text = System.Text.RegularExpressions.Regex.Replace(ShippedConveyor(id), "\"scale\": *[0-9.]+", "\"scale\": 0.5");
            Assume.That(text, Does.Contain("\"scale\": 0.5"), "the shipped conveyor carries a scale to change");
            var report = c.Override(NoLevels, new Dictionary<string, string> { [id] = text });
            Assert.That(report.Problems, Is.Empty);
            Assert.That(report.ChangedConveyors, Is.EqualTo(new[] { id }));
            for (int i = 0; i < c.Count; i++)
                if (c.Get(i).Conveyor.Id == id) Assert.That(c.Get(i).Conveyor.Scale, Is.EqualTo(0.5), c.Order[i]);
            Assert.That(scale, Is.Not.EqualTo(0.5));
        }

        [Test]
        public void A_conveyor_with_the_same_text_or_a_broken_one_changes_nothing()
        {
            var c = FromDisk();
            string id = c.Get(0).Conveyor.Id!;
            var before = c.Get(0);
            string elsewhere = ShippedConveyor(id).Replace("\"$schema\": \"../", "\"$schema\": \"../../") + "\n";
            var report = c.Override(NoLevels, new Dictionary<string, string> { [id] = elsewhere });
            Assert.That(report.ChangedConveyors, Is.Empty);
            Assert.That(report.Unchanged, Is.EqualTo(1));
            report = c.Override(NoLevels, new Dictionary<string, string> { [id] = "{ \"id\": \"" + id + "\" }" });
            Assert.That(report.ChangedConveyors, Is.Empty);
            Assert.That(report.Problems, Has.Some.StartsWith(ConveyorJson.FileOf(id) + ":"));
            Assert.That(c.Get(0), Is.SameAs(before), "a refused conveyor keeps its levels as they were");
        }

        [Test]
        public void A_conveyor_that_would_break_a_level_on_it_is_refused_by_name()
        {
            var c = FromDisk();
            string id = c.Get(0).Conveyor.Id!;
            // fewer rows than the level's own belt content needs, but still a well-formed layout
            string text = System.Text.RegularExpressions.Regex.Replace(ShippedConveyor(id), "\"width\": *[0-9]+", "\"width\": 1");
            var report = c.Override(NoLevels, new Dictionary<string, string> { [id] = text });
            Assume.That(report.Problems, Is.Not.Empty, "width 1 breaks the levels on it");
            Assert.That(report.ChangedConveyors, Is.Empty);
            Assert.That(report.Problems.Single(), Does.Contain("refused — it would break").Or.StartsWith(ConveyorJson.FileOf(id)));
        }

        [Test]
        public void A_new_conveyor_from_the_sheet_can_carry_a_level_synced_with_it()
        {
            var c = FromDisk();
            string from = c.Get(0).Conveyor.Id!, fresh = "sheet_copy";
            string conveyor = ShippedConveyor(from).Replace("\"id\": \"" + from + "\"", "\"id\": \"" + fresh + "\"");
            string level = Shipped(c.Order[0]).Replace("\"conveyor\": \"" + from + "\"", "\"conveyor\": \"" + fresh + "\"");
            var report = c.Override(new Dictionary<string, string> { [c.Order[0]] = level },
                                    new Dictionary<string, string> { [fresh] = conveyor });
            Assert.That(report.Problems, Is.Empty);
            Assert.That(report.ChangedConveyors, Is.EqualTo(new[] { fresh }));
            Assert.That(c.Get(0).Conveyor.Id, Is.EqualTo(fresh));
        }
}
}
