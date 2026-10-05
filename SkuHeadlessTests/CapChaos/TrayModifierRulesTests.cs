using System.Linq;
using Game.Domain;
using NUnit.Framework;
using static CapsChaos.SkuHeadlessTests.CapChaos.LevelBuilder;

namespace CapsChaos.SkuHeadlessTests.CapChaos
{
    /// <summary>GDD §5.5 — hidden (R17), locked (R18) and linked (R19) trays. A belt of yellow bottles that no
    /// tray takes keeps the other trays waiting in their slots, so a trace shows only the tray rules.</summary>
    public sealed class TrayModifierRulesTests
    {
        private static readonly string[] NoMatch = { "YY" };

        [Test]
        public void R17_a_hidden_tray_shows_its_colour_when_it_reaches_the_front()
        {
            var g = new CapChaosGame(Level(NoMatch, new[] { "Ro" }, slots: 5, capacity: 1));
            Assert.That(g.IsTrayHidden(0, 1), Is.True, "behind the front tray");
            var t = Trace(g.Tap(0).Facts);
            Assert.That(t, Does.StartWith("place(L0->S0:R) advance(L0:1) trayReveal(L0#1:O)"));
            Assert.That(g.IsTrayHidden(0, 1), Is.False);
        }

        [Test]
        public void R17_a_hidden_tray_that_starts_at_the_front_is_never_hidden()
        {
            var g = new CapChaosGame(Level(NoMatch, new[] { "o" }, slots: 5, capacity: 1));
            Assert.That(g.IsTrayHidden(0, 0), Is.False);
            Assert.That(Trace(g.Tap(0).Facts), Does.StartWith("place(L0->S0:O)"));
        }

        [Test]
        public void R18_a_locked_front_tray_counts_down_one_per_tray_that_flies_to_a_slot()
        {
            var g = new CapChaosGame(Level(new[] { "RBB" }, new[] { "R", "BB" }, slots: 3, capacity: 1,
                locks: new[] { new[] { 0, 0, 2 } }));
            Assert.That(g.LockLeft(0), Is.EqualTo(2));
            Assert.That(g.Tap(0).Outcome, Is.EqualTo(TapOutcome.RejectedLocked));
            Assert.That(Trace(g.Tap(1).Facts), Does.Contain("lock(L0#0:1)"));
            Assert.That(g.Tap(0).Outcome, Is.EqualTo(TapOutcome.RejectedLocked), "one turn left");
            Assert.That(Trace(g.Tap(1).Facts), Does.Contain("lock(L0#0:0)"));
            Assert.That(Trace(g.Tap(0).Facts), Does.EndWith("WIN"), "unlocked");
        }

        [Test]
        public void R18_a_lock_only_counts_while_its_tray_stands_at_the_front()
        {
            var g = new CapChaosGame(Level(new[] { "RBB" }, new[] { "BR", "B" }, slots: 3, capacity: 1,
                locks: new[] { new[] { 0, 1, 1 } }));
            Assert.That(g.LockLeft(0), Is.EqualTo(0), "the locked tray is not at the front yet");
            var first = g.Tap(0);
            Assert.That(first.Facts.OfType<TrayLockTicked>(), Is.Empty, "the placement that brought it to the front does not count");
            Assert.That(g.LockLeft(0), Is.EqualTo(1));
            Assert.That(g.Tap(0).Outcome, Is.EqualTo(TapOutcome.RejectedLocked));
            Assert.That(Trace(g.Tap(1).Facts), Does.Contain("lock(L0#1:0)"));
            Assert.That(g.Tap(0).Accepted, Is.True);
        }

        [Test]
        public void R18_trays_locked_for_good_are_a_loss_not_a_hang()
        {
            var g = new CapChaosGame(Level(new[] { "RB" }, new[] { "B", "R" }, slots: 3, capacity: 1,
                locks: new[] { new[] { 1, 0, 5 } }));
            Assert.That(Trace(g.Tap(0).Facts), Does.EndWith("lock(L1#0:4) pick(0,1->S0:B) cap(S0:1) pack(S0:B) FAIL(NoMovesLeft)"));
            Assert.That(g.Status, Is.EqualTo(GameStatus.Lost));
        }

