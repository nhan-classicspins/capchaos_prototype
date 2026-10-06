using System.Collections.Generic;
using System.Linq;
using Game.Domain;

namespace CapsChaos.SkuHeadlessTests.CapChaos
{
    /// <summary>Terse in-test level authoring: the belt's starting rows as strings (row 0 first — it starts at track
    /// position 0, in the pick zone; one char per track, '.' = empty spot), padded with empty rows to <c>rows</c>;
    /// lanes as strings (lowercase = hidden tray); <c>feeders</c> as bottle strings joining at <c>mergeAt</c>;
    /// <c>locks</c> as {lane, tray, turns}, <c>slotLocks</c> as {slot, turns}, <c>links</c> as {laneA, trayA, laneB, trayB}, <c>sizes</c> as {lane, tray, size 1–4}.</summary>
    internal static class LevelBuilder
    {
        /// <summary>The conveyor every built level runs on — its layout comes from the builder's arguments.</summary>
        public const string ConveyorId = "test_loop";

        /// <summary>The library a written built level parses back against: just its own conveyor.</summary>
        public static ConveyorLibrary Conveyors(LevelDefinition level) => new ConveyorLibrary(new[] { level.Conveyor });

        public static LevelDefinition Level(string[] belt, string[] lanes, int slots = 3, int capacity = 4, string colors = null,
            int[][] locks = null, int[][] links = null, int rows = 8, int pickRows = 1, string[] feeders = null, int[] mergeAt = null, int width = 0, int extraSlots = 0, int[][] sizes = null, int[][] slotLocks = null)
        {
            if (width == 0) width = belt.Length > 0 ? belt[0].Length : LoopDefinition.DefaultWidth;
            var used = new HashSet<char>();
            foreach (var r in belt) foreach (var c in r) if (c != '.') used.Add(c);
            foreach (var f in feeders ?? new string[0]) foreach (var c in f) used.Add(c);
            foreach (var l in lanes) foreach (var c in l) used.Add(char.ToUpperInvariant(c));
            var colorList = CapColorCodes.ParseList(colors ?? new string(CapColorCodes.Codes.Where(used.Contains).ToArray()));

            var initial = new List<IReadOnlyList<CapColor>>();
            for (int r = 0; r < System.Math.Max(rows, belt.Length); r++)
                initial.Add(Enumerable.Range(0, width)
                    .Select(k => r < belt.Length && belt[r][k] != '.' ? CapColorCodes.Parse(belt[r][k]) : CapColor.None).ToList());
            var feederList = feeders ?? new string[0];
            var conveyor = new ConveyorDefinition(ConveyorId, initial.Count, width, pickRows,
                feederList.Select((f, i) => mergeAt?[i] ?? rows - 1 - i).ToList());
            var loop = new LoopDefinition(conveyor,
                feederList.Select(f => (IReadOnlyList<CapColor>)CapColorCodes.ParseList(f)).ToList(), belt.Length > 0 ? initial : null);

            var laneList = lanes.Select(l => (IReadOnlyList<CapColor>)CapColorCodes.ParseList(l.ToUpperInvariant())).ToList();
            var hidden = new List<TrayRef>();
            for (int j = 0; j < lanes.Length; j++)
                for (int t = 0; t < lanes[j].Length; t++) if (char.IsLower(lanes[j][t])) hidden.Add(new TrayRef(j, t));
            var lockList = (locks ?? new int[0][]).Select(l => new TrayLock(new TrayRef(l[0], l[1]), l[2])).ToList();
            var linkList = (links ?? new int[0][]).Select(l => new TrayLink(new TrayRef(l[0], l[1]), new TrayRef(l[2], l[3]))).ToList();
            return new LevelDefinition("level_0001", slots, capacity, colorList, loop, laneList,
                hiddenTrays: hidden, locks: lockList, links: linkList, extraSlots: extraSlots,
                traySizes: (sizes ?? new int[0][]).ToDictionary(z => new TrayRef(z[0], z[1]), z => (TraySize)z[2]),
                slotLocks: (slotLocks ?? new int[0][]).Select(z => new SlotLock(z[0], z[1])).ToList());
        }

        /// <summary>The belt as strings, one per row id (not per position), '.' = empty.</summary>
        public static string Rows(LoopBelt belt) =>
            string.Join(" ", Enumerable.Range(0, belt.Rows).Select(r =>
                new string(Enumerable.Range(0, belt.Width).Select(k => CapColorCodes.ToCode(belt.At(r, k))).ToArray())));

        public static string Trace(IEnumerable<GameFact> facts) => string.Join(" ", facts.Select(Describe));

        public static string Describe(GameFact f) => f switch
        {
            TrayPlaced t => $"place(L{t.Lane}->S{t.Slot}:{C(t.Color)})",
            LaneAdvanced a => $"advance(L{a.Lane}:{a.Remaining})",
            BottlePicked p => $"pick({p.Row},{p.Track}->S{p.Slot}:{C(p.Color)})",
            BottleFed d => $"feed(F{d.Feeder}.{d.Track}->{d.Row}:{C(d.Color)})",
            SlotUnlocked u => $"unlock(S{u.Slot})",
            SlotsRanOut _ => "RANOUT",
            BottleCapped c => $"cap(S{c.Slot}:{c.Filled})",
            TrayPacked k => $"pack(S{k.Slot}:{C(k.Color)})",
            TrayRevealed r => $"trayReveal(L{r.Lane}#{r.Tray}:{C(r.Color)})",
            TrayLockTicked k => $"lock(L{k.Lane}#{k.Tray}:{k.Remaining})",
            SlotLockTicked k => $"slotLock(S{k.Slot}:{k.Remaining})",
            LevelCompleted _ => "WIN",
            LevelFailed l => $"FAIL({l.Reason})",
            _ => f.GetType().Name,
        };

        /// <summary>Traces spell colours by their level-file code, so an expected trace reads like the level.</summary>
        private static char C(CapColor c) => CapColorCodes.ToCode(c);
    }
}
