using System.Collections.Generic;
using System.Linq;
using Game.Domain;

namespace CapsChaos.SkuHeadlessTests.CapChaos
{
    /// <summary>Terse in-test level authoring: layers of rows (row 0 = back), lanes as strings (lowercase = hidden
    /// tray); <c>locks</c> as {lane, tray, turns}, <c>links</c> as {laneA, trayA, laneB, trayB}.</summary>
    internal static class LevelBuilder
    {
        public static LevelDefinition Level(string[][] layers, string[] lanes, int slots = 3, int capacity = 4, string colors = null,
            int[][] locks = null, int[][] links = null)
        {
            int rows = layers[0].Length, cols = layers[0][0].Length;
            var used = new HashSet<char>();
            foreach (var l in layers) foreach (var r in l) foreach (var c in r) if (c != '.') used.Add(char.ToUpperInvariant(c));
            foreach (var l in lanes) foreach (var c in l) used.Add(char.ToUpperInvariant(c));
            var colorList = CapColorCodes.ParseList(colors ?? new string(CapColorCodes.Codes.Where(used.Contains).ToArray()));
            var stack = StackDefinition.FromRows(cols, rows, layers.Select(l => (IReadOnlyList<string>)l.ToList()).ToList());
            var laneList = lanes.Select(l => (IReadOnlyList<CapColor>)CapColorCodes.ParseList(l.ToUpperInvariant())).ToList();
            var hidden = new List<TrayRef>();
            for (int j = 0; j < lanes.Length; j++)
                for (int t = 0; t < lanes[j].Length; t++) if (char.IsLower(lanes[j][t])) hidden.Add(new TrayRef(j, t));
            var lockList = (locks ?? new int[0][]).Select(l => new TrayLock(new TrayRef(l[0], l[1]), l[2])).ToList();
            var linkList = (links ?? new int[0][]).Select(l => new TrayLink(new TrayRef(l[0], l[1]), new TrayRef(l[2], l[3]))).ToList();
            return new LevelDefinition("level_0001", slots, capacity, colorList, stack, laneList,
                hiddenTrays: hidden, locks: lockList, links: linkList);
        }

        public static string Trace(IEnumerable<GameFact> facts) => string.Join(" ", facts.Select(Describe));

        public static string Describe(GameFact f) => f switch
        {
            TrayPlaced t => $"place(L{t.Lane}->S{t.Slot}:{C(t.Color)})",
            LaneAdvanced a => $"advance(L{a.Lane}:{a.Remaining})",
            BottlePicked p => $"pick({p.X},{p.Z}->S{p.Slot}:{C(p.Color)})",
            StackDropped d => $"drop({d.X},{d.Z}:{d.Height})",
            BottleRevealed r => $"reveal({r.X},{r.Z}:{C(r.Color)})",
            BottleCapped c => $"cap(S{c.Slot}:{c.Filled})",
            TrayPacked k => $"pack(S{k.Slot}:{C(k.Color)})",
            TrayRevealed r => $"trayReveal(L{r.Lane}#{r.Tray}:{C(r.Color)})",
            TrayLockTicked k => $"lock(L{k.Lane}#{k.Tray}:{k.Remaining})",
            LevelCompleted _ => "WIN",
            LevelFailed l => $"FAIL({l.Reason})",
            _ => f.GetType().Name,
        };

        /// <summary>Traces spell colours by their level-file code, so an expected trace reads like the level.</summary>
        private static char C(CapColor c) => CapColorCodes.ToCode(c);
    }
}
