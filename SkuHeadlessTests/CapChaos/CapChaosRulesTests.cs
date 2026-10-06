using System.Collections.Generic;
using System.Linq;
using Game.Domain;
using NUnit.Framework;
using static CapsChaos.SkuHeadlessTests.CapChaos.LevelBuilder;

namespace CapsChaos.SkuHeadlessTests.CapChaos
{
    /// <summary>GDD §5 — one test per rule, named after it. Belts are 8 rows with a 1-row pick zone unless a test says
    /// otherwise; row r starts at track position r.</summary>
    public sealed class CapChaosRulesTests
    {
        private static IReadOnlyList<GameFact> Steps(CapChaosGame g, int n)
        {
            var all = new List<GameFact>();
            for (int i = 0; i < n; i++) all.AddRange(g.Step());
            return all;
        }

        [Test]
        public void R1_the_belt_moves_one_row_per_step_and_every_row_keeps_its_bottles()
        {
            var g = new CapChaosGame(Level(new[] { "RB", "..", "OG" }, new[] { "Y" }, colors: "ROBGY"));
            var belt = g.Belt;
            Assert.That(belt.RowAt(0), Is.EqualTo(0));
            g.Step();
            Assert.That(belt.PositionOf(0), Is.EqualTo(1), "row 0 moved on");
            Assert.That(belt.RowAt(0), Is.EqualTo(7), "the last row came round to position 0");
            Assert.That(Rows(belt), Does.StartWith("RB .. OG"), "a row is a piece of belt: its bottles go with it");
            Steps(g, 7);
            Assert.That(belt.Offset, Is.EqualTo(0), "a full turn");
        }

        [Test]
        public void R2_only_bottles_in_the_pick_zone_are_picked_so_a_tray_waits_for_its_colour_to_come_round()
        {
            // the R row starts at position 5; the pick zone is position 0
            var g = new CapChaosGame(Level(new[] { "OOOO", "....", "....", "....", "....", "RRRR" }, new[] { "R", "O" }));
            var tap = Trace(g.Tap(0).Facts);
            Assert.That(tap, Is.EqualTo("place(L0->S0:R) advance(L0:0)"), "no R in the pick zone yet — the tray waits");
            Assert.That(Trace(Steps(g, 2)), Is.Empty, "rows 7 and 6 pass position 0 empty");
            Assert.That(Trace(g.Step()), Is.EqualTo(
                "pick(5,0->S0:R) cap(S0:1) pick(5,1->S0:R) cap(S0:2) pick(5,2->S0:R) cap(S0:3) pick(5,3->S0:R) cap(S0:4) pack(S0:R)"),
                "the R row reaches position 0: the whole row flies, tracks left→right");
        }

        [Test]
        public void R3_a_picked_bottle_leaves_an_empty_spot_that_travels_with_the_belt()
        {
            var g = new CapChaosGame(Level(new[] { "RO" }, new[] { "R", "O" }, capacity: 1));
            Assert.That(Trace(g.Tap(0).Facts), Does.Contain("pick(0,0->S0:R)"));
            Assert.That(Rows(g.Belt), Does.StartWith(".O"), "nothing slides into the hole");
            g.Step();
            Assert.That(g.Belt.At(0, 0), Is.EqualTo(CapColor.None), "the hole moved on with row 0");
            Assert.That(g.Belt.At(0, 1), Is.EqualTo(CapColor.Orange));
        }

        [Test]
        public void R4_a_feeder_puts_a_whole_row_on_the_free_belt_row_nearest_its_entrance()
        {
            // a full belt except row 3 (one hole) and row 2 (empty); the feeder's entrance is position 6 (looks at 4..8)
            var g = new CapChaosGame(Level(
                new[] { "YY", "YY", "..", "Y.", "YY", "YY", "YY", "YY" }, new[] { "Y" }, colors: "ROY",
                feeders: new[] { "RROO" }, mergeAt: new[] { 6 }, capacity: 1));
            Assert.That(Trace(g.Step()), Is.Empty, "positions 4..7 hold rows 3..6: none has room on both tracks (8 = the pick zone)");
            Assert.That(Trace(g.Step()), Is.EqualTo("feed(F0.0->2:R) feed(F0.1->2:R)"),
                "row 2 reaches position 4, two before the entrance: the front queue row steps on whole");
            Assert.That(g.Belt.FeederRemaining(0, 0), Is.EqualTo(1));
            Assert.That(g.Belt.FeederAt(0, 1, 0), Is.EqualTo(CapColor.Orange), "the next queue row moved up");
        }

