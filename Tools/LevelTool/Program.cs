using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using ClassicSpins.PrototypeFramework.Domain;
using Game.Domain;

namespace CapsChaos.LevelTool
{
    /// <summary>
    ///   LevelTool generate [--specs P] [--out DIR] [--check]   build seed levels (--check: exit 1 if any file would change)
    ///   LevelTool validate [--dir DIR] [--budget N]            V1–V6 over every level_*.json + the index
    /// Exit codes (framework CLI contract): 0 Ok · 1 Drift · 2 Error.
    /// </summary>
    public static class Program
    {
        private const int Ok = 0, Drift = 1, Error = 2;
        private const string DefaultSpecs = "Tools/LevelTool/seed-levels.json";
        private const string DefaultDir = "Assets/CapsChaos/Content/Resources/Levels";
        private const string IndexFile = "levels.index.json";

        public static int Main(string[] args)
        {
            try
            {
                if (args.Length == 0) return Usage();
                var opts = Options(args.Skip(1).ToArray());
                string root = RepoRoot();
                switch (args[0])
                {
                    case "generate":
                        return Generate(Path.Combine(root, Get(opts, "specs", DefaultSpecs)), Path.Combine(root, Get(opts, "out", DefaultDir)), opts.ContainsKey("check"));
                    case "validate":
                        return Validate(Path.Combine(root, Get(opts, "dir", DefaultDir)),
                            int.Parse(Get(opts, "budget", LevelSolver.DefaultNodeBudget.ToString(CultureInfo.InvariantCulture)), CultureInfo.InvariantCulture));
                    case "stats":
                        return Stats(Path.Combine(root, Get(opts, "dir", DefaultDir)),
                            int.Parse(Get(opts, "plays", "500"), CultureInfo.InvariantCulture));
                    default: return Usage();
                }
            }
            catch (Exception e)
            {
                Console.Error.WriteLine("error: " + e.Message);
                return Error;
            }
        }

        private static int Usage()
        {
            Console.Error.WriteLine("usage: LevelTool generate [--specs P] [--out DIR] [--check] | validate [--dir DIR] [--budget N]");
            return Error;
        }

        // ── generate ─────────────────────────────────────────────────────────────────────────
        private static int Generate(string specsPath, string outDir, bool check)
        {
            var specs = ReadSpecs(specsPath);
            Directory.CreateDirectory(outDir);
            var drift = new List<string>();
            var ids = new List<string>();
            Console.WriteLine($"{"id",-11} {"seed",6} {"colors",-6} {"bottles",7} {"hidden",6} {"trays",5} {"layers",6} {"tries",5}  lanes");
            foreach (var (spec, seed) in specs)
            {
                var gen = new LevelGenerator(new Pcg32(seed)).Generate(spec);
                var errors = LevelValidator.Validate(gen.Level);
                if (errors.Count > 0) throw new InvalidOperationException($"{spec.Id}: generator produced an invalid level: {string.Join("; ", errors)}");
                if (LevelSolver.Prove(gen.Level).Status != SolveStatus.Solvable) throw new InvalidOperationException($"{spec.Id}: construction solution does not win");

                string text = LevelJson.Write(gen.Level);
                string file = Path.Combine(outDir, spec.Id + ".json");
                Emit(file, text, check, drift);
                ids.Add(spec.Id);

                var (bottles, hidden) = Count(gen.Level);
                Console.WriteLine($"{spec.Id,-11} {seed,6} {spec.Colors,-6} {bottles,7} {hidden,6} {bottles / spec.TrayCapacity,5} {gen.Level.Stack.Layers.Count,6} {gen.Attempts,5}  " +
                                  string.Join(" ", gen.Level.Lanes.Select(l => l.Count)));
            }
            var index = new StringBuilder("{\n  \"order\": [\n");
            for (int i = 0; i < ids.Count; i++) index.Append("    \"").Append(ids[i]).Append(i < ids.Count - 1 ? "\",\n" : "\"\n");
            index.Append("  ]\n}\n");
            Emit(Path.Combine(outDir, IndexFile), index.ToString(), check, drift);

            if (check && drift.Count > 0)
            {
                foreach (var d in drift) Console.WriteLine("drift: " + d);
                return Drift;
            }
            Console.WriteLine(check ? "ok: generated levels are up to date" : $"wrote {ids.Count} levels + {IndexFile} to {outDir}");
            return Ok;
        }

