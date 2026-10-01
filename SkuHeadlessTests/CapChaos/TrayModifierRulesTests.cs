using System.Linq;
using Game.Domain;
using NUnit.Framework;
using static CapsChaos.SkuHeadlessTests.CapChaos.LevelBuilder;

namespace CapsChaos.SkuHeadlessTests.CapChaos
{
    /// <summary>GDD §5.5 — hidden (R17), locked (R18) and linked (R19) trays. A front row of yellow bottles that no
    /// tray takes keeps the other trays waiting in their slots, so a trace shows only the tray rules.</summary>
    public sealed class TrayModifierRulesTests
    {
        private static readonly string[][] NoMatch = { new[] { "YY" } };

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
            var g = new CapChaosGame(Level(new[] { new[] { "RBB" } }, new[] { "R", "BB" }, slots: 3, capacity: 1,
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
            var g = new CapChaosGame(Level(new[] { new[] { "RBB" } }, new[] { "BR", "B" }, slots: 3, capacity: 1,
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
            var g = new CapChaosGame(Level(new[] { new[] { "RB" } }, new[] { "B", "R" }, slots: 3, capacity: 1,
                locks: new[] { new[] { 1, 0, 5 } }));
            Assert.That(Trace(g.Tap(0).Facts), Does.EndWith("lock(L1#0:4) pick(1,0->S0:B) cap(S0:1) pack(S0:B) FAIL(NoMovesLeft)"));
            Assert.That(g.Status, Is.EqualTo(GameStatus.Lost));
        }

        [Test]
        public void R19_two_linked_trays_in_one_lane_fly_together_front_first()
        {
            var g = new CapChaosGame(Level(NoMatch, new[] { "ROB" }, slots: 5, capacity: 1, links: new[] { new[] { 0, 0, 0, 1 } }));
            Assert.That(Trace(g.Tap(0).Facts), Does.StartWith("place(L0->S0:R) place(L0->S1:O) advance(L0:2) advance(L0:1)"));
            Assert.That(g.LaneFront(0), Is.EqualTo(CapColor.Blue));
        }

        [Test]
        public void R19_trays_linked_across_lanes_wait_until_both_are_at_the_front()
        {
            var g = new CapChaosGame(Level(NoMatch, new[] { "RO", "BG" }, slots: 5, capacity: 1, links: new[] { new[] { 0, 1, 1, 1 } }));
            g.Tap(0);                                                                      // R leaves; O (linked) is at the front
            Assert.That(g.Tap(0).Outcome, Is.EqualTo(TapOutcome.RejectedLinkNotReady), "G is still behind B");
            g.Tap(1);                                                                      // B leaves; G reaches the front
            // tapped on the RIGHT lane, still placed left lane first
            Assert.That(Trace(g.Tap(1).Facts), Does.StartWith("place(L0->S2:O) place(L1->S3:G) advance(L0:0) advance(L1:0)"));
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
            var l = Level(new[] { new[] { "RBB" } }, new[] { "R", "BB" }, slots: 3, capacity: 1, locks: new[] { new[] { 0, 0, 2 } });
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
            var l = Level(new[] { new[] { "RBOG" } }, new[] { "RO", "BG" }, slots: 3, capacity: 1,
                locks: new[] { new[] { 0, 0, 1 } }, links: new[] { new[] { 0, 1, 1, 1 } });
            var report = LevelSolver.Solve(l);
            Assert.That(report.Status, Is.EqualTo(SolveStatus.Solvable));
            Assert.That(report.Solution, Is.EqualTo(new[] { 1, 0, 0 }));
        }

        [Test]
        public void V7_locks_and_links_must_name_real_neighbouring_trays()
        {
            var l = Level(new[] { new[] { "RROO" } }, new[] { "RO", "RO" }, capacity: 1,
                locks: new[] { new[] { 0, 5, 1 }, new[] { 1, 1, 2 } },
                links: new[] { new[] { 0, 0, 1, 1 }, new[] { 0, 1, 1, 1 }, new[] { 0, 1, 0, 0 } });
            var errors = LevelValidator.Validate(l);
            Assert.That(errors, Has.Some.Contains("V7 locks[0]: lanes[0][5] does not exist"));
            Assert.That(errors, Has.Some.Contains("V7 links[0]: lanes[0][0] and lanes[1][1] are not neighbours"));
            Assert.That(errors, Has.Some.Contains("V7 links[1]: lanes[1][1] is locked"));
            Assert.That(errors, Has.Some.Contains("V7 links[2]: lanes[0][1] is already in another link"));
        }

        [Test]
        public void Hidden_trays_locks_and_links_round_trip_through_json()
        {
            var l = Level(new[] { new[] { "RROO" } }, new[] { "Ro", "rO" }, capacity: 2,
                locks: new[] { new[] { 0, 1, 3 } }, links: new[] { new[] { 1, 0, 1, 1 } });
            var text = LevelJson.Write(l);
            Assert.That(text, Does.Contain("[\"R\", \"o\"]").And.Contain("\"turns\": 3").And.Contain("\"a\": [1, 0], \"b\": [1, 1]"));
            var back = LevelJson.Parse(text);
            Assert.That(back.Errors, Is.Empty);
            Assert.That(back.Level!.IsHiddenTray(new TrayRef(0, 1)), Is.True);
            Assert.That(back.Level.IsHiddenTray(new TrayRef(0, 0)), Is.False);
            Assert.That(back.Level.LockTurns(new TrayRef(0, 1)), Is.EqualTo(3));
            Assert.That(back.Level.TryGetLinkPartner(new TrayRef(1, 1), out var p) && p.Equals(new TrayRef(1, 0)), Is.True);
            Assert.That(LevelJson.Write(back.Level), Is.EqualTo(text));
        }
    }
}
