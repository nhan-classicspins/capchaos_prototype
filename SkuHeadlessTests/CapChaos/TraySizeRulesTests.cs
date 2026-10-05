using System.Linq;
using Game.Domain;
using NUnit.Framework;
using static CapsChaos.SkuHeadlessTests.CapChaos.LevelBuilder;

namespace CapsChaos.SkuHeadlessTests.CapChaos
{
    /// <summary>GDD R21 — container sizes: a tray takes size × trayCapacity items (S 4 · M 8 · L 12 · XL 16 at trayCapacity 4).</summary>
    public sealed class TraySizeRulesTests
    {
        [TestCase(TraySize.S, 4)]
        [TestCase(TraySize.M, 8)]
        [TestCase(TraySize.L, 12)]
        [TestCase(TraySize.XL, 16)]
        public void R21_a_tray_takes_its_size_times_the_tray_capacity(TraySize size, int items)
        {
            var l = Level(new[] { "R" }, new[] { "R" }, capacity: 4, sizes: new[] { new[] { 0, 0, (int)size } });
            Assert.That(l.CapacityOf(new TrayRef(0, 0)), Is.EqualTo(items));
            var g = new CapChaosGame(l);
            Assert.That(g.TrayCapacity(0, 0), Is.EqualTo(items));
            var placed = g.Tap(0).Facts.OfType<TrayPlaced>().Single();
            Assert.That(placed.Capacity, Is.EqualTo(items), "the fact carries it, so the board can show the count");
        }

        [Test]
        public void R21_an_M_tray_packs_only_after_twice_the_items_of_an_S()
        {
            var g = new CapChaosGame(Level(new[] { "RRR" }, new[] { "R" }, capacity: 1, sizes: new[] { new[] { 0, 0, 2 } }));
            var facts = g.Tap(0).Facts;
            var caps = facts.OfType<BottleCapped>().Select(c => c.Filled).ToArray();
            Assert.That(caps, Is.EqualTo(new[] { 1, 2 }), "two items, then full");
            Assert.That(Trace(facts), Does.Contain("cap(S0:2) pack(S0:R)").And.Not.Contain("cap(S0:1) pack"));
        }

        [Test]
        public void V4_balances_items_against_each_trays_size()
        {
            var ok = Level(new[] { "RRRRRRRR" }, new[] { "R" }, capacity: 4, sizes: new[] { new[] { 0, 0, 2 } });
            Assert.That(LevelValidator.Validate(ok).Where(e => e.StartsWith("V4")), Is.Empty, "8 items = one M tray");
            var bad = Level(new[] { "RRRRRRRR" }, new[] { "R" }, capacity: 4);
            Assert.That(LevelValidator.Validate(bad), Has.Some.Contains("V4 colour 1 (Red): 8 bottles vs 4 tray places"));
        }

        [Test]
        public void Sizes_round_trip_through_json_as_numbers()
        {
            var l = Level(new[] { "RRRR" }, new[] { "RO" }, capacity: 2, sizes: new[] { new[] { 0, 1, 4 } });
            var text = LevelJson.Write(l);
            Assert.That(text, Does.Contain("[{ \"color\": 1 }, { \"color\": 2, \"size\": 4 }]"), "S is the default and not written");
            var back = LevelJson.Parse(text);
            Assert.That(back.Errors, Is.Empty);
            Assert.That(back.Level!.SizeOf(new TrayRef(0, 1)), Is.EqualTo(TraySize.XL));
            Assert.That(back.Level.SizeOf(new TrayRef(0, 0)), Is.EqualTo(TraySize.S));
            Assert.That(LevelJson.Write(back.Level), Is.EqualTo(text));

            var broken = LevelJson.Parse(text.Replace("\"size\": 4", "\"size\": 5"));
            Assert.That(broken.Errors, Has.Some.Contains("$.lanes[0][1].size: 5 outside 1..4"));
        }

        [Test]
        public void Size_numbers_are_pinned()
        {
            Assert.That(new[] { TraySize.S, TraySize.M, TraySize.L, TraySize.XL }.Select(s => (int)s), Is.EqualTo(new[] { 1, 2, 3, 4 }),
                "designers' tools write these numbers — never renumber");
        }
    }
}
