using System.Collections.Generic;
using System.IO;
using Game.Application;
using NUnit.Framework;

namespace CapsChaos.SkuHeadlessTests.CapChaos
{
    public sealed class LevelCatalogTests
    {
        private sealed class DiskSource : ILevelSource
        {
            private static string Dir => RepoLayout.Path("Assets", "CapsChaos", "Content", "Resources", "Levels");
            public string ReadIndex() => File.ReadAllText(Path.Combine(Dir, "levels.index.json"));
            public string ReadLevel(string id) { var p = Path.Combine(Dir, id + ".json"); return File.Exists(p) ? File.ReadAllText(p) : null; }
        }

        private sealed class MemorySource : ILevelSource
        {
            public string Index = "{ \"order\": [\"level_0001\"] }";
            public readonly Dictionary<string, string> Levels = new Dictionary<string, string>();
            public string ReadIndex() => Index;
            public string ReadLevel(string id) => Levels.TryGetValue(id, out var t) ? t : null;
        }

        [Test]
        public void Loads_the_shipped_levels_in_index_order_and_wraps_past_the_end()
        {
            var c = new LevelCatalog(new DiskSource());
            Assert.That(c.Count, Is.GreaterThan(0));
            Assert.That(c.Load(0).Id, Is.EqualTo(c.Order[0]));
            Assert.That(c.Load(c.Count).Id, Is.EqualTo(c.Order[0]), "next after the last level wraps");
            Assert.That(c.Normalize(-1), Is.EqualTo(c.Count - 1));
        }

        [Test]
        public void A_missing_level_is_refused_by_name()
        {
            var e = Assert.Throws<LevelLoadException>(() => new LevelCatalog(new MemorySource()).Load(0));
            Assert.That(e!.Message, Does.Contain("level_0001: listed in the index but not found"));
        }

        [Test]
        public void An_invalid_level_is_refused_with_every_problem()
        {
            var src = new MemorySource();
            src.Levels["level_0001"] = "{ \"formatVersion\": 1, \"id\": \"level_0001\", \"colors\": [\"R\"], " +
                                       "\"stack\": { \"cols\": 2, \"rows\": 1, \"layers\": [[\"Rr\"]] }, \"lanes\": [[\"R\"]] }";
            var e = Assert.Throws<LevelLoadException>(() => new LevelCatalog(src).Load(0));
            Assert.That(e!.Message, Does.Contain("V3").And.Contains("V4"));
        }
    }
}
