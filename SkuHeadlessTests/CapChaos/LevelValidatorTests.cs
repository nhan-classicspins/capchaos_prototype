using Game.Domain;
using NUnit.Framework;
using static CapsChaos.SkuHeadlessTests.CapChaos.LevelBuilder;

namespace CapsChaos.SkuHeadlessTests.CapChaos
{
    /// <summary>V4–V8 (GDD §6.4).</summary>
    public sealed class LevelValidatorTests
    {
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
            Assert.That(e, Has.Some.StartsWith("V5 loop.initial[0][0]: colour 1 (Red) is not in colors"));
            Assert.That(e, Has.Some.StartsWith("V5 colors: 2 (Orange) is declared but never used"));
        }

        [Test]
        public void V8_merge_points_lie_on_the_track_outside_the_pick_zone_and_are_not_shared()
        {
            var l = Level(new[] { "RRRR" }, new[] { "R", "R", "R", "R" }, pickRows: 1,
                feeders: new[] { "RRRR", "RRRR", "RRRR" }, mergeAt: new[] { 0, 9, 9 });
            var e = LevelValidator.Validate(l);
            Assert.That(e, Has.Some.Contains("V8 loop.feeders[0].mergeAt: 0 is inside the pick zone 0..0"));
            Assert.That(e, Has.Some.Contains("V8 loop.feeders[1].mergeAt: 9 is not a track position 0..7"));
            Assert.That(e, Has.Some.Contains("V8 loop.feeders[2].mergeAt: 9 is another feeder's merge point"));
        }

        [Test]
        public void V8_the_pick_zone_leaves_room_for_the_back_straight_and_the_bends()
        {
            var l = Level(new[] { "RRRR" }, new[] { "R" }, rows: 8, pickRows: 2);
            Assert.That(LevelValidator.Validate(l), Has.Some.StartsWith("V8 loop.pickRows: 2 pick rows need rows ≥ 10"));
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
