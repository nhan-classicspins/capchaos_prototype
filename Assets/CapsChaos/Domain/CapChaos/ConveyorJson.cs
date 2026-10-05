using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Game.Domain
{
    public sealed class ConveyorParseResult
    {
        public ConveyorDefinition Conveyor { get; }
        public IReadOnlyList<string> Errors { get; }
        public bool Ok => Conveyor != null && Errors.Count == 0;

        public ConveyorParseResult(ConveyorDefinition conveyor, IReadOnlyList<string> errors) { Conveyor = conveyor; Errors = errors; }
    }

    /// <summary>
    /// Conveyor JSON ⇄ <see cref="ConveyorDefinition"/> — the shared layout files in <c>Content/LevelConfig/Conveyors/</c>
    /// (GDD §6.2b, <c>docs/design/conveyor.schema.json</c>). <see cref="Parse"/> is validator leg V1 for a conveyor:
    /// every structural rule, reported all at once with a JSON path; V8/V9 are <see cref="ConveyorValidator"/>.
    /// <para>Format v2 (2026-10-05): the loop and the three feeders (right, left, middle) are splines of nodes, the way
    /// ConveyorKit stores a conveyor (<c>x</c>, <c>z</c>, <c>yRotation</c>, <c>tangentMode</c> 0 smooth / 1 sharp).
    /// v1 drew the loop from a preset shape: it is rejected.</para>
    /// </summary>
    public static class ConveyorJson
    {
        /// <summary>The conveyor files' folder, relative to the level folder (and under the LevelConfig address).</summary>
        public const string Folder = "Conveyors";

        public const int MinRows = 8, MaxRows = 64, MaxWidth = 6;
        public const double MinScale = 0.1, MaxScale = 4.0;

        private static readonly HashSet<string> RootKeys = new HashSet<string>(StringComparer.Ordinal)
            { "$schema", "formatVersion", "id", "rows", "width", "pickRows", "scale", "loop", "feeders", "meta" };
        private static readonly HashSet<string> LoopKeys = new HashSet<string>(StringComparer.Ordinal) { "nodes" };
        private static readonly HashSet<string> FeederKeys = new HashSet<string>(StringComparer.Ordinal) { "side", "mergeAt", "nodes" };
        private static readonly HashSet<string> NodeKeys = new HashSet<string>(StringComparer.Ordinal) { "x", "z", "yRotation", "tangentMode" };
        private static readonly HashSet<string> MetaKeys = new HashSet<string>(StringComparer.Ordinal) { "name", "notes" };
        /// <summary>How a file spells each <see cref="FeederSide"/>, in the required order.</summary>
        public static readonly string[] SideNames = { "right", "left", "middle" };

        /// <summary>The path of conveyor <paramref name="id"/>'s file, relative to the level folder.</summary>
        public static string FileOf(string id) => Folder + "/" + id + ".json";

        public static ConveyorParseResult Parse(string json)
        {
            var errors = new List<string>();
            JsonValue root;
            try { root = JsonReader.Parse(json); }
            catch (JsonParseException e) { errors.Add("json: " + e.Message); return new ConveyorParseResult(null, errors); }
            if (root.Kind != JsonKind.Object) { errors.Add("$: must be an object"); return new ConveyorParseResult(null, errors); }

            int version = LevelJson.Int(root, "formatVersion", "$", errors, required: true, min: 1,
                max: ConveyorDefinition.CurrentFormatVersion, fallback: ConveyorDefinition.CurrentFormatVersion);
            if (version < ConveyorDefinition.CurrentFormatVersion)
            {
                errors.Add($"$.formatVersion: {version} draws the loop from a preset shape; format {ConveyorDefinition.CurrentFormatVersion} " +
                           "describes the loop and three feeders (right, left, middle) as splines of nodes — see GDD §6.2b");
                return new ConveyorParseResult(null, errors);
            }
            LevelJson.Unknown(root, RootKeys, "$", errors);
            string id = LevelJson.Str(root, "id", "$", errors, required: true);
            if (id != null && !ConveyorDefinition.IsConveyorId(id))
                errors.Add($"$.id: '{id}' must be lowercase letters, digits and _, starting with a letter (it names the file)");
            int rows = LevelJson.Int(root, "rows", "$", errors, required: true, min: MinRows, max: MaxRows, fallback: 0);
            int width = LevelJson.Int(root, "width", "$", errors, required: false, min: 1, max: MaxWidth, fallback: LoopDefinition.DefaultWidth);
            int pick = LevelJson.Int(root, "pickRows", "$", errors, required: true, min: 1, max: MaxRows, fallback: 0);
            double scale = 1.0;
            if (Num(root, "scale", "$", errors, required: false, out double sv) && root.TryGet("scale", out _))
            {
                if (sv < MinScale || sv > MaxScale) errors.Add($"$.scale: {Num(sv)} outside {Num(MinScale)}..{Num(MaxScale)}");
                else scale = sv;
            }

            List<ConveyorNode> loop = null;
            if (!root.TryGet("loop", out var lp)) errors.Add("$.loop: required");
            else if (lp.Kind != JsonKind.Object) errors.Add("$.loop: must be { nodes }");
            else
            {
                LevelJson.Unknown(lp, LoopKeys, "$.loop", errors);
                loop = ReadNodes(lp, "$.loop", 3, errors);
            }

            var feeders = new List<FeederLayout>();
            if (!root.TryGet("feeders", out var fn)) errors.Add("$.feeders: required");
            else if (fn.Kind != JsonKind.Array) errors.Add("$.feeders: must be an array of { side, mergeAt, nodes }");
            else
            {
                if (fn.Items.Count != ConveyorDefinition.FeederSlots)
                    errors.Add($"$.feeders: exactly {ConveyorDefinition.FeederSlots} feeders, in the order {string.Join(", ", SideNames)} — a level uses the first N");
                for (int f = 0; f < fn.Items.Count; f++)
                {
                    var o = fn.Items[f];
                    string fp = $"$.feeders[{f}]";
                    if (o.Kind != JsonKind.Object) { errors.Add(fp + ": must be { side, mergeAt, nodes }"); continue; }
                    LevelJson.Unknown(o, FeederKeys, fp, errors);
                    string side = LevelJson.Str(o, "side", fp, errors, required: true);
                    string expected = f < SideNames.Length ? SideNames[f] : null;
                    if (side != null && side != expected)
                        errors.Add(expected != null ? $"{fp}.side: '{side}' — feeder {f} is the {expected} one (order: {string.Join(", ", SideNames)})"
                                                    : $"{fp}: there are only {SideNames.Length} feeders");
                    int merge = LevelJson.Int(o, "mergeAt", fp, errors, required: true, min: 0, max: MaxRows - 1, fallback: -1);
                    var nodes = ReadNodes(o, fp, 2, errors);
                    if (f < SideNames.Length) feeders.Add(new FeederLayout((FeederSide)f, merge, nodes));
                }
            }

            string name = null, notes = null;
            if (root.TryGet("meta", out var meta))
            {
                if (meta.Kind != JsonKind.Object) errors.Add("$.meta: must be an object");
                else
                {
                    LevelJson.Unknown(meta, MetaKeys, "$.meta", errors);
                    name = LevelJson.Str(meta, "name", "$.meta", errors, required: false);
                    notes = LevelJson.Str(meta, "notes", "$.meta", errors, required: false);
                }
            }

            if (errors.Count > 0 || id == null || loop == null) return new ConveyorParseResult(null, errors);
            return new ConveyorParseResult(new ConveyorDefinition(id, rows, width, pick, loop, feeders, name, notes, scale), errors);
        }

        /// <summary><c>"nodes": [ { "x", "z", "yRotation"?, "tangentMode"? }, … ]</c> on <paramref name="owner"/>.</summary>
        private static List<ConveyorNode> ReadNodes(JsonValue owner, string path, int min, List<string> errors)
        {
            var nodes = new List<ConveyorNode>();
            if (!owner.TryGet("nodes", out var arr)) { errors.Add(path + ".nodes: required"); return nodes; }
            if (arr.Kind != JsonKind.Array) { errors.Add(path + ".nodes: must be an array of { x, z, yRotation?, tangentMode? }"); return nodes; }
            if (arr.Items.Count < min) errors.Add($"{path}.nodes: at least {min} nodes");
            for (int i = 0; i < arr.Items.Count; i++)
            {
                var n = arr.Items[i];
                string np = $"{path}.nodes[{i}]";
                if (n.Kind != JsonKind.Object) { errors.Add(np + ": must be { x, z, yRotation?, tangentMode? }"); continue; }
                LevelJson.Unknown(n, NodeKeys, np, errors);
                bool ok = Num(n, "x", np, errors, required: true, out double x);
                ok &= Num(n, "z", np, errors, required: true, out double z);
                ok &= Num(n, "yRotation", np, errors, required: false, out double yaw);
                int mode = LevelJson.Int(n, "tangentMode", np, errors, required: false, min: 0, max: 1, fallback: 0);
                if (ok) nodes.Add(new ConveyorNode(x, z, yaw, mode == 1));
            }
            return nodes;
        }

        private static bool Num(JsonValue obj, string name, string path, List<string> errors, bool required, out double value)
        {
            value = 0;
            if (!obj.TryGet(name, out var v)) { if (required) { errors.Add($"{path}.{name}: required"); return false; } return true; }
            if (v.Kind != JsonKind.Number) { errors.Add($"{path}.{name}: must be a number"); return false; }
            value = v.Number;
            return true;
        }

        // ── writer — stable, diff-friendly layout: one node per line ──
        public static string Write(ConveyorDefinition c)
        {
            if (c.Id == null) throw new InvalidOperationException("a conveyor built in code has no id, so it has no file to be written to");
            if (c.Feeders.Count != ConveyorDefinition.FeederSlots)
                throw new InvalidOperationException($"conveyor '{c.Id}' has {c.Feeders.Count} feeders; a file has exactly {ConveyorDefinition.FeederSlots}");
            var sb = new StringBuilder();
            sb.Append("{\n");
            // conveyors live in Assets/CapsChaos/Content/LevelConfig/Conveyors/ — five levels below the repo root
            sb.Append("  \"$schema\": \"../../../../../docs/design/conveyor.schema.json\",\n");
            sb.Append($"  \"formatVersion\": {ConveyorDefinition.CurrentFormatVersion},\n");
            sb.Append($"  \"id\": {LevelJson.Q(c.Id)},\n");
            sb.Append($"  \"rows\": {c.Rows},\n");
            sb.Append($"  \"width\": {c.Width},\n");
            sb.Append($"  \"pickRows\": {c.PickRows},\n");
            sb.Append($"  \"scale\": {Num(c.Scale)},\n");
            sb.Append("  \"loop\": { \"nodes\": ");
            Nodes(sb, c.Loop, "    ");
            sb.Append(" },\n");
            sb.Append("  \"feeders\": [\n");
            for (int f = 0; f < c.Feeders.Count; f++)
            {
                var fd = c.Feeders[f];
                sb.Append($"    {{ \"side\": {LevelJson.Q(SideNames[(int)fd.Side])}, \"mergeAt\": {fd.MergeAt}, \"nodes\": ");
                Nodes(sb, fd.Nodes, "      ");
                sb.Append(" }").Append(f < c.Feeders.Count - 1 ? ",\n" : "\n");
            }
            sb.Append("  ]");
            var meta = new List<string>();
            if (c.Name != null) meta.Add($"\"name\": {LevelJson.Q(c.Name)}");
            if (c.Notes != null) meta.Add($"\"notes\": {LevelJson.Q(c.Notes)}");
            if (meta.Count > 0) sb.Append(",\n  \"meta\": { ").Append(string.Join(", ", meta)).Append(" }");
            sb.Append("\n}\n");
            return sb.ToString();
        }

        private static void Nodes(StringBuilder sb, IReadOnlyList<ConveyorNode> nodes, string indent)
        {
            if (nodes.Count == 0) { sb.Append("[]"); return; }
            sb.Append("[\n");
            for (int i = 0; i < nodes.Count; i++)
            {
                var n = nodes[i];
                sb.Append(indent).Append($"{{ \"x\": {Num(n.X)}, \"z\": {Num(n.Z)}, \"yRotation\": {Num(n.YRotation)}");
                if (n.Linear) sb.Append(", \"tangentMode\": 1");
                sb.Append(" }").Append(i < nodes.Count - 1 ? ",\n" : "\n");
            }
            sb.Append(indent, 0, indent.Length - 2).Append("]");
        }

        /// <summary>Three decimals: readable in a diff; the belt's shape does not change at that precision.</summary>
        internal static string Num(double d) => d.ToString("0.###", CultureInfo.InvariantCulture);
    }
}
