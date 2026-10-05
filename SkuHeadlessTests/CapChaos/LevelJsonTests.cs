using System.IO;
using System.Linq;
using Game.Domain;
using NUnit.Framework;

namespace CapsChaos.SkuHeadlessTests.CapChaos
{
    /// <summary>V1 — the structural rules of docs/design/level.schema.json (format v3).</summary>
    public sealed class LevelJsonTests
    {
        private const string Minimal = @"{
  ""formatVersion"": 3,
  ""id"": ""level_0007"",
  ""colors"": [1, 2],
  ""loop"": { ""rows"": 8, ""pickRows"": 1, ""width"": 2, ""feeders"": [ { ""mergeAt"": 6, ""bottles"": [1, 2, 1, 2] } ] },
  ""lanes"": [ [{ ""color"": 1 }], [{ ""color"": 2, ""hidden"": true, ""lockTurns"": 2 }] ]
}";

        [Test]
        public void A_minimal_level_parses_with_defaults()
        {
            var r = LevelJson.Parse(Minimal);
            Assert.That(r.Errors, Is.Empty);
            Assert.That(r.Level!.Id, Is.EqualTo("level_0007"));
            Assert.That(r.Level.Slots, Is.EqualTo(4));
            Assert.That(r.Level.ExtraSlots, Is.EqualTo(2), "two locked slots unless the level says otherwise (R20)");
            Assert.That(r.Level.TrayCapacity, Is.EqualTo(4));
            var loop = r.Level.Loop;
            Assert.That((loop.Rows, loop.Width, loop.PickRows), Is.EqualTo((8, 2, 1)));
            Assert.That(loop.Feeders.Single().MergeAt, Is.EqualTo(6));
            Assert.That(loop.Feeders.Single().Bottles, Is.EqualTo(new[] { CapColor.Red, CapColor.Orange, CapColor.Red, CapColor.Orange }));
            Assert.That(loop.Initial, Is.Null);
            Assert.That(r.Level.Lanes[1][0], Is.EqualTo(CapColor.Orange));
            Assert.That(r.Level.IsHiddenTray(new TrayRef(1, 0)), Is.True);
            Assert.That(r.Level.LockTurns(new TrayRef(1, 0)), Is.EqualTo(2));
        }

