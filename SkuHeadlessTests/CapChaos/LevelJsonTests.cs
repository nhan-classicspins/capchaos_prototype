using System;
using System.IO;
using System.Linq;
using Game.Domain;
using NUnit.Framework;

namespace CapsChaos.SkuHeadlessTests.CapChaos
{
    /// <summary>V1 — the structural rules of docs/design/level.schema.json (format v4: the level's items, naming a
    /// shared conveyor layout).</summary>
    public sealed class LevelJsonTests
    {
        /// <summary>8 rows of 2, a one-row pick zone, one feeder merging at 6.</summary>
        internal static readonly ConveyorDefinition Conveyor = new ConveyorDefinition("test_8_1f", 8, 2, 1, new[] { 6 });
        internal static ConveyorLibrary Library => new ConveyorLibrary(new[] { Conveyor });

        internal const string Minimal = @"{
  ""formatVersion"": 4,
  ""id"": ""level_0007"",
  ""conveyor"": ""test_8_1f"",
  ""colors"": [1, 2],
  ""feeders"": [ { ""bottles"": [1, 2, 1, 2] } ],
  ""lanes"": [ [{ ""color"": 1 }], [{ ""color"": 2, ""hidden"": true, ""lockTurns"": 2 }] ]
}";

        private static LevelParseResult Parse(string json) => LevelJson.Parse(json, Library);

        [Test]
        public void A_minimal_level_parses_with_defaults_on_the_conveyor_it_names()
        {
            var r = Parse(Minimal);
            Assert.That(r.Errors, Is.Empty);
            Assert.That(r.Level!.Id, Is.EqualTo("level_0007"));
            Assert.That(r.Level.Conveyor, Is.SameAs(Conveyor));
            Assert.That(r.Level.Slots, Is.EqualTo(4));
            Assert.That(r.Level.ExtraSlots, Is.EqualTo(2), "two locked slots unless the level says otherwise (R20)");
            Assert.That(r.Level.TrayCapacity, Is.EqualTo(4));
            var loop = r.Level.Loop;
            Assert.That((loop.Rows, loop.Width, loop.PickRows), Is.EqualTo((8, 2, 1)), "the layout is the conveyor's");
            Assert.That(loop.Feeders.Single().MergeAt, Is.EqualTo(6), "the merge point is the conveyor's");
            Assert.That(loop.Feeders.Single().Bottles, Is.EqualTo(new[] { CapColor.Red, CapColor.Orange, CapColor.Red, CapColor.Orange }));
            Assert.That(loop.Initial, Is.Null);
            Assert.That(r.Level.Lanes[1][0], Is.EqualTo(CapColor.Orange));
            Assert.That(r.Level.IsHiddenTray(new TrayRef(1, 0)), Is.True);
            Assert.That(r.Level.LockTurns(new TrayRef(1, 0)), Is.EqualTo(2));
        }

        [Test]
        public void Two_levels_can_share_one_conveyor()
        {
            var a = Parse(Minimal).Level!;
            var b = Parse(Minimal.Replace("level_0007", "level_0008").Replace("[1, 2, 1, 2]", "[2, 1, 2, 1]")).Level!;
            Assert.That(b.Conveyor, Is.SameAs(a.Conveyor));
            Assert.That(b.Loop.Feeders[0].Bottles, Is.Not.EqualTo(a.Loop.Feeders[0].Bottles), "the bottles are each level's own");
        }

        [Test]
        public void Initial_rows_are_read_by_row_then_track()
        {
            string rows = string.Join(", ", Enumerable.Repeat("[0, 0]", 7));
            var r = Parse(Minimal.Replace(@"""bottles"": [1, 2, 1, 2] } ]", @"""bottles"": [1, 2] } ], ""initial"": [ [1, 2], " + rows + " ]"));
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
            var r = Parse(Minimal.Replace("\"formatVersion\": 4", $"\"formatVersion\": {version}"));
            Assert.That(r.Ok, Is.False);
            Assert.That(r.Errors.Single(), Does.Contain("bottle stack").And.Contain("LevelTool -- generate"));
        }

