using System.Linq;
using Game.Domain;
using NUnit.Framework;

namespace CapsChaos.SkuHeadlessTests.CapChaos
{
    /// <summary>The loop's shape (a conveyor's <c>shape</c>) and V9.</summary>
    public sealed class LoopShapeTests
    {
        private static string WithShape(string shape) =>
            ConveyorJsonTests.Minimal.Replace("\"formatVersion\": 1,", "\"formatVersion\": 1, \"shape\": " + shape + ",");

        [TestCase(LoopShape.Oval)]
        [TestCase(LoopShape.Circle)]
        [TestCase(LoopShape.Triangle)]
        public void Every_preset_is_a_valid_rounded_polygon(string preset)
        {
            var (xs, zs, radii) = LoopShape.Named(preset).Corners(28, 6);
            Assert.That(LoopShape.Check(xs, zs, radii, "shape"), Is.Empty);
        }

        [Test]
        public void A_level_is_drawn_with_its_conveyor_shape()
        {
            var triangle = ConveyorJson.Parse(WithShape("\"triangle\"")).Conveyor!;
            var level = LevelJson.Parse(LevelJsonTests.Minimal.Replace("test_8_1f", "oval_16_1f").Replace("[1, 2, 1, 2]", "[1, 1, 1, 1, 2, 2, 2, 2]"),
                new ConveyorLibrary(new[] { triangle })).Level!;
            Assert.That(level.Shape.Preset, Is.EqualTo(LoopShape.Triangle));
            Assert.That(LevelJson.Write(level), Does.Not.Contain("triangle").And.Not.Contain("loopShape"), "the level only names its conveyor");
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
                var r = ConveyorJson.Parse(WithShape(shape));
                Assert.That(r.Errors, Is.Empty, shape);
                var text = ConveyorJson.Write(r.Conveyor!);
                Assert.That(ConveyorJson.Write(ConveyorJson.Parse(text).Conveyor!), Is.EqualTo(text));
            }
            var custom = ConveyorJson.Parse(WithShape("{ \"points\": [[2, 0], [-2, 0], [0, 2]], \"radius\": 0.5 }")).Conveyor!.Shape;
            Assert.That(custom.Preset, Is.Null);
            Assert.That(custom.Radii, Is.EqualTo(new[] { 0.5, 0.5, 0.5 }), "one radius rounds every corner");
        }

        [TestCase("\"star\"", "$.shape: 'star' not in [oval, circle, triangle]")]
        [TestCase("{ \"points\": [[2, 0], [-2, 0]], \"radius\": 0.5 }", "$.shape.points: at least 3 corners")]
        [TestCase("{ \"points\": [[2, 0], [-2, 0], [0, 2]], \"radius\": 0 }", "$.shape.radius[0]: 0 must be > 0")]
        [TestCase("{ \"points\": [[-2, 0], [2, 0], [0, 2]], \"radius\": 0.5 }", "the belt runs clockwise")]
        [TestCase("{ \"points\": [[2, 0], [-2, 0], [-2, 2], [0, 1], [2, 2]], \"radius\": 0.2 }", "$.shape.points[3]: the shape must be convex")]
        [TestCase("{ \"points\": [[0, 2], [2, 0], [-2, 0]], \"radius\": 0.5 }", "the first edge (corner 0 → 1) is the front one")]
        [TestCase("{ \"points\": [[2, 0], [-2, 0], [0, 2]], \"radius\": 3 }", "rounded more than their edge")]
        [TestCase("{ \"points\": [[2, 0], [-2, 0], [0, 2]], \"radius\": 0.5, \"spin\": 1 }", "$.shape.spin: unknown property")]
        public void A_broken_shape_is_reported(string shape, string expected)
        {
            var r = ConveyorJson.Parse(WithShape(shape));
            Assert.That(r.Ok, Is.False);
            Assert.That(r.Errors, Has.Some.Contains(expected));
        }

        [Test]
        public void V9_also_checks_a_level_built_in_memory()
        {
            var bad = LoopShape.Custom(new[] { -2.0, 2, 0 }, new[] { 0.0, 0, 2 }, new[] { 0.5, 0.5, 0.5 });
            var conveyor = new ConveyorDefinition("bent", 8, 2, 1, new[] { 6 }, bad);
            var l = LevelJson.Parse(LevelJsonTests.Minimal, LevelJsonTests.Library).Level!;
            var level = new LevelDefinition(l.Id, l.Slots, l.TrayCapacity, l.Colors,
                new LoopDefinition(conveyor, l.Loop.Feeders.Select(f => f.Bottles).ToList()), l.Lanes);
            Assert.That(LevelValidator.Validate(level).Where(e => e.StartsWith("V9 conveyor bent.shape")), Is.Not.Empty);
        }
    }
}