        [Test]
        public void R19_two_linked_trays_in_one_lane_fly_together_front_first()
        {
            var g = new CapChaosGame(Level(NoMatch, new[] { "ROB" }, slots: 5, capacity: 1, links: new[] { new[] { 0, 0, 0, 1 } }));
            Assert.That(Trace(g.Tap(0).Facts), Does.StartWith("place(L0->S0:R) place(L0->S1:O) advance(L0:1) advance(L0:1)"),
                "both leave, then the belt steps twice");
            Assert.That(g.LaneFront(0), Is.EqualTo(CapColor.Blue));
        }

        [Test]
        public void R19_a_belt_carrying_a_linked_tray_is_held_until_the_linked_belt_can_move_too()
        {
            // level_0018's lanes: B O R | O R O, with O (lane 0, #1) tied to R (lane 1, #1)
            var g = new CapChaosGame(Level(NoMatch, new[] { "BOR", "ORO" }, slots: 5, capacity: 1, links: new[] { new[] { 0, 1, 1, 1 } }));
            Assert.That(Trace(g.Tap(0).Facts), Is.EqualTo("place(L0->S0:B)"),
                "B leaves, belt 0 does NOT move: its O must stay beside lane 1's R");
            Assert.That(g.LaneGap(0), Is.EqualTo(1));
            Assert.That(g.LaneFront(0), Is.EqualTo(CapColor.None), "the front of belt 0 is empty");
            Assert.That(g.Tap(0).Outcome, Is.EqualTo(TapOutcome.RejectedBeltHeld));

            Assert.That(Trace(g.Tap(1).Facts), Does.StartWith("place(L1->S1:O) advance(L0:2) advance(L1:2)"),
                "lane 1's front leaves: both belts step together");
            Assert.That(g.IsAtFront(0, 1) && g.IsAtFront(1, 1), Is.True, "the pair arrives at the front side by side");
            Assert.That(Trace(g.Tap(1).Facts), Does.StartWith("place(L0->S2:O) place(L1->S3:R) advance(L0:1) advance(L1:1)"),
                "a tap on either tray of the pair releases both, left lane first; the belts are free again");
        }

        [Test]
        public void R19_a_held_linked_tray_only_holds_the_trays_behind_it_the_trays_in_front_still_move_up()
        {
            // lane 0: B G O R with O (#2) tied to lane 1's O (#2); lane 1: R Y O B
            var g = new CapChaosGame(Level(NoMatch, new[] { "BGOR", "RYOB" }, slots: 6, capacity: 1, links: new[] { new[] { 0, 2, 1, 2 } }));

            Assert.That(Trace(g.Tap(0).Facts), Is.EqualTo("place(L0->S0:B)"), "B leaves; the held part of lane 0 does not step");
            Assert.That(g.LaneFront(0), Is.EqualTo(CapColor.Green), "G is in front of the linked O: it moves up to the front");
            Assert.That(g.IsAtFront(0, 1), Is.True);
            Assert.That(g.TrayPosition(0, 1), Is.EqualTo(0));
            Assert.That(g.TrayPosition(0, 2), Is.EqualTo(2), "the linked O stays beside lane 1's O, a hole opens in front of it");
            Assert.That(g.TrayPosition(0, 3), Is.EqualTo(3), "R is behind the linked O: it is held too");
            Assert.That(g.TrayPosition(0, 2), Is.EqualTo(g.TrayPosition(1, 2)), "the pair still stands side by side");

            Assert.That(g.Tap(0).Accepted, Is.True, "the moved-up G is tappable");
            Assert.That(g.LaneFront(0), Is.EqualTo(CapColor.None), "now the held O is next: the front is empty");
            Assert.That(g.Tap(0).Outcome, Is.EqualTo(TapOutcome.RejectedBeltHeld));
            Assert.That(g.TrayPosition(0, 2), Is.EqualTo(2));

            g.Tap(1);                                                                            // R leaves lane 1
            Assert.That(g.TrayPosition(0, 2), Is.EqualTo(1).And.EqualTo(g.TrayPosition(1, 2)), "both linked trays stepped together");
            Assert.That(g.TrayPosition(1, 1), Is.EqualTo(0), "lane 1's Y moved up to the front");
            g.Tap(1);                                                                            // Y leaves lane 1
            Assert.That(g.IsAtFront(0, 2) && g.IsAtFront(1, 2), Is.True, "the pair reaches the front side by side");
            Assert.That(g.TrayPosition(0, 3), Is.EqualTo(1), "R followed the linked O");
        }

