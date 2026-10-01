using System.IO;
using System.Linq;
using Game.Domain;
using NUnit.Framework;

namespace CapsChaos.SkuHeadlessTests.CapChaos
{
    /// <summary>V1 — the structural rules of docs/design/level.schema.json (format v2), and the v1 reader.</summary>
    public sealed class LevelJsonTests
    {
        private const string Minimal = @"{
  ""formatVersion"": 2,
  ""id"": ""level_0007"",
  ""colors"": [1, 2],
  ""stack"": { ""cols"": 2, ""rows"": 1, ""layers"": [ [[1, 2]], [[1, 2]] ], ""hidden"": [ { ""layer"": 1, ""row"": 0, ""col"": 1 } ] },
  ""lanes"": [ [{ ""color"": 1 }], [{ ""color"": 2, ""hidden"": true, ""lockTurns"": 2 }] ]
}";

        /// <summary>The same level in format v1 (letter codes, lowercase = hidden, root locks).</summary>
        private const string MinimalV1 = @"{
  ""formatVersion"": 1,
  ""id"": ""level_0007"",
  ""colors"": [""R"", ""O""],
  ""stack"": { ""cols"": 2, ""rows"": 1, ""layers"": [ [""RO""], [""Ro""] ] },
  ""lanes"": [ [""R""], [""o""] ],
  ""locks"": [ { ""lane"": 1, ""tray"": 0, ""turns"": 2 } ]
}";

        [Test]
        public void A_minimal_level_parses_with_defaults()
        {
            var r = LevelJson.Parse(Minimal);
            Assert.That(r.Errors, Is.Empty);
            Assert.That(r.Level!.Id, Is.EqualTo("level_0007"));
            Assert.That(r.Level.Slots, Is.EqualTo(3));
            Assert.That(r.Level.TrayCapacity, Is.EqualTo(4));
            Assert.That(r.Level.Stack.At(1, 0, 1), Is.EqualTo(new StackCell(CapColor.Orange, hidden: true)));
            Assert.That(r.Level.Stack.At(1, 0, 0), Is.EqualTo(new StackCell(CapColor.Red, hidden: false)));
            Assert.That(r.Level.Lanes[1][0], Is.EqualTo(CapColor.Orange));
            Assert.That(r.Level.IsHiddenTray(new TrayRef(1, 0)), Is.True);
            Assert.That(r.Level.LockTurns(new TrayRef(1, 0)), Is.EqualTo(2));
        }

        [Test]
        public void Colour_numbers_are_the_CapColor_values()
        {
            Assert.That(new[] { CapColor.None, CapColor.Red, CapColor.Orange, CapColor.Blue, CapColor.Green, CapColor.Purple,
                CapColor.Yellow, CapColor.Cyan, CapColor.Brown }.Select(c => (int)c), Is.EqualTo(Enumerable.Range(0, 9)),
                "designers' tools write these numbers — never renumber");
        }

        [Test]
        public void A_format_v1_file_still_loads_as_the_same_level()
        {
            var v1 = LevelJson.Parse(MinimalV1);
            Assert.That(v1.Errors, Is.Empty);
            Assert.That(LevelJson.Write(v1.Level!), Is.EqualTo(LevelJson.Write(LevelJson.Parse(Minimal).Level!)));
            Assert.That(LevelJson.Write(v1.Level!), Does.Contain("\"formatVersion\": 2"), "writing always upgrades to v2");
        }

        [TestCase("\"id\": \"level_0007\"", "\"id\": \"L7\"", "$.id: 'L7' must match level_NNNN")]
        [TestCase("\"formatVersion\": 2,", "\"formatVersion\": 2, \"bonus\": 3,", "$.bonus: unknown property")]
        [TestCase("\"formatVersion\": 2,", "\"formatVersion\": 2, \"locks\": [],", "$.locks: unknown property")]
        [TestCase("[[1, 2]], [[1, 2]]", "[[1, 2, 1]], [[1, 2]]", "$.stack.layers[0][0]: 3 cells ≠ cols 2")]
        [TestCase("[[1, 2]], [[1, 2]]", "[[1, 9]], [[1, 2]]", "$.stack.layers[0][0][1]: must be a colour number 0..8")]
        [TestCase("\"col\": 1 }", "\"col\": 1 }, { \"layer\": 0, \"row\": 0, \"col\": 5 }", "$.stack.hidden[1].col: 5 outside 0..1")]
        [TestCase("[{ \"color\": 1 }]", "[{ \"color\": 0 }]", "$.lanes[0][0].color: must be a colour number 1..8")]
        [TestCase("[{ \"color\": 1 }]", "[\"R\"]", "$.lanes[0][0]: must be a tray { color, hidden?, lockTurns? }")]
        [TestCase("\"hidden\": true", "\"hidden\": 1", "$.lanes[1][0].hidden: must be true or false")]
        [TestCase("\"lockTurns\": 2", "\"lockTurns\": 0", "$.lanes[1][0].lockTurns: 0 outside 1..99")]
        [TestCase("\"colors\": [1, 2]", "\"colors\": [1, 1]", "$.colors[1]: duplicate 1 (Red)")]
        [TestCase("\"formatVersion\": 2,", "\"formatVersion\": 2, \"slots\": 9,", "$.slots: 9 outside 1..5")]
        public void Structural_errors_are_reported_by_path(string find, string replace, string expected)
        {
            var r = LevelJson.Parse(Minimal.Replace(find, replace));
            Assert.That(r.Ok, Is.False);
            Assert.That(r.Errors, Has.Some.Contains(expected));
        }

        [Test]
        public void A_hidden_entry_must_name_a_bottle()
        {
            var r = LevelJson.Parse(Minimal.Replace("[[1, 2]], [[1, 2]]", "[[1, 2]], [[1, 0]]"));
            Assert.That(r.Errors, Has.Some.Contains("$.stack.hidden[0]: cell (layer 1, row 0, col 1) is empty"));
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

        /// <summary>The schema file is the contract; the parser must agree with it on the colour numbers and root keys.</summary>
        [Test]
        public void Parser_agrees_with_the_schema_file()
        {
            var schema = JsonReader.Parse(File.ReadAllText(RepoLayout.Path("docs", "design", "level.schema.json")));
            schema.TryGet("$defs", out var defs); defs.TryGet("colorId", out var cc); cc.TryGet("enum", out var en);
            Assert.That(en.Items.Select(i => (CapColor)(int)i.Number), Is.EqualTo(CapColorCodes.All));
            schema.TryGet("properties", out var props); props.TryGet("formatVersion", out var fv); fv.TryGet("const", out var ver);
            Assert.That((int)ver.Number, Is.EqualTo(LevelDefinition.CurrentFormatVersion));

            var withAll = Minimal.Replace("\"formatVersion\": 2,",
                "\"$schema\": \"x\", \"formatVersion\": 2, \"slots\": 3, \"trayCapacity\": 4, " +
                "\"links\": [ { \"a\": { \"lane\": 0, \"tray\": 0 }, \"b\": { \"lane\": 1, \"tray\": 0 } } ], " +
                "\"view\": { \"cameraPreset\": \"tall\", \"stackScale\": 1.5 }, \"meta\": { \"name\": \"n\", \"difficulty\": \"easy\", \"notes\": \"x\" },");
            var parsed = JsonReader.Parse(withAll);
            Assert.That(parsed.Members.Select(m => m.Key).OrderBy(k => k), Is.EqualTo(props.Members.Select(m => m.Key).OrderBy(k => k)),
                "every schema root property is exercised here");
            Assert.That(LevelJson.Parse(withAll).Errors, Is.Empty);
        }
    }
}
