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
        public void R19_linked_trays_move_on_their_own_and_the_link_breaks_once_both_are_at_the_front()
        {
            // lane 0: B O R with O (#1) tied to lane 1's O (#2); lane 1: G R O
            var g = new CapChaosGame(Level(NoMatch, new[] { "BOR", "GRO" }, slots: 6, capacity: 1, links: new[] { new[] { 0, 1, 1, 2 } }));
            Assert.That(Trace(g.Tap(0).Facts), Is.EqualTo("place(L0->S0:B) advance(L0:2)"), "lane 0 moves up although its O is linked");
            Assert.That(g.IsAtFront(0, 1), Is.True);
            Assert.That(g.Tap(0).Outcome, Is.EqualTo(TapOutcome.RejectedLinkNotReady), "its partner is not at the front yet");
            Assert.That(Trace(g.Tap(1).Facts), Is.EqualTo("place(L1->S1:G) advance(L1:2)"), "lane 1 moves on its own too");
            Assert.That(Trace(g.Tap(1).Facts), Is.EqualTo("place(L1->S2:R) advance(L1:1) unlink(L0#1,L1#2)"),
                "lane 1's O reaches the front: both linked trays stand at the front, the link breaks");
            Assert.That(g.TryPartner(0, 1, out _) || g.TryPartner(1, 2, out _), Is.False);
            Assert.That(Trace(g.Tap(1).Facts), Is.EqualTo("place(L1->S3:O) advance(L1:0)"), "each leaves on its own tap");
            Assert.That(Trace(g.Tap(0).Facts), Is.EqualTo("place(L0->S4:O) advance(L0:1)"));
        }

        [Test]
        public void R19_the_trays_behind_and_in_front_of_a_linked_tray_never_wait_for_it()
        {
            var g = new CapChaosGame(Level(NoMatch, new[] { "BGOR", "RBOG" }, slots: 6, capacity: 1, links: new[] { new[] { 0, 2, 1, 3 } }));
            g.Tap(0); g.Tap(0);
            Assert.That(g.IsAtFront(0, 2), Is.True, "the linked O reached the front of lane 0");
            for (int t = 2; t < 4; t++) Assert.That(g.TrayPosition(0, t), Is.EqualTo(t - 2), "lane 0 stands closed up behind it");
            Assert.That(g.Tap(0).Outcome, Is.EqualTo(TapOutcome.RejectedLinkNotReady), "and it blocks its lane until lane 1's G is there");
            Assert.That(g.Tap(1).Accepted && g.Tap(1).Accepted && g.Tap(1).Accepted, Is.True, "lane 1 is free all the way");
            Assert.That(g.IsAtFront(1, 3), Is.True);
            Assert.That(g.Tap(0).Accepted, Is.True, "the link broke: lane 0's O leaves alone");
        }

        [Test]
        public void R19_a_tray_that_left_has_no_position()
        {
            var g = new CapChaosGame(Level(NoMatch, new[] { "BGOR" }, slots: 6, capacity: 1));
            g.Tap(0);
            for (int t = 1; t < 4; t++) Assert.That(g.TrayPosition(0, t), Is.EqualTo(t - 1));
            Assert.That(g.TrayPosition(0, 0), Is.EqualTo(-1));
        }

        [Test]
        public void R19_links_waiting_on_each_other_are_a_loss_not_a_hang()
        {
            // each lane's front is tied to the tray BEHIND the other lane's front: neither front can ever leave
            var l = Level(NoMatch, new[] { "BO", "OB" }, slots: 4, capacity: 1, links: new[] { new[] { 0, 0, 1, 1 }, new[] { 0, 1, 1, 0 } });
            var g = new CapChaosGame(l);
            Assert.That(g.Tap(0).Outcome, Is.EqualTo(TapOutcome.RejectedLinkNotReady));
            Assert.That(g.Tap(1).Outcome, Is.EqualTo(TapOutcome.RejectedLinkNotReady));
            Assert.That(LevelSolver.Solve(l).Status, Is.EqualTo(SolveStatus.Unsolvable), "V6 catches the cycle");
        }

        [Test]
        public void R19_a_link_whose_trays_both_start_at_the_front_is_broken_from_the_start()
        {
            var l = Level(NoMatch, new[] { "R", "O" }, slots: 3, capacity: 1, links: new[] { new[] { 0, 0, 1, 0 } });
            var g = new CapChaosGame(l);
            Assert.That(g.TryPartner(0, 0, out _), Is.False);
            Assert.That(g.Tap(0).Accepted, Is.True);
            Assert.That(LevelValidator.Validate(l), Has.Some.Contains("V7 links[0]: lanes[0][0] and lanes[1][0] both start at the front"));
        }

        [Test]
        public void R19_R17_a_hidden_tray_behind_a_linked_tray_shows_when_it_reaches_the_front()
        {
            var g = new CapChaosGame(Level(NoMatch, new[] { "BOr", "GRO" }, slots: 6, capacity: 1, links: new[] { new[] { 0, 1, 1, 2 } }));
            g.Tap(0); g.Tap(1); g.Tap(1);                                                       // the link breaks
            Assert.That(g.IsTrayHidden(0, 2), Is.True);
            Assert.That(Trace(g.Tap(0).Facts), Does.Contain("advance(L0:1) trayReveal(L0#2:R)"));
        }

        [Test]
        public void V7_a_link_joins_two_different_lanes_at_any_positions()
        {
            var ok = Level(NoMatch, new[] { "BO", "RY", "GOB" }, capacity: 1, links: new[] { new[] { 0, 1, 2, 2 } });
            Assert.That(LevelValidator.Validate(ok).Where(e => e.StartsWith("V7")), Is.Empty, "different positions, a lane between: fine");
            var same = Level(NoMatch, new[] { "BOR" }, capacity: 1, links: new[] { new[] { 0, 1, 0, 2 } });
            Assert.That(LevelValidator.Validate(same), Has.Some.Contains("V7 links[0]: lanes[0][1] and lanes[0][2] are on the same lane"));
        }

        [Test]
        public void A_broken_link_is_part_of_the_state_key()
        {
            var g = new CapChaosGame(Level(NoMatch, new[] { "BO", "O" }, slots: 4, capacity: 1, links: new[] { new[] { 0, 1, 1, 0 } }));
            var before = g.Clone();
            g.Tap(0);
            Assert.That(g.TryPartner(1, 0, out _), Is.False, "broken");
            Assert.That(before.TryPartner(1, 0, out _), Is.True, "the clone keeps its own link");
            Assert.That(g.StateKey(), Is.Not.EqualTo(before.StateKey()));
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
            // R needs one placement first; lane 0's O is tied to lane 1's G, which needs B gone
            var l = Level(new[] { "RBOG" }, new[] { "RO", "BG" }, slots: 3, capacity: 1,
                locks: new[] { new[] { 0, 0, 1 } }, links: new[] { new[] { 0, 1, 1, 1 } });
            var report = LevelSolver.Solve(l);
            Assert.That(report.Status, Is.EqualTo(SolveStatus.Solvable));
            Assert.That(report.Solution, Is.EqualTo(new[] { 1, 0, 0, 1 }));
        }

        [Test]
        public void V7_locks_and_links_must_name_real_trays_that_can_be_linked()
        {
            var l = Level(new[] { "RROO" }, new[] { "RO", "RO" }, capacity: 1,
                locks: new[] { new[] { 0, 5, 1 }, new[] { 1, 1, 2 } },
                links: new[] { new[] { 0, 0, 0, 1 }, new[] { 0, 1, 1, 1 }, new[] { 0, 1, 1, 0 } });
            var errors = LevelValidator.Validate(l);
            Assert.That(errors, Has.Some.Contains("V7 locks[0]: lanes[0][5] does not exist"));
            Assert.That(errors, Has.Some.Contains("V7 links[0]: lanes[0][0] and lanes[0][1] are on the same lane"));
            Assert.That(errors, Has.Some.Contains("V7 links[1]: lanes[1][1] is locked"));
            Assert.That(errors, Has.Some.Contains("V7 links[2]: lanes[0][1] is already in another link"));
        }

        [Test]
        public void Hidden_trays_locks_and_links_round_trip_through_json()
        {
            var l = Level(new[] { "RROO" }, new[] { "Ro", "rO" }, capacity: 2,
                locks: new[] { new[] { 0, 1, 3 } }, links: new[] { new[] { 1, 0, 0, 0 } });
            var text = LevelJson.Write(l);
            Assert.That(text, Does.Contain("[{ \"color\": 1 }, { \"color\": 2, \"hidden\": true, \"lockTurns\": 3 }]")
                .And.Contain("{ \"a\": { \"lane\": 1, \"tray\": 0 }, \"b\": { \"lane\": 0, \"tray\": 0 } }"));
            var back = LevelJson.Parse(text, Conveyors(l));
            Assert.That(back.Errors, Is.Empty);
            Assert.That(back.Level!.IsHiddenTray(new TrayRef(0, 1)), Is.True);
            Assert.That(back.Level.IsHiddenTray(new TrayRef(0, 0)), Is.False);
            Assert.That(back.Level.LockTurns(new TrayRef(0, 1)), Is.EqualTo(3));
            Assert.That(back.Level.TryGetLinkPartner(new TrayRef(0, 0), out var p) && p.Equals(new TrayRef(1, 0)), Is.True);
            Assert.That(LevelJson.Write(back.Level), Is.EqualTo(text));
        }
    }
}
