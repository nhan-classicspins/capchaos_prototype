using System.Collections.Generic;
using System.Linq;
using Game.Domain;

namespace CapsChaos.SkuHeadlessTests.CapChaos
{
    /// <summary>Terse in-test level authoring: layers of rows (row 0 = back), lanes as strings.</summary>
    internal static class LevelBuilder
    {
        public static LevelDefinition Level(string[][] layers, string[] lanes, int slots = 3, int capacity = 4, string colors = null)
        {
            int rows = layers[0].Length, cols = layers[0][0].Length;
            var used = new HashSet<char>();
            foreach (var l in layers) foreach (var r in l) foreach (var c in r) if (c != '.') used.Add(char.ToUpperInvariant(c));
            foreach (var l in lanes) foreach (var c in l) used.Add(c);
            var colorList = (colors ?? new string(CapColors.Codes.Where(used.Contains).ToArray())).ToList();
            var stack = new StackDefinition(cols, rows, layers.Select(l => (IReadOnlyList<string>)l.ToList()).ToList());
            var laneList = lanes.Select(l => (IReadOnlyList<char>)l.ToList()).ToList();
            return new LevelDefinition("level_0001", slots, capacity, colorList, stack, laneList);
        }

        public static string Trace(IEnumerable<GameFact> facts) => string.Join(" ", facts.Select(Describe));

        public static string Describe(GameFact f) => f switch
        {
            TrayPlaced t => $"place(L{t.Lane}->S{t.Slot}:{t.Color})",
            LaneAdvanced a => $"advance(L{a.Lane}:{a.Remaining})",
            BottlePicked p => $"pick({p.X},{p.Z}->S{p.Slot}:{p.Color})",
            StackDropped d => $"drop({d.X},{d.Z}:{d.Height})",
            BottleRevealed r => $"reveal({r.X},{r.Z}:{r.Color})",
            BottleCapped c => $"cap(S{c.Slot}:{c.Filled})",
            TrayPacked k => $"pack(S{k.Slot}:{k.Color})",
            LevelCompleted _ => "WIN",
            LevelFailed l => $"FAIL({l.Reason})",
            _ => f.GetType().Name,
        };
    }
}
