using Game.Domain;
using NUnit.Framework;
using static CapsChaos.SkuHeadlessTests.CapChaos.LevelBuilder;

namespace CapsChaos.SkuHeadlessTests.CapChaos
{
    /// <summary>V2–V6 (GDD §6.4).</summary>
    public sealed class LevelValidatorTests
    {
        [Test]
        public void A_balanced_level_is_clean()
        {
            var l = Level(new[] { new[] { "RRRR" } }, new[] { "R" });
            Assert.That(LevelValidator.Validate(l), Is.Empty);
        }

        [Test]
        public void V2_floating_bottle()
        {
            var l = Level(new[] { new[] { "RRR." }, new[] { "...R" } }, new[] { "R" });
            Assert.That(LevelValidator.Validate(l), Has.Some.StartsWith("V2 stack.layers[1][0][3]"));
        }

        [Test]
        public void V3_hidden_bottle_on_the_ground()
        {
            var l = Level(new[] { new[] { "RRRr" } }, new[] { "R" });
            Assert.That(LevelValidator.Validate(l), Has.Some.StartsWith("V3 stack.layers[0][0][3]"));
        }

        [Test]
        public void V4_colour_balance()
        {
            var l = Level(new[] { new[] { "RRRO" } }, new[] { "R" });
            var e = LevelValidator.Validate(l);
            Assert.That(e, Has.Some.Contains("V4 colour R: 3 bottles vs 1 trays × 4 = 4"));
            Assert.That(e, Has.Some.Contains("V4 colour O: 1 bottles vs 0 trays"));
        }

        [Test]
        public void V5_undeclared_and_unused_colours()
        {
            var l = Level(new[] { new[] { "RRRR" } }, new[] { "R" }, colors: "OB");
            var e = LevelValidator.Validate(l);
            Assert.That(e, Has.Some.StartsWith("V5 stack.layers[0][0][0]: colour 'R' is not in colors"));
            Assert.That(e, Has.Some.StartsWith("V5 colors: 'O' is declared but never used"));
        }

        [Test]
        public void V6_solver_finds_the_only_order()
        {
            // one column, back→front: O O R R; capacity 2, one slot — R must go first
            var l = Level(new[] { new[] { "O", "O", "R", "R" } }, new[] { "O", "R" }, slots: 1, capacity: 2);
            var r = LevelSolver.Solve(l);
            Assert.That(r.Status, Is.EqualTo(SolveStatus.Solvable));
            Assert.That(r.Solution, Is.EqualTo(new[] { 1, 0 }));
        }

        [Test]
        public void V6_solver_proves_unsolvable()
        {
            var l = Level(new[] { new[] { "O", "O", "R", "R" } }, new[] { "OR" }, slots: 1, capacity: 2);
            var r = LevelSolver.Solve(l);
            Assert.That(r.Status, Is.EqualTo(SolveStatus.Unsolvable));
            Assert.That(r.DeadEnds, Is.GreaterThan(0));
        }

        [Test]
        public void V6_solver_says_unknown_over_budget_rather_than_guessing()
        {
            var l = Level(new[] { new[] { "O", "O", "R", "R" } }, new[] { "OR" }, slots: 1, capacity: 2);
            Assert.That(LevelSolver.Solve(l, nodeBudget: 0).Status, Is.EqualTo(SolveStatus.Unknown));
        }
    }
}
