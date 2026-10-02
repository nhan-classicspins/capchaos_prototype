using System.Linq;
using Game.Domain;
using NUnit.Framework;

namespace CapsChaos.SkuHeadlessTests.CapChaos
{
    /// <summary>The loop's shape (level <c>view.loopShape</c>) and V9.</summary>
    public sealed class LoopShapeTests
    {
        private const string Minimal = @"{
  ""formatVersion"": 3,
  ""id"": ""level_0007"",
  ""colors"": [1, 2],
  ""loop"": { ""rows"": 8, ""pickRows"": 1, ""width"": 2, ""feeders"": [ { ""mergeAt"": 6, ""bottles"": [1, 2, 1, 2] } ] },
  ""lanes"": [ [{ ""color"": 1 }], [{ ""color"": 2 }] ]
}";

        private static string WithShape(string shape) =>
            Minimal.Replace("\"formatVersion\": 3,", "\"formatVersion\": 3, \"view\": { \"loopShape\": " + shape + " },");

        [TestCase(LoopShape.Oval)]
        [TestCase(LoopShape.Circle)]
        [TestCase(LoopShape.Triangle)]
        public void Every_preset_is_a_valid_rounded_polygon(string preset)
        {
            var (xs, zs, radii) = LoopShape.Named(preset).Corners(28, 6);
            Assert.That(LoopShape.Check(xs, zs, radii, "shape"), Is.Empty);
        }

        [Test]
        public void No_shape_means_the_oval()
        {
            var level = LevelJson.Parse(Minimal).Level!;
            Assert.That(level.Shape.Preset, Is.EqualTo(LoopShape.Oval));
            Assert.That(LevelJson.Write(level), Does.Not.Contain("loopShape"), "the default is not written");
        }

        [Test]
        public void The_oval_straights_take_the_pick_zone_share_of_the_loop()
        {
            var (xs, _, _) = LoopShape.Default.Corners(28, 6);
            double a = xs[0] - 1, straight = 2 * a, length = 2 * straight + 2 * System.Math.PI;
            Assert.That(straight / length, Is.EqualTo(6.0 / 28).Within(1e-9));
        }

        [Test]
        public void A_preset_and_a_custom_shape_round_trip()
        {
            foreach (var shape in new[] { "\"triangle\"", "{ \"points\": [[2, 0], [-2, 0], [-1, 2], [1, 2]], \"radius\": [0.5, 0.5, 0.4, 0.4] }" })
            {
                var r = LevelJson.Parse(WithShape(shape));
                Assert.That(r.Errors, Is.Empty, shape);
                var text = LevelJson.Write(r.Level!);
                Assert.That(LevelJson.Write(LevelJson.Parse(text).Level!), Is.EqualTo(text));
            }
            var custom = LevelJson.Parse(WithShape("{ \"points\": [[2, 0], [-2, 0], [0, 2]], \"radius\": 0.5 }")).Level!.Shape;
            Assert.That(custom.Preset, Is.Null);
            Assert.That(custom.Radii, Is.EqualTo(new[] { 0.5, 0.5, 0.5 }), "one radius rounds every corner");
        }

        [TestCase("\"star\"", "$.view.loopShape: 'star' not in [oval, circle, triangle]")]
        [TestCase("{ \"points\": [[2, 0], [-2, 0]], \"radius\": 0.5 }", "$.view.loopShape.points: at least 3 corners")]
        [TestCase("{ \"points\": [[2, 0], [-2, 0], [0, 2]], \"radius\": 0 }", "$.view.loopShape.radius[0]: 0 must be > 0")]
        [TestCase("{ \"points\": [[-2, 0], [2, 0], [0, 2]], \"radius\": 0.5 }", "the belt runs clockwise")]
        [TestCase("{ \"points\": [[2, 0], [-2, 0], [-2, 2], [0, 1], [2, 2]], \"radius\": 0.2 }", "$.view.loopShape.points[3]: the shape must be convex")]
        [TestCase("{ \"points\": [[0, 2], [2, 0], [-2, 0]], \"radius\": 0.5 }", "the first edge (corner 0 → 1) is the front one")]
        [TestCase("{ \"points\": [[2, 0], [-2, 0], [0, 2]], \"radius\": 3 }", "rounded more than their edge")]
        [TestCase("{ \"points\": [[2, 0], [-2, 0], [0, 2]], \"radius\": 0.5, \"spin\": 1 }", "$.view.loopShape.spin: unknown property")]
        public void A_broken_shape_is_reported(string shape, string expected)
        {
            var r = LevelJson.Parse(WithShape(shape));
            Assert.That(r.Ok, Is.False);
            Assert.That(r.Errors, Has.Some.Contains(expected));
        }

        [Test]
        public void V9_also_checks_a_level_built_in_memory()
        {
            var bad = LoopShape.Custom(new[] { -2.0, 2, 0 }, new[] { 0.0, 0, 2 }, new[] { 0.5, 0.5, 0.5 });
            var l = LevelJson.Parse(Minimal).Level!;
            var level = new LevelDefinition(l.Id, l.Slots, l.TrayCapacity, l.Colors, l.Loop, l.Lanes, loopShape: bad);
            Assert.That(LevelValidator.Validate(level).Where(e => e.StartsWith("V9")), Is.Not.Empty);
        }
    }
}