        private static void Emit(string file, string text, bool check, List<string> drift)
        {
            bool same = File.Exists(file) && File.ReadAllText(file) == text;
            if (check) { if (!same) drift.Add(file); return; }
            if (!same) File.WriteAllText(file, text, new UTF8Encoding(false));
        }

        private static (int bottles, int hidden) Count(LevelDefinition l)
        {
            int b = 0, h = 0;
            foreach (var layer in l.Stack.Layers) foreach (var row in layer) foreach (char c in row)
                if (c != CapColors.Empty) { b++; if (char.IsLower(c)) h++; }
            return (b, h);
        }

        // ── validate ─────────────────────────────────────────────────────────────────────────
        private static int Validate(string dir, int budget)
        {
            int bad = 0;
            var files = Directory.GetFiles(dir, "level_*.json").OrderBy(f => f, StringComparer.Ordinal).ToList();
            foreach (var f in files)
            {
                var problems = new List<string>();
                var parsed = LevelJson.Parse(File.ReadAllText(f));
                problems.AddRange(parsed.Errors.Select(e => "V1 " + e));
                string idFromFile = Path.GetFileNameWithoutExtension(f);
                SolveReport proof = null;
                if (parsed.Ok)
                {
                    if (parsed.Level.Id != idFromFile) problems.Add($"V1 $.id: '{parsed.Level.Id}' ≠ file name '{idFromFile}'");
                    problems.AddRange(LevelValidator.Validate(parsed.Level));
                    if (problems.Count == 0)
                    {
                        proof = LevelSolver.Prove(parsed.Level, budget);
                        if (proof.Status != SolveStatus.Solvable) problems.Add($"V6 {proof.Status} (nodes {proof.NodesExplored})");
                    }
                }
                Console.WriteLine($"{(problems.Count == 0 ? "ok  " : "FAIL")} {idFromFile}" +
                                  (proof != null ? $"  proof={(parsed.Level.Solution != null ? "replay" : "search")} taps={proof.Solution.Count}" : ""));
                foreach (var p in problems) Console.WriteLine("       " + p);
                if (problems.Count > 0) bad++;
            }

            var indexProblems = CheckIndex(dir, files.Select(Path.GetFileNameWithoutExtension).ToList());
            foreach (var p in indexProblems) Console.WriteLine("FAIL index: " + p);
            if (indexProblems.Count > 0) bad++;

            Console.WriteLine(bad == 0 ? $"ok: {files.Count} levels valid and solvable" : $"{bad} problem file(s)");
            return bad == 0 ? Ok : Error;
        }

        // ── stats: a difficulty read-out, not a gate ─────────────────────────────────────────
        /// <summary>Random-play win rate (uniform over non-empty lanes, fixed seed) + solver effort per level.</summary>
        private static int Stats(string dir, int plays)
        {
            const long seed = 20260930;
            Console.WriteLine($"random-play seed {seed}, {plays} plays per level");
            Console.WriteLine($"{"id",-11} {"difficulty",-9} {"trays",5} {"random win",10} {"solver nodes",12} {"dead-end",8}");
            foreach (var f in Directory.GetFiles(dir, "level_*.json").OrderBy(f => f, StringComparer.Ordinal))
            {
                var level = LevelJson.Parse(File.ReadAllText(f)).Level;
                if (level == null) { Console.WriteLine($"{Path.GetFileName(f)}: does not parse (run validate)"); continue; }
                var rng = new Pcg32(seed);
                int wins = 0;
                for (int p = 0; p < plays; p++)
                {
                    var g = new CapChaosGame(level);
                    var open = new List<int>();
                    while (g.Status == GameStatus.Playing)
                    {
                        open.Clear();
                        for (int j = 0; j < g.LaneCount; j++) if (g.LaneRemaining(j) > 0) open.Add(j);
                        g.Tap(open[rng.NextInt(0, open.Count)]);
                    }
                    if (g.Status == GameStatus.Won) wins++;
                }
                var search = LevelSolver.Solve(level);
                Console.WriteLine($"{level.Id,-11} {level.Difficulty ?? "-",-9} {level.Lanes.Sum(l => l.Count),5} {100.0 * wins / plays,9:0.0}% " +
                                  $"{(search.Status == SolveStatus.Solvable ? search.NodesExplored.ToString(CultureInfo.InvariantCulture) : search.Status.ToString()),12} {search.DeadEndRatio,8:0.00}");
            }
            return Ok;
        }