        [Test]
        public void V7_two_lanes_with_a_lane_between_them_can_link_at_the_same_position_but_not_at_different_ones()
        {
            var ok = Level(NoMatch, new[] { "BO", "RY", "GO" }, capacity: 1, links: new[] { new[] { 0, 1, 2, 1 } });
            Assert.That(LevelValidator.Validate(ok).Where(e => e.StartsWith("V7")), Is.Empty, "lanes 0 and 2, both at position 1");
            var skew = Level(NoMatch, new[] { "BO", "RY", "GO" }, capacity: 1, links: new[] { new[] { 0, 1, 2, 0 } });
            Assert.That(LevelValidator.Validate(skew), Has.Some.Contains("V7 links[0]: lanes[0][1] and lanes[2][0] can not be linked"),
                "different positions would hold each other's belt for ever");
        }

        [Test]
        public void R19_a_link_across_a_lane_holds_its_two_belts_and_leaves_the_lane_between_free()
        {
            // lane 0: B O, lane 1: R Y, lane 2: G O — lane 0's O (#1) tied to lane 2's O (#1)
            var g = new CapChaosGame(Level(NoMatch, new[] { "BO", "RY", "GO" }, slots: 6, capacity: 1, links: new[] { new[] { 0, 1, 2, 1 } }));
            Assert.That(Trace(g.Tap(0).Facts), Is.EqualTo("place(L0->S0:B)"), "lane 0 is held: its O waits for lane 2's O");
            Assert.That(g.Tap(0).Outcome, Is.EqualTo(TapOutcome.RejectedBeltHeld));
            Assert.That(Trace(g.Tap(1).Facts), Does.StartWith("place(L1->S1:R) advance(L1:1)").And.Not.Contains("advance(L0").And.Not.Contains("advance(L2"),
                "the lane between is not part of the link: it steps on its own");
            Assert.That(Trace(g.Tap(2).Facts), Does.StartWith("place(L2->S2:G) advance(L0:1) advance(L2:1)"),
                "lane 2's front leaves: the two linked belts step together");
            Assert.That(g.IsAtFront(0, 1) && g.IsAtFront(2, 1), Is.True);
            Assert.That(Trace(g.Tap(2).Facts), Does.StartWith("place(L0->S3:O) place(L2->S4:O)"),
                "a tap on either releases both, the left lane first");
        }

        [Test]
        public void R19_a_lane_without_a_cross_lane_link_never_holds()
        {
            var g = new CapChaosGame(Level(NoMatch, new[] { "BGOR" }, slots: 6, capacity: 1));
            g.Tap(0);
            for (int t = 1; t < 4; t++) Assert.That(g.TrayPosition(0, t), Is.EqualTo(t - 1));
            Assert.That(g.LaneGap(0), Is.EqualTo(0));
            Assert.That(g.TrayPosition(0, 0), Is.EqualTo(-1), "a tray that left has no position");
        }

        [Test]
        public void R19_the_two_belts_of_a_link_step_together_whichever_front_leaves_last()
        {
            var g = new CapChaosGame(Level(NoMatch, new[] { "BOR", "ORO" }, slots: 5, capacity: 1, links: new[] { new[] { 0, 1, 1, 1 } }));
            Assert.That(Trace(g.Tap(1).Facts), Does.StartWith("place(L1->S0:O)").And.Not.Contains("advance"));
            Assert.That(Trace(g.Tap(0).Facts), Does.StartWith("place(L0->S1:B) advance(L0:2) advance(L1:2)"));
        }

        [Test]
        public void R19_R17_a_hidden_tray_on_a_held_belt_stays_hidden_until_the_belts_move()
        {
            var g = new CapChaosGame(Level(NoMatch, new[] { "BoR", "ORO" }, slots: 5, capacity: 1, links: new[] { new[] { 0, 1, 1, 1 } }));
            Assert.That(g.Tap(0).Facts.OfType<TrayRevealed>(), Is.Empty, "belt 0 is held: the ? tray is not at the front");
            Assert.That(g.IsTrayHidden(0, 1), Is.True);
            Assert.That(Trace(g.Tap(1).Facts), Does.Contain("advance(L0:2) advance(L1:2) trayReveal(L0#1:O)"));
        }

