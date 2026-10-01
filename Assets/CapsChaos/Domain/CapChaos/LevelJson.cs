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

    /// <summary>What a format reader yields: the version-specific part of a level (colours, stack, lanes, tray modifiers).</summary>
    internal sealed class LevelContent
    {
        public readonly List<CapColor> Colors = new List<CapColor>();
        public StackDefinition Stack;
        public List<IReadOnlyList<CapColor>> Lanes;
        public readonly List<TrayRef> HiddenTrays = new List<TrayRef>();
        public List<TrayLock> Locks = new List<TrayLock>();
        public List<TrayLink> Links = new List<TrayLink>();
    }

    /// <summary>
    /// Level JSON ⇄ <see cref="LevelDefinition"/>. <see cref="Parse"/> is validator leg V1: every
    /// structural rule of <c>docs/design/level.schema.json</c>, reported all at once with a JSON path.
    /// The schema file is the contract; this class mirrors it and a headless test pins the two together.
    /// <para>Format v2 (2026-10-01) is written for designers and their own tools: every colour is the NUMBER of its
    /// <see cref="CapColor"/> (0 = empty cell), and every flag is a named field — <c>stack.hidden</c> for hidden
    /// bottles, <c>hidden</c> / <c>lockTurns</c> on a tray object, <c>links</c> by <c>{lane, tray}</c>. v1 files
    /// (letter codes) still load through <see cref="LevelFormatV1"/>; <see cref="Write"/> always writes v2.</para>
    /// </summary>
    public static class LevelJson
    {
        private static readonly HashSet<string> RootKeys = new HashSet<string>(StringComparer.Ordinal)
            { "$schema", "formatVersion", "id", "slots", "trayCapacity", "colors", "stack", "lanes", "links", "view", "meta" };
        private static readonly HashSet<string> StackKeys = new HashSet<string>(StringComparer.Ordinal) { "cols", "rows", "layers", "hidden" };
        private static readonly HashSet<string> CellKeys = new HashSet<string>(StringComparer.Ordinal) { "layer", "row", "col" };
        private static readonly HashSet<string> TrayKeys = new HashSet<string>(StringComparer.Ordinal) { "color", "hidden", "lockTurns" };
        private static readonly HashSet<string> LinkKeys = new HashSet<string>(StringComparer.Ordinal) { "a", "b" };
        private static readonly HashSet<string> RefKeys = new HashSet<string>(StringComparer.Ordinal) { "lane", "tray" };
        private static readonly HashSet<string> ViewKeys = new HashSet<string>(StringComparer.Ordinal) { "cameraPreset", "stackScale" };
        private static readonly HashSet<string> MetaKeys = new HashSet<string>(StringComparer.Ordinal) { "name", "difficulty", "notes", "solution" };
        private static readonly string[] CameraPresets = { "default", "tall", "wide" };
        private static readonly string[] Difficulties = { "tutorial", "easy", "medium", "hard", "breather" };
        public const int MaxLockTurns = 99;
        private const int MaxColorId = 8;

        public static LevelParseResult Parse(string json)
        {
            var errors = new List<string>();
            JsonValue root;
            try { root = JsonReader.Parse(json); }
            catch (JsonParseException e) { errors.Add("json: " + e.Message); return new LevelParseResult(null, errors); }

            if (root.Kind != JsonKind.Object) { errors.Add("$: must be an object"); return new LevelParseResult(null, errors); }

            int formatVersion = Int(root, "formatVersion", "$", errors, required: true, min: 1, max: LevelDefinition.CurrentFormatVersion,
                fallback: LevelDefinition.CurrentFormatVersion);
            bool v1 = formatVersion == 1;
            Unknown(root, v1 ? LevelFormatV1.RootKeys : RootKeys, "$", errors);
            string id = Str(root, "id", "$", errors, required: true);
            if (id != null && !IsLevelId(id)) errors.Add($"$.id: '{id}' must match level_NNNN");
            int slots = Int(root, "slots", "$", errors, required: false, min: 1, max: 5, fallback: LevelDefinition.DefaultSlots);
            int cap = Int(root, "trayCapacity", "$", errors, required: false, min: 2, max: 6, fallback: LevelDefinition.DefaultTrayCapacity);

            var content = v1 ? LevelFormatV1.Read(root, errors) : ReadV2(root, errors);
            var lanes = content.Lanes;
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

            if (errors.Count > 0 || id == null || content.Stack == null || lanes == null) return new LevelParseResult(null, errors);
            var level = new LevelDefinition(id, slots, cap, content.Colors, content.Stack, lanes, preset, scale, name, difficulty, notes,
                LevelDefinition.CurrentFormatVersion, solution, content.HiddenTrays, content.Locks, content.Links);
            return new LevelParseResult(level, errors);
        }

        // ── format v2 ─────────────────────────────────────────────────────────────────────────
        private static LevelContent ReadV2(JsonValue root, List<string> errors)
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
            c.Stack = ReadStack(root, errors);
            c.Lanes = ReadLanes(root, c, errors);
            c.Links = ReadLinks(root, errors);
            return c;
        }

        private static StackDefinition ReadStack(JsonValue root, List<string> errors)
        {
            if (!root.TryGet("stack", out var st)) { errors.Add("$.stack: required"); return null; }
            if (st.Kind != JsonKind.Object) { errors.Add("$.stack: must be an object"); return null; }
            Unknown(st, StackKeys, "$.stack", errors);
            int cols = Int(st, "cols", "$.stack", errors, required: true, min: 1, max: 16, fallback: 0);
            int rows = Int(st, "rows", "$.stack", errors, required: true, min: 1, max: 16, fallback: 0);
            if (!st.TryGet("layers", out var layersNode)) { errors.Add("$.stack.layers: required"); return null; }
            if (layersNode.Kind != JsonKind.Array) { errors.Add("$.stack.layers: must be an array"); return null; }
            if (layersNode.Items.Count < 1 || layersNode.Items.Count > 8) errors.Add("$.stack.layers: 1..8 layers");
            int before = errors.Count;
            if (cols <= 0 || rows <= 0) return null;

            var cells = new StackCell[layersNode.Items.Count, rows, cols];
            for (int k = 0; k < layersNode.Items.Count; k++)
            {
                var ln = layersNode.Items[k];
                string lp = $"$.stack.layers[{k}]";
                if (ln.Kind != JsonKind.Array) { errors.Add(lp + ": must be an array of rows"); continue; }
                if (ln.Items.Count != rows) { errors.Add($"{lp}: {ln.Items.Count} rows ≠ rows {rows}"); continue; }
                for (int r = 0; r < rows; r++)
                {
                    var row = ln.Items[r];
                    string rp = $"{lp}[{r}]";
                    if (row.Kind != JsonKind.Array) { errors.Add(rp + ": must be an array of colour numbers"); continue; }
                    if (row.Items.Count != cols) { errors.Add($"{rp}: {row.Items.Count} cells ≠ cols {cols}"); continue; }
                    for (int x = 0; x < cols; x++)
                        cells[k, r, x] = new StackCell(ColorId(row.Items[x], $"{rp}[{x}]", errors, allowEmpty: true), hidden: false);
                }
            }

            if (st.TryGet("hidden", out var hiddenNode))
            {
                if (hiddenNode.Kind != JsonKind.Array) errors.Add("$.stack.hidden: must be an array of { layer, row, col }");
                else
                    for (int i = 0; i < hiddenNode.Items.Count; i++)
                    {
                        var h = hiddenNode.Items[i];
                        string hp = $"$.stack.hidden[{i}]";
                        if (h.Kind != JsonKind.Object) { errors.Add(hp + ": must be { layer, row, col }"); continue; }
                        Unknown(h, CellKeys, hp, errors);
                        int k = Int(h, "layer", hp, errors, required: true, min: 0, max: cells.GetLength(0) - 1, fallback: -1);
                        int r = Int(h, "row", hp, errors, required: true, min: 0, max: rows - 1, fallback: -1);
                        int x = Int(h, "col", hp, errors, required: true, min: 0, max: cols - 1, fallback: -1);
                        if (k < 0 || r < 0 || x < 0) continue;
                        if (cells[k, r, x].IsEmpty) errors.Add($"{hp}: cell (layer {k}, row {r}, col {x}) is empty — only a bottle can be hidden");
                        else cells[k, r, x] = new StackCell(cells[k, r, x].Color, hidden: true);
                    }
            }
            return errors.Count == before ? new StackDefinition(cells) : null;
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
                    if (tr.Kind != JsonKind.Object) { errors.Add(tp + ": must be a tray { color, hidden?, lockTurns? }"); continue; }
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

        // ── writer (generator output, format v2) — stable, diff-friendly layout: one stack row / one lane per line ──
        public static string Write(LevelDefinition level)
        {
            var sb = new StringBuilder();
            sb.Append("{\n");
            // levels live in Assets/CapsChaos/Content/LevelConfig/ — four levels below the repo root
            sb.Append("  \"$schema\": \"../../../../docs/design/level.schema.json\",\n");
            sb.Append($"  \"formatVersion\": {LevelDefinition.CurrentFormatVersion},\n");
            sb.Append($"  \"id\": {Q(level.Id)},\n");
            sb.Append($"  \"slots\": {level.Slots},\n");
            sb.Append($"  \"trayCapacity\": {level.TrayCapacity},\n");
            sb.Append("  \"colors\": [").Append(Numbers(level.Colors)).Append("],\n");

            var st = level.Stack;
            sb.Append("  \"stack\": {\n");
            sb.Append($"    \"cols\": {st.Cols},\n    \"rows\": {st.Rows},\n    \"layers\": [\n");
            var hidden = new List<string>();
            for (int k = 0; k < st.LayerCount; k++)
            {
                sb.Append("      [\n");
                for (int r = 0; r < st.Rows; r++)
                {
                    var row = new List<string>();
                    for (int x = 0; x < st.Cols; x++)
                    {
                        var cell = st.At(k, r, x);
                        row.Add(((int)cell.Color).ToString(CultureInfo.InvariantCulture));
                        if (cell.Hidden) hidden.Add($"{{ \"layer\": {k}, \"row\": {r}, \"col\": {x} }}");
                    }
                    sb.Append("        [").Append(string.Join(", ", row)).Append(r < st.Rows - 1 ? "],\n" : "]\n");
                }
                sb.Append("      ]").Append(k < st.LayerCount - 1 ? ",\n" : "\n");
            }
            sb.Append(hidden.Count > 0 ? "    ],\n" : "    ]\n");
            if (hidden.Count > 0)
            {
                sb.Append("    \"hidden\": [\n");
                for (int i = 0; i < hidden.Count; i++) sb.Append("      ").Append(hidden[i]).Append(i < hidden.Count - 1 ? ",\n" : "\n");
                sb.Append("    ]\n");
            }
            sb.Append("  },\n");

            sb.Append("  \"lanes\": [\n");
            for (int j = 0; j < level.Lanes.Count; j++)
            {
                var trays = new List<string>();
                for (int t = 0; t < level.Lanes[j].Count; t++)
                {
                    var at = new TrayRef(j, t);
                    var tray = new StringBuilder($"{{ \"color\": {(int)level.Lanes[j][t]}");
                    if (level.IsHiddenTray(at)) tray.Append(", \"hidden\": true");
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

        /// <summary>A lane tray: a colour code, lowercase = hidden (R17) — the stack's spelling.</summary>
    }
}