        [Test]
        public void Width_defaults_to_four_bottles_a_row()
        {
            var r = LevelJson.Parse(Minimal.Replace(@"""width"": 2, ", ""));
            Assert.That(r.Errors, Is.Empty);
            Assert.That(r.Level!.Loop.Width, Is.EqualTo(LoopDefinition.DefaultWidth));
        }

        [Test]
        public void Initial_rows_are_read_by_row_then_track()
        {
            string rows = string.Join(", ", Enumerable.Repeat("[0, 0]", 7));
            var r = LevelJson.Parse(Minimal.Replace(@"""bottles"": [1, 2, 1, 2] } ]", @"""bottles"": [1, 2] } ], ""initial"": [ [1, 2], " + rows + " ]"));
            Assert.That(r.Errors, Is.Empty);
            Assert.That(r.Level!.Loop.Initial![0], Is.EqualTo(new[] { CapColor.Red, CapColor.Orange }));
            Assert.That(r.Level.Loop.Initial[1], Is.EqualTo(new[] { CapColor.None, CapColor.None }));
        }

        [Test]
        public void Colour_numbers_are_the_CapColor_values()
        {
            Assert.That(new[] { CapColor.None, CapColor.Red, CapColor.Orange, CapColor.Blue, CapColor.Green, CapColor.Purple,
                CapColor.Yellow, CapColor.Cyan, CapColor.Brown }.Select(c => (int)c), Is.EqualTo(Enumerable.Range(0, 9)),
                "designers' tools write these numbers — never renumber");
        }

        [TestCase(1)]
        [TestCase(2)]
        public void A_stack_level_is_rejected_with_a_pointer_to_the_generator(int version)
        {
            var r = LevelJson.Parse(Minimal.Replace("\"formatVersion\": 3", $"\"formatVersion\": {version}"));
            Assert.That(r.Ok, Is.False);
            Assert.That(r.Errors.Single(), Does.Contain("bottle stack").And.Contain("LevelTool -- generate"));
        }

        [TestCase("\"id\": \"level_0007\"", "\"id\": \"L7\"", "$.id: 'L7' must match level_NNNN")]
        [TestCase("\"formatVersion\": 3,", "\"formatVersion\": 3, \"bonus\": 3,", "$.bonus: unknown property")]
        [TestCase("\"formatVersion\": 3,", "\"formatVersion\": 3, \"stack\": {},", "$.stack: unknown property")]
        [TestCase("\"rows\": 8", "\"rows\": 4", "$.loop.rows: 4 outside 8..64")]
        [TestCase("\"width\": 2", "\"width\": 9", "$.loop.width: 9 outside 1..6")]
        [TestCase("\"pickRows\": 1, ", "", "$.loop.pickRows: required")]
        [TestCase("\"bottles\": [1, 2, 1, 2]", "\"bottles\": [1, 0]", "$.loop.feeders[0].bottles[1]: must be a colour number 1..8")]
        [TestCase("\"mergeAt\": 6, ", "", "$.loop.feeders[0].mergeAt: required")]
        [TestCase("\"feeders\": [", "\"speed\": 2, \"feeders\": [", "$.loop.speed: unknown property")]
        [TestCase("[{ \"color\": 1 }]", "[{ \"color\": 0 }]", "$.lanes[0][0].color: must be a colour number 1..8")]
        [TestCase("[{ \"color\": 1 }]", "[\"R\"]", "$.lanes[0][0]: must be a tray { color, size?, hidden?, lockTurns? }")]
        [TestCase("\"hidden\": true", "\"hidden\": 1", "$.lanes[1][0].hidden: must be true or false")]
        [TestCase("\"lockTurns\": 2", "\"lockTurns\": 0", "$.lanes[1][0].lockTurns: 0 outside 1..99")]
        [TestCase("\"colors\": [1, 2]", "\"colors\": [1, 1]", "$.colors[1]: duplicate 1 (Red)")]
        [TestCase("\"formatVersion\": 3,", "\"formatVersion\": 3, \"slots\": 9,", "$.slots: 9 outside 1..6")]
        [TestCase("\"formatVersion\": 3,", "\"formatVersion\": 3, \"slots\": 5, \"extraSlots\": 2,", "$.extraSlots: slots 5 + extraSlots 2 > 6")]
        public void Structural_errors_are_reported_by_path(string find, string replace, string expected)
        {
            var r = LevelJson.Parse(Minimal.Replace(find, replace));
            Assert.That(r.Ok, Is.False);
            Assert.That(r.Errors, Has.Some.Contains(expected));
        }

        [Test]
        public void Initial_rows_must_match_the_belt_size()
        {
            var r = LevelJson.Parse(Minimal.Replace("\"bottles\": [1, 2, 1, 2] } ]", "\"bottles\": [1, 2] } ], \"initial\": [ [1, 2, 1] ]"));
            Assert.That(r.Errors, Has.Some.Contains("$.loop.initial: 1 rows ≠ rows 8"));
            Assert.That(r.Errors, Has.Some.Contains("$.loop.initial[0]: 3 spots ≠ width 2"));
        }

        [Test]
        public void Broken_json_is_one_error_not_an_exception()
        {
            var r = LevelJson.Parse("{ \"id\": ");
            Assert.That(r.Ok, Is.False);
            Assert.That(r.Errors.Single(), Does.StartWith("json:"));
        }

        [Test]
        public void Write_then_parse_round_trips()
        {
            var a = LevelJson.Parse(Minimal).Level!;
            var text = LevelJson.Write(a);
            var b = LevelJson.Parse(text);
            Assert.That(b.Errors, Is.Empty);
            Assert.That(LevelJson.Write(b.Level!), Is.EqualTo(text));
        }

        [Test]
        public void The_writer_puts_one_feeder_row_on_a_line()
        {
            var text = LevelJson.Write(LevelJson.Parse(Minimal).Level!);
            Assert.That(text, Does.Contain("{ \"mergeAt\": 6, \"bottles\": [\n        1, 2,\n        1, 2\n      ] }"));
        }

        /// <summary>The schema file is the contract; the parser must agree with it on the colour numbers and root keys.</summary>
        [Test]
        public void Parser_agrees_with_the_schema_file()
        {
            var schema = JsonReader.Parse(File.ReadAllText(RepoLayout.Path("docs", "design", "level.schema.json")));
            schema.TryGet("$defs", out var defs); defs.TryGet("colorId", out var cc); cc.TryGet("enum", out var en);
            Assert.That(en.Items.Select(i => (CapColor)(int)i.Number), Is.EqualTo(CapColorCodes.All));
            schema.TryGet("properties", out var props); props.TryGet("formatVersion", out var fv); fv.TryGet("const", out var ver);
            Assert.That((int)ver.Number, Is.EqualTo(LevelDefinition.CurrentFormatVersion));
            props.TryGet("loop", out var loopSchema); loopSchema.TryGet("properties", out var loopProps);
            Assert.That(loopProps.Members.Select(m => m.Key).OrderBy(k => k),
                Is.EqualTo(new[] { "feeders", "initial", "pickRows", "rows", "width" }));

            var withAll = Minimal.Replace("\"formatVersion\": 3,",
                "\"$schema\": \"x\", \"formatVersion\": 3, \"slots\": 4, \"extraSlots\": 2, \"trayCapacity\": 4, " +
                "\"links\": [ { \"a\": { \"lane\": 0, \"tray\": 0 }, \"b\": { \"lane\": 1, \"tray\": 0 } } ], " +
                "\"view\": { \"cameraPreset\": \"tall\" }, \"meta\": { \"name\": \"n\", \"difficulty\": \"easy\", \"notes\": \"x\" },");
            var parsed = JsonReader.Parse(withAll);
            Assert.That(parsed.Members.Select(m => m.Key).OrderBy(k => k), Is.EqualTo(props.Members.Select(m => m.Key).OrderBy(k => k)),
                "every schema root property is exercised here");
            Assert.That(LevelJson.Parse(withAll).Errors, Is.Empty);
        }
    }
}
