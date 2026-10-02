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
        public List<TrayLock> Locks = new List<TrayLock>();
        public List<TrayLink> Links = new List<TrayLink>();
    }

    /// <summary>
    /// Level JSON ⇄ <see cref="LevelDefinition"/>. <see cref="Parse"/> is validator leg V1: every
    /// structural rule of <c>docs/design/level.schema.json</c>, reported all at once with a JSON path.
    /// The schema file is the contract; this class mirrors it and a headless test pins the two together.
    /// <para>Format v3 (2026-10-02) replaced the bottle stack with the oval belt (<c>loop</c>: rows, width, pick zone,
    /// feeders, optional initial rows). Every colour is the NUMBER of its <see cref="CapColor"/> (0 = empty spot), and
    /// every flag is a named field — <c>hidden</c> / <c>lockTurns</c> on a tray object, <c>links</c> by
    /// <c>{lane, tray}</c>. v1/v2 files describe a stack, which has no belt equivalent: they are rejected with a
    /// pointer to the generator, never guessed at.</para>
    /// </summary>
    public static class LevelJson
    {
        private static readonly HashSet<string> RootKeys = new HashSet<string>(StringComparer.Ordinal)
            { "$schema", "formatVersion", "id", "slots", "extraSlots", "trayCapacity", "colors", "loop", "lanes", "links", "view", "meta" };
        private static readonly HashSet<string> LoopKeys = new HashSet<string>(StringComparer.Ordinal) { "rows", "width", "pickRows", "feeders", "initial" };
        private static readonly HashSet<string> FeederKeys = new HashSet<string>(StringComparer.Ordinal) { "mergeAt", "bottles" };
        private static readonly HashSet<string> TrayKeys = new HashSet<string>(StringComparer.Ordinal) { "color", "hidden", "lockTurns" };
        private static readonly HashSet<string> LinkKeys = new HashSet<string>(StringComparer.Ordinal) { "a", "b" };
        private static readonly HashSet<string> RefKeys = new HashSet<string>(StringComparer.Ordinal) { "lane", "tray" };
        private static readonly HashSet<string> ShapeKeys = new HashSet<string>(StringComparer.Ordinal) { "points", "radius" };
        private static readonly HashSet<string> ViewKeys = new HashSet<string>(StringComparer.Ordinal) { "cameraPreset", "loopShape" };
        private static readonly HashSet<string> MetaKeys = new HashSet<string>(StringComparer.Ordinal) { "name", "difficulty", "notes", "solution" };
        private static readonly string[] CameraPresets = { "default", "tall", "wide" };
        private static readonly string[] Difficulties = { "tutorial", "easy", "medium", "hard", "breather" };
        public const int MaxLockTurns = 99;
        public const int MinRows = 8, MaxRows = 64, MaxWidth = 6, MaxFeeders = 4;
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

            var content = ReadContent(root, errors);
            var lanes = content.Lanes;
            string preset = "default";
            LoopShape shape = null;
            if (root.TryGet("view", out var view))
            {
                if (view.Kind != JsonKind.Object) errors.Add("$.view: must be an object");
                else
                {
                    Unknown(view, ViewKeys, "$.view", errors);
                    var p = Str(view, "cameraPreset", "$.view", errors, required: false);
                    if (p != null) { if (Array.IndexOf(CameraPresets, p) < 0) errors.Add($"$.view.cameraPreset: '{p}' not in [default, tall, wide]"); else preset = p; }
                    if (view.TryGet("loopShape", out var ls)) shape = ReadShape(ls, errors);
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
                LevelDefinition.CurrentFormatVersion, solution, content.HiddenTrays, content.Locks, content.Links, extra, shape);
            return new LevelParseResult(level, errors);
        }

        /// <summary><c>view.loopShape</c>: a preset name, or <c>{ "points": [[x, z], …], "radius": r | [r, …] }</c> (V9).</summary>
        private static LoopShape ReadShape(JsonValue v, List<string> errors)
        {
            const string path = "$.view.loopShape";
            if (v.Kind == JsonKind.String)
            {
                if (Array.IndexOf(LoopShape.Presets, v.String) >= 0) return LoopShape.Named(v.String);
                errors.Add($"{path}: '{v.String}' not in [{string.Join(", ", LoopShape.Presets)}]");
                return null;
            }
            if (v.Kind != JsonKind.Object) { errors.Add($"{path}: a preset name or {{ points, radius }}"); return null; }
            Unknown(v, ShapeKeys, path, errors);
            var xs = new List<double>(); var zs = new List<double>(); var radii = new List<double>();
            if (!v.TryGet("points", out var pts) || pts.Kind != JsonKind.Array) { errors.Add($"{path}.points: required, an array of [x, z]"); return null; }
            for (int i = 0; i < pts.Items.Count; i++)
            {
                var pt = pts.Items[i];
                if (pt.Kind != JsonKind.Array || pt.Items.Count != 2 || pt.Items[0].Kind != JsonKind.Number || pt.Items[1].Kind != JsonKind.Number)
                { errors.Add($"{path}.points[{i}]: must be [x, z]"); return null; }
                xs.Add(pt.Items[0].Number); zs.Add(pt.Items[1].Number);
            }
            if (!v.TryGet("radius", out var rad)) { errors.Add($"{path}.radius: required, a number or one per corner"); return null; }
            if (rad.Kind == JsonKind.Number) foreach (var _ in xs) radii.Add(rad.Number);
            else if (rad.Kind == JsonKind.Array)
            {
                foreach (var r in rad.Items)
                {
                    if (r.Kind != JsonKind.Number) { errors.Add($"{path}.radius: numbers only"); return null; }
                    radii.Add(r.Number);
                }
            }
            else { errors.Add($"{path}.radius: a number or one per corner"); return null; }
            var problems = LoopShape.Check(xs, zs, radii, path);
            if (problems.Count > 0) { errors.AddRange(problems); return null; }
            return LoopShape.Custom(xs, zs, radii);
        }

        private static string WriteShape(LoopShape shape)
        {
            if (shape.Preset != null) return Q(shape.Preset);
            var pts = new List<string>();
            for (int i = 0; i < shape.Xs.Count; i++) pts.Add($"[{Num(shape.Xs[i])}, {Num(shape.Zs[i])}]");
            var radii = new List<string>();
            bool same = true;
            for (int i = 0; i < shape.Radii.Count; i++) { radii.Add(Num(shape.Radii[i])); same &= shape.Radii[i] == shape.Radii[0]; }
            return $"{{ \"points\": [{string.Join(", ", pts)}], \"radius\": {(same ? radii[0] : "[" + string.Join(", ", radii) + "]")} }}";
        }

        private static string Num(double d) => d.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture);

        // ── content ───────────────────────────────────────────────────────────────────────────
        private static LevelContent ReadContent(JsonValue root, List<string> errors)
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
            c.Loop = ReadLoop(root, errors);
            c.Lanes = ReadLanes(root, c, errors);
            c.Links = ReadLinks(root, errors);
            return c;
        }

        // whether merge points, the pick zone and the bottle counts make sense together is V8/V4 (LevelValidator)
        private static LoopDefinition ReadLoop(JsonValue root, List<string> errors)
        {
            if (!root.TryGet("loop", out var lp)) { errors.Add("$.loop: required"); return null; }
            if (lp.Kind != JsonKind.Object) { errors.Add("$.loop: must be an object"); return null; }
            int before = errors.Count;
            Unknown(lp, LoopKeys, "$.loop", errors);
            int rows = Int(lp, "rows", "$.loop", errors, required: true, min: MinRows, max: MaxRows, fallback: 0);
            int width = Int(lp, "width", "$.loop", errors, required: false, min: 1, max: MaxWidth, fallback: LoopDefinition.DefaultWidth);
            int pick = Int(lp, "pickRows", "$.loop", errors, required: true, min: 1, max: MaxRows, fallback: 0);

            var feeders = new List<FeederDefinition>();
            if (!lp.TryGet("feeders", out var fn)) errors.Add("$.loop.feeders: required");
            else if (fn.Kind != JsonKind.Array) errors.Add("$.loop.feeders: must be an array");
            else
            {
                if (fn.Items.Count > MaxFeeders) errors.Add($"$.loop.feeders: 0..{MaxFeeders} feeders");
                for (int f = 0; f < fn.Items.Count; f++)
                {
                    var o = fn.Items[f];
                    string fp = $"$.loop.feeders[{f}]";
                    if (o.Kind != JsonKind.Object) { errors.Add(fp + ": must be { mergeAt, bottles }"); continue; }
                    Unknown(o, FeederKeys, fp, errors);
                    int merge = Int(o, "mergeAt", fp, errors, required: true, min: 0, max: MaxRows - 1, fallback: -1);
                    var bottles = new List<CapColor>();
                    if (!o.TryGet("bottles", out var bn)) errors.Add(fp + ".bottles: required");
                    else if (bn.Kind != JsonKind.Array) errors.Add(fp + ".bottles: must be an array of colour numbers");
                    else for (int i = 0; i < bn.Items.Count; i++)
                    {
                        var col = ColorId(bn.Items[i], $"{fp}.bottles[{i}]", errors, allowEmpty: false);
                        if (col != CapColor.None) bottles.Add(col);
                    }
                    feeders.Add(new FeederDefinition(merge, bottles));
                }
            }

            List<IReadOnlyList<CapColor>> initial = null;
            if (lp.TryGet("initial", out var init))
            {
                if (init.Kind != JsonKind.Array) errors.Add("$.loop.initial: must be an array of rows");
                else
                {
                    if (rows > 0 && init.Items.Count != rows) errors.Add($"$.loop.initial: {init.Items.Count} rows ≠ rows {rows}");
                    initial = new List<IReadOnlyList<CapColor>>();
                    for (int r = 0; r < init.Items.Count; r++)
                    {
                        var row = init.Items[r];
                        string rp = $"$.loop.initial[{r}]";
                        if (row.Kind != JsonKind.Array) { errors.Add(rp + ": must be an array of colour numbers"); continue; }
                        if (row.Items.Count != width) { errors.Add($"{rp}: {row.Items.Count} spots ≠ width {width}"); continue; }
                        var spots = new List<CapColor>();
                        for (int k = 0; k < width; k++) spots.Add(ColorId(row.Items[k], $"{rp}[{k}]", errors, allowEmpty: true));
                        initial.Add(spots);
                    }
                }
            }
            return errors.Count == before ? new LoopDefinition(rows, width, pick, feeders, initial) : null;
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

        // ── writer (generator output, format v3) — stable, diff-friendly layout: one feeder row / one lane per line ──
        public static string Write(LevelDefinition level)
        {
            var sb = new StringBuilder();
            sb.Append("{\n");
            // levels live in Assets/CapsChaos/Content/LevelConfig/ — four levels below the repo root
            sb.Append("  \"$schema\": \"../../../../docs/design/level.schema.json\",\n");
            sb.Append($"  \"formatVersion\": {LevelDefinition.CurrentFormatVersion},\n");
            sb.Append($"  \"id\": {Q(level.Id)},\n");
            sb.Append($"  \"slots\": {level.Slots},\n");
            sb.Append($"  \"extraSlots\": {level.ExtraSlots},\n");
            sb.Append($"  \"trayCapacity\": {level.TrayCapacity},\n");
            sb.Append("  \"colors\": [").Append(Numbers(level.Colors)).Append("],\n");

            var lp = level.Loop;
            sb.Append("  \"loop\": {\n");
            sb.Append($"    \"rows\": {lp.Rows},\n    \"width\": {lp.Width},\n    \"pickRows\": {lp.PickRows},\n    \"feeders\": [\n");
            for (int f = 0; f < lp.Feeders.Count; f++)
            {
                var fd = lp.Feeders[f];
                sb.Append($"      {{ \"mergeAt\": {fd.MergeAt}, \"bottles\": [\n");
                // one feeder row (width bottles) per line: the file reads like the queue looks
                for (int i = 0; i < fd.Bottles.Count; i += lp.Width)
                {
                    var row = new List<CapColor>();
                    for (int k = i; k < Math.Min(i + lp.Width, fd.Bottles.Count); k++) row.Add(fd.Bottles[k]);
                    sb.Append("        ").Append(Numbers(row)).Append(i + lp.Width < fd.Bottles.Count ? ",\n" : "\n");
                }
                sb.Append("      ] }").Append(f < lp.Feeders.Count - 1 ? ",\n" : "\n");
            }
            sb.Append(lp.Initial != null ? "    ],\n" : "    ]\n");
            if (lp.Initial != null)
            {
                sb.Append("    \"initial\": [\n");
                for (int r = 0; r < lp.Initial.Count; r++)
                    sb.Append("      [").Append(Numbers(lp.Initial[r])).Append(r < lp.Initial.Count - 1 ? "],\n" : "]\n");
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
            sb.Append($"  \"view\": {{ \"cameraPreset\": {Q(level.CameraPreset)}")
              .Append(level.Shape.IsDefault ? "" : ", \"loopShape\": " + WriteShape(level.Shape)).Append(" }");
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

    }
}