        [Test]
        public void R4_the_row_at_the_entrance_wins_then_the_one_about_to_arrive()
        {
            // rows 1..3 empty; after one step they stand at positions 2..4 round an entrance at 3
            var at = new CapChaosGame(Level(new[] { "YY", "..", "..", "..", "YY", "YY", "YY", "YY" }, new[] { "Y" }, colors: "RY",
                feeders: new[] { "RR" }, mergeAt: new[] { 3 }, capacity: 1));
            Assert.That(Trace(at.Step()), Is.EqualTo("feed(F0.0->2:R) feed(F0.1->2:R)"), "row 2 is at the entrance itself");
            // only rows 1 and 3 empty: position 2 (upstream, about to pass) beats position 4 (just gone by)
            var near = new CapChaosGame(Level(new[] { "YY", "..", "YY", "..", "YY", "YY", "YY", "YY" }, new[] { "Y" }, colors: "RY",
                feeders: new[] { "RR" }, mergeAt: new[] { 3 }, capacity: 1));
            Assert.That(Trace(near.Step()), Is.EqualTo("feed(F0.0->1:R) feed(F0.1->1:R)"));
        }

        [Test]
        public void R4_without_authored_rows_the_belt_starts_empty_and_the_feeders_fill_it_while_it_runs()
        {
            var l = Level(new string[0], new[] { "R" }, feeders: new[] { "RRRROOOO" }, rows: 8, mergeAt: new[] { 7 }, colors: "RO");
            var g = new CapChaosGame(l);
            Assert.That(g.Belt.Count, Is.Zero);
            Assert.That(g.Belt.FeederRemainingTotal, Is.EqualTo(8));
            Steps(g, 2);
            // after the first step row 6 stands at the merge point (position 7) and takes the first feeder row; row 5 the next
            Assert.That(Rows(g.Belt), Is.EqualTo(".... .... .... .... .... OOOO RRRR ...."));
        }

        [Test]
        public void R6_R7_a_tap_moves_the_front_tray_to_the_left_most_free_slot_and_advances_the_lane()
        {
            var g = new CapChaosGame(Level(new[] { "....", "OOOO", "BBBB" }, new[] { "BO" }));
            var r = g.Tap(0);
            Assert.That(r.Outcome, Is.EqualTo(TapOutcome.Accepted));
            Assert.That(Trace(r.Facts), Does.StartWith("place(L0->S0:B) advance(L0:1)"));
            Assert.That(g.LaneFront(0), Is.EqualTo(CapColor.Orange));
        }

        [Test]
        public void R11_the_zone_is_read_front_most_row_first_and_each_bottle_goes_to_the_left_most_waiting_slot()
        {
            // pick zone = positions 0..1; position 1 leaves the zone first
            var g = new CapChaosGame(Level(new[] { "RO", "OR" }, new[] { "O", "R" }, capacity: 2, pickRows: 2));
            Assert.That(Trace(g.Tap(0).Facts), Is.EqualTo(
                "place(L0->S0:O) advance(L0:0) pick(1,0->S0:O) cap(S0:1) pick(0,1->S0:O) cap(S0:2) pack(S0:O)"));
            Assert.That(Trace(g.Tap(1).Facts), Is.EqualTo(
                "place(L1->S0:R) advance(L1:0) pick(1,1->S0:R) cap(S0:1) pick(0,0->S0:R) cap(S0:2) pack(S0:R) WIN"));
        }

        [Test]
        public void R14_the_round_is_won_when_the_belt_the_feeders_and_the_slots_are_empty()
        {
            var g = new CapChaosGame(Level(new[] { "RR" }, new[] { "R", "R" }, capacity: 2, feeders: new[] { "RR" }, mergeAt: new[] { 4 }));
            Assert.That(Trace(g.Tap(0).Facts), Does.Not.Contain("WIN"));
            var rest = Trace(g.Tap(1).Facts) + " " + Trace(g.Settle());
            Assert.That(rest, Does.EndWith("pack(S0:R) WIN"));
            Assert.That(g.Status, Is.EqualTo(GameStatus.Won));
        }

