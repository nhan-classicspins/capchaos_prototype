using System.Collections.Generic;
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
            Colors = "ROBG",
            Shape = new List<List<string>>
            {
                new List<string> { "######", "######", "######", "######" },
                new List<string> { "......", "??????", "??????", "......" },
            },
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
            foreach (int lane in gen.Solution) Assert.That(game.Tap(lane).Accepted, Is.True);
            Assert.That(game.Status, Is.EqualTo(GameStatus.Won));

            var reparsed = LevelJson.Parse(LevelJson.Write(gen.Level));
            Assert.That(reparsed.Errors, Is.Empty, "the writer emits schema-valid JSON");
        }

        [Test]
        public void Hidden_cells_in_the_shape_become_lowercase_bottles()
        {
            var gen = new LevelGenerator(new Pcg32(3)).Generate(Spec());
            foreach (char c in gen.Level.Stack.Layers[1][1]) Assert.That(char.IsLower(c), Is.True);
            foreach (char c in gen.Level.Stack.Layers[0][0]) Assert.That(char.IsUpper(c), Is.True);
        }
    }
}
