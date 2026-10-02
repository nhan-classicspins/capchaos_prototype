using System.Collections.Generic;
using System.IO;
using System.Linq;
using Game.Application;
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
            var c = new LevelCatalog();
            c.Populate(order, texts);
            return c;
        }

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
                new LevelCatalog().Populate(LevelCatalog.ParseOrder(Index), new Dictionary<string, string>()));
            Assert.That(e!.Message, Does.Contain("level_0001: listed in the index but not found"));
        }

        [Test]
        public void An_invalid_level_is_refused_with_every_problem_and_the_catalog_stays_empty()
        {
            var texts = new Dictionary<string, string>
            {
                ["level_0001"] = "{ \"formatVersion\": 3, \"id\": \"level_0001\", \"colors\": [1], " +
                                 "\"loop\": { \"rows\": 8, \"pickRows\": 1, \"feeders\": [ { \"mergeAt\": 0, \"bottles\": [1, 1] } ] }, " +
                                 "\"lanes\": [[{ \"color\": 1 }]] }",
            };
            var c = new LevelCatalog();
            var e = Assert.Throws<LevelLoadException>(() => c.Populate(LevelCatalog.ParseOrder(Index), texts));
            Assert.That(e!.Message, Does.Contain("V8").And.Contains("V4"));
            Assert.That(c.IsLoaded, Is.False, "all-or-nothing");
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
