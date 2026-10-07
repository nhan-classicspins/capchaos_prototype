using System.Linq;
using Game.Domain;
using NUnit.Framework;
using static CapsChaos.SkuHeadlessTests.CapChaos.LevelBuilder;

namespace CapsChaos.SkuHeadlessTests.CapChaos
{
    /// <summary>GDD §5.6d — locked items (R24, rewritten 2026-10-07): a locked feeder queue row joins the loop like any
    /// other, but no tray takes its bottles until enough trays have flown to the slots WHILE it is on the loop; when the
    /// lanes run out of trays every row still locked opens. Yellow trays take nothing from the belt, so they stay in their
    /// slots and a trace shows only the lock.</summary>
    public sealed class FeederRowLockRulesTests
    {
        /// <summary>Step the belt <paramref name="steps"/> times; true if any feeder bottle joined.</summary>
        private static bool Fed(CapChaosGame g, int steps = 16)
        {
            bool fed = false;
            for (int i = 0; i < steps; i++) fed |= g.Step().OfType<BottleFed>().Any();
            return fed;
        }

        /// <summary>Step the belt <paramref name="steps"/> times; how many bottles flew to a tray.</summary>
        private static int Picked(CapChaosGame g, int steps = 24) =>
            Enumerable.Range(0, steps).Sum(_ => g.Step().OfType<BottlePicked>().Count());

        [Test]
        public void R24_a_locked_row_joins_the_loop_but_no_tray_takes_its_bottles_until_it_opens()
        {
            var g = new CapChaosGame(Level(new[] { ".." }, new[] { "R", "Y", "Y", "Y" }, slots: 4, capacity: 2,
                feeders: new[] { "RR" }, feederLocks: new[] { new[] { 0, 0, 2 } }));
            Assert.That(Trace(g.Tap(0).Facts), Does.Not.Contain("rowLock"), "still queued: the lock does not count yet");
            Assert.That(Fed(g), Is.True, "a locked row joins the loop like any other");
            Assert.That(g.Belt.FeederRowLockLeft(0, 0), Is.EqualTo(2));
            Assert.That(Picked(g), Is.Zero, "the R tray waits, but the locked R bottles ride past it");
            Assert.That(Trace(g.Tap(1).Facts), Does.Contain("rowLock(F0#0:1)"));
            Assert.That(Picked(g), Is.Zero);
            Assert.That(Trace(g.Tap(2).Facts), Does.Contain("rowLock(F0#0:0)"));
            Assert.That(Picked(g), Is.EqualTo(2), "open: the R tray takes both");
        }

        [Test]
        public void R24_a_locked_row_never_holds_the_rows_behind_it()
        {
            var g = new CapChaosGame(Level(new[] { ".." }, new[] { "Y" }, slots: 3, capacity: 2,
                feeders: new[] { "RROO" }, feederLocks: new[] { new[] { 0, 0, 3 } }));
            Fed(g, 32);
            Assert.That(g.Belt.FeederRowsJoined(0), Is.EqualTo(2), "both rows are on the loop");
        }

        [Test]
        public void R24_a_lock_only_counts_once_its_row_is_on_the_loop()
        {
            var g = new CapChaosGame(Level(new[] { ".." }, new[] { "Y", "Y", "Y" }, slots: 4, capacity: 2,
                feeders: new[] { "RROO" }, feederLocks: new[] { new[] { 0, 1, 2 } }));
            Assert.That(g.Tap(0).Facts.OfType<FeederRowLockTicked>(), Is.Empty, "row 1 is still queued");
            Assert.That(g.Belt.FeederRowLockLeft(0, 1), Is.EqualTo(2));
            Fed(g, 32);
            Assert.That(g.Belt.FeederRowsJoined(0), Is.EqualTo(2));
            Assert.That(Trace(g.Tap(1).Facts), Does.Contain("rowLock(F0#1:1)"), "on the loop: it counts");
        }

        [Test]
        public void R24_when_the_lanes_run_out_of_trays_a_locked_row_on_the_loop_opens()
        {
            var g = new CapChaosGame(Level(new[] { ".." }, new[] { "R" }, slots: 3, capacity: 2,
                feeders: new[] { "RR" }, feederLocks: new[] { new[] { 0, 0, 5 } }));
            Assert.That(Fed(g), Is.True);
            Assert.That(Trace(g.Tap(0).Facts), Does.Contain("rowLock(F0#0:4) advance(L0:0) rowLock(F0#0:0)"),
                "the last tray left the lanes: the lock could never count down to 0, so it opens");
            g.Settle();
            Assert.That(g.Status, Is.EqualTo(GameStatus.Won));
        }

        [Test]
        public void R24_when_the_lanes_run_out_of_trays_a_locked_row_still_queued_opens_too()
        {
            var g = new CapChaosGame(Level(new[] { ".." }, new[] { "R", "R" }, slots: 3, capacity: 2,
                feeders: new[] { "RRRR" }, feederLocks: new[] { new[] { 0, 1, 9 } }));
            g.Tap(0);
            Assert.That(Trace(g.Tap(1).Facts), Does.Contain("rowLock(F0#1:0)"));
            Assert.That(g.Belt.FeederRowLockLeft(0, 1), Is.Zero);
            g.Settle();
            Assert.That(g.Status, Is.EqualTo(GameStatus.Won), "it joins unlocked and fills the second tray");
        }

        [Test]
        public void R24_locked_bottles_never_count_as_something_a_full_slot_bar_waits_for()
        {
            // the one slot holds the R tray; only locked R bottles are on the loop, and no other tray can fly to count
            var g = new CapChaosGame(Level(new[] { ".." }, new[] { "R", "Y" }, slots: 1, capacity: 2,
                feeders: new[] { "RR" }, feederLocks: new[] { new[] { 0, 0, 3 } }));
            Fed(g);
            g.Tap(0);
            g.Settle();
            Assert.That(g.Status, Is.EqualTo(GameStatus.Lost));
        }

        [Test]
        public void R24_the_state_key_and_a_clone_carry_the_lock()
        {
            var a = new CapChaosGame(Level(new[] { ".." }, new[] { "Y", "Y" }, slots: 3, capacity: 2,
                feeders: new[] { "RR" }, feederLocks: new[] { new[] { 0, 0, 2 } }));
            Fed(a);
            var c = a.Clone();
            c.Tap(0);
            Assert.That((a.Belt.FeederRowLockLeft(0, 0), c.Belt.FeederRowLockLeft(0, 0)), Is.EqualTo((2, 1)));
            Assert.That(a.StateKey(), Is.Not.EqualTo(c.StateKey()));
        }

        [Test]
        public void R24_the_solver_plays_through_a_locked_row()
        {
            var l = Level(new[] { ".." }, new[] { "R", "R" }, slots: 2, capacity: 2,
                feeders: new[] { "RRRR" }, feederLocks: new[] { new[] { 0, 1, 1 } });
            Assert.That(LevelValidator.Validate(l), Is.Empty);
            Assert.That(LevelSolver.Solve(l).Status, Is.EqualTo(SolveStatus.Solvable));
        }
    }
}
