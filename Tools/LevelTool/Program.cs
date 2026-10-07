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
    ///   LevelTool validate [--dir DIR] [--budget N]            V1–V9 over every ../ConveyorConfig/*.json, every level_*.json + the index
    ///   LevelTool migrate  [--dir DIR] [--check]               rewrite every conveyor and level file in the current format, content unchanged
    /// Levels name a shared conveyor layout in DIR/../ConveyorConfig/&lt;id&gt;.json (beside the level folder) (GDD §6.2b); generate never writes one.
    /// Exit codes (framework CLI contract): 0 Ok · 1 Drift · 2 Error.
    /// </summary>
    public static class Program
    {
        private const int Ok = 0, Drift = 1, Error = 2;
        private const string DefaultSpecs = "Tools/LevelTool/seed-levels.json";
        private const string DefaultDir = "Assets/CapsChaos/Content/Configs/LevelConfig";
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
                    case "conveyors":
                        return Conveyors(Path.Combine(root, Get(opts, "dir", DefaultDir)));
                    case "validate":
                        return Validate(Path.Combine(root, Get(opts, "dir", DefaultDir)),
                            int.Parse(Get(opts, "budget", LevelSolver.DefaultNodeBudget.ToString(CultureInfo.InvariantCulture)), CultureInfo.InvariantCulture));
                    case "migrate":
                        return Migrate(Path.Combine(root, Get(opts, "dir", DefaultDir)), opts.ContainsKey("check"));
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
            Console.Error.WriteLine("usage: LevelTool generate [--specs P] [--out DIR] [--check] | validate [--dir DIR] [--budget N] | migrate [--dir DIR] [--check] | stats [--dir DIR] [--plays N] | conveyors [--dir DIR]");
            return Error;
        }

        // ── generate ─────────────────────────────────────────────────────────────────────────
        private static int Generate(string specsPath, string outDir, bool check)
        {
            var library = LoadLibrary(outDir);
            var specs = ReadSpecs(specsPath, library);
            Directory.CreateDirectory(outDir);
            var drift = new List<string>();
            var ids = new List<string>();
            Console.WriteLine($"{"id",-11} {"seed",6} {"colors",-6} {"bottles",7} {"trays",5} {"conveyor",-15} {"tries",5}  lanes");
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

                int bottles = gen.Level.Loop.AllBottles().Count();
                Console.WriteLine($"{spec.Id,-11} {seed,6} {CapColorCodes.ToCodes(spec.Colors),-6} {bottles,7} {bottles / spec.TrayCapacity,5} {spec.Conveyor.Id,-15} {gen.Attempts,5}  " +
                                  string.Join(" ", gen.Level.Lanes.Select(l => l.Count)));
            }
            int generated = ids.Count;
            // hand-authored levels (not in the spec) are kept; the play order is the id order, so a level's number is its place
            if (Directory.Exists(outDir))
                ids.AddRange(Directory.GetFiles(outDir, "level_*.json").Select(Path.GetFileNameWithoutExtension)
                    .Where(id => !ids.Contains(id)));
            ids.Sort(StringComparer.Ordinal);
            var index = new StringBuilder("{\n  \"order\": [\n");
            for (int i = 0; i < ids.Count; i++) index.Append("    \"").Append(ids[i]).Append(i < ids.Count - 1 ? "\",\n" : "\"\n");
            index.Append("  ]\n}\n");
            Emit(Path.Combine(outDir, IndexFile), index.ToString(), check, drift);

            if (check && drift.Count > 0)
            {
                foreach (var d in drift) Console.WriteLine("drift: " + d);
                return Drift;
            }
            Console.WriteLine(check ? "ok: generated levels are up to date" : $"wrote {generated} levels + {IndexFile} ({ids.Count - generated} hand-authored kept) to {outDir}");
            return Ok;
        }

        private static void Emit(string file, string text, bool check, List<string> drift)
        {
            bool same = File.Exists(file) && File.ReadAllText(file) == text;
            if (check) { if (!same) drift.Add(file); return; }
            if (!same) File.WriteAllText(file, text, new UTF8Encoding(false));
        }

        // ── migrate ──────────────────────────────────────────────────────────────────────────
        /// <summary>
        /// Read every conveyor and level and write it back in the current layout (ConveyorJson.Write / LevelJson.Write).
        /// Nothing changes but the formatting — hand edits, hand-authored levels and solutions survive. --check: exit 1
        /// if a file is not in the current layout yet.
        /// </summary>
        private static int Migrate(string dir, bool check)
        {
            var drift = new List<string>();
            int failed = 0;
            var library = new ConveyorLibrary();
            foreach (var f in ConveyorFiles(dir))
            {
                var parsed = ConveyorJson.Parse(File.ReadAllText(f));
                if (!parsed.Ok)
                {
                    failed++;
                    foreach (var e in parsed.Errors) Console.WriteLine($"FAIL {ConveyorJson.Folder}/{Path.GetFileName(f)}: {e}");
                    continue;
                }
                library.Add(parsed.Conveyor);
                Emit(f, ConveyorJson.Write(parsed.Conveyor), check, drift);
            }
            foreach (var f in Directory.GetFiles(dir, "level_*.json").OrderBy(f => f, StringComparer.Ordinal))
            {
                var parsed = LevelJson.Parse(File.ReadAllText(f), library);
                if (!parsed.Ok)
                {
                    failed++;
                    foreach (var e in parsed.Errors) Console.WriteLine($"FAIL {Path.GetFileName(f)}: {e}");
                    continue;
                }
                Emit(f, LevelJson.Write(parsed.Level), check, drift);
            }
            if (failed > 0) return Error;
            if (check && drift.Count > 0) { foreach (var d in drift) Console.WriteLine("drift: " + d); return Drift; }
            Console.WriteLine(check ? "ok: every level is in format " + LevelDefinition.CurrentFormatVersion
                                    : $"migrated {Directory.GetFiles(dir, "level_*.json").Length} levels to format {LevelDefinition.CurrentFormatVersion}");
            return Ok;
        }

        // ── validate ─────────────────────────────────────────────────────────────────────────
        private static int Validate(string dir, int budget)
        {
            int bad = 0;
            var library = new ConveyorLibrary();
            var conveyorFiles = ConveyorFiles(dir);
            foreach (var f in conveyorFiles)
            {
                var problems = ConveyorProblems(f, out var conveyor);
                if (conveyor != null && problems.Count == 0) library.Add(conveyor);
                Console.WriteLine($"{(problems.Count == 0 ? "ok  " : "FAIL")} {ConveyorJson.Folder}/{Path.GetFileName(f)}");
                foreach (var p in problems) Console.WriteLine("       " + p);
                if (problems.Count > 0) bad++;
            }

            var used = new HashSet<string>(StringComparer.Ordinal);
            var files = Directory.GetFiles(dir, "level_*.json").OrderBy(f => f, StringComparer.Ordinal).ToList();
            foreach (var f in files)
            {
                var problems = new List<string>();
                var parsed = LevelJson.Parse(File.ReadAllText(f), library);
                problems.AddRange(parsed.Errors.Select(e => "V1 " + e));
                string idFromFile = Path.GetFileNameWithoutExtension(f);
                SolveReport proof = null;
                if (parsed.Ok)
                {
                    used.Add(parsed.Level.Conveyor.Id);
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

            foreach (var c in library.All.Where(c => !used.Contains(c.Id)).OrderBy(c => c.Id, StringComparer.Ordinal))
                Console.WriteLine($"note: conveyor '{c.Id}' is not used by any level");
            Console.WriteLine(bad == 0 ? $"ok: {conveyorFiles.Count} conveyors and {files.Count} levels valid, every level solvable" : $"{bad} problem file(s)");
            return bad == 0 ? Ok : Error;
        }

        // ── stats: a difficulty read-out, not a gate ─────────────────────────────────────────
        /// <summary>Random-play win rate (uniform over non-empty lanes, tapping when the board is quiet, fixed seed) +
        /// solver effort per level.</summary>
        private static int Stats(string dir, int plays)
        {
            const long seed = 20260930;
            Console.WriteLine($"random-play seed {seed}, {plays} plays per level");
            Console.WriteLine($"{"id",-11} {"difficulty",-9} {"trays",5} {"random win",10} {"solver nodes",12} {"dead-end",8}");
            var library = LoadLibrary(dir);
            foreach (var f in Directory.GetFiles(dir, "level_*.json").OrderBy(f => f, StringComparer.Ordinal))
            {
                var level = LevelJson.Parse(File.ReadAllText(f), library).Level;
                if (level == null) { Console.WriteLine($"{Path.GetFileName(f)}: does not parse (run validate)"); continue; }
                var rng = new Pcg32(seed);
                int wins = 0;
                for (int p = 0; p < plays; p++)
                {
                    var g = new CapChaosGame(level);
                    g.Settle();
                    var open = new List<int>();
                    while (g.Status == GameStatus.Playing)
                    {
                        open.Clear();
                        for (int j = 0; j < g.LaneCount; j++) if (g.LaneRemaining(j) > 0) open.Add(j);
                        g.Tap(open[rng.NextInt(0, open.Count)]);
                        g.Settle();
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

        // ── conveyors ───────────────────────────────────────────────────────────────────────
        /// <summary>Every conveyor and how many levels run on it — which layouts there are to reuse.</summary>
        private static int Conveyors(string dir)
        {
            var library = LoadLibrary(dir);
            var uses = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            foreach (var f in Directory.GetFiles(dir, "level_*.json").OrderBy(f => f, StringComparer.Ordinal))
            {
                string text = File.ReadAllText(f);
                string id = LevelJson.ConveyorIdOf(text) ?? "?";
                var root = JsonReader.Parse(text);
                int feeders = root.TryGet("feeders", out var fn) && fn.Kind == JsonKind.Array ? fn.Items.Count : 0;
                if (!uses.TryGetValue(id, out var list)) uses[id] = list = new List<string>();
                list.Add($"{Path.GetFileNameWithoutExtension(f)}({feeders}f)");
            }
            Console.WriteLine($"{"conveyor",-15} {"knots",5} {"rows",4} {"width",5} {"pick",4} {"mergeAt R,L,M",-13} levels (feeders used)");
            foreach (var c in library.All.OrderBy(c => c.Id, StringComparer.Ordinal))
            {
                var levels = uses.TryGetValue(c.Id, out var l) ? l : new List<string>();
                Console.WriteLine($"{c.Id,-15} {c.Loop.Count,5} {c.Rows,4} {c.Width,5} {c.PickRows,4} {string.Join(",", c.MergeAt),-13} " +
                                  (levels.Count == 0 ? "(unused)" : string.Join(" ", levels)));
            }
            return Ok;
        }

        private static List<string> ConveyorFiles(string dir)
        {
            string folder = Path.Combine(dir, "..", ConveyorJson.Folder);   // beside the level folder
            return Directory.Exists(folder)
                ? Directory.GetFiles(folder, "*.json").OrderBy(f => f, StringComparer.Ordinal).ToList()
                : new List<string>();
        }

        /// <summary>V1 (structure, id = file name) and V8/V9 for one conveyor file.</summary>
        private static List<string> ConveyorProblems(string file, out ConveyorDefinition conveyor)
        {
            var problems = new List<string>();
            var parsed = ConveyorJson.Parse(File.ReadAllText(file));
            conveyor = parsed.Conveyor;
            problems.AddRange(parsed.Errors.Select(e => "V1 " + e));
            if (conveyor == null) return problems;
            string idFromFile = Path.GetFileNameWithoutExtension(file);
            if (conveyor.Id != idFromFile) problems.Add($"V1 $.id: '{conveyor.Id}' ≠ file name '{idFromFile}'");
            problems.AddRange(ConveyorValidator.Validate(conveyor));
            return problems;
        }

        /// <summary>The conveyor library under DIR; any broken conveyor file is an error (run validate for the detail).</summary>
        private static ConveyorLibrary LoadLibrary(string dir)
        {
            var library = new ConveyorLibrary();
            foreach (var f in ConveyorFiles(dir))
            {
                var problems = ConveyorProblems(f, out var conveyor);
                if (problems.Count > 0) throw new InvalidDataException($"{ConveyorJson.Folder}/{Path.GetFileName(f)}: {string.Join("; ", problems)}");
                library.Add(conveyor);
            }
            return library;
        }

        // ── specs ────────────────────────────────────────────────────────────────────────────
        private static List<(LevelSpec spec, long seed)> ReadSpecs(string path, ConveyorLibrary library)
        {
            var root = JsonReader.Parse(File.ReadAllText(path));
            if (!root.TryGet("levels", out var levels)) throw new InvalidDataException("specs: 'levels' missing");
            var list = new List<(LevelSpec, long)>();
            foreach (var l in levels.Items)
            {
                var spec = new LevelSpec
                {
                    Id = S(l, "id"), Name = S(l, "name"), Difficulty = S(l, "difficulty"), Notes = S(l, "notes"),
                    Colors = CapColorCodes.ParseList(S(l, "colors") ?? "ROBG"),
                    Slots = (int)N(l, "slots", LevelDefinition.DefaultSlots),
                    TrayCapacity = (int)N(l, "trayCapacity", LevelDefinition.DefaultTrayCapacity),
                    Lanes = (int)N(l, "lanes", 3),
                    Greed = N(l, "greed", 0.6),
                    Clustering = N(l, "clustering", 0.3),
                };
                string conveyor = S(l, "conveyor") ?? throw new InvalidDataException($"{spec.Id}: 'conveyor' missing");
                if (!library.TryGet(conveyor, out var c))
                    throw new InvalidDataException($"{spec.Id}: conveyor '{conveyor}' not found ({ConveyorJson.FileOf(conveyor)})");
                spec.Conveyor = c;
                if (!l.TryGet("feeders", out var feeders) || feeders.Kind != JsonKind.Array)
                    throw new InvalidDataException($"{spec.Id}: 'feeders' missing — bottles per conveyor feeder, e.g. [64, 32]");
                foreach (var f in feeders.Items) spec.FeederBottles.Add((int)f.Number);
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