        [Test]
        public void A_v3_level_with_its_conveyor_inline_is_rejected_with_a_pointer_to_the_split()
        {
            var r = Parse(Minimal.Replace("\"formatVersion\": 4", "\"formatVersion\": 3"));
            Assert.That(r.Ok, Is.False);
            Assert.That(r.Errors.Single(), Does.Contain("Conveyors/").And.Contain("\"conveyor\""));
        }

        [TestCase("\"id\": \"level_0007\"", "\"id\": \"L7\"", "$.id: 'L7' must match level_NNNN")]
        [TestCase("\"formatVersion\": 4,", "\"formatVersion\": 4, \"bonus\": 3,", "$.bonus: unknown property")]
        [TestCase("\"formatVersion\": 4,", "\"formatVersion\": 4, \"stack\": {},", "$.stack: unknown property")]
        [TestCase("\"formatVersion\": 4,", "\"formatVersion\": 4, \"loop\": {},", "$.loop: unknown property")]
        [TestCase("\"bottles\": [1, 2, 1, 2]", "\"bottles\": [1, 0]", "$.feeders[0].bottles[1]: must be a colour number 1..8")]
        [TestCase("{ \"bottles\": [1, 2, 1, 2] }", "{ \"mergeAt\": 6, \"bottles\": [1, 2, 1, 2] }", "$.feeders[0].mergeAt: unknown property")]
        [TestCase("{ \"bottles\": [1, 2, 1, 2] }", "{ }", "$.feeders[0].bottles: required")]
        [TestCase("[{ \"color\": 1 }]", "[{ \"color\": 0 }]", "$.lanes[0][0].color: must be a colour number 1..8")]
        [TestCase("[{ \"color\": 1 }]", "[\"R\"]", "$.lanes[0][0]: must be a tray { color, size?, hidden?, lockTurns? }")]
        [TestCase("\"hidden\": true", "\"hidden\": 1", "$.lanes[1][0].hidden: must be true or false")]
        [TestCase("\"lockTurns\": 2", "\"lockTurns\": 0", "$.lanes[1][0].lockTurns: 0 outside 1..99")]
        [TestCase("\"colors\": [1, 2]", "\"colors\": [1, 1]", "$.colors[1]: duplicate 1 (Red)")]
        [TestCase("\"formatVersion\": 4,", "\"formatVersion\": 4, \"slots\": 9,", "$.slots: 9 outside 1..6")]
        [TestCase("\"formatVersion\": 4,", "\"formatVersion\": 4, \"slots\": 5, \"extraSlots\": 2,", "$.extraSlots: slots 5 + extraSlots 2 > 6")]
        [TestCase("{ \"bottles\": [1, 2, 1, 2] }", "{ \"bottles\": [1, 2, 1, 2], \"hiddenRows\": 1 }", "$.feeders[0].hiddenRows: must be an array of queue row indices")]
        [TestCase("{ \"bottles\": [1, 2, 1, 2] }", "{ \"bottles\": [1, 2, 1, 2], \"hiddenRows\": [-1] }", "$.feeders[0].hiddenRows[0]: must be a queue row index ≥ 0")]
        [TestCase("{ \"bottles\": [1, 2, 1, 2] }", "{ \"bottles\": [1, 2, 1, 2], \"hiddenRows\": [1, 1] }", "$.feeders[0].hiddenRows[1]: row 1 is listed twice")]
        [TestCase("\"formatVersion\": 4,", "\"formatVersion\": 4, \"slotLocks\": { \"slot\": 1 },", "$.slotLocks: must be an array of { slot, lockTurns }")]
        [TestCase("\"formatVersion\": 4,", "\"formatVersion\": 4, \"slotLocks\": [ { \"slot\": 1 } ],", "$.slotLocks[0].lockTurns: required")]
        [TestCase("\"formatVersion\": 4,", "\"formatVersion\": 4, \"slotLocks\": [ { \"slot\": 1, \"lockTurns\": 0 } ],", "$.slotLocks[0].lockTurns: 0 outside 1..99")]
        [TestCase("\"formatVersion\": 4,", "\"formatVersion\": 4, \"slotLocks\": [ { \"slot\": 1, \"turns\": 2 } ],", "$.slotLocks[0].turns: unknown property")]
        public void Structural_errors_are_reported_by_path(string find, string replace, string expected)
        {
            var r = Parse(Minimal.Replace(find, replace));
            Assert.That(r.Ok, Is.False);
            Assert.That(r.Errors, Has.Some.Contains(expected));
        }

