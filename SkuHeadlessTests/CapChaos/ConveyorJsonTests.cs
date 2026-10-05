using System.IO;
using System.Linq;
using Game.Domain;
using NUnit.Framework;

namespace CapsChaos.SkuHeadlessTests.CapChaos
{
    /// <summary>The shared top-conveyor files (format v2: a loop and three feeder splines): V1
    /// (docs/design/conveyor.schema.json), V8 and V9 (ConveyorValidator).</summary>
    public sealed class ConveyorJsonTests
    {
        private const string Feeder = @"[ { ""x"": 2, ""z"": 9, ""yRotation"": 180 }, { ""x"": 1.3, ""z"": 4.1, ""yRotation"": 140 } ]";

        internal const string Minimal = @"{
  ""formatVersion"": 2,
  ""id"": ""oval_16"",
  ""rows"": 16,
  ""pickRows"": 4,
  ""loop"": { ""nodes"": [
    { ""x"": 0.9, ""z"": 3.2, ""yRotation"": 270 },
    { ""x"": -0.9, ""z"": 3.2, ""yRotation"": 270 },
    { ""x"": -1.4, ""z"": 3.7, ""yRotation"": 0 },
    { ""x"": 0, ""z"": 4.3, ""yRotation"": 90, ""tangentMode"": 1 }
  ] },
  ""feeders"": [
    { ""side"": ""right"", ""mergeAt"": 11, ""nodes"": " + Feeder + @" },
    { ""side"": ""left"", ""mergeAt"": 7, ""nodes"": " + Feeder + @" },
    { ""side"": ""middle"", ""mergeAt"": 9, ""nodes"": " + Feeder + @" }
  ]
}";

        private static ConveyorParseResult Parse(string json) => ConveyorJson.Parse(json);

        /// <summary>A valid conveyor file with these rule numbers (a plain triangle loop, two-node feeders).</summary>
        internal static string Text(string id, int rows, int pickRows, int[] mergeAt, int width = 4)
        {
            var loop = new[] { new ConveyorNode(1, 3, 270), new ConveyorNode(-1, 3, 0), new ConveyorNode(0, 4.5, 90) };
            var feeders = mergeAt.Select((m, f) => new FeederLayout((FeederSide)f, m,
                new[] { new ConveyorNode(f, 9, 180), new ConveyorNode(f * 0.5, 4.5, 90) })).ToList();
            return ConveyorJson.Write(new ConveyorDefinition(id, rows, width, pickRows, loop, feeders));
        }

        [Test]
        public void A_minimal_conveyor_parses_a_loop_and_three_feeders()
        {
            var r = Parse(Minimal);
            Assert.That(r.Errors, Is.Empty);
            var c = r.Conveyor!;
            Assert.That((c.Id, c.Rows, c.Width, c.PickRows), Is.EqualTo(("oval_16", 16, LoopDefinition.DefaultWidth, 4)));
            Assert.That(c.Loop, Has.Count.EqualTo(4));
            Assert.That(c.Loop[0], Is.EqualTo(new ConveyorNode(0.9, 3.2, 270)), "the first node is where the pick zone starts");
            Assert.That(c.Loop[3].Linear, Is.True, "tangentMode 1 is a sharp corner, as in ConveyorKit");
            Assert.That(c.Feeders.Select(f => f.Side), Is.EqualTo(new[] { FeederSide.Right, FeederSide.Left, FeederSide.Middle }));
            Assert.That(c.MergeAt, Is.EqualTo(new[] { 11, 7, 9 }));
            Assert.That(c.Feeders[0].Nodes, Has.Count.EqualTo(2));
            Assert.That(ConveyorValidator.Validate(c), Is.Empty);
        }

        [TestCase("\"id\": \"oval_16\"", "\"id\": \"Oval-16\"", "$.id: 'Oval-16' must be lowercase letters, digits and _")]
        [TestCase("\"id\": \"oval_16\",", "", "$.id: required")]
        [TestCase("\"formatVersion\": 2", "\"formatVersion\": 3", "$.formatVersion: 3 outside 1..2")]
        [TestCase("\"rows\": 16", "\"rows\": 4", "$.rows: 4 outside 8..64")]
        [TestCase("\"rows\": 16,", "\"rows\": 16, \"width\": 9,", "$.width: 9 outside 1..6")]
        [TestCase("\"pickRows\": 4,", "", "$.pickRows: required")]
        [TestCase("\"rows\": 16,", "\"rows\": 16, \"shape\": \"oval\",", "$.shape: unknown property")]
        [TestCase("\"mergeAt\": 11, ", "", "$.feeders[0].mergeAt: required")]
        [TestCase("\"side\": \"left\"", "\"side\": \"middle\"", "$.feeders[1].side: 'middle' — feeder 1 is the left one")]
        [TestCase("\"mergeAt\": 11,", "\"mergeAt\": 11, \"bottles\": [1],", "$.feeders[0].bottles: unknown property")]
        [TestCase("{ \"x\": 0.9, \"z\": 3.2, \"yRotation\": 270 }", "{ \"x\": 0.9, \"yRotation\": 270 }", "$.loop.nodes[0].z: required")]
        [TestCase("\"tangentMode\": 1", "\"tangentMode\": 2", "$.loop.nodes[3].tangentMode: 2 outside 0..1")]
        [TestCase("\"x\": 0.9,", "\"x\": \"0.9\",", "$.loop.nodes[0].x: must be a number")]
        [TestCase("\"rows\": 16,", "\"rows\": 16, \"speed\": 2,", "$.speed: unknown property")]
        [TestCase("\"rows\": 16,", "\"rows\": 16, \"scale\": 0,", "$.scale: 0 outside 0.1..4")]
        [TestCase("\"rows\": 16,", "\"rows\": 16, \"meta\": { \"author\": \"x\" },", "$.meta.author: unknown property")]
        public void Structural_errors_are_reported_by_path(string find, string replace, string expected)
        {
            var r = Parse(Minimal.Replace(find, replace));
            Assert.That(r.Ok, Is.False);
            Assert.That(r.Errors, Has.Some.Contains(expected));
        }

