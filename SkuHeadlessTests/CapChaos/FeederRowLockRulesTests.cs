using System.Linq;
using Game.Domain;
using NUnit.Framework;
using static CapsChaos.SkuHeadlessTests.CapChaos.LevelBuilder;

namespace CapsChaos.SkuHeadlessTests.CapChaos
{
    /// <summary>GDD §5.6d — locked items (R24): a locked feeder queue row stops at the merge point, holds the rows behind
    /// it, and counts the trays that fly to the slots down — only once it has reached the merge point. Yellow trays take
    /// nothing from the belt, so they stay in their slots and a trace shows only the lock.</summary>
    public sealed class FeederRowLockRulesTests
    {
        /// <summary>Step the belt <paramref name="steps"/> times; true if any feeder bottle joined.</summary>
        private static bool Fed(CapChaosGame g, int steps = 16)
        {
            bool fed = false;
            for (int i = 0; i < steps; i++) fed |= g.Step().OfType<BottleFed>().Any();
            return fed;
        }

        [Test]
        public void R24_a_locked_row_waits_at_the_merge_point_until_enough_trays_have_flown()
        {
            var g = new CapChaosGame(Level(new[] { ".." }, new[] { "Y", "Y", "R" }, slots: 3, capacity: 2,
                feeders: new[] { "RR" }, feederLocks: new[] { new[] { 0, 0, 2 } }));
            Assert.That(g.Belt.FeederLockLeft(0), Is.EqualTo(2));
            Assert.That(Fed(g), Is.False, "locked: it never joins on its own");
            Assert.That(Trace(g.Tap(0).Facts), Does.Contain("rowLock(F0#0:1)"));
            Assert.That(Fed(g), Is.False, "one turn left");
            Assert.That(Trace(g.Tap(1).Facts), Does.Contain("rowLock(F0#0:0)"));
            Assert.That(Fed(g), Is.True, "open: it joins the loop");
            Assert.That(g.Belt.Count, Is.EqualTo(2));
        }

        [Test]
        public void R24_a_locked_row_holds_the_rows_behind_it()
        {
            var g = new CapChaosGame(Level(new[] { ".." }, new[] { "Y" }, slots: 3, capacity: 2,
                feeders: new[] { "RROO" }, feederLocks: new[] { new[] { 0, 0, 3 } }));
            Fed(g, 32);
            Assert.That(g.Belt.FeederRowsJoined(0), Is.EqualTo(0), "row 1 does not overtake the locked row 0");
            Assert.That(g.Belt.FeederRemaining(0, 0), Is.EqualTo(2));
        }

        [Test]
        public void R24_a_lock_only_counts_once_its_row_has_reached_the_merge_point()
        {
            var g = new CapChaosGame(Level(new[] { ".." }, new[] { "Y", "Y" }, slots: 3, capacity: 2,
                feeders: new[] { "RROO" }, feederLocks: new[] { new[] { 0, 1, 1 } }));
            Assert.That(g.Tap(0).Facts.OfType<FeederRowLockTicked>(), Is.Empty, "row 1 is still behind row 0");
            Assert.That(g.Belt.FeederLockLeft(0), Is.EqualTo(0), "the front row (0) is not locked");
            Assert.That(Fed(g), Is.True, "row 0 joins");
            Assert.That((g.Belt.FeederRowsJoined(0), g.Belt.FeederLockLeft(0)), Is.EqualTo((1, 1)), "row 1 reached the merge point");
            Assert.That(Fed(g), Is.False);
            Assert.That(Trace(g.Tap(1).Facts), Does.Contain("rowLock(F0#1:0)"));
            Assert.That(Fed(g), Is.True);
        }

        [Test]
        public void R24_a_linked_pair_counts_two()
        {
            var g = new CapChaosGame(Level(new[] { ".." }, new[] { "Y", "Y" }, slots: 3, capacity: 2,
                feeders: new[] { "RR" }, feederLocks: new[] { new[] { 0, 0, 3 } }, links: new[] { new[] { 0, 0, 1, 0 } }));
            Assert.That(Trace(g.Tap(0).Facts), Does.Contain("rowLock(F0#0:1)"));
        }

        [Test]
        public void R24_a_row_locked_for_good_is_a_loss_not_a_hang()
        {
            var g = new CapChaosGame(Level(new[] { ".." }, new[] { "R" }, slots: 3, capacity: 2,
                feeders: new[] { "RR" }, feederLocks: new[] { new[] { 0, 0, 5 } }));
            Assert.That(Trace(g.Tap(0).Facts), Does.EndWith("rowLock(F0#0:4) advance(L0:0) FAIL(NoMovesLeft)"),
                "nothing can fly any more, so the lock can never open");
            Assert.That(g.Status, Is.EqualTo(GameStatus.Lost));
        }

        [Test]
        public void R24_the_state_key_and_a_clone_carry_the_lock()
        {
            LevelDefinition L(int turns) => Level(new[] { ".." }, new[] { "Y", "Y" }, slots: 3, capacity: 2,
                feeders: new[] { "RR" }, feederLocks: new[] { new[] { 0, 0, turns } });
            var a = new CapChaosGame(L(2));
            Assert.That(a.StateKey(), Is.Not.EqualTo(new CapChaosGame(L(3)).StateKey()));
            var c = a.Clone();
            c.Tap(0);
            Assert.That((a.Belt.FeederLockLeft(0), c.Belt.FeederLockLeft(0)), Is.EqualTo((2, 1)));
        }

        [Test]
        public void R24_the_solver_unlocks_a_row_to_win()
        {
            // the second R row is locked for one tray: the player must place a tray before it can join
            var l = Level(new[] { ".." }, new[] { "R", "R" }, slots: 2, capacity: 2,
                feeders: new[] { "RRRR" }, feederLocks: new[] { new[] { 0, 1, 1 } });
            Assert.That(LevelValidator.Validate(l), Is.Empty);
            Assert.That(LevelSolver.Solve(l).Status, Is.EqualTo(SolveStatus.Solvable));
        }
    }
}
