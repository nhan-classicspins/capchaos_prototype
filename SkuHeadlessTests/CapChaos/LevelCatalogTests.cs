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
        private static string Dir => RepoLayout.Path("Assets", "CapsChaos", "Content", "LevelConfig");

        /// <summary>What LevelConfigNode does at boot, with the disk standing in for Addressables.</summary>
        private static LevelCatalog FromDisk()
        {
            var order = LevelCatalog.ParseOrder(File.ReadAllText(Path.Combine(Dir, LevelCatalog.IndexFile)));
            var texts = order.Where(id => File.Exists(Path.Combine(Dir, id + ".json")))
                             .ToDictionary(id => id, id => File.ReadAllText(Path.Combine(Dir, id + ".json")));
            var conveyors = texts.Values.Select(LevelJson.ConveyorIdOf).Distinct()
                                 .ToDictionary(id => id!, id => File.ReadAllText(Path.Combine(Dir, ConveyorJson.FileOf(id!))));
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
                ["bad_8"] = "{ \"formatVersion\": 1, \"id\": \"bad_8\", \"rows\": 8, \"pickRows\": 1, \"feeders\": [ { \"mergeAt\": 0 } ] }",
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

            var broken = new Dictionary<string, string> { ["test_8_1f"] = "{ \"formatVersion\": 1, \"id\": \"other\", \"rows\": 8, \"pickRows\": 1, \"feeders\": [] }" };
            var e = Assert.Throws<LevelLoadException>(() => new LevelCatalog().Populate(LevelCatalog.ParseOrder(Index), texts, broken));
            Assert.That(e!.Message, Does.Contain("Conveyors/test_8_1f.json: file declares id 'other'"));
        }

        [TestCase("{ \"order\": [] }", "lists no levels")]
        [TestCase("{ \"levels\": [] }", "'order' array missing")]
        [TestCase("{ \"order\": [1] }", "must be strings")]
        public void A_bad_index_is_refused(string json, string expected)
        {
            var e = Assert.Throws<LevelLoadException>(() => LevelCatalog.ParseOrder(json));
            Assert.That(e!.Message, Does.Contain(expected));
        }
    }
}
