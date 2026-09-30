using System.Linq;
using Game.Domain;
using NUnit.Framework;
using static CapsChaos.SkuHeadlessTests.CapChaos.LevelBuilder;

namespace CapsChaos.SkuHeadlessTests.CapChaos
{
    /// <summary>GDD §5 — one test per rule, named after it.</summary>
    public sealed class CapChaosRulesTests
    {
        [Test]
        public void R2_only_ground_front_most_visible_bottles_are_exposed()
        {
            // back row "OO", front row "R."; layer 1 hides a bottle above the front R
            var g = new CapChaosGame(Level(new[] { new[] { "OO", "R." }, new[] { "..", "o." } }, new[] { "R", "OO" }));
            var s = g.Stack;
            Assert.That(s.IsExposed(0, 0), Is.True, "front R");
            Assert.That(s.IsExposed(0, 1), Is.False, "O behind R in the same column");
            Assert.That(s.IsExposed(1, 1), Is.True, "O whose column has nothing in front");
        }

        [Test]
        public void R6_R7_a_tap_moves_the_front_tray_to_the_left_most_free_slot_and_advances_the_lane()
        {
            var g = new CapChaosGame(Level(new[] { new[] { "OOOOBBBB" } }, new[] { "BO" }));
            var r = g.Tap(0);
            Assert.That(r.Outcome, Is.EqualTo(TapOutcome.Accepted));
            Assert.That(Trace(r.Facts), Does.StartWith("place(L0->S0:B) advance(L0:1)"));
            Assert.That(g.LaneFront(0), Is.EqualTo('O'));
        }

        [Test]
        public void R10_R11_R13_a_tray_pulls_exposed_matches_front_first_then_packs_and_frees_its_slot()
        {
            var g = new CapChaosGame(Level(new[] { new[] { "RRRR" } }, new[] { "R" }));
            var t = Trace(g.Tap(0).Facts);
            Assert.That(t, Is.EqualTo(
                "place(L0->S0:R) advance(L0:0) " +
                // slot 0's centre is column (0.5 × 4/3) − 0.5 ≈ 0.17 → nearest columns first
                "pick(0,0->S0:R) cap(S0:1) pick(1,0->S0:R) cap(S0:2) pick(2,0->S0:R) cap(S0:3) pick(3,0->S0:R) cap(S0:4) " +
                "pack(S0:R) WIN"));
            Assert.That(g.Status, Is.EqualTo(GameStatus.Won));
        }

        [Test]
        public void R3_R4_taking_a_ground_bottle_drops_the_pile_and_reveals_a_hidden_one_that_lands()
        {
            // column 0: ground R, above it hidden o; column 1..3: R R R
            var g = new CapChaosGame(Level(new[] { new[] { "RRRR", "OOO." }, new[] { "....", "o..." } },
                new[] { "O", "R" }));
            // rows are back→front, so row 1 is the FRONT row: front = "OOO.", pile at (0,0) = O then hidden o
            var r = g.Tap(0);   // O tray
            var t = Trace(r.Facts);
            Assert.That(t, Does.Contain("pick(0,0->S0:O) drop(0,0:1) reveal(0,0:O)"),
                "the drop and the reveal follow the pick, in that order");
            Assert.That(r.Facts.OfType<BottleCapped>().Last().Filled, Is.EqualTo(4),
                "the revealed O is exposed at once and the same tray takes it");
        }

        [Test]
        public void R12_a_tray_waits_and_fills_later_when_matches_get_exposed()
        {
            // one column: back O, front R (capacity 1 keeps it small)
            var g = new CapChaosGame(Level(new[] { new[] { "O", "R" } }, new[] { "O", "R" }, capacity: 1));
            var first = Trace(g.Tap(0).Facts);
            Assert.That(first, Is.EqualTo("place(L0->S0:O) advance(L0:0)"), "no O is exposed yet — the tray waits");
            var second = Trace(g.Tap(1).Facts);
            Assert.That(second, Is.EqualTo(
                "place(L1->S1:R) advance(L1:0) pick(0,0->S1:R) cap(S1:1) pack(S1:R) " +
                "pick(0,1->S0:O) cap(S0:1) pack(S0:O) WIN"));
        }