        public static List<string> CheckIndex(string dir, List<string> levelIds)
        {
            var problems = new List<string>();
            string path = Path.Combine(dir, IndexFile);
            if (!File.Exists(path)) { problems.Add($"{IndexFile} missing"); return problems; }
            var root = JsonReader.Parse(File.ReadAllText(path));
            if (!root.TryGet("order", out var order) || order.Kind != JsonKind.Array) { problems.Add("'order' array missing"); return problems; }
            var listed = order.Items.Select(i => i.String).ToList();
            foreach (var dup in listed.GroupBy(x => x).Where(g => g.Count() > 1)) problems.Add($"'{dup.Key}' listed {dup.Count()} times");
            foreach (var id in listed.Except(levelIds)) problems.Add($"'{id}' listed but no {id}.json");
            foreach (var id in levelIds.Except(listed)) problems.Add($"{id}.json exists but is not in the order");
            return problems;
        }

        // ── specs ────────────────────────────────────────────────────────────────────────────
        private static List<(LevelSpec spec, long seed)> ReadSpecs(string path)
        {
            var root = JsonReader.Parse(File.ReadAllText(path));
            if (!root.TryGet("levels", out var levels)) throw new InvalidDataException("specs: 'levels' missing");
            var list = new List<(LevelSpec, long)>();
            foreach (var l in levels.Items)
            {
                var spec = new LevelSpec
                {
                    Id = S(l, "id"), Name = S(l, "name"), Difficulty = S(l, "difficulty"), Notes = S(l, "notes"),
                    Colors = S(l, "colors") ?? "ROBG",
                    Slots = (int)N(l, "slots", LevelDefinition.DefaultSlots),
                    TrayCapacity = (int)N(l, "trayCapacity", LevelDefinition.DefaultTrayCapacity),
                    Lanes = (int)N(l, "lanes", 3),
                    Greed = N(l, "greed", 0.6),
                    Clustering = N(l, "clustering", 0.3),
                };
                if (!l.TryGet("shape", out var shape)) throw new InvalidDataException($"{spec.Id}: 'shape' missing");
                foreach (var layer in shape.Items) spec.Shape.Add(layer.Items.Select(r => r.String).ToList());
                list.Add((spec, (long)N(l, "seed", 1)));
            }
            return list;
        }

        private static string S(JsonValue o, string k) => o.TryGet(k, out var v) && v.Kind == JsonKind.String ? v.String : null;
        private static double N(JsonValue o, string k, double d) => o.TryGet(k, out var v) && v.Kind == JsonKind.Number ? v.Number : d;

        // ── plumbing ─────────────────────────────────────────────────────────────────────────
        private static Dictionary<string, string> Options(string[] a)
        {
            var d = new Dictionary<string, string>(StringComparer.Ordinal);
            for (int i = 0; i < a.Length; i++)
            {
                if (!a[i].StartsWith("--", StringComparison.Ordinal)) throw new ArgumentException($"unexpected argument '{a[i]}'");
                string key = a[i].Substring(2);
                if (i + 1 < a.Length && !a[i + 1].StartsWith("--", StringComparison.Ordinal)) d[key] = a[++i];
                else d[key] = "true";
            }
            return d;
        }

        private static string Get(Dictionary<string, string> d, string k, string fallback) => d.TryGetValue(k, out var v) ? v : fallback;

        private static string RepoRoot()
        {
            var d = new DirectoryInfo(Directory.GetCurrentDirectory());
            while (d != null && !File.Exists(Path.Combine(d.FullName, "framework.config.json"))) d = d.Parent;
            return d?.FullName ?? throw new InvalidOperationException("run inside the SKU repo (no framework.config.json found above the working directory)");
        }
    }
}