        [TestCase("\"conveyor\": \"test_8_1f\",", "", "$.conveyor: required")]
        [TestCase("\"test_8_1f\"", "\"oval_99\"", "$.conveyor: 'oval_99' is not a conveyor (Conveyors/oval_99.json not found)")]
        [TestCase("{ \"bottles\": [1, 2, 1, 2] } ]", "{ \"bottles\": [1, 2] }, { \"bottles\": [1, 2] } ]",
            "$.feeders: 2 queue(s), but conveyor 'test_8_1f' has 1 feeder(s)")]
        public void The_level_must_fit_the_conveyor_it_names(string find, string replace, string expected)
        {
            var r = Parse(Minimal.Replace(find, replace));
            Assert.That(r.Ok, Is.False);
            Assert.That(r.Errors, Has.Some.Contains(expected));
        }

        [Test]
        public void A_level_uses_the_first_N_feeders_of_its_conveyor()
        {
            var three = new ConveyorDefinition("three", 16, 2, 4, new[] { 11, 7, 9 });
            var r = LevelJson.Parse(Minimal.Replace("test_8_1f", "three"), new ConveyorLibrary(new[] { three }));
            Assert.That(r.Errors, Is.Empty);
            Assert.That(r.Level!.Loop.Feeders.Select(f => f.MergeAt), Is.EqualTo(new[] { 11 }), "one queue ⇒ the right feeder only");
            Assert.That(r.Level.Conveyor.FeederCount, Is.EqualTo(3), "the conveyor keeps its other feeders for other levels");
        }

        [Test]
        public void Initial_rows_must_match_the_belt_size()
        {
            var r = Parse(Minimal.Replace("\"bottles\": [1, 2, 1, 2] } ]", "\"bottles\": [1, 2] } ], \"initial\": [ [1, 2, 1] ]"));
            Assert.That(r.Errors, Has.Some.Contains("$.initial: 1 rows ≠ rows 8 of conveyor 'test_8_1f'"));
            Assert.That(r.Errors, Has.Some.Contains("$.initial[0]: 3 spots ≠ width 2"));
        }

        [Test]
        public void Broken_json_is_one_error_not_an_exception()
        {
            var r = Parse("{ \"id\": ");
            Assert.That(r.Ok, Is.False);
            Assert.That(r.Errors.Single(), Does.StartWith("json:"));
        }

        [Test]
        public void The_conveyor_id_can_be_read_before_the_conveyors_are_loaded()
        {
            Assert.That(LevelJson.ConveyorIdOf(Minimal), Is.EqualTo("test_8_1f"));
            Assert.That(LevelJson.ConveyorIdOf("{ \"id\": "), Is.Null);
            Assert.That(LevelJson.ConveyorIdOf("{ \"conveyor\": 3 }"), Is.Null);
        }

        [Test]
        public void Write_then_parse_round_trips()
        {
            var a = Parse(Minimal).Level!;
            var text = LevelJson.Write(a);
            Assert.That(text, Does.Contain("\"conveyor\": \"test_8_1f\""));
            var b = Parse(text);
            Assert.That(b.Errors, Is.Empty);
            Assert.That(LevelJson.Write(b.Level!), Is.EqualTo(text));
        }

