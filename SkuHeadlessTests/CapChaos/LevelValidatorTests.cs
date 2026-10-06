using Game.Domain;
using NUnit.Framework;
using static CapsChaos.SkuHeadlessTests.CapChaos.LevelBuilder;

namespace CapsChaos.SkuHeadlessTests.CapChaos
{
    /// <summary>V4–V10 (GDD §6.4).</summary>
    public sealed class LevelValidatorTests
    {
        [Test]
        public void V8_a_hidden_feeder_row_must_exist_in_the_queue()
        {
            var conveyor = new ConveyorDefinition("test_8_1f", 8, 2, 1, new[] { 6 });
            var colors = new[] { CapColor.Red };
            var lanes = new[] { (System.Collections.Generic.IReadOnlyList<CapColor>)new[] { CapColor.Red } };
            LevelDefinition With(params int[] hidden) => new LevelDefinition("level_0001", 3, 4, colors,
                new LoopDefinition(conveyor, new[] { (System.Collections.Generic.IReadOnlyList<CapColor>)new[] { CapColor.Red, CapColor.Red, CapColor.Red, CapColor.Red } },
                    null, new[] { (System.Collections.Generic.IEnumerable<int>)hidden }), lanes);
            Assert.That(LevelValidator.Validate(With(0, 1)), Is.Empty, "4 bottles, 2 per row: rows 0 and 1");
            Assert.That(LevelValidator.Validate(With(2)), Has.Some.EqualTo("V8 feeders[0].hiddenRows: row 2 is past the queue's 2 row(s)"));
        }

        [Test]
        public void V8_a_locked_feeder_row_must_exist_in_the_queue()
        {
            var l = Level(new[] { ".." }, new[] { "R" }, capacity: 2, feeders: new[] { "RR" }, feederLocks: new[] { new[] { 0, 1, 2 } });
            Assert.That(LevelValidator.Validate(l), Has.Some.EqualTo("V8 feeders[0].lockedRows: row 1 is past the queue's 1 row(s)"));
        }

        [Test]
        public void V10_a_slot_lock_names_one_open_slot_once_and_leaves_one_open_slot_unlocked()
        {
            var bad = Level(new[] { "RRRR" }, new[] { "R" }, slots: 2, extraSlots: 1,
                slotLocks: new[] { new[] { 2, 3 }, new[] { 1, 2 }, new[] { 1, 4 } });
            var e = LevelValidator.Validate(bad);
            Assert.That(e, Has.Some.EqualTo("V10 slotLocks[0]: slot 2 is not an open slot 0..1"), "an extra slot opens for coins, not turns");
            Assert.That(e, Has.Some.EqualTo("V10 slotLocks[2]: slot 1 is locked twice"));

            var all = Level(new[] { "RRRR" }, new[] { "R" }, slots: 2, slotLocks: new[] { new[] { 0, 1 }, new[] { 1, 1 } });
            Assert.That(LevelValidator.Validate(all), Has.Some.StartsWith("V10 slotLocks: all 2 open slots are locked"));

            var ok = Level(new[] { "RRRR" }, new[] { "R" }, slots: 2, slotLocks: new[] { new[] { 1, 3 } });
            Assert.That(LevelValidator.Validate(ok), Is.Empty);
        }

        [Test]
        public void A_balanced_level_is_clean()
        {
            var l = Level(new[] { "RRRR" }, new[] { "R" });
            Assert.That(LevelValidator.Validate(l), Is.Empty);
        }

        [Test]
        public void V4_colour_balance_counts_the_belt_and_the_feeders()
        {
            var l = Level(new[] { "RRRO" }, new[] { "R" }, feeders: new[] { "RRRR" }, mergeAt: new[] { 5 });
            var e = LevelValidator.Validate(l);
            Assert.That(e, Has.Some.Contains("V4 colour 1 (Red): 7 bottles vs 4 tray places"));
            Assert.That(e, Has.Some.Contains("V4 colour 2 (Orange): 1 bottles vs 0 tray places"));
        }

        [Test]
        public void V5_undeclared_and_unused_colours()
        {
            var l = Level(new[] { "RRRR" }, new[] { "R" }, colors: "OB");
            var e = LevelValidator.Validate(l);
            Assert.That(e, Has.Some.StartsWith("V5 initial[0][0]: colour 1 (Red) is not in colors"));
            Assert.That(e, Has.Some.StartsWith("V5 colors: 2 (Orange) is declared but never used"));
        }

        [Test]
        public void V8_merge_points_lie_on_the_track_outside_the_pick_zone_and_are_not_shared()
        {
            var l = Level(new[] { "RRRR" }, new[] { "R", "R", "R", "R" }, pickRows: 1,
                feeders: new[] { "RRRR", "RRRR", "RRRR" }, mergeAt: new[] { 0, 9, 9 });
            var e = LevelValidator.Validate(l);
            Assert.That(e, Has.Some.Contains("V8 conveyor test_loop.feeders[0].mergeAt: 0 is inside the pick zone 0..0"));
            Assert.That(e, Has.Some.Contains("V8 conveyor test_loop.feeders[1].mergeAt: 9 is not a track position 0..7"));
            Assert.That(e, Has.Some.Contains("V8 conveyor test_loop.feeders[2].mergeAt: 9 is another feeder's merge point"));
        }

        [Test]
        public void V8_the_pick_zone_leaves_room_for_the_back_straight_and_the_bends()
        {
            var l = Level(new[] { "RRRR" }, new[] { "R" }, rows: 8, pickRows: 2);
            Assert.That(LevelValidator.Validate(l), Has.Some.StartsWith("V8 conveyor test_loop.pickRows: 2 pick rows need rows ≥ 10"));
        }

        [Test]
        public void V6_solver_finds_the_only_order()
        {
            // the belt is all R; the O bottles can only join through the feeder once R bottles left holes — R first
            var l = Level(new[] { "R", "R", "R", "R", "R", "R", "R", "R" }, new[] { "O", "R" }, slots: 1, capacity: 8,
                feeders: new[] { "OOOOOOOO" }, mergeAt: new[] { 4 }, colors: "RO");
            var r = LevelSolver.Solve(l);
            Assert.That(r.Status, Is.EqualTo(SolveStatus.Solvable));
            Assert.That(r.Solution, Is.EqualTo(new[] { 1, 0 }));
        }

        [Test]
        public void V6_solver_proves_unsolvable()
        {
            // same belt, one lane: the O tray comes first and jams the only slot
            var l = Level(new[] { "R", "R", "R", "R", "R", "R", "R", "R" }, new[] { "OR" }, slots: 1, capacity: 8,
                feeders: new[] { "OOOOOOOO" }, mergeAt: new[] { 4 }, colors: "RO");
            var r = LevelSolver.Solve(l);
            Assert.That(r.Status, Is.EqualTo(SolveStatus.Unsolvable));
            Assert.That(r.DeadEnds, Is.GreaterThan(0));
        }

        [Test]
        public void V6_solver_says_unknown_over_budget_rather_than_guessing()
        {
            var l = Level(new[] { "R", "R", "R", "R", "R", "R", "R", "R" }, new[] { "OR" }, slots: 1, capacity: 8,
                feeders: new[] { "OOOOOOOO" }, mergeAt: new[] { 4 }, colors: "RO");
            Assert.That(LevelSolver.Solve(l, nodeBudget: 0).Status, Is.EqualTo(SolveStatus.Unknown));
        }
    }
}