        [Test]
        public void R15_full_slots_are_not_a_loss_while_a_matching_bottle_is_still_coming_round()
        {
            var g = new CapChaosGame(Level(new[] { "O", ".", ".", "R" }, new[] { "R", "O" }, slots: 1, capacity: 1));
            Assert.That(Trace(g.Tap(0).Facts), Is.EqualTo("place(L0->S0:R) advance(L0:0)"));
            Assert.That(g.Status, Is.EqualTo(GameStatus.Playing), "the R is on the belt: it will reach the zone");
            Assert.That(Trace(g.Settle()), Is.EqualTo("pick(3,0->S0:R) cap(S0:1) pack(S0:R)"));
        }

        [Test]
        public void R15_three_jammed_slots_lose_when_no_bottle_on_the_belt_matches_and_no_feeder_can_move()
        {
            // the belt is full of blue; the orange / green / red bottles wait in the feeder behind it
            var l = Level(new[] { "BB", "BB", "BB", "BB", "BB", "BB", "BB", "BB" }, new[] { "O", "G", "RB" }, capacity: 1,
                feeders: new[] { "OGRB" }, mergeAt: new[] { 5 });
            var g = new CapChaosGame(l);
            g.Tap(0); g.Tap(1);
            var r = g.Tap(2);
            Assert.That(Trace(r.Facts), Does.EndWith("FAIL(SlotsJammed)"));
            Assert.That(g.Status, Is.EqualTo(GameStatus.Lost));
            Assert.That(g.Tap(2).Outcome, Is.EqualTo(TapOutcome.RejectedGameOver));
            Assert.That(g.Step(), Is.Empty, "a finished round does not move");
        }

        [TestCase(1)]
        [TestCase(3)]
        [TestCase(5)]
        public void R15_the_level_config_decides_how_many_waiting_trays_fit_before_a_jam(int slots)
        {
            // only blue on the belt; every tray is a colour that never comes round, so each one waits
            var l = Level(new[] { "BBBBB" }, new[] { "ROGYP" }, slots: slots, capacity: 1);
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
        public void Settle_steps_until_nothing_can_change_without_a_tap()
        {
            var g = new CapChaosGame(Level(new[] { "R", ".", ".", ".", "O" }, new[] { "O", "R" }, slots: 2, capacity: 1));
            g.Tap(0);
            Assert.That(g.IsQuiescent, Is.False, "the O is coming round");
            Assert.That(Trace(g.Settle()), Is.EqualTo("pick(4,0->S0:O) cap(S0:1) pack(S0:O)"));
            Assert.That(g.IsQuiescent, Is.True);
            Assert.That(g.Settle(), Is.Empty);
        }

        [Test]
        public void Slots_are_read_from_the_level_json()
        {
            var level = Level(new[] { "RRRR" }, new[] { "R" }, slots: 5);
            var json = LevelJson.Write(level);
            Assert.That(json, Does.Contain("\"slots\": 5"));
            var game = new CapChaosGame(LevelJson.Parse(json, Conveyors(level)).Level!);
            Assert.That(game.SlotCount, Is.EqualTo(5));
        }

        [Test]
        public void R5_invalid_taps_are_rejected_without_facts()
        {
            var g = new CapChaosGame(Level(new[] { "RRRR", "OOOO" }, new[] { "R", "O" }));
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
                var g = new CapChaosGame(Level(new[] { "ROBG", "GBOR" }, new[] { "RB", "OG", "RBOG" }, capacity: 2,
                    feeders: new[] { "ROBGROBG" }, mergeAt: new[] { 5 }));
                return string.Join("|", new[] { 0, 1, 2, 0, 1, 2, 2, 2 }.Select(i => Trace(g.Tap(i).Facts) + Trace(Steps(g, 3))));
            }
            Assert.That(Play(), Is.EqualTo(Play()));
        }

