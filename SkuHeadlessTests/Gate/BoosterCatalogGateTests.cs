using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace CapsChaos.SkuHeadlessTests.Gate
{
    /// <summary>
    /// The booster config (<c>Content/Configs/BoosterCatalog.asset</c>, a <c>Game.Views.BoosterCatalog</c>) stays true to
    /// the two things it names by string: every booster id is a resource the wallet counts (<c>game.resources.json</c>),
    /// once, and every text key is a row of <c>loc.csv</c> (read from the <c>LocKeys.gen.cs</c> codegen makes of it). Read off the asset's YAML, so a typo in the Inspector fails
    /// here instead of showing a raw key (or a count that never moves) on screen.
    /// </summary>
    [TestFixture]
    public sealed class BoosterCatalogGateTests
    {
        private static string Asset => RepoLayout.Path("Assets", "CapsChaos", "Content", "Configs", "BoosterCatalog.asset");

        private static List<Dictionary<string, string>> Boosters()
        {
            Assert.That(File.Exists(Asset), Is.True, Asset + " is missing");
            var list = new List<Dictionary<string, string>>();
            Dictionary<string, string> current = null;
            foreach (var line in File.ReadAllLines(Asset))
            {
                var m = Regex.Match(line, @"^\s*(-\s+)?(_id|_nameKey|_descriptionKey|_unlockedDescriptionKey):\s*(.*)$");
                if (!m.Success) continue;
                if (m.Groups[1].Success && m.Groups[1].Value.Length > 0) list.Add(current = new Dictionary<string, string>());
                current![m.Groups[2].Value] = m.Groups[3].Value.Trim();
            }
            return list;
        }

        [Test]
        public void Every_booster_id_is_a_wallet_resource_listed_once()
        {
            var manifest = JObject.Parse(File.ReadAllText(RepoLayout.Path("Tools", "Codegen", "Manifests", "game.resources.json")));
            var resources = manifest["entries"]!.Select(e => (string)e["value"]!).ToHashSet();
            var ids = Boosters().Select(b => b["_id"]).ToList();
            Assert.That(ids, Is.Not.Empty, "the catalog lists no booster");
            Assert.That(ids, Is.Unique);
            foreach (var id in ids)
                Assert.That(resources, Does.Contain(id), $"booster '{id}' is not a resource in game.resources.json — the wallet would never count it");
        }

        [Test]
        public void Every_booster_text_is_a_localization_key()
        {
            // the keys codegen generated from loc.csv (LocKeys.gen.cs) — the same set the game can resolve
            var keys = Regex.Matches(File.ReadAllText(RepoLayout.Path("Assets", "CapsChaos", "Gen", "LocKeys.gen.cs")),
                    "new\\(\"([^\"]+)\"\\)").Select(m => m.Groups[1].Value).ToHashSet();
            foreach (var b in Boosters())
                foreach (var field in new[] { "_nameKey", "_descriptionKey", "_unlockedDescriptionKey" })
                {
                    Assert.That(b.TryGetValue(field, out var key) && key.Length > 0, Is.True, $"booster '{b["_id"]}' has no {field}");
                    Assert.That(keys, Does.Contain(key), $"booster '{b["_id"]}' {field} '{key}' is not a localization key — add it to loc.csv and run Framework/Codegen/Generate");
                }
        }
    }
}
