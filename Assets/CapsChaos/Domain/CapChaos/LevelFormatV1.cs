using System;
using System.Collections.Generic;

namespace Game.Domain
{
    /// <summary>
    /// Reader for level <c>formatVersion 1</c> (shipped 2026-09-30 – 2026-10-01): colours as one-letter codes
    /// (<c>"R"</c>), a hidden bottle or tray as its lowercase code, stack rows as strings, <c>locks</c> at the root and
    /// links as <c>[lane, tray]</c> pairs. Reads into the same model as v2, so old files keep loading; nothing
    /// writes v1 any more. A shipped migrator is immutable (rule #2): fix a v1 reading bug in a NEW migrator,
    /// never here. Schema: <c>docs/design/level.schema.v1.json</c>.
    /// </summary>
    internal static class LevelFormatV1
    {
        internal static readonly HashSet<string> RootKeys = new HashSet<string>(StringComparer.Ordinal)
            { "$schema", "formatVersion", "id", "slots", "trayCapacity", "colors", "stack", "lanes", "locks", "links", "view", "meta" };
        private static readonly HashSet<string> LockKeys = new HashSet<string>(StringComparer.Ordinal) { "lane", "tray", "turns" };
        private static readonly HashSet<string> LinkKeys = new HashSet<string>(StringComparer.Ordinal) { "a", "b" };
        private static readonly HashSet<string> StackKeys = new HashSet<string>(StringComparer.Ordinal) { "cols", "rows", "layers" };

        internal static LevelContent Read(JsonValue root, List<string> errors)
        {
            var c = new LevelContent();
            if (!root.TryGet("colors", out var colorsNode)) errors.Add("$.colors: required");
            else if (colorsNode.Kind != JsonKind.Array) errors.Add("$.colors: must be an array");
            else
            {
                if (colorsNode.Items.Count < 1 || colorsNode.Items.Count > 8) errors.Add("$.colors: 1..8 entries");
                for (int i = 0; i < colorsNode.Items.Count; i++)
                {
                    var col = ColorCode(colorsNode.Items[i], $"$.colors[{i}]", errors);
                    if (col == CapColor.None) continue;
                    if (c.Colors.Contains(col)) errors.Add($"$.colors[{i}]: duplicate '{CapColorCodes.ToCode(col)}'");
                    else c.Colors.Add(col);
                }
            }
            c.Stack = ParseStack(root, errors);
            c.Lanes = ParseLanes(root, c.HiddenTrays, errors);
            c.Locks = ParseLocks(root, errors);
            c.Links = ParseLinks(root, errors);
            return c;
        }

        private static StackDefinition ParseStack(JsonValue root, List<string> errors)
        {
            if (!root.TryGet("stack", out var st)) { errors.Add("$.stack: required"); return null; }
            if (st.Kind != JsonKind.Object) { errors.Add("$.stack: must be an object"); return null; }
            LevelJson.Unknown(st, StackKeys, "$.stack", errors);
            int cols = LevelJson.Int(st, "cols", "$.stack", errors, required: true, min: 1, max: 16, fallback: 0);
            int rows = LevelJson.Int(st, "rows", "$.stack", errors, required: true, min: 1, max: 16, fallback: 0);
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
                LevelJson.Unknown(o, LockKeys, path, errors);
                int lane = LevelJson.Int(o, "lane", path, errors, required: true, min: 0, max: 3, fallback: -1);
                int tray = LevelJson.Int(o, "tray", path, errors, required: true, min: 0, max: 999, fallback: -1);
                int turns = LevelJson.Int(o, "turns", path, errors, required: true, min: 1, max: LevelJson.MaxLockTurns, fallback: 0);
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
                LevelJson.Unknown(o, LinkKeys, path, errors);
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
