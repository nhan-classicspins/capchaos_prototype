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

    /// <summary>
    /// Level JSON ⇄ <see cref="LevelDefinition"/>. <see cref="Parse"/> is validator leg V1: every
    /// structural rule of <c>docs/design/level.schema.json</c>, reported all at once with a JSON path.
    /// The schema file is the contract; this class mirrors it and a headless test pins the two together.
    /// </summary>
    public static class LevelJson
    {
        private static readonly HashSet<string> RootKeys = new HashSet<string>(StringComparer.Ordinal)
            { "$schema", "formatVersion", "id", "slots", "trayCapacity", "colors", "stack", "lanes", "locks", "links", "view", "meta" };
        private static readonly HashSet<string> LockKeys = new HashSet<string>(StringComparer.Ordinal) { "lane", "tray", "turns" };
        private static readonly HashSet<string> LinkKeys = new HashSet<string>(StringComparer.Ordinal) { "a", "b" };
        public const int MaxLockTurns = 99;
        private static readonly HashSet<string> StackKeys = new HashSet<string>(StringComparer.Ordinal) { "cols", "rows", "layers" };
        private static readonly HashSet<string> ViewKeys = new HashSet<string>(StringComparer.Ordinal) { "cameraPreset", "stackScale" };
        private static readonly HashSet<string> MetaKeys = new HashSet<string>(StringComparer.Ordinal) { "name", "difficulty", "notes", "solution" };
        private static readonly string[] CameraPresets = { "default", "tall", "wide" };
        private static readonly string[] Difficulties = { "tutorial", "easy", "medium", "hard", "breather" };

        public static LevelParseResult Parse(string json)
        {
            var errors = new List<string>();
            JsonValue root;
            try { root = JsonReader.Parse(json); }
            catch (JsonParseException e) { errors.Add("json: " + e.Message); return new LevelParseResult(null, errors); }

            if (root.Kind != JsonKind.Object) { errors.Add("$: must be an object"); return new LevelParseResult(null, errors); }
            Unknown(root, RootKeys, "$", errors);

            int formatVersion = Int(root, "formatVersion", "$", errors, required: true, min: 1, max: 1, fallback: 1);
            string id = Str(root, "id", "$", errors, required: true);
            if (id != null && !IsLevelId(id)) errors.Add($"$.id: '{id}' must match level_NNNN");
            int slots = Int(root, "slots", "$", errors, required: false, min: 1, max: 5, fallback: LevelDefinition.DefaultSlots);
            int cap = Int(root, "trayCapacity", "$", errors, required: false, min: 2, max: 6, fallback: LevelDefinition.DefaultTrayCapacity);

            var colors = new List<CapColor>();
            if (!root.TryGet("colors", out var colorsNode)) errors.Add("$.colors: required");
            else if (colorsNode.Kind != JsonKind.Array) errors.Add("$.colors: must be an array");
            else
            {
                if (colorsNode.Items.Count < 1 || colorsNode.Items.Count > 8) errors.Add("$.colors: 1..8 entries");
                for (int i = 0; i < colorsNode.Items.Count; i++)
                {
                    var c = ColorCode(colorsNode.Items[i], $"$.colors[{i}]", errors);
                    if (c == CapColor.None) continue;
                    if (colors.Contains(c)) errors.Add($"$.colors[{i}]: duplicate '{CapColorCodes.ToCode(c)}'");
                    else colors.Add(c);
                }
            }

            StackDefinition stack = ParseStack(root, errors);
            var hidden = new List<TrayRef>();
            var lanes = ParseLanes(root, hidden, errors);
            var locks = ParseLocks(root, errors);
            var links = ParseLinks(root, errors);

            string preset = "default"; double scale = 1.0;
            if (root.TryGet("view", out var view))
            {
                if (view.Kind != JsonKind.Object) errors.Add("$.view: must be an object");
                else
                {
                    Unknown(view, ViewKeys, "$.view", errors);
                    var p = Str(view, "cameraPreset", "$.view", errors, required: false);
                    if (p != null) { if (Array.IndexOf(CameraPresets, p) < 0) errors.Add($"$.view.cameraPreset: '{p}' not in [default, tall, wide]"); else preset = p; }
                    if (view.TryGet("stackScale", out var s))
                    {
                        if (s.Kind != JsonKind.Number || s.Number <= 0 || s.Number > 2) errors.Add("$.view.stackScale: number in (0, 2]");
                        else scale = s.Number;
                    }
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

            if (errors.Count > 0 || id == null || stack == null || lanes == null) return new LevelParseResult(null, errors);
            var level = new LevelDefinition(id, slots, cap, colors, stack, lanes, preset, scale, name, difficulty, notes, formatVersion, solution,
                hidden, locks, links);
            return new LevelParseResult(level, errors);
        }

        private static StackDefinition ParseStack(JsonValue root, List<string> errors)
        {
            if (!root.TryGet("stack", out var st)) { errors.Add("$.stack: required"); return null; }
            if (st.Kind != JsonKind.Object) { errors.Add("$.stack: must be an object"); return null; }
            Unknown(st, StackKeys, "$.stack", errors);
            int cols = Int(st, "cols", "$.stack", errors, required: true, min: 1, max: 16, fallback: 0);
            int rows = Int(st, "rows", "$.stack", errors, required: true, min: 1, max: 16, fallback: 0);
            if (!st.TryGet("layers", out var layersNode)) { errors.Add("$.stack.layers: required"); return null; }
            if (layersNode.Kind != JsonKind.Array) { errors.Add("$.stack.layers: must be an array"); return null; }
            if (layersNode.Items.Count < 1 || layersNode.Items.Count > 8) errors.Add("$.stack.layers: 1..8 layers");
            int errorsBefore = errors.Count;
            var layers = new List<IReadOnlyList<string>>();
            for (int k = 0; k < layersNode.Items.Count; k++)
            {
                var ln = layersNode.Items[k];
                string lp = $"$.stack.layers[{k}]";
                if (ln.Kind != JsonKind.Array) { errors.Add(lp + ": must be an array of row strings"); continue; }
                if (rows > 0 && ln.Items.Count != rows) errors.Add($"{lp}: {ln.Items.Count} rows ≠ rows {rows}");
                var rowsList = new List<string>();
                for (int r = 0; r < ln.Items.Count; r++)
                {
                    var rv = ln.Items[r];
                    string rp = $"{lp}[{r}]";
                    if (rv.Kind != JsonKind.String) { errors.Add(rp + ": must be a string"); rowsList.Add(""); continue; }
                    if (cols > 0 && rv.String.Length != cols) errors.Add($"{rp}: length {rv.String.Length} ≠ cols {cols}");
                    for (int i = 0; i < rv.String.Length; i++)
                        if (!StackCell.TryParse(rv.String[i], out _)) errors.Add($"{rp}[{i}]: '{rv.String[i]}' is not '.', a colour code or its lowercase (hidden)");
                    rowsList.Add(rv.String);
                }
                layers.Add(rowsList);
            }
            // only well-formed rows become cells; anything else is already reported above
            return cols > 0 && rows > 0 && errors.Count == errorsBefore ? StackDefinition.FromRows(cols, rows, layers) : null;
        }

        private static List<IReadOnlyList<CapColor>> ParseLanes(JsonValue root, List<TrayRef> hidden, List<string> errors)
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
                    var c = TrayCode(lane.Items[t], $"$.lanes[{j}][{t}]", errors, out bool isHidden);
                    if (c == CapColor.None) continue;
                    if (isHidden) hidden.Add(new TrayRef(j, trays.Count));
                    trays.Add(c);
                }
                lanes.Add(trays);
            }
            return lanes;
        }

        // locks / links are checked for SHAPE here; whether they point at a real tray is V7 (LevelValidator)
        private static List<TrayLock> ParseLocks(JsonValue root, List<string> errors)
        {
            var locks = new List<TrayLock>();
            if (!root.TryGet("locks", out var node)) return locks;
            if (node.Kind != JsonKind.Array) { errors.Add("$.locks: must be an array"); return locks; }
            for (int i = 0; i < node.Items.Count; i++)
            {
                var o = node.Items[i];
                string path = $"$.locks[{i}]";
                if (o.Kind != JsonKind.Object) { errors.Add(path + ": must be an object"); continue; }
                Unknown(o, LockKeys, path, errors);
                int lane = Int(o, "lane", path, errors, required: true, min: 0, max: 3, fallback: -1);
                int tray = Int(o, "tray", path, errors, required: true, min: 0, max: 999, fallback: -1);
                int turns = Int(o, "turns", path, errors, required: true, min: 1, max: MaxLockTurns, fallback: 0);
                if (lane >= 0 && tray >= 0 && turns > 0) locks.Add(new TrayLock(new TrayRef(lane, tray), turns));
            }
            return locks;
        }

        private static List<TrayLink> ParseLinks(JsonValue root, List<string> errors)
        {
            var links = new List<TrayLink>();
            if (!root.TryGet("links", out var node)) return links;
            if (node.Kind != JsonKind.Array) { errors.Add("$.links: must be an array"); return links; }
            for (int i = 0; i < node.Items.Count; i++)
            {
                var o = node.Items[i];
                string path = $"$.links[{i}]";
                if (o.Kind != JsonKind.Object) { errors.Add(path + ": must be an object"); continue; }
                Unknown(o, LinkKeys, path, errors);
                bool okA = Ref(o, "a", path, errors, out var a), okB = Ref(o, "b", path, errors, out var b);
                if (okA && okB) links.Add(new TrayLink(a, b));
            }
            return links;
        }

        /// <summary><c>"a": [lane, tray]</c>.</summary>
        private static bool Ref(JsonValue obj, string name, string path, List<string> errors, out TrayRef tray)
        {
            tray = default;
            if (!obj.TryGet(name, out var v)) { errors.Add($"{path}.{name}: required"); return false; }
            bool ok = v.Kind == JsonKind.Array && v.Items.Count == 2;
            for (int k = 0; ok && k < 2; k++)
                ok = v.Items[k].Kind == JsonKind.Number && v.Items[k].Number == Math.Floor(v.Items[k].Number) && v.Items[k].Number >= 0;
            if (!ok) { errors.Add($"{path}.{name}: must be [lane, tray] (two non-negative integers)"); return false; }
            tray = new TrayRef((int)v.Items[0].Number, (int)v.Items[1].Number);
            return true;
        }

        // ── writer (generator output) — stable, diff-friendly layout: one stack row per line ──
        public static string Write(LevelDefinition level)
        {
            var sb = new StringBuilder();
            sb.Append("{\n");
            // levels live in Assets/CapsChaos/Content/LevelConfig/ — four levels below the repo root
            sb.Append("  \"$schema\": \"../../../../docs/design/level.schema.json\",\n");
            sb.Append($"  \"formatVersion\": {level.FormatVersion},\n");
            sb.Append($"  \"id\": {Q(level.Id)},\n");
            sb.Append($"  \"slots\": {level.Slots},\n");
            sb.Append($"  \"trayCapacity\": {level.TrayCapacity},\n");
            sb.Append("  \"colors\": [").Append(string.Join(", ", Map(level.Colors, c => Q(CapColorCodes.ToCode(c).ToString())))).Append("],\n");
            sb.Append("  \"stack\": {\n");
            sb.Append($"    \"cols\": {level.Stack.Cols},\n    \"rows\": {level.Stack.Rows},\n    \"layers\": [\n");
            for (int k = 0; k < level.Stack.LayerCount; k++)
            {
                sb.Append("      [\n");
                int rows = level.Stack.Rows;
                for (int r = 0; r < rows; r++)
                    sb.Append("        ").Append(Q(level.Stack.RowCodes(k, r))).Append(r < rows - 1 ? ",\n" : "\n");
                sb.Append("      ]").Append(k < level.Stack.LayerCount - 1 ? ",\n" : "\n");
            }
            sb.Append("    ]\n  },\n");
            sb.Append("  \"lanes\": [\n");
            for (int j = 0; j < level.Lanes.Count; j++)
            {
                var codes = new List<string>();
                for (int t = 0; t < level.Lanes[j].Count; t++)
                {
                    char c = CapColorCodes.ToCode(level.Lanes[j][t]);
                    codes.Add(Q((level.IsHiddenTray(new TrayRef(j, t)) ? char.ToLowerInvariant(c) : c).ToString()));
                }
                sb.Append("    [").Append(string.Join(", ", codes)).Append(j < level.Lanes.Count - 1 ? "],\n" : "]\n");
            }
            sb.Append("  ],\n");
            if (level.Locks.Count > 0)
            {
                sb.Append("  \"locks\": [\n");
                for (int i = 0; i < level.Locks.Count; i++)
                {
                    var l = level.Locks[i];
                    sb.Append($"    {{ \"lane\": {l.Tray.Lane}, \"tray\": {l.Tray.Index}, \"turns\": {l.Turns} }}").Append(i < level.Locks.Count - 1 ? ",\n" : "\n");
                }
                sb.Append("  ],\n");
            }
            if (level.Links.Count > 0)
            {
                sb.Append("  \"links\": [\n");
                for (int i = 0; i < level.Links.Count; i++)
                {
                    var l = level.Links[i];
                    sb.Append($"    {{ \"a\": [{l.A.Lane}, {l.A.Index}], \"b\": [{l.B.Lane}, {l.B.Index}] }}").Append(i < level.Links.Count - 1 ? ",\n" : "\n");
                }
                sb.Append("  ],\n");
            }
            sb.Append($"  \"view\": {{ \"cameraPreset\": {Q(level.CameraPreset)}, \"stackScale\": {level.StackScale.ToString("0.###", CultureInfo.InvariantCulture)} }}");
            var meta = new List<string>();
            if (level.Name != null) meta.Add($"\"name\": {Q(level.Name)}");
            if (level.Difficulty != null) meta.Add($"\"difficulty\": {Q(level.Difficulty)}");
            if (level.Notes != null) meta.Add($"\"notes\": {Q(level.Notes)}");
            if (level.Solution != null) meta.Add("\"solution\": [" + string.Join(", ", level.Solution) + "]");
            if (meta.Count > 0) sb.Append(",\n  \"meta\": { ").Append(string.Join(", ", meta)).Append(" }");
            sb.Append("\n}\n");
            return sb.ToString();
        }

        public static bool IsLevelId(string id)
        {
            if (id.Length != 10 || !id.StartsWith("level_", StringComparison.Ordinal)) return false;
            for (int i = 6; i < 10; i++) if (id[i] < '0' || id[i] > '9') return false;
            return true;
        }

        // ── helpers ───────────────────────────────────────────────────────────────────────────
        private static IEnumerable<string> Map(IEnumerable<CapColor> src, Func<CapColor, string> f) { foreach (var c in src) yield return f(c); }

        private static string Q(string s)
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

        private static void Unknown(JsonValue obj, HashSet<string> allowed, string path, List<string> errors)
        {
            foreach (var m in obj.Members)
                if (!allowed.Contains(m.Key)) errors.Add($"{path}.{m.Key}: unknown property");
        }

        private static int Int(JsonValue obj, string name, string path, List<string> errors, bool required, int min, int max, int fallback)
        {
            if (!obj.TryGet(name, out var v)) { if (required) errors.Add($"{path}.{name}: required"); return fallback; }
            if (v.Kind != JsonKind.Number || v.Number != Math.Floor(v.Number)) { errors.Add($"{path}.{name}: must be an integer"); return fallback; }
            if (v.Number < min || v.Number > max) { errors.Add($"{path}.{name}: {v.Number} outside {min}..{max}"); return fallback; }
            return (int)v.Number;
        }

        private static string Str(JsonValue obj, string name, string path, List<string> errors, bool required)
        {
            if (!obj.TryGet(name, out var v)) { if (required) errors.Add($"{path}.{name}: required"); return null; }
            if (v.Kind != JsonKind.String) { errors.Add($"{path}.{name}: must be a string"); return null; }
            return v.String;
        }

        /// <summary>A lane tray: a colour code, lowercase = hidden (R17) — the stack's spelling.</summary>
        private static CapColor TrayCode(JsonValue v, string path, List<string> errors, out bool hidden)
        {
            hidden = false;
            if (v.Kind != JsonKind.String || v.String.Length != 1 || !CapColorCodes.TryParse(char.ToUpperInvariant(v.String[0]), out var color))
            {
                errors.Add($"{path}: must be one of \"R\",\"O\",\"B\",\"G\",\"P\",\"Y\",\"C\",\"N\" (lowercase = hidden tray)");
                return CapColor.None;
            }
            hidden = char.IsLower(v.String[0]);
            return color;
        }

        private static CapColor ColorCode(JsonValue v, string path, List<string> errors)
        {
            if (v.Kind != JsonKind.String || v.String.Length != 1 || !CapColorCodes.TryParse(v.String[0], out var color))
            {
                errors.Add($"{path}: must be one of \"R\",\"O\",\"B\",\"G\",\"P\",\"Y\",\"C\",\"N\"");
                return CapColor.None;
            }
            return color;
        }
    }
}
