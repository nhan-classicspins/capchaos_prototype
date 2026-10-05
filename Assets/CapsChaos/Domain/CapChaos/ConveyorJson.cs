using System;
using System.Collections.Generic;
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
    /// </summary>
    public static class ConveyorJson
    {
        /// <summary>The conveyor files' folder, relative to the level folder (and under the LevelConfig address).</summary>
        public const string Folder = "Conveyors";

        public const int MinRows = 8, MaxRows = 64, MaxWidth = 6, MaxFeeders = 2;

        private static readonly HashSet<string> RootKeys = new HashSet<string>(StringComparer.Ordinal)
            { "$schema", "formatVersion", "id", "rows", "width", "pickRows", "shape", "feeders", "meta" };
        private static readonly HashSet<string> FeederKeys = new HashSet<string>(StringComparer.Ordinal) { "mergeAt" };
        private static readonly HashSet<string> ShapeKeys = new HashSet<string>(StringComparer.Ordinal) { "points", "radius" };
        private static readonly HashSet<string> MetaKeys = new HashSet<string>(StringComparer.Ordinal) { "name", "notes" };

        /// <summary>The path of conveyor <paramref name="id"/>'s file, relative to the level folder.</summary>
        public static string FileOf(string id) => Folder + "/" + id + ".json";

        public static ConveyorParseResult Parse(string json)
        {
            var errors = new List<string>();
            JsonValue root;
            try { root = JsonReader.Parse(json); }
            catch (JsonParseException e) { errors.Add("json: " + e.Message); return new ConveyorParseResult(null, errors); }
            if (root.Kind != JsonKind.Object) { errors.Add("$: must be an object"); return new ConveyorParseResult(null, errors); }

            LevelJson.Int(root, "formatVersion", "$", errors, required: true, min: ConveyorDefinition.CurrentFormatVersion,
                max: ConveyorDefinition.CurrentFormatVersion, fallback: ConveyorDefinition.CurrentFormatVersion);
            LevelJson.Unknown(root, RootKeys, "$", errors);
            string id = LevelJson.Str(root, "id", "$", errors, required: true);
            if (id != null && !ConveyorDefinition.IsConveyorId(id))
                errors.Add($"$.id: '{id}' must be lowercase letters, digits and _, starting with a letter (it names the file)");
            int rows = LevelJson.Int(root, "rows", "$", errors, required: true, min: MinRows, max: MaxRows, fallback: 0);
            int width = LevelJson.Int(root, "width", "$", errors, required: false, min: 1, max: MaxWidth, fallback: LoopDefinition.DefaultWidth);
            int pick = LevelJson.Int(root, "pickRows", "$", errors, required: true, min: 1, max: MaxRows, fallback: 0);
            LoopShape shape = root.TryGet("shape", out var sv) ? ReadShape(sv, "$.shape", errors) : LoopShape.Default;

            var mergeAt = new List<int>();
            if (!root.TryGet("feeders", out var fn)) errors.Add("$.feeders: required");
            else if (fn.Kind != JsonKind.Array) errors.Add("$.feeders: must be an array of { mergeAt }");
            else
            {
                if (fn.Items.Count > MaxFeeders) errors.Add($"$.feeders: 0..{MaxFeeders} feeders");
                for (int f = 0; f < fn.Items.Count; f++)
                {
                    var o = fn.Items[f];
                    string fp = $"$.feeders[{f}]";
                    if (o.Kind != JsonKind.Object) { errors.Add(fp + ": must be { mergeAt }"); continue; }
                    LevelJson.Unknown(o, FeederKeys, fp, errors);
                    mergeAt.Add(LevelJson.Int(o, "mergeAt", fp, errors, required: true, min: 0, max: MaxRows - 1, fallback: -1));
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

            if (errors.Count > 0 || id == null || shape == null) return new ConveyorParseResult(null, errors);
            return new ConveyorParseResult(new ConveyorDefinition(id, rows, width, pick, mergeAt, shape, name, notes), errors);
        }

        /// <summary><c>shape</c>: a preset name, or <c>{ "points": [[x, z], …], "radius": r | [r, …] }</c> (V9).</summary>
        private static LoopShape ReadShape(JsonValue v, string path, List<string> errors)
        {
            if (v.Kind == JsonKind.String)
            {
                if (Array.IndexOf(LoopShape.Presets, v.String) >= 0) return LoopShape.Named(v.String);
                errors.Add($"{path}: '{v.String}' not in [{string.Join(", ", LoopShape.Presets)}]");
                return null;
            }
            if (v.Kind != JsonKind.Object) { errors.Add($"{path}: a preset name or {{ points, radius }}"); return null; }
            LevelJson.Unknown(v, ShapeKeys, path, errors);
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

        // ── writer — stable, diff-friendly layout: one feeder per line ──
        public static string Write(ConveyorDefinition c)
        {
            if (c.Id == null) throw new InvalidOperationException("a conveyor built in code has no id, so it has no file to be written to");
            var sb = new StringBuilder();
            sb.Append("{\n");
            // conveyors live in Assets/CapsChaos/Content/LevelConfig/Conveyors/ — five levels below the repo root
            sb.Append("  \"$schema\": \"../../../../../docs/design/conveyor.schema.json\",\n");
            sb.Append($"  \"formatVersion\": {ConveyorDefinition.CurrentFormatVersion},\n");
            sb.Append($"  \"id\": {LevelJson.Q(c.Id)},\n");
            sb.Append($"  \"rows\": {c.Rows},\n");
            sb.Append($"  \"width\": {c.Width},\n");
            sb.Append($"  \"pickRows\": {c.PickRows},\n");
            sb.Append($"  \"shape\": {WriteShape(c.Shape)},\n");
            sb.Append("  \"feeders\": [");
            if (c.MergeAt.Count == 0) sb.Append("]");
            else
            {
                sb.Append("\n");
                for (int f = 0; f < c.MergeAt.Count; f++)
                    sb.Append($"    {{ \"mergeAt\": {c.MergeAt[f]} }}").Append(f < c.MergeAt.Count - 1 ? ",\n" : "\n");
                sb.Append("  ]");
            }
            var meta = new List<string>();
            if (c.Name != null) meta.Add($"\"name\": {LevelJson.Q(c.Name)}");
            if (c.Notes != null) meta.Add($"\"notes\": {LevelJson.Q(c.Notes)}");
            if (meta.Count > 0) sb.Append(",\n  \"meta\": { ").Append(string.Join(", ", meta)).Append(" }");
            sb.Append("\n}\n");
            return sb.ToString();
        }

        private static string WriteShape(LoopShape shape)
        {
            if (shape.Preset != null) return LevelJson.Q(shape.Preset);
            var pts = new List<string>();
            for (int i = 0; i < shape.Xs.Count; i++) pts.Add($"[{Num(shape.Xs[i])}, {Num(shape.Zs[i])}]");
            var radii = new List<string>();
            bool same = true;
            for (int i = 0; i < shape.Radii.Count; i++) { radii.Add(Num(shape.Radii[i])); same &= shape.Radii[i] == shape.Radii[0]; }
            return $"{{ \"points\": [{string.Join(", ", pts)}], \"radius\": {(same ? radii[0] : "[" + string.Join(", ", radii) + "]")} }}";
        }

        private static string Num(double d) => d.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture);
    }
}