        [Test]
        public void A_file_has_exactly_three_feeders()
        {
            var two = Minimal.Replace(",\n    { \"side\": \"middle\", \"mergeAt\": 9, \"nodes\": " + Feeder + " }", "");
            Assert.That(Parse(two).Errors, Has.Some.Contains("$.feeders: exactly 3 feeders, in the order right, left, middle"));
        }

        [Test]
        public void A_v1_conveyor_with_a_preset_shape_is_rejected_with_a_pointer_to_the_splines()
        {
            var r = Parse(Minimal.Replace("\"formatVersion\": 2", "\"formatVersion\": 1"));
            Assert.That(r.Ok, Is.False);
            Assert.That(r.Errors.Single(), Does.Contain("splines of nodes"));
        }

        [Test]
        public void V8_merge_points_lie_on_the_track_outside_the_pick_zone_and_are_not_shared()
        {
            var c = new ConveyorDefinition("bad", 8, 4, 1, new[] { 0, 9, 9 });
            var e = ConveyorValidator.Validate(c);
            Assert.That(e, Has.Some.Contains("V8 conveyor bad.feeders[0].mergeAt: 0 is inside the pick zone 0..0"));
            Assert.That(e, Has.Some.Contains("V8 conveyor bad.feeders[1].mergeAt: 9 is not a track position 0..7"));
            Assert.That(e, Has.Some.Contains("V8 conveyor bad.feeders[2].mergeAt: 9 is another feeder's merge point"));
        }

        [Test]
        public void V8_the_pick_zone_leaves_room_for_the_back_straight_and_the_bends()
        {
            var c = new ConveyorDefinition("tight", 8, 4, 2, new int[0]);
            Assert.That(ConveyorValidator.Validate(c), Has.Some.StartsWith("V8 conveyor tight.pickRows: 2 pick rows need rows ≥ 10"));
        }

        [Test]
        public void V9_a_loop_needs_three_knots_a_feeder_two_and_no_two_neighbours_on_one_spot()
        {
            var c = Parse(Minimal).Conveyor!;
            var loop = c.Loop.Take(2).ToList();
            var feeders = c.Feeders.Select(f => f.Side == FeederSide.Middle
                ? new FeederLayout(f.Side, f.MergeAt, new[] { new ConveyorNode(1, 1, 0), new ConveyorNode(1.001, 1, 0) })
                : new FeederLayout(f.Side, f.MergeAt, f.Nodes.Take(1).ToList())).ToList();
            var e = ConveyorValidator.Validate(new ConveyorDefinition("bent", c.Rows, c.Width, c.PickRows, loop, feeders));
            Assert.That(e, Has.Some.StartsWith("V9 conveyor bent.loop.nodes: at least 3 nodes"));
            Assert.That(e, Has.Some.StartsWith("V9 conveyor bent.feeders[0].nodes: at least 2 nodes"));
            Assert.That(e, Has.Some.StartsWith("V9 conveyor bent.feeders[2].nodes[0]: it and node 1 stand on the same spot"));
        }

        [Test]
        public void Write_then_parse_round_trips()
        {
            var text = ConveyorJson.Write(Parse(Minimal.Replace("\"rows\": 16,", "\"rows\": 16, \"meta\": { \"name\": \"Starter\" },")).Conveyor!);
            Assert.That(text, Does.Contain("    { \"x\": 0, \"z\": 4.3, \"yRotation\": 90, \"tangentMode\": 1 }"), "one node per line; smooth is the default");
            var back = Parse(text);
            Assert.That(back.Errors, Is.Empty);
            Assert.That(back.Conveyor!.Name, Is.EqualTo("Starter"));
            Assert.That(ConveyorJson.Write(back.Conveyor), Is.EqualTo(text));
        }

        [Test]
        public void Parser_agrees_with_the_schema_file()
        {
            var schema = JsonReader.Parse(File.ReadAllText(RepoLayout.Path("docs", "design", "conveyor.schema.json")));
            schema.TryGet("properties", out var props); props.TryGet("formatVersion", out var fv); fv.TryGet("const", out var ver);
            Assert.That((int)ver.Number, Is.EqualTo(ConveyorDefinition.CurrentFormatVersion));
            var withAll = Minimal.Replace("\"formatVersion\": 2,",
                "\"$schema\": \"x\", \"formatVersion\": 2, \"width\": 4, \"scale\": 0.5, \"meta\": { \"name\": \"n\", \"notes\": \"x\" },");
            var parsed = JsonReader.Parse(withAll);
            Assert.That(parsed.Members.Select(m => m.Key).OrderBy(k => k), Is.EqualTo(props.Members.Select(m => m.Key).OrderBy(k => k)),
                "every schema root property is exercised here");
            Assert.That(Parse(withAll).Errors, Is.Empty);
        }
    }
}
