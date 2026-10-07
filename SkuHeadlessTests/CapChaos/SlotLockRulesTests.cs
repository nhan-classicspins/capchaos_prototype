using System.Linq;
using Game.Domain;
using NUnit.Framework;
using static CapsChaos.SkuHeadlessTests.CapChaos.LevelBuilder;

namespace CapsChaos.SkuHeadlessTests.CapChaos
{
    /// <summary>GDD §5.6b — slots locked for turns (R22). A belt of yellow bottles that no tray takes keeps every tray
    /// waiting in its slot, so a trace shows only the slot rules.</summary>
    public sealed class SlotLockRulesTests
    {
        private static readonly string[] NoMatch = { "YY" };

        [Test]
        public void R22_a_locked_slot_takes_no_tray_and_counts_down_one_per_tray_that_flies()
        {
            var g = new CapChaosGame(Level(NoMatch, new[] { "R", "O", "G" }, slots: 3, capacity: 1, slotLocks: new[] { new[] { 0, 2 } }));
            Assert.That(g.SlotLockLeft(0), Is.EqualTo(2));
            Assert.That(Trace(g.Tap(0).Facts), Does.StartWith("place(L0->S1:R) slotLock(S0:1)"), "the left-most free slot that is not locked");
            Assert.That(Trace(g.Tap(1).Facts), Does.StartWith("place(L1->S2:O) slotLock(S0:0)"));
            Assert.That(g.SlotLockLeft(0), Is.EqualTo(0));
            Assert.That(Trace(g.Tap(2).Facts), Does.StartWith("place(L2->S0:G)"), "open: it takes the next tray");
        }

        [Test]
        public void R22_a_locked_slot_is_no_room_for_a_tray()
        {
            var g = new CapChaosGame(Level(NoMatch, new[] { "R", "O" }, slots: 2, capacity: 1, slotLocks: new[] { new[] { 1, 3 } }));
            Assert.That(Trace(g.Tap(0).Facts), Does.EndWith("place(L0->S0:R) slotLock(S1:2) advance(L0:0) FAIL(SlotsJammed)"),
                "the only other slot is locked and nothing can fly to count it down");
            Assert.That(g.Status, Is.EqualTo(GameStatus.Lost));
        }

        [Test]
        public void R22_a_paid_slot_opens_an_extra_slot_never_a_slot_locked_for_turns()
        {
            var g = new CapChaosGame(Level(NoMatch, new[] { "R", "O" }, slots: 2, extraSlots: 1, capacity: 1,
                slotLocks: new[] { new[] { 1, 3 } }));
            Assert.That(Trace(g.Tap(0).Facts), Does.EndWith("RANOUT"), "R20 offers a slot before the round is lost");
            Assert.That(Trace(g.UnlockSlot()), Is.EqualTo("unlock(S2)"));
            Assert.That(g.SlotLockLeft(1), Is.EqualTo(2), "the turn lock is untouched");
            Assert.That(Trace(g.Tap(1).Facts), Does.StartWith("place(L1->S2:O) slotLock(S1:1)"));
        }

        [Test]
        public void R22_linked_trays_count_one_each_as_they_leave()
        {
            var g = new CapChaosGame(Level(NoMatch, new[] { "BR", "O" }, slots: 4, capacity: 1,
                slotLocks: new[] { new[] { 0, 3 } }, links: new[] { new[] { 0, 1, 1, 0 } }));
            Assert.That(Trace(g.Tap(0).Facts), Does.StartWith("place(L0->S1:B) slotLock(S0:2) advance(L0:1) unlink(L0#1,L1#0)"));
            Assert.That(Trace(g.Tap(1).Facts), Does.StartWith("place(L1->S2:O) slotLock(S0:1)"), "the broken link's trays leave one by one");
        }

        [Test]
        public void R22_a_clone_keeps_the_lock_and_the_state_key_tells_locks_apart()
        {
            var a = new CapChaosGame(Level(NoMatch, new[] { "R", "O" }, slots: 3, capacity: 1, slotLocks: new[] { new[] { 2, 2 } }));
            var b = new CapChaosGame(Level(NoMatch, new[] { "R", "O" }, slots: 3, capacity: 1, slotLocks: new[] { new[] { 2, 3 } }));
            Assert.That(a.StateKey(), Is.Not.EqualTo(b.StateKey()));
            var c = a.Clone();
            c.Tap(0);
            Assert.That((a.SlotLockLeft(2), c.SlotLockLeft(2)), Is.EqualTo((2, 1)), "the clone counts on its own");
        }

        [Test]
        public void R22_a_level_that_unlocks_its_slot_in_time_is_solvable()
        {
            // three R trays and one O: slot 0 opens after two trays, the solver must use it
            var l = Level(new[] { "RRRO" }, new[] { "RR", "RO" }, slots: 2, capacity: 1, slotLocks: new[] { new[] { 0, 2 } });
            Assert.That(LevelValidator.Validate(l).Where(e => e.StartsWith("V10")), Is.Empty);
            Assert.That(LevelSolver.Solve(l).Status, Is.EqualTo(SolveStatus.Solvable));
        }
    }
}