        [Test]
        public void R20_locked_slots_take_no_tray_and_running_out_offers_one_instead_of_losing()
        {
            var g = new CapChaosGame(Level(new[] { "BBBB" }, new[] { "R", "O", "G" }, slots: 1, extraSlots: 2, capacity: 1));
            Assert.That(g.SlotCount, Is.EqualTo(3));
            Assert.That(g.LockedSlotCount, Is.EqualTo(2));
            Assert.That(Trace(g.Tap(0).Facts), Is.EqualTo("place(L0->S0:R) advance(L0:0) RANOUT"),
                "the only open slot is jammed, but a slot can still be opened: not a loss");
            Assert.That(g.Status, Is.EqualTo(GameStatus.Playing));
            Assert.That(g.SlotsRanOut, Is.True);
            Assert.That(g.Step(), Is.Empty, "the stall is reported once");
            Assert.That(g.Tap(1).Outcome, Is.EqualTo(TapOutcome.RejectedNoFreeSlot), "a locked slot takes no tray");

            Assert.That(Trace(g.UnlockSlot()), Is.EqualTo("unlock(S1)"));
            Assert.That(g.SlotsRanOut, Is.False);
            Assert.That(Trace(g.Tap(1).Facts), Is.EqualTo("place(L1->S1:O) advance(L1:0) RANOUT"));
            g.UnlockSlot();
            Assert.That(Trace(g.Tap(2).Facts), Does.EndWith("FAIL(SlotsJammed)"), "no slot left to open: R15");
        }

        [Test]
        public void R20_V6_proves_a_level_with_the_open_slots_only()
        {
            // one open slot: the O tray jams it for good unless a paid slot opens — that is not a proof
            var l = Level(new[] { "R", "R", "R", "R", "R", "R", "R", "R" }, new[] { "OR" }, slots: 1, extraSlots: 2, capacity: 8,
                feeders: new[] { "OOOOOOOO" }, mergeAt: new[] { 4 }, colors: "RO");
            Assert.That(LevelSolver.Solve(l).Status, Is.EqualTo(SolveStatus.Unsolvable));
        }

        [Test]
        public void Clones_are_independent()
        {
            var a = new CapChaosGame(Level(new[] { "RRRR" }, new[] { "R" }));
            var b = a.Clone();
            a.Tap(0);
            a.Step();
            Assert.That(b.Belt.Count, Is.EqualTo(4));
            Assert.That(b.Belt.Offset, Is.EqualTo(0));
            Assert.That(b.Status, Is.EqualTo(GameStatus.Playing));
            Assert.That(b.StateKey(), Is.Not.EqualTo(a.StateKey()));
        }

        [Test]
        public void R23_a_feeder_counts_the_queue_rows_that_joined_the_belt()
        {
            var g = new CapChaosGame(Level(new[] { ".." }, new[] { "R", "O" }, capacity: 2, feeders: new[] { "RROO" }));
            Assert.That(g.Belt.FeederRowsJoined(0), Is.EqualTo(0));
            for (int i = 0; i < 16 && !g.Step().OfType<BottleFed>().Any(); i++) { }
            Assert.That(g.Belt.FeederRowsJoined(0), Is.EqualTo(1), "a whole queue row joins at once");
            Assert.That(g.Belt.FeederAt(0, 0, 0), Is.EqualTo(CapColor.Orange), "depth 0 is now queue row 1");
        }

        [Test]
        public void A_step_without_feeding_turns_the_belt_but_no_feeder_joins()
        {
            var g = new CapChaosGame(Level(new[] { ".." }, new[] { "R" }, capacity: 2, feeders: new[] { "RR" }));
            int offset = g.Belt.Offset;
            Assert.That(g.Step(feed: false).OfType<BottleFed>(), Is.Empty);
            Assert.That(g.Belt.Offset, Is.Not.EqualTo(offset), "the belt still turns");
            Assert.That(g.IsQuiescent, Is.False, "a feeder that could join keeps the board from counting as quiet");
            Assert.That(g.Status, Is.EqualTo(GameStatus.Playing));
            Assert.That(g.Step().OfType<BottleFed>(), Is.Not.Empty, "feeding again: the queue joins");
        }
    }
}
