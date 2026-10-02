using System.Collections.Generic;
using System.Linq;
using ClassicSpins.PrototypeFramework.Domain;
using Game.Domain;
using NUnit.Framework;

namespace CapsChaos.SkuHeadlessTests.CapChaos
{
    public sealed class LevelGeneratorTests
    {
        private static LevelSpec Spec() => new LevelSpec
        {
            Id = "level_0099",
            Colors = CapColorCodes.ParseList("ROBG"),
            Rows = 20, PickRows = 5,
            Feeders = new List<(int, int)> { (17, 48), (12, 16) },
            Greed = 0.5, Clustering = 0.3,
        };

        [Test]
        public void The_same_seed_generates_the_same_level()
        {
            var a = new LevelGenerator(new Pcg32(42)).Generate(Spec());
            var b = new LevelGenerator(new Pcg32(42)).Generate(Spec());
            Assert.That(LevelJson.Write(a.Level), Is.EqualTo(LevelJson.Write(b.Level)));
        }

        [Test]
        public void A_generated_level_is_valid_and_its_construction_order_wins()
        {
            var gen = new LevelGenerator(new Pcg32(7)).Generate(Spec());
            Assert.That(LevelValidator.Validate(gen.Level), Is.Empty);

            var game = new CapChaosGame(gen.Level);
            game.Settle();
            foreach (int lane in gen.Solution)
            {
                Assert.That(game.Tap(lane).Accepted, Is.True);
                game.Settle();
            }
            Assert.That(game.Status, Is.EqualTo(GameStatus.Won), "tap, wait until the board is quiet, tap the next");

            var reparsed = LevelJson.Parse(LevelJson.Write(gen.Level));
            Assert.That(reparsed.Errors, Is.Empty, "the writer emits schema-valid JSON");
        }

        [TestCase(1)]
        [TestCase(5)]
        public void The_spec_slot_count_reaches_the_level_and_its_solution_still_wins(int slots)
        {
            var spec = Spec();
            spec.Slots = slots;
            var gen = new LevelGenerator(new Pcg32(11)).Generate(spec);
            Assert.That(gen.Level.Slots, Is.EqualTo(slots));
            Assert.That(LevelSolver.Prove(gen.Level).Status, Is.EqualTo(SolveStatus.Solvable));
        }

        [Test]
        public void The_feeders_carry_every_bottle_in_rows_of_one_colour_and_the_belt_starts_empty()
        {
            var gen = new LevelGenerator(new Pcg32(3)).Generate(Spec());
            var loop = gen.Level.Loop;
            Assert.That(loop.Initial, Is.Null, "the generator authors feeders only");
            Assert.That(loop.Feeders.Select(f => f.MergeAt), Is.EqualTo(new[] { 17, 12 }));
            Assert.That(loop.Feeders.Select(f => f.Bottles.Count), Is.EqualTo(new[] { 48, 16 }));
            foreach (var f in loop.Feeders)
                for (int row = 0; row < f.Bottles.Count / loop.Width; row++)
                    Assert.That(f.Bottles.Skip(row * loop.Width).Take(loop.Width).Distinct().Count(), Is.EqualTo(1),
                        "R4: a queue row is one colour");
            var belt = new CapChaosGame(gen.Level).Belt;
            Assert.That(belt.Count, Is.Zero, "every bottle starts in a feeder");
            Assert.That(belt.FeederRemainingTotal, Is.EqualTo(64));
        }
    }
}
