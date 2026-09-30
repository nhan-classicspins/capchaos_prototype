using System.IO;
using System.Linq;
using Game.Domain;
using NUnit.Framework;

namespace CapsChaos.SkuHeadlessTests.CapChaos
{
    /// <summary>V1 — the structural rules of docs/design/level.schema.json.</summary>
    public sealed class LevelJsonTests
    {
        private const string Minimal = @"{
  ""formatVersion"": 1,
  ""id"": ""level_0007"",
  ""colors"": [""R"", ""O""],
  ""stack"": { ""cols"": 2, ""rows"": 1, ""layers"": [ [""RO""], [""ro""] ] },
  ""lanes"": [ [""R""], [""O""] ]
}";

        [Test]
        public void A_minimal_level_parses_with_defaults()
        {
            var r = LevelJson.Parse(Minimal);
            Assert.That(r.Errors, Is.Empty);
            Assert.That(r.Level!.Id, Is.EqualTo("level_0007"));
            Assert.That(r.Level.Slots, Is.EqualTo(3));
            Assert.That(r.Level.TrayCapacity, Is.EqualTo(4));
            Assert.That(r.Level.Stack.At(1, 0, 1), Is.EqualTo('o'));
            Assert.That(r.Level.Lanes[1][0], Is.EqualTo('O'));
        }

        [TestCase("\"id\": \"level_0007\"", "\"id\": \"L7\"", "$.id: 'L7' must match level_NNNN")]
        [TestCase("\"formatVersion\": 1,", "\"formatVersion\": 1, \"bonus\": 3,", "$.bonus: unknown property")]
        [TestCase("[\"RO\"], [\"ro\"]", "[\"ROO\"], [\"ro\"]", "$.stack.layers[0][0]: length 3 ≠ cols 2")]
        [TestCase("[\"RO\"], [\"ro\"]", "[\"RX\"], [\"ro\"]", "$.stack.layers[0][0][1]: 'X'")]
        [TestCase("[ [\"R\"], [\"O\"] ]", "[ [\"R\"], [\"Q\"] ]", "$.lanes[1][0]: must be one of")]
        [TestCase("\"colors\": [\"R\", \"O\"]", "\"colors\": [\"R\", \"R\"]", "$.colors[1]: duplicate 'R'")]
        [TestCase("\"formatVersion\": 1,", "\"formatVersion\": 1, \"slots\": 9,", "$.slots: 9 outside 1..5")]
        public void Structural_errors_are_reported_by_path(string find, string replace, string expected)
        {
            var r = LevelJson.Parse(Minimal.Replace(find, replace));
            Assert.That(r.Ok, Is.False);
            Assert.That(r.Errors, Has.Some.Contains(expected));
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

        /// <summary>The schema file is the contract; the parser must agree with it on the colour codes and root keys.</summary>
        [Test]
        public void Parser_agrees_with_the_schema_file()
        {
            var schema = JsonReader.Parse(File.ReadAllText(RepoLayout.Path("docs", "design", "level.schema.json")));
            schema.TryGet("$defs", out var defs); defs.TryGet("colorCode", out var cc); cc.TryGet("enum", out var en);
            Assert.That(string.Concat(en.Items.Select(i => i.String)), Is.EqualTo(CapColors.Codes));

            schema.TryGet("properties", out var props);
            var withAll = Minimal.Replace("\"formatVersion\": 1,",
                "\"$schema\": \"x\", \"formatVersion\": 1, \"slots\": 3, \"trayCapacity\": 4, " +
                "\"view\": { \"cameraPreset\": \"tall\", \"stackScale\": 1.5 }, \"meta\": { \"name\": \"n\", \"difficulty\": \"easy\", \"notes\": \"x\" },");
            var parsed = JsonReader.Parse(withAll);
            Assert.That(parsed.Members.Select(m => m.Key).OrderBy(k => k), Is.EqualTo(props.Members.Select(m => m.Key).OrderBy(k => k)),
                "every schema root property is exercised here");
            Assert.That(LevelJson.Parse(withAll).Errors, Is.Empty);
        }
    }
}
