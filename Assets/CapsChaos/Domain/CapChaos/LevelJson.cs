using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Game.Domain
{
    public sealed class LevelParseResult
    {
        public LevelDefinition Level { get; }
        public IReadOnlyList<string> Errors { get; }
        public bool Ok => Level != null && Errors.Count == 0;

        public LevelParseResult(LevelDefinition level, IReadOnlyList<string> errors) { Level = level; Errors = errors; }
    }

    /// <summary>What the format reader yields: the content part of a level (colours, belt, lanes, tray modifiers).</summary>
    internal sealed class LevelContent
    {
        public readonly List<CapColor> Colors = new List<CapColor>();
        public LoopDefinition Loop;
        public List<IReadOnlyList<CapColor>> Lanes;
        public readonly List<TrayRef> HiddenTrays = new List<TrayRef>();
        public readonly Dictionary<TrayRef, TraySize> Sizes = new Dictionary<TrayRef, TraySize>();
        public List<TrayLock> Locks = new List<TrayLock>();
        public List<TrayLink> Links = new List<TrayLink>();
    }

    /// <summary>
    /// Level JSON ⇄ <see cref="LevelDefinition"/>. <see cref="Parse"/> is validator leg V1: every
    /// structural rule of <c>docs/design/level.schema.json</c>, reported all at once with a JSON path.
    /// The schema file is the contract; this class mirrors it and a headless test pins the two together.
    /// <para>Format v4 (2026-10-05) splits a level in two: the top conveyor's LAYOUT (rows, width, pick zone, shape,
    /// merge points) is a shared file in <c>ConveyorConfig/</c> that the level names by id (<c>conveyor</c>, see
    /// <see cref="ConveyorJson"/>); the level file keeps the ITEMS — the bottles in each feeder (<c>feeders</c>, one
    /// per conveyor feeder, in its order), optional <c>initial</c> rows, the lanes and their trays. Every colour is the
    /// NUMBER of its <see cref="CapColor"/> (0 = empty spot), and every flag is a named field — <c>hidden</c> /
    /// <c>lockTurns</c> on a tray object, <c>links</c> by <c>{lane, tray}</c>. v3 files (conveyor inline) are rejected
    /// with a pointer to the split; v1/v2 files describe a stack, which has no belt equivalent: they are rejected with
    /// a pointer to the generator, never guessed at.</para>
    /// </summary>
    public static class LevelJson
    {
        private static readonly HashSet<string> RootKeys = new HashSet<string>(StringComparer.Ordinal)
            { "$schema", "formatVersion", "id", "conveyor", "slots", "extraSlots", "slotLocks", "trayCapacity", "colors", "feeders", "initial", "lanes", "links", "view", "meta" };
        private static readonly HashSet<string> FeederKeys = new HashSet<string>(StringComparer.Ordinal) { "bottles", "hiddenRows", "lockedRows" };
        private static readonly HashSet<string> RowLockKeys = new HashSet<string>(StringComparer.Ordinal) { "row", "lockTurns" };
        private static readonly HashSet<string> TrayKeys = new HashSet<string>(StringComparer.Ordinal) { "color", "size", "hidden", "lockTurns" };
        private static readonly HashSet<string> SlotLockKeys = new HashSet<string>(StringComparer.Ordinal) { "slot", "lockTurns" };
        private static readonly HashSet<string> LinkKeys = new HashSet<string>(StringComparer.Ordinal) { "a", "b" };
        private static readonly HashSet<string> RefKeys = new HashSet<string>(StringComparer.Ordinal) { "lane", "tray" };
        private static readonly HashSet<string> ViewKeys = new HashSet<string>(StringComparer.Ordinal) { "cameraPreset" };
        private static readonly HashSet<string> MetaKeys = new HashSet<string>(StringComparer.Ordinal) { "name", "difficulty", "notes", "solution" };
        private static readonly string[] CameraPresets = { "default", "tall", "wide" };
        private static readonly string[] Difficulties = { "tutorial", "easy", "medium", "hard", "breather" };
        public const int MaxLockTurns = 99;
        private const int MaxColorId = 8;

        /// <summary>Parse a level file; the conveyor it names is looked up in <paramref name="conveyors"/> (an unknown id
        /// is a V1 error on <c>$.conveyor</c>).</summary>
        public static LevelParseResult Parse(string json, ConveyorLibrary conveyors)
        {
            if (conveyors == null) throw new ArgumentNullException(nameof(conveyors));
            var errors = new List<string>();
            JsonValue root;
            try { root = JsonReader.Parse(json); }
            catch (JsonParseException e) { errors.Add("json: " + e.Message); return new LevelParseResult(null, errors); }

            if (root.Kind != JsonKind.Object) { errors.Add("$: must be an object"); return new LevelParseResult(null, errors); }

            int formatVersion = Int(root, "formatVersion", "$", errors, required: true, min: 1, max: LevelDefinition.CurrentFormatVersion,
                fallback: LevelDefinition.CurrentFormatVersion);
            if (formatVersion == 3)
            {
                errors.Add($"$.formatVersion: 3 keeps the conveyor inside the level (loop, view.loopShape); format {LevelDefinition.CurrentFormatVersion} " +
                           $"moves its layout to a shared file in {ConveyorJson.Folder}/ that the level names (\"conveyor\") — see GDD §6.2");
                return new LevelParseResult(null, errors);
            }
            if (formatVersion < LevelDefinition.CurrentFormatVersion)
            {
                errors.Add($"$.formatVersion: {formatVersion} describes a bottle stack, which format {LevelDefinition.CurrentFormatVersion} replaced " +
                           "with the oval belt — regenerate the level (`dotnet run --project Tools/LevelTool -- generate`) or re-author it");
                return new LevelParseResult(null, errors);
            }
            Unknown(root, RootKeys, "$", errors);
            string id = Str(root, "id", "$", errors, required: true);
            if (id != null && !IsLevelId(id)) errors.Add($"$.id: '{id}' must match level_NNNN");
            int slots = Int(root, "slots", "$", errors, required: false, min: 1, max: LevelDefinition.MaxSlots, fallback: LevelDefinition.DefaultSlots);
            int extra = Int(root, "extraSlots", "$", errors, required: false, min: 0, max: LevelDefinition.MaxSlots - 1,
                fallback: LevelDefinition.DefaultExtraSlots);
            if (slots + extra > LevelDefinition.MaxSlots) errors.Add($"$.extraSlots: slots {slots} + extraSlots {extra} > {LevelDefinition.MaxSlots}");
            int cap = Int(root, "trayCapacity", "$", errors, required: false, min: 2, max: 6, fallback: LevelDefinition.DefaultTrayCapacity);
            var slotLocks = ReadSlotLocks(root, errors);

            var content = ReadContent(root, conveyors, errors);
            var lanes = content.Lanes;
            string preset = "default";
            if (root.TryGet("view", out var view))
            {
                if (view.Kind != JsonKind.Object) errors.Add("$.view: must be an object");
                else
                {
                    Unknown(view, ViewKeys, "$.view", errors);
                    var p = Str(view, "cameraPreset", "$.view", errors, required: false);
                    if (p != null) { if (Array.IndexOf(CameraPresets, p) < 0) errors.Add($"$.view.cameraPreset: '{p}' not in [default, tall, wide]"); else preset = p; }
                }
            }

            string name = null, difficulty = null, notes = null;
            List<int> solution = null;
            if (root.TryGet("meta", out var meta))
            {
                if (meta.Kind != JsonKind.Object) errors.Add("$.meta: must be an object");
                else
                {
                    Unknown(meta, MetaKeys, "$.meta", errors);
                    name = Str(meta, "name", "$.meta", errors, required: false);
                    difficulty = Str(meta, "difficulty", "$.meta", errors, required: false);
                    if (difficulty != null && Array.IndexOf(Difficulties, difficulty) < 0)
                        errors.Add($"$.meta.difficulty: '{difficulty}' not in [tutorial, easy, medium, hard, breather]");
                    notes = Str(meta, "notes", "$.meta", errors, required: false);
                    if (meta.TryGet("solution", out var sol))
                    {
                        if (sol.Kind != JsonKind.Array) errors.Add("$.meta.solution: must be an array of lane indices");
                        else
                        {
                            solution = new List<int>();
                            for (int i = 0; i < sol.Items.Count; i++)
                            {
                                var v = sol.Items[i];
                                bool okLane = v.Kind == JsonKind.Number && v.Number == Math.Floor(v.Number) && v.Number >= 0
                                              && (lanes == null || v.Number < lanes.Count);
                                if (!okLane) errors.Add($"$.meta.solution[{i}]: must be a lane index");
                                else solution.Add((int)v.Number);
                            }
                        }
                    }
                }
            }

            if (errors.Count > 0 || id == null || content.Loop == null || lanes == null) return new LevelParseResult(null, errors);
            var level = new LevelDefinition(id, slots, cap, content.Colors, content.Loop, lanes, preset, name, difficulty, notes,
                LevelDefinition.CurrentFormatVersion, solution, content.HiddenTrays, content.Locks, content.Links, extra, content.Sizes, slotLocks);
            return new LevelParseResult(level, errors);
        }

        /// <summary>R22: <c>slotLocks</c> — <c>[{ "slot": 3, "lockTurns": 5 }]</c>. Which slot it may name is V10.</summary>
        private static List<SlotLock> ReadSlotLocks(JsonValue root, List<string> errors)
        {
            var locks = new List<SlotLock>();
            if (!root.TryGet("slotLocks", out var arr)) return locks;
            if (arr.Kind != JsonKind.Array) { errors.Add("$.slotLocks: must be an array of { slot, lockTurns }"); return locks; }
            for (int i = 0; i < arr.Items.Count; i++)
            {
                var item = arr.Items[i];
                string path = $"$.slotLocks[{i}]";
                if (item.Kind != JsonKind.Object) { errors.Add(path + ": must be { slot, lockTurns }"); continue; }
                Unknown(item, SlotLockKeys, path, errors);
                int slot = Int(item, "slot", path, errors, required: true, min: 0, max: LevelDefinition.MaxSlots - 1, fallback: -1);
                int turns = Int(item, "lockTurns", path, errors, required: true, min: 1, max: MaxLockTurns, fallback: 0);
                if (slot >= 0 && turns > 0) locks.Add(new SlotLock(slot, turns));
            }
            return locks;
        }

        // ── content ───────────────────────────────────────────────────────────────────────────
        private static LevelContent ReadContent(JsonValue root, ConveyorLibrary conveyors, List<string> errors)
        {
            var c = new LevelContent();
            if (!root.TryGet("colors", out var colorsNode)) errors.Add("$.colors: required");
            else if (colorsNode.Kind != JsonKind.Array) errors.Add("$.colors: must be an array");
            else
            {
                if (colorsNode.Items.Count < 1 || colorsNode.Items.Count > MaxColorId) errors.Add("$.colors: 1..8 entries");
                for (int i = 0; i < colorsNode.Items.Count; i++)
                {
                    var col = ColorId(colorsNode.Items[i], $"$.colors[{i}]", errors, allowEmpty: false);
                    if (col == CapColor.None) continue;
                    if (c.Colors.Contains(col)) errors.Add($"$.colors[{i}]: duplicate {Name(col)}");
                    else c.Colors.Add(col);
                }
            }
            c.Loop = ReadBelt(root, conveyors, errors);
            c.Lanes = ReadLanes(root, c, errors);
            c.Links = ReadLinks(root, errors);
            return c;
        }

        /// <summary>The conveyor a level file names, or null when it names none (or is not JSON) — so a loader can fetch
        /// the conveyor files before it parses the levels.</summary>
        public static string ConveyorIdOf(string json)
        {
            try
            {
                var root = JsonReader.Parse(json);
                return root.Kind == JsonKind.Object && root.TryGet("conveyor", out var v) && v.Kind == JsonKind.String ? v.String : null;
            }
            catch (JsonParseException) { return null; }
        }

        // the conveyor's own rules (pick zone, merge points, splines) are V8/V9 (ConveyorValidator); here: the level's
        // bottles fit the conveyor it names — a queue for each of the first N feeders it uses (N = how many queues it
        // lists, at most the conveyor's), initial rows of the belt's size
        private static LoopDefinition ReadBelt(JsonValue root, ConveyorLibrary conveyors, List<string> errors)
        {
            int before = errors.Count;
            string id = Str(root, "conveyor", "$", errors, required: true);
            ConveyorDefinition conveyor = null;
            if (id != null && !conveyors.TryGet(id, out conveyor))
                errors.Add($"$.conveyor: '{id}' is not a conveyor ({ConveyorJson.FileOf(id)} not found)");

            var feeders = new List<IReadOnlyList<CapColor>>();
            var hidden = new List<IEnumerable<int>>();
            var locked = new List<IReadOnlyDictionary<int, int>>();
            if (root.TryGet("feeders", out var fn))
            {
                if (fn.Kind != JsonKind.Array) errors.Add("$.feeders: must be an array of { bottles }");
                else
                {
                    for (int f = 0; f < fn.Items.Count; f++)
                    {
                        var o = fn.Items[f];
                        string fp = $"$.feeders[{f}]";
                        if (o.Kind != JsonKind.Object) { errors.Add(fp + ": must be { bottles }"); continue; }
                        Unknown(o, FeederKeys, fp, errors);
                        var bottles = new List<CapColor>();
                        if (!o.TryGet("bottles", out var bn)) errors.Add(fp + ".bottles: required");
                        else if (bn.Kind != JsonKind.Array) errors.Add(fp + ".bottles: must be an array of colour numbers");
                        else for (int i = 0; i < bn.Items.Count; i++)
                        {
                            var col = ColorId(bn.Items[i], $"{fp}.bottles[{i}]", errors, allowEmpty: false);
                            if (col != CapColor.None) bottles.Add(col);
                        }
                        feeders.Add(bottles);
                        hidden.Add(ReadHiddenRows(o, fp, errors));
                        locked.Add(ReadLockedRows(o, fp, errors));
                    }
                    if (conveyor != null && fn.Items.Count > conveyor.FeederCount)
                        errors.Add($"$.feeders: {fn.Items.Count} queue(s), but conveyor '{conveyor.Id}' has {conveyor.FeederCount} feeder(s) — " +
                                   "the level uses the first N, in the conveyor's order (right, left, middle)");
                }
            }

            List<IReadOnlyList<CapColor>> initial = null;
            if (root.TryGet("initial", out var init))
            {
                if (init.Kind != JsonKind.Array) errors.Add("$.initial: must be an array of rows");
                else
                {
                    if (conveyor != null && init.Items.Count != conveyor.Rows)
                        errors.Add($"$.initial: {init.Items.Count} rows ≠ rows {conveyor.Rows} of conveyor '{conveyor.Id}'");
                    initial = new List<IReadOnlyList<CapColor>>();
                    for (int r = 0; r < init.Items.Count; r++)
                    {
                        var row = init.Items[r];
                        string rp = $"$.initial[{r}]";
                        if (row.Kind != JsonKind.Array) { errors.Add(rp + ": must be an array of colour numbers"); continue; }
                        if (conveyor != null && row.Items.Count != conveyor.Width) { errors.Add($"{rp}: {row.Items.Count} spots ≠ width {conveyor.Width}"); continue; }
                        var spots = new List<CapColor>();
                        for (int k = 0; k < row.Items.Count; k++) spots.Add(ColorId(row.Items[k], $"{rp}[{k}]", errors, allowEmpty: true));
                        initial.Add(spots);
                    }
                }
            }
            return errors.Count == before && conveyor != null ? new LoopDefinition(conveyor, feeders, initial, hidden, locked) : null;
        }

        /// <summary>R24: a feeder's <c>lockedRows</c> — <c>[{ "row": 3, "lockTurns": 5 }]</c>, each row once. Whether a row
        /// exists is V8.</summary>
        private static Dictionary<int, int> ReadLockedRows(JsonValue feeder, string fp, List<string> errors)
        {
            var rows = new Dictionary<int, int>();
            if (!feeder.TryGet("lockedRows", out var ln)) return rows;
            if (ln.Kind != JsonKind.Array) { errors.Add(fp + ".lockedRows: must be an array of { row, lockTurns }"); return rows; }
            for (int i = 0; i < ln.Items.Count; i++)
            {
                var item = ln.Items[i];
                string path = $"{fp}.lockedRows[{i}]";
                if (item.Kind != JsonKind.Object) { errors.Add(path + ": must be { row, lockTurns }"); continue; }
                Unknown(item, RowLockKeys, path, errors);
                int row = Int(item, "row", path, errors, required: true, min: 0, max: int.MaxValue, fallback: -1);
                int turns = Int(item, "lockTurns", path, errors, required: true, min: 1, max: MaxLockTurns, fallback: 0);
                if (row < 0 || turns <= 0) continue;
                if (rows.ContainsKey(row)) errors.Add($"{path}.row: row {row} is locked twice");
                else rows[row] = turns;
            }
            return rows;
        }

        /// <summary>R23: a feeder's <c>hiddenRows</c> — queue row indices, each once. Whether a row exists is V8.</summary>
        private static List<int> ReadHiddenRows(JsonValue feeder, string fp, List<string> errors)
        {
            var rows = new List<int>();
            if (!feeder.TryGet("hiddenRows", out var hn)) return rows;
            if (hn.Kind != JsonKind.Array) { errors.Add(fp + ".hiddenRows: must be an array of queue row indices"); return rows; }
            for (int i = 0; i < hn.Items.Count; i++)
            {
                var v = hn.Items[i];
                if (v.Kind != JsonKind.Number || v.Number != Math.Floor(v.Number) || v.Number < 0)
                    errors.Add($"{fp}.hiddenRows[{i}]: must be a queue row index ≥ 0");
                else if (rows.Contains((int)v.Number)) errors.Add($"{fp}.hiddenRows[{i}]: row {(int)v.Number} is listed twice");
                else rows.Add((int)v.Number);
            }
            return rows;
        }

        private static List<IReadOnlyList<CapColor>> ReadLanes(JsonValue root, LevelContent c, List<string> errors)
        {
            if (!root.TryGet("lanes", out var ln)) { errors.Add("$.lanes: required"); return null; }
            if (ln.Kind != JsonKind.Array) { errors.Add("$.lanes: must be an array"); return null; }
            if (ln.Items.Count < 1 || ln.Items.Count > 4) errors.Add("$.lanes: 1..4 lanes");
            var lanes = new List<IReadOnlyList<CapColor>>();
            for (int j = 0; j < ln.Items.Count; j++)
            {
                var lane = ln.Items[j];
                if (lane.Kind != JsonKind.Array) { errors.Add($"$.lanes[{j}]: must be an array"); continue; }
                if (lane.Items.Count < 1) errors.Add($"$.lanes[{j}]: at least one tray");
                var trays = new List<CapColor>();
                for (int t = 0; t < lane.Items.Count; t++)
                {
                    var tr = lane.Items[t];
                    string tp = $"$.lanes[{j}][{t}]";
                    if (tr.Kind != JsonKind.Object) { errors.Add(tp + ": must be a tray { color, size?, hidden?, lockTurns? }"); continue; }
                    Unknown(tr, TrayKeys, tp, errors);
                    CapColor color = CapColor.None;
                    if (!tr.TryGet("color", out var cv)) errors.Add(tp + ".color: required");
                    else color = ColorId(cv, tp + ".color", errors, allowEmpty: false);
                    if (color == CapColor.None) continue;
                    var at = new TrayRef(j, trays.Count);
                    if (tr.TryGet("hidden", out var hv))
                    {
                        if (hv.Kind != JsonKind.Bool) errors.Add(tp + ".hidden: must be true or false");
                        else if (hv.Bool) c.HiddenTrays.Add(at);
                    }
                    if (tr.TryGet("size", out _))
                    {
                        int size = Int(tr, "size", tp, errors, required: false, min: (int)TraySize.S, max: (int)TraySize.XL, fallback: 0);
                        if (size > (int)TraySize.S) c.Sizes[at] = (TraySize)size;
                    }
                    if (tr.TryGet("lockTurns", out _))
                    {
                        int turns = Int(tr, "lockTurns", tp, errors, required: false, min: 1, max: MaxLockTurns, fallback: 0);
                        if (turns > 0) c.Locks.Add(new TrayLock(at, turns));
                    }
                    trays.Add(color);
                }
                lanes.Add(trays);
            }
            return lanes;
        }

        // whether a link points at real neighbouring trays is V7 (LevelValidator); this only checks its shape
        private static List<TrayLink> ReadLinks(JsonValue root, List<string> errors)
        {
            var links = new List<TrayLink>();
            if (!root.TryGet("links", out var node)) return links;
            if (node.Kind != JsonKind.Array) { errors.Add("$.links: must be an array"); return links; }
            for (int i = 0; i < node.Items.Count; i++)
            {
                var o = node.Items[i];
                string path = $"$.links[{i}]";
                if (o.Kind != JsonKind.Object) { errors.Add(path + ": must be { a, b }"); continue; }
                Unknown(o, LinkKeys, path, errors);
                bool okA = ReadRef(o, "a", path, errors, out var a), okB = ReadRef(o, "b", path, errors, out var b);
                if (okA && okB) links.Add(new TrayLink(a, b));
            }
            return links;
        }

        /// <summary><c>"a": { "lane": 0, "tray": 2 }</c>.</summary>
        private static bool ReadRef(JsonValue obj, string name, string path, List<string> errors, out TrayRef tray)
        {
            tray = default;
            string p = $"{path}.{name}";
            if (!obj.TryGet(name, out var v)) { errors.Add(p + ": required"); return false; }
            if (v.Kind != JsonKind.Object) { errors.Add(p + ": must be { lane, tray }"); return false; }
            Unknown(v, RefKeys, p, errors);
            int lane = Int(v, "lane", p, errors, required: true, min: 0, max: 3, fallback: -1);
            int t = Int(v, "tray", p, errors, required: true, min: 0, max: 999, fallback: -1);
            if (lane < 0 || t < 0) return false;
            tray = new TrayRef(lane, t);
            return true;
        }

        /// <summary>A colour number: the value of its <see cref="CapColor"/> (1 Red … 8 Brown); 0 = empty, where allowed.</summary>
        private static CapColor ColorId(JsonValue v, string path, List<string> errors, bool allowEmpty)
        {
            int min = allowEmpty ? 0 : 1;
            if (v.Kind != JsonKind.Number || v.Number != Math.Floor(v.Number) || v.Number < min || v.Number > MaxColorId)
            {
                errors.Add($"{path}: must be a colour number {min}..{MaxColorId} ({(allowEmpty ? "0 empty, " : "")}1 Red, 2 Orange, 3 Blue, 4 Green, 5 Purple, 6 Yellow, 7 Cyan, 8 Brown)");
                return CapColor.None;
            }
            return (CapColor)(int)v.Number;
        }

        /// <summary>How a message names a colour: its number and its name, e.g. <c>3 (Blue)</c>.</summary>
        public static string Name(CapColor c) => $"{(int)c} ({c})";

        // ── writer (generator output, format v4) — stable, diff-friendly layout: one feeder row / one lane per line ──
        /// <summary>The level file. Its conveyor is written separately (<see cref="ConveyorJson.Write"/>) — the level only
        /// names it, so a level whose conveyor was built in code (no id) can not be written.</summary>
        public static string Write(LevelDefinition level)
        {
            if (level.Conveyor.Id == null)
                throw new InvalidOperationException($"{level.Id}: its conveyor was built in code (no id), so the level file could not name it");
            var sb = new StringBuilder();
            sb.Append("{\n");
            // levels live in Assets/CapsChaos/Content/Configs/LevelConfig/ — five levels below the repo root
            sb.Append("  \"$schema\": \"../../../../../docs/design/level.schema.json\",\n");
            sb.Append($"  \"formatVersion\": {LevelDefinition.CurrentFormatVersion},\n");
            sb.Append($"  \"id\": {Q(level.Id)},\n");
            sb.Append($"  \"conveyor\": {Q(level.Conveyor.Id)},\n");
            sb.Append($"  \"slots\": {level.Slots},\n");
            sb.Append($"  \"extraSlots\": {level.ExtraSlots},\n");
            if (level.SlotLocks.Count > 0)
            {
                var locks = new List<string>();
                foreach (var l in level.SlotLocks) locks.Add($"{{ \"slot\": {l.Slot}, \"lockTurns\": {l.Turns} }}");
                sb.Append("  \"slotLocks\": [").Append(string.Join(", ", locks)).Append("],\n");
            }
            sb.Append($"  \"trayCapacity\": {level.TrayCapacity},\n");
            sb.Append("  \"colors\": [").Append(Numbers(level.Colors)).Append("],\n");

            var lp = level.Loop;
            sb.Append("  \"feeders\": [");
            if (lp.Feeders.Count == 0) sb.Append("]");
            else
            {
                sb.Append("\n");
                for (int f = 0; f < lp.Feeders.Count; f++)
                {
                    var fd = lp.Feeders[f];
                    sb.Append("    { \"bottles\": [\n");
                    // one feeder row (width bottles) per line: the file reads like the queue looks
                    for (int i = 0; i < fd.Bottles.Count; i += lp.Width)
                    {
                        var row = new List<CapColor>();
                        for (int k = i; k < Math.Min(i + lp.Width, fd.Bottles.Count); k++) row.Add(fd.Bottles[k]);
                        sb.Append("      ").Append(Numbers(row)).Append(i + lp.Width < fd.Bottles.Count ? ",\n" : "\n");
                    }
                    sb.Append("    ]");
                    if (fd.HiddenRows.Count > 0)
                    {
                        var rows = new List<int>(fd.HiddenRows);
                        rows.Sort();
                        sb.Append(", \"hiddenRows\": [").Append(string.Join(", ", rows)).Append(']');
                    }
                    if (fd.LockedRows.Count > 0)
                    {
                        var rows = new List<int>(fd.LockedRows.Keys);
                        rows.Sort();
                        var locks = new List<string>();
                        foreach (int r in rows) locks.Add($"{{ \"row\": {r}, \"lockTurns\": {fd.LockedRows[r]} }}");
                        sb.Append(", \"lockedRows\": [").Append(string.Join(", ", locks)).Append(']');
                    }
                    sb.Append(" }").Append(f < lp.Feeders.Count - 1 ? ",\n" : "\n");
                }
                sb.Append("  ]");
            }
            sb.Append(",\n");
            if (lp.Initial != null)
            {
                sb.Append("  \"initial\": [\n");
                for (int r = 0; r < lp.Initial.Count; r++)
                    sb.Append("    [").Append(Numbers(lp.Initial[r])).Append(r < lp.Initial.Count - 1 ? "],\n" : "]\n");
                sb.Append("  ],\n");
            }

            sb.Append("  \"lanes\": [\n");
            for (int j = 0; j < level.Lanes.Count; j++)
            {
                var trays = new List<string>();
                for (int t = 0; t < level.Lanes[j].Count; t++)
                {
                    var at = new TrayRef(j, t);
                    var tray = new StringBuilder($"{{ \"color\": {(int)level.Lanes[j][t]}");
                    if (level.IsHiddenTray(at)) tray.Append(", \"hidden\": true");
                    var size = level.SizeOf(at);
                    if (size != TraySize.S) tray.Append($", \"size\": {(int)size}");
                    int turns = level.LockTurns(at);
                    if (turns > 0) tray.Append($", \"lockTurns\": {turns}");
                    trays.Add(tray.Append(" }").ToString());
                }
                sb.Append("    [").Append(string.Join(", ", trays)).Append(j < level.Lanes.Count - 1 ? "],\n" : "]\n");
            }
            sb.Append("  ],\n");
            if (level.Links.Count > 0)
            {
                sb.Append("  \"links\": [\n");
                for (int i = 0; i < level.Links.Count; i++)
                {
                    var l = level.Links[i];
                    sb.Append($"    {{ \"a\": {{ \"lane\": {l.A.Lane}, \"tray\": {l.A.Index} }}, \"b\": {{ \"lane\": {l.B.Lane}, \"tray\": {l.B.Index} }} }}")
                      .Append(i < level.Links.Count - 1 ? ",\n" : "\n");
                }
                sb.Append("  ],\n");
            }
            sb.Append($"  \"view\": {{ \"cameraPreset\": {Q(level.CameraPreset)} }}");
            var meta = new List<string>();
            if (level.Name != null) meta.Add($"\"name\": {Q(level.Name)}");
            if (level.Difficulty != null) meta.Add($"\"difficulty\": {Q(level.Difficulty)}");
            if (level.Notes != null) meta.Add($"\"notes\": {Q(level.Notes)}");
            if (level.Solution != null) meta.Add("\"solution\": [" + string.Join(", ", level.Solution) + "]");
            if (meta.Count > 0) sb.Append(",\n  \"meta\": { ").Append(string.Join(", ", meta)).Append(" }");
            sb.Append("\n}\n");
            return sb.ToString();
        }

        private static string Numbers(IEnumerable<CapColor> colors)
        {
            var parts = new List<string>();
            foreach (var c in colors) parts.Add(((int)c).ToString(CultureInfo.InvariantCulture));
            return string.Join(", ", parts);
        }

        public static bool IsLevelId(string id)
        {
            if (id.Length != 10 || !id.StartsWith("level_", StringComparison.Ordinal)) return false;
            for (int i = 6; i < 10; i++) if (id[i] < '0' || id[i] > '9') return false;
            return true;
        }

        // ── helpers ───────────────────────────────────────────────────────────────────────────
        internal static string Q(string s)
        {
            var sb = new StringBuilder("\"");
            foreach (char c in s)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        else sb.Append(c);
                        break;
                }
            }
            return sb.Append('"').ToString();
        }

        internal static void Unknown(JsonValue obj, HashSet<string> allowed, string path, List<string> errors)
        {
            foreach (var m in obj.Members)
                if (!allowed.Contains(m.Key)) errors.Add($"{path}.{m.Key}: unknown property");
        }

        internal static int Int(JsonValue obj, string name, string path, List<string> errors, bool required, int min, int max, int fallback)
        {
            if (!obj.TryGet(name, out var v)) { if (required) errors.Add($"{path}.{name}: required"); return fallback; }
            if (v.Kind != JsonKind.Number || v.Number != Math.Floor(v.Number)) { errors.Add($"{path}.{name}: must be an integer"); return fallback; }
            if (v.Number < min || v.Number > max) { errors.Add($"{path}.{name}: {v.Number} outside {min}..{max}"); return fallback; }
            return (int)v.Number;
        }

        internal static string Str(JsonValue obj, string name, string path, List<string> errors, bool required)
        {
            if (!obj.TryGet(name, out var v)) { if (required) errors.Add($"{path}.{name}: required"); return null; }
            if (v.Kind != JsonKind.String) { errors.Add($"{path}.{name}: must be a string"); return null; }
            return v.String;
        }

    }
}
