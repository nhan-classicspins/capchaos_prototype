using System.IO;
using System.Linq;
using Game.Domain;
using NUnit.Framework;

namespace CapsChaos.SkuHeadlessTests.CapChaos
{
    /// <summary>The shared top-conveyor files: V1 (docs/design/conveyor.schema.json) and V8 (ConveyorValidator).</summary>
    public sealed class ConveyorJsonTests
    {
        internal const string Minimal = @"{
  ""formatVersion"": 1,
  ""id"": ""oval_16_1f"",
  ""rows"": 16,
  ""pickRows"": 4,
  ""feeders"": [ { ""mergeAt"": 11 } ]
}";

        [Test]
        public void A_minimal_conveyor_parses_with_defaults()
        {
            var r = ConveyorJson.Parse(Minimal);
            Assert.That(r.Errors, Is.Empty);
            var c = r.Conveyor!;
            Assert.That((c.Id, c.Rows, c.Width, c.PickRows), Is.EqualTo(("oval_16_1f", 16, LoopDefinition.DefaultWidth, 4)));
            Assert.That(c.MergeAt, Is.EqualTo(new[] { 11 }));
            Assert.That(c.Shape.Preset, Is.EqualTo(LoopShape.Oval), "no shape means the oval");
            Assert.That(ConveyorValidator.Validate(c), Is.Empty);
        }

        [TestCase("\"id\": \"oval_16_1f\"", "\"id\": \"Oval-16\"", "$.id: 'Oval-16' must be lowercase letters, digits and _")]
        [TestCase("\"id\": \"oval_16_1f\",", "", "$.id: required")]
        [TestCase("\"formatVersion\": 1", "\"formatVersion\": 2", "$.formatVersion: 2 outside 1..1")]
        [TestCase("\"rows\": 16", "\"rows\": 4", "$.rows: 4 outside 8..64")]
        [TestCase("\"rows\": 16,", "\"rows\": 16, \"width\": 9,", "$.width: 9 outside 1..6")]
        [TestCase("\"pickRows\": 4,", "", "$.pickRows: required")]
        [TestCase("{ \"mergeAt\": 11 }", "{ }", "$.feeders[0].mergeAt: required")]
        [TestCase("{ \"mergeAt\": 11 }", "{ \"mergeAt\": 11, \"bottles\": [1] }", "$.feeders[0].bottles: unknown property")]
        [TestCase("{ \"mergeAt\": 11 } ]", "{ \"mergeAt\": 11 }, { \"mergeAt\": 9 }, { \"mergeAt\": 7 } ]", "$.feeders: 0..2 feeders")]
        [TestCase("\"rows\": 16,", "\"rows\": 16, \"speed\": 2,", "$.speed: unknown property")]
        [TestCase("\"rows\": 16,", "\"rows\": 16, \"meta\": { \"author\": \"x\" },", "$.meta.author: unknown property")]
        public void Structural_errors_are_reported_by_path(string find, string replace, string expected)
        {
            var r = ConveyorJson.Parse(Minimal.Replace(find, replace));
            Assert.That(r.Ok, Is.False);
            Assert.That(r.Errors, Has.Some.Contains(expected));
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
        public void Write_then_parse_round_trips()
        {
            var text = ConveyorJson.Write(ConveyorJson.Parse(Minimal.Replace("\"rows\": 16,", "\"rows\": 16, \"meta\": { \"name\": \"Starter\" },")).Conveyor!);
            Assert.That(text, Does.Contain("\"shape\": \"oval\"").And.Contain("    { \"mergeAt\": 11 }"), "the shape is always written: a library file says what it is");
            var back = ConveyorJson.Parse(text);
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
            var withAll = Minimal.Replace("\"formatVersion\": 1,",
                "\"$schema\": \"x\", \"formatVersion\": 1, \"width\": 4, \"shape\": \"triangle\", \"meta\": { \"name\": \"n\", \"notes\": \"x\" },");
            var parsed = JsonReader.Parse(withAll);
            Assert.That(parsed.Members.Select(m => m.Key).OrderBy(k => k), Is.EqualTo(props.Members.Select(m => m.Key).OrderBy(k => k)),
                "every schema root property is exercised here");
            Assert.That(ConveyorJson.Parse(withAll).Errors, Is.Empty);
        }
    }
}
