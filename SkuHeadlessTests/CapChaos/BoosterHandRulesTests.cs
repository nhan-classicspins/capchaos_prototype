using System.Linq;
using Game.Domain;
using NUnit.Framework;
using static CapsChaos.SkuHeadlessTests.CapChaos.LevelBuilder;

namespace CapsChaos.SkuHeadlessTests.CapChaos
{
    /// <summary>Booster Hand (<see cref="CapChaosGame.TakeTray"/>): any tray still waiting in a lane flies to a slot. A
    /// belt of yellow bottles that no tray takes keeps the trays in their slots, so a trace shows only the lane rules.</summary>
    public sealed class BoosterHandRulesTests
    {
        private static readonly string[] NoMatch = { "YY" };

        [Test]
        public void A_tray_from_the_middle_of_a_lane_flies_and_the_trays_behind_it_move_up()
        {
            var g = new CapChaosGame(Level(NoMatch, new[] { "RGB" }, slots: 5, capacity: 1));
            var r = g.TakeTray(0, 1);
            Assert.That(r.Accepted, Is.True);
            Assert.That(Trace(r.Facts), Does.StartWith("place(L0->S0:G) advance(L0:2)"));
            var placed = r.Facts.OfType<TrayPlaced>().Single();
            Assert.That((placed.Tray, placed.Position), Is.EqualTo((1, 1)));
            Assert.That(g.LaneFront(0), Is.EqualTo(CapColor.Red), "the front tray stays");
            Assert.That(g.TrayPosition(0, 1), Is.EqualTo(-1), "it left");
            Assert.That(g.TrayPosition(0, 2), Is.EqualTo(1), "the tray behind moved up");
            Assert.That(g.TrayAtPosition(0, 1), Is.EqualTo(2));
            Assert.That(g.LaneAt(0, 1), Is.EqualTo(CapColor.Blue));
        }

        [Test]
        public void After_the_front_tray_leaves_the_head_skips_a_tray_the_hand_took()
        {
            var g = new CapChaosGame(Level(NoMatch, new[] { "RGB", "O" }, slots: 5, capacity: 1));   // lane 1 keeps a move
            g.TakeTray(0, 1);
            Assert.That(Trace(g.Tap(0).Facts), Does.StartWith("place(L0->S1:R) advance(L0:1)"));
            Assert.That(g.IsAtFront(0, 2), Is.True);
            Assert.That(g.LaneHead(0), Is.EqualTo(2));
            Assert.That(Trace(g.Tap(0).Facts), Does.StartWith("place(L0->S2:B) advance(L0:0)"));
            Assert.That(g.Tap(0).Outcome, Is.EqualTo(TapOutcome.RejectedEmptyLane));
        }

        [Test]
        public void Taking_the_last_tray_from_the_back_empties_the_lane_count()
        {
            var g = new CapChaosGame(Level(NoMatch, new[] { "RG", "O" }, slots: 5, capacity: 1));
            g.TakeTray(0, 1);
            g.Tap(0);
            Assert.That(g.LaneRemaining(0), Is.EqualTo(0));
            Assert.That(g.TakeTray(0, 1).Outcome, Is.EqualTo(TapOutcome.RejectedNotInLane));
        }

        [Test]
        public void A_hidden_tray_shows_its_colour_before_it_flies()
        {
            var g = new CapChaosGame(Level(NoMatch, new[] { "Rog" }, slots: 5, capacity: 1));
            Assert.That(g.IsTrayHidden(0, 2), Is.True);
            Assert.That(Trace(g.TakeTray(0, 2).Facts), Does.StartWith("trayReveal(L0#2:G) place(L0->S0:G) advance(L0:2)"));
        }

        [Test]
        public void A_linked_tray_is_untied_and_its_partner_is_a_free_tray()
        {
            // lane 0: B O with O (#1) tied to lane 1's O (#1); lane 1: G O
            var g = new CapChaosGame(Level(NoMatch, new[] { "BO", "GO" }, slots: 6, capacity: 1, links: new[] { new[] { 0, 1, 1, 1 } }));
            Assert.That(Trace(g.TakeTray(0, 1).Facts), Does.StartWith("unlink(L0#1,L1#1) place(L0->S0:O)"));
            Assert.That(g.TryPartner(1, 1, out _), Is.False);
            g.Tap(1);
            Assert.That(g.Tap(1).Accepted, Is.True, "the partner leaves on its own tap");
        }

        [Test]
        public void A_locked_front_tray_does_not_hold_the_hand()
        {
            var g = new CapChaosGame(Level(NoMatch, new[] { "RB" }, slots: 3, capacity: 1, locks: new[] { new[] { 0, 0, 3 } }));
            Assert.That(g.Tap(0).Outcome, Is.EqualTo(TapOutcome.RejectedLocked));
            Assert.That(g.TakeTray(0, 0).Accepted, Is.True);
            Assert.That(g.LockLeft(0), Is.EqualTo(0));
            Assert.That(g.Tap(0).Accepted, Is.True);
        }

        [Test]
        public void A_hand_placement_counts_down_the_lock_of_the_front_tray()
        {
            var g = new CapChaosGame(Level(NoMatch, new[] { "RBG" }, slots: 4, capacity: 1, locks: new[] { new[] { 0, 0, 2 } }));
            Assert.That(Trace(g.TakeTray(0, 2).Facts), Does.Contain("lock(L0#0:1)"));
        }

        [Test]
        public void No_free_slot_refuses_the_hand_and_nothing_changes()
        {
            var g = new CapChaosGame(Level(NoMatch, new[] { "RGB" }, slots: 1, capacity: 1, extraSlots: 1));
            g.Tap(0);
            Assert.That(g.SlotsRanOut, Is.True, "an extra slot is on offer: the round waits");
            string before = g.StateKey();
            Assert.That(g.TakeTray(0, 2).Outcome, Is.EqualTo(TapOutcome.RejectedNoFreeSlot));
            Assert.That(g.StateKey(), Is.EqualTo(before));
        }

        [Test]
        public void A_clone_keeps_the_taken_trays_and_does_not_share_them()
        {
            var g = new CapChaosGame(Level(NoMatch, new[] { "RGBO" }, slots: 5, capacity: 1));
            g.TakeTray(0, 1);
            var c = g.Clone();
            Assert.That(c.StateKey(), Is.EqualTo(g.StateKey()));
            c.TakeTray(0, 2);
            Assert.That(g.TrayPosition(0, 2), Is.EqualTo(1), "the original still has it");
            Assert.That(c.StateKey(), Is.Not.EqualTo(g.StateKey()));
        }
    }
}