        [Test]
        public void R19_a_linked_pair_needs_two_free_slots()
        {
            var g = new CapChaosGame(Level(NoMatch, new[] { "R", "BG" }, slots: 2, capacity: 1, links: new[] { new[] { 1, 0, 1, 1 } }));
            // one tray waits in a slot; the pair can never fit in the one slot left, and nothing else can move
            Assert.That(Trace(g.Tap(0).Facts), Does.EndWith("FAIL(NoMovesLeft)"));
        }

        [Test]
        public void R17_R19_a_hidden_tray_shows_with_the_front_tray_of_its_pair()
        {
            var g = new CapChaosGame(Level(NoMatch, new[] { "BRo" }, slots: 5, capacity: 1, links: new[] { new[] { 0, 1, 0, 2 } }));
            Assert.That(g.IsTrayHidden(0, 2), Is.True);
            Assert.That(Trace(g.Tap(0).Facts), Does.Contain("advance(L0:2) trayReveal(L0#2:O)"));
            Assert.That(g.IsTrayHidden(0, 2), Is.False);
        }

        [Test]
        public void Locks_and_links_are_part_of_the_state_key()
        {
            var l = Level(new[] { "RBB" }, new[] { "R", "BB" }, slots: 3, capacity: 1, locks: new[] { new[] { 0, 0, 2 } });
            var a = new CapChaosGame(l);
            var b = a.Clone();
            a.Tap(1);
            Assert.That(b.LockLeft(0), Is.EqualTo(2), "clones count independently");
            Assert.That(a.StateKey(), Is.Not.EqualTo(b.StateKey()));
        }

        [Test]
        public void V6_the_solver_plays_through_locks_and_links()
        {
            // R needs two placements first; the O+G pair needs B gone. Exactly one order wins.
            var l = Level(new[] { "RBOG" }, new[] { "RO", "BG" }, slots: 3, capacity: 1,
                locks: new[] { new[] { 0, 0, 1 } }, links: new[] { new[] { 0, 1, 1, 1 } });
            var report = LevelSolver.Solve(l);
            Assert.That(report.Status, Is.EqualTo(SolveStatus.Solvable));
            Assert.That(report.Solution, Is.EqualTo(new[] { 1, 0, 0 }));
        }

        [Test]
        public void V7_locks_and_links_must_name_real_trays_that_can_be_linked()
        {
            var l = Level(new[] { "RROO" }, new[] { "RO", "RO" }, capacity: 1,
                locks: new[] { new[] { 0, 5, 1 }, new[] { 1, 1, 2 } },
                links: new[] { new[] { 0, 0, 1, 1 }, new[] { 0, 1, 1, 1 }, new[] { 0, 1, 0, 0 } });
            var errors = LevelValidator.Validate(l);
            Assert.That(errors, Has.Some.Contains("V7 locks[0]: lanes[0][5] does not exist"));
            Assert.That(errors, Has.Some.Contains("V7 links[0]: lanes[0][0] and lanes[1][1] can not be linked"));
            Assert.That(errors, Has.Some.Contains("V7 links[1]: lanes[1][1] is locked"));
            Assert.That(errors, Has.Some.Contains("V7 links[2]: lanes[0][1] is already in another link"));
        }

        [Test]
        public void Hidden_trays_locks_and_links_round_trip_through_json()
        {
            var l = Level(new[] { "RROO" }, new[] { "Ro", "rO" }, capacity: 2,
                locks: new[] { new[] { 0, 1, 3 } }, links: new[] { new[] { 1, 0, 1, 1 } });
            var text = LevelJson.Write(l);
            Assert.That(text, Does.Contain("[{ \"color\": 1 }, { \"color\": 2, \"hidden\": true, \"lockTurns\": 3 }]")
                .And.Contain("{ \"a\": { \"lane\": 1, \"tray\": 0 }, \"b\": { \"lane\": 1, \"tray\": 1 } }"));
            var back = LevelJson.Parse(text, Conveyors(l));
            Assert.That(back.Errors, Is.Empty);
            Assert.That(back.Level!.IsHiddenTray(new TrayRef(0, 1)), Is.True);
            Assert.That(back.Level.IsHiddenTray(new TrayRef(0, 0)), Is.False);
            Assert.That(back.Level.LockTurns(new TrayRef(0, 1)), Is.EqualTo(3));
            Assert.That(back.Level.TryGetLinkPartner(new TrayRef(1, 1), out var p) && p.Equals(new TrayRef(1, 0)), Is.True);
            Assert.That(LevelJson.Write(back.Level), Is.EqualTo(text));
        }
    }
}