        [Test]
        public void Slot_locks_are_read_and_written_back_unchanged()
        {
            var r = Parse(Minimal.Replace("\"formatVersion\": 4,",
                "\"formatVersion\": 4, \"slotLocks\": [ { \"slot\": 3, \"lockTurns\": 5 }, { \"slot\": 1, \"lockTurns\": 2 } ],"));
            Assert.That(r.Errors, Is.Empty);
            Assert.That((r.Level!.SlotLockTurns(3), r.Level.SlotLockTurns(1), r.Level.SlotLockTurns(0)), Is.EqualTo((5, 2, 0)));
            var text = LevelJson.Write(r.Level);
            Assert.That(text, Does.Contain("  \"slotLocks\": [{ \"slot\": 3, \"lockTurns\": 5 }, { \"slot\": 1, \"lockTurns\": 2 }],\n"));
            Assert.That(LevelJson.Write(Parse(text).Level!), Is.EqualTo(text));
            Assert.That(LevelJson.Write(Parse(Minimal).Level!), Does.Not.Contain("slotLocks"), "a level without one writes none");
        }

        [Test]
        public void Hidden_feeder_rows_are_read_and_written_back_unchanged()
        {
            var r = Parse(Minimal.Replace("{ \"bottles\": [1, 2, 1, 2] }", "{ \"bottles\": [1, 2, 1, 2], \"hiddenRows\": [1] }"));
            Assert.That(r.Errors, Is.Empty);
            var feeder = r.Level!.Loop.Feeders[0];
            Assert.That((feeder.IsHiddenRow(0), feeder.IsHiddenRow(1)), Is.EqualTo((false, true)), "row 1 = bottles 2 and 3 (width 2)");
            var text = LevelJson.Write(r.Level);
            Assert.That(text, Does.Contain("      1, 2\n    ], \"hiddenRows\": [1] }"));
            Assert.That(LevelJson.Write(Parse(text).Level!), Is.EqualTo(text));
            Assert.That(LevelJson.Write(Parse(Minimal).Level!), Does.Not.Contain("hiddenRows"), "a feeder without one writes none");
        }

        [Test]
        public void The_writer_puts_one_feeder_row_on_a_line()
        {
            var text = LevelJson.Write(Parse(Minimal).Level!);
            Assert.That(text, Does.Contain("{ \"bottles\": [\n      1, 2,\n      1, 2\n    ] }"));
        }

        [Test]
        public void A_level_whose_conveyor_was_built_in_code_can_not_be_written()
        {
            var l = Parse(Minimal).Level!;
            var anonymous = new LoopDefinition(l.Loop.Rows, l.Loop.Width, l.Loop.PickRows, l.Loop.Feeders);
            var level = new LevelDefinition(l.Id, l.Slots, l.TrayCapacity, l.Colors, anonymous, l.Lanes);
            Assert.Throws<InvalidOperationException>(() => LevelJson.Write(level));
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

            string rows = string.Join(", ", Enumerable.Repeat("[0, 0]", 8));
            var withAll = Minimal.Replace("\"formatVersion\": 4,",
                "\"$schema\": \"x\", \"formatVersion\": 4, \"slots\": 4, \"extraSlots\": 2, \"slotLocks\": [ { \"slot\": 1, \"lockTurns\": 3 } ], \"trayCapacity\": 4, \"initial\": [" + rows + "], " +
                "\"links\": [ { \"a\": { \"lane\": 0, \"tray\": 0 }, \"b\": { \"lane\": 1, \"tray\": 0 } } ], " +
                "\"view\": { \"cameraPreset\": \"tall\" }, \"meta\": { \"name\": \"n\", \"difficulty\": \"easy\", \"notes\": \"x\" },");
            var parsed = JsonReader.Parse(withAll);
            Assert.That(parsed.Members.Select(m => m.Key).OrderBy(k => k), Is.EqualTo(props.Members.Select(m => m.Key).OrderBy(k => k)),
                "every schema root property is exercised here");
            Assert.That(Parse(withAll).Errors, Is.Empty);
        }
    }
}