        [Test]
        public void R15_three_jammed_slots_lose_like_video_1_at_76s()
        {
            // front row all blue; the orange / green / pink bottles sit behind it
            var l = Level(new[] { new[] { "OGRO", "BBBB" } }, new[] { "O", "G", "RB" }, capacity: 1);
            var g = new CapChaosGame(l);
            g.Tap(0); g.Tap(1);
            var r = g.Tap(2);
            Assert.That(Trace(r.Facts), Does.EndWith("FAIL(SlotsJammed)"));
            Assert.That(g.Status, Is.EqualTo(GameStatus.Lost));
            Assert.That(g.Tap(2).Outcome, Is.EqualTo(TapOutcome.RejectedGameOver));
        }

        [TestCase(1)]
        [TestCase(3)]
        [TestCase(5)]
        public void R15_the_level_config_decides_how_many_waiting_trays_fit_before_a_jam(int slots)
        {
            // the front row is all blue; every tray is a colour that is never exposed, so each one waits
            var l = Level(new[] { new[] { "ROGYP", "BBBBB" } }, new[] { "ROGYP" }, slots: slots, capacity: 1);
            var g = new CapChaosGame(l);
            Assert.That(g.SlotCount, Is.EqualTo(slots));
            for (int tap = 1; tap < slots; tap++)
            {
                g.Tap(0);
                Assert.That(g.Status, Is.EqualTo(GameStatus.Playing), $"tap {tap} of {slots} slots still fits");
            }
            Assert.That(Trace(g.Tap(0).Facts), Does.EndWith("FAIL(SlotsJammed)"), $"tap {slots} fills the last slot");
        }

        [Test]
        public void Slots_are_read_from_the_level_json()
        {
            var json = LevelJson.Write(Level(new[] { new[] { "RRRR" } }, new[] { "R" }, slots: 5));
            Assert.That(json, Does.Contain("\"slots\": 5"));
            var game = new CapChaosGame(LevelJson.Parse(json).Level!);
            Assert.That(game.SlotCount, Is.EqualTo(5));
        }

        [Test]
        public void R5_invalid_taps_are_rejected_without_facts()
        {
            var g = new CapChaosGame(Level(new[] { new[] { "RRRROOOO" } }, new[] { "R", "O" }));
            g.Tap(0);
            var empty = g.Tap(0);
            Assert.That(empty.Outcome, Is.EqualTo(TapOutcome.RejectedEmptyLane));
            Assert.That(empty.Facts, Is.Empty);
            Assert.That(g.Tap(7).Outcome, Is.EqualTo(TapOutcome.RejectedBadLane));
        }

        [Test]
        public void R11_the_rules_are_deterministic()
        {
            string Play()
            {
                var g = new CapChaosGame(Level(new[] { new[] { "ROBGROBG", "GBORGBOR" } }, new[] { "RB", "OG", "RBOG" }, capacity: 2));
                return string.Join("|", new[] { 0, 1, 2, 0, 1, 2, 2, 2 }.Select(i => Trace(g.Tap(i).Facts)));
            }
            Assert.That(Play(), Is.EqualTo(Play()));
        }

        [Test]
        public void Clones_are_independent()
        {
            var a = new CapChaosGame(Level(new[] { new[] { "RRRR" } }, new[] { "R" }));
            var b = a.Clone();
            a.Tap(0);
            Assert.That(b.Stack.Count, Is.EqualTo(4));
            Assert.That(b.Status, Is.EqualTo(GameStatus.Playing));
            Assert.That(b.StateKey(), Is.Not.EqualTo(a.StateKey()));
        }
    }
}
