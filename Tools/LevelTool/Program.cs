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
    ///   LevelTool generate [--specs P] [--out DIR] [--check] [--plays N]   build seed levels (--check: exit 1 if any file would change;
    ///                                                            --plays: print each level's random-play win rate, a difficulty read-out)
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
                        return Generate(Path.Combine(root, Get(opts, "specs", DefaultSpecs)), Path.Combine(root, Get(opts, "out", DefaultDir)), opts.ContainsKey("check"),
                            int.Parse(Get(opts, "plays", "0"), CultureInfo.InvariantCulture));
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
            Console.Error.WriteLine("usage: LevelTool generate [--specs P] [--out DIR] [--check] [--plays N] | validate [--dir DIR] [--budget N] | migrate [--dir DIR] [--check] | stats [--dir DIR] [--plays N] | conveyors [--dir DIR]");
            return Error;
        }

        // ── generate ─────────────────────────────────────────────────────────────────────────
        private static int Generate(string specsPath, string outDir, bool check, int plays)
        {
            var library = LoadLibrary(outDir);
            var specs = ReadSpecs(specsPath, library);
            Directory.CreateDirectory(outDir);
            var drift = new List<string>();
            var ids = new List<string>();
            Console.WriteLine($"{"id",-11} {"seed",6} {"colors",-8} {"bottles",7} {"trays",5} {"conveyor",-12} {"rsd",5} {"proof",-7} {"random win",10}  lanes");
            foreach (var (spec, seed, mods) in specs)
            {
                // a spec with a targetWin band is regenerated (seed, seed + 1·Reseed, …) until a placement lands in the band;
                // every stream derives from the spec's seed (rule #14: one logged seed per level)
                Candidate best = null;
                for (int r = 0; r < (mods.HasTarget ? MaxReseeds : 1) && !(best?.InBand ?? false); r++)
                {
                    long s = seed + r * Reseed;
                    var gen = new LevelGenerator(new Pcg32(s)).Generate(spec);
                    var built = LevelValidator.Validate(gen.Level);
                    if (built.Count > 0) throw new InvalidOperationException($"{spec.Id}: generator produced an invalid level: {string.Join("; ", built)}");
                    if (LevelSolver.Prove(gen.Level).Status != SolveStatus.Solvable) throw new InvalidOperationException($"{spec.Id}: construction solution does not win");
                    var c = mods.Any ? Decorate(gen.Level, mods, new Pcg32(s * 7919 + 17)) : mods.Rate(gen.Level, "built");
                    if (c != null) c.Reseed = r;
                    if (c != null && (best == null || c.Miss < best.Miss)) best = c;
                }
                if (best == null) throw new InvalidOperationException($"{spec.Id}: no provable placement of {mods} — fewer / shorter locks");
                var level = best.Level;
                string proof = best.Proof;
                if (!best.InBand) Console.WriteLine($"warn {spec.Id}: closest to targetWin {mods.TargetMin}..{mods.TargetMax}% is {best.Win:0.0}%");
                var errors = LevelValidator.Validate(level);
                if (errors.Count > 0) throw new InvalidOperationException($"{spec.Id}: decorated level is invalid: {string.Join("; ", errors)}");
                string text = LevelJson.Write(level);
                string file = Path.Combine(outDir, spec.Id + ".json");
                Emit(file, text, check, drift);
                ids.Add(spec.Id);

                int bottles = level.Loop.AllBottles().Count();
                string win = plays > 0 ? $"{100.0 * RandomWins(level, plays) / plays,9:0.0}%" : mods.HasTarget ? $"{best.Win,9:0.0}%" : "";
                Console.WriteLine($"{spec.Id,-11} {seed,6} {CapColorCodes.ToCodes(spec.Colors),-8} {bottles,7} {bottles / spec.TrayCapacity,5} {spec.Conveyor.Id,-12} {best.Reseed,5} {proof,-7} {win,10}  " +
                                  string.Join(" ", level.Lanes.Select(l => l.Count)) + (mods.Any ? "  " + mods : ""));
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
            Console.WriteLine($"random-play seed {RandomPlaySeed}, {plays} plays per level");
            Console.WriteLine($"{"id",-11} {"difficulty",-9} {"trays",5} {"random win",10} {"solver nodes",12} {"dead-end",8}");
            var library = LoadLibrary(dir);
            foreach (var f in Directory.GetFiles(dir, "level_*.json").OrderBy(f => f, StringComparer.Ordinal))
            {
                var level = LevelJson.Parse(File.ReadAllText(f), library).Level;
                if (level == null) { Console.WriteLine($"{Path.GetFileName(f)}: does not parse (run validate)"); continue; }
                int wins = RandomWins(level, plays);
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
        private static List<(LevelSpec spec, long seed, Modifiers mods)> ReadSpecs(string path, ConveyorLibrary library)
        {
            var root = JsonReader.Parse(File.ReadAllText(path));
            if (!root.TryGet("levels", out var levels)) throw new InvalidDataException("specs: 'levels' missing");
            var list = new List<(LevelSpec, long, Modifiers)>();
            foreach (var l in levels.Items)
            {
                var spec = new LevelSpec
                {
                    Id = S(l, "id"), Name = S(l, "name"), Difficulty = S(l, "difficulty"), Notes = S(l, "notes"),
                    Colors = CapColorCodes.ParseList(S(l, "colors") ?? "ROBG"),
                    Slots = (int)N(l, "slots", LevelDefinition.DefaultSlots),
                    ExtraSlots = (int)N(l, "extraSlots", LevelDefinition.DefaultExtraSlots),
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
                list.Add((spec, (long)N(l, "seed", 1), ReadModifiers(l, spec.Id)));
            }
            return list;
        }

        /// <summary>
        /// Optional "modifiers" of a spec — how many of each special element the level gets; WHERE they go is picked by the
        /// tool from the level's seed: <c>{"hiddenRows": 3, "hiddenTrays": 2, "lockedRows": 1, "lockedTrays": 2,
        /// "lockTurns": [2, 4], "sizedTrays": 2, "sizes": [2, 3], "slotLocks": [{"slot": 3, "lockTurns": 4}]}</c>.
        /// </summary>
        private static Modifiers ReadModifiers(JsonValue spec, string id)
        {
            var m = new Modifiers();
            if (spec.TryGet("targetWin", out var top) && top.Kind == JsonKind.Array && top.Items.Count == 2)
            { m.TargetMin = top.Items[0].Number; m.TargetMax = top.Items[1].Number; }
            if (!spec.TryGet("modifiers", out var o)) return m;
            m.HiddenRows = (int)N(o, "hiddenRows", 0);
            m.HiddenTrays = (int)N(o, "hiddenTrays", 0);
            m.LockedRows = (int)N(o, "lockedRows", 0);
            m.LockedTrays = (int)N(o, "lockedTrays", 0);
            m.SizedTrays = (int)N(o, "sizedTrays", 0);
            if (o.TryGet("sizes", out var sz) && sz.Kind == JsonKind.Array && sz.Items.Count == 2)
            { m.SizeMin = (int)sz.Items[0].Number; m.SizeMax = (int)sz.Items[1].Number; }
            if (m.SizeMin < 2 || m.SizeMax > 4 || m.SizeMax < m.SizeMin) throw new InvalidDataException($"{id}: modifiers.sizes must be [min, max] within 2..4");
            if (o.TryGet("lockTurns", out var t) && t.Kind == JsonKind.Array && t.Items.Count == 2)
            { m.LockMin = (int)t.Items[0].Number; m.LockMax = (int)t.Items[1].Number; }
            if (m.LockMin < 1 || m.LockMax < m.LockMin) throw new InvalidDataException($"{id}: modifiers.lockTurns must be [min, max], 1 ≤ min ≤ max");
            if (o.TryGet("targetWin", out var tw) && tw.Kind == JsonKind.Array && tw.Items.Count == 2)
            { m.TargetMin = tw.Items[0].Number; m.TargetMax = tw.Items[1].Number; }
            if (o.TryGet("slotLocks", out var sl) && sl.Kind == JsonKind.Array)
                foreach (var s in sl.Items) m.SlotLocks.Add(new SlotLock((int)N(s, "slot", 0), (int)N(s, "lockTurns", 1)));
            return m;
        }

        private sealed class Modifiers
        {
            public int HiddenRows, HiddenTrays, LockedRows, LockedTrays;
            /// <summary>R21: how many bigger containers, each of a size drawn from SizeMin..SizeMax (2 M · 3 L · 4 XL).</summary>
            public int SizedTrays, SizeMin = 2, SizeMax = 2;
            public int LockMin = 2, LockMax = 4;
            public readonly List<SlotLock> SlotLocks = new List<SlotLock>();
            /// <summary>"targetWin": [min, max] — the random-play win rate (%, open slots only) the level must land in.</summary>
            public double TargetMin = 0, TargetMax = 100;
            public bool HasTarget => TargetMin > 0 || TargetMax < 100;
            /// <summary>The level with its random-play win rate and how far that is from the band (0 = inside).</summary>
            public Candidate Rate(LevelDefinition level, string proof)
            {
                double win = HasTarget ? 100.0 * RandomWins(level, TargetPlays) / TargetPlays : 100;
                double miss = !HasTarget ? 0 : win < TargetMin ? TargetMin - win : win > TargetMax ? win - TargetMax : 0;
                return new Candidate { Level = level, Proof = proof, Win = win, Miss = miss };
            }
            public bool Any => HiddenRows + HiddenTrays + LockedRows + LockedTrays + SizedTrays + SlotLocks.Count > 0;
            /// <summary>Hidden rows / trays are a look only (R17, R23); locks change what can be played.</summary>
            public bool ChangesRules => LockedRows + LockedTrays + SizedTrays + SlotLocks.Count > 0;
            public override string ToString()
            {
                var parts = new List<string>();
                if (HiddenRows > 0) parts.Add($"hiddenRows {HiddenRows}");
                if (HiddenTrays > 0) parts.Add($"hiddenTrays {HiddenTrays}");
                if (LockedRows > 0) parts.Add($"lockedRows {LockedRows}");
                if (LockedTrays > 0) parts.Add($"lockedTrays {LockedTrays}");
                if (SizedTrays > 0) parts.Add($"sized {SizedTrays} ({SizeMin}..{SizeMax})");
                foreach (var s in SlotLocks) parts.Add($"slot {s.Slot} locked {s.Turns}");
                return string.Join(", ", parts);
            }
        }

        /// <summary>Merge <see cref="Modifiers.SizedTrays"/> containers: each picks a lane and a colour it has at least
        /// `size` trays of, keeps the first of them as one tray of that size (R21: size × trayCapacity bottles) and drops the
        /// others. The construction solution no longer fits the lanes; the caller proves the result again.</summary>
        private static List<IReadOnlyList<CapColor>> MergeTrays(IReadOnlyList<IReadOnlyList<CapColor>> source, Modifiers m, IRandom rng,
            out Dictionary<TrayRef, TraySize> sizes)
        {
            var lanes = source.Select(l => new List<CapColor>(l)).ToList();
            var size = lanes.Select(l => l.Select(_ => 1).ToList()).ToList();   // per tray, kept in step with lanes
            for (int n = 0; n < m.SizedTrays; n++)
            {
                int want = rng.NextInt(m.SizeMin, m.SizeMax + 1);
                var options = new List<(int lane, CapColor color)>();
                for (int j = 0; j < lanes.Count; j++)
                    foreach (var c in lanes[j].Distinct())
                        if (Enumerable.Range(0, lanes[j].Count).Count(t => lanes[j][t] == c && size[j][t] == 1) >= want && lanes[j].Count > want)
                            options.Add((j, c));
                if (options.Count == 0) continue;
                var (lane, color) = options[rng.NextInt(0, options.Count)];
                var at = Enumerable.Range(0, lanes[lane].Count).Where(t => lanes[lane][t] == color && size[lane][t] == 1).ToList();
                int keep = at[0];
                for (int k = want - 1; k >= 1; k--) { lanes[lane].RemoveAt(at[k]); size[lane].RemoveAt(at[k]); }
                size[lane][keep] = want;
            }
            sizes = new Dictionary<TrayRef, TraySize>();
            for (int j = 0; j < lanes.Count; j++)
                for (int t = 0; t < lanes[j].Count; t++)
                    if (size[j][t] > 1) sizes[new TrayRef(j, t)] = (TraySize)size[j][t];
            return lanes.Select(l => (IReadOnlyList<CapColor>)l).ToList();
        }

        private sealed class Candidate
        {
            public LevelDefinition Level;
            public string Proof;
            public double Win, Miss;
            /// <summary>Which reseed it came from: generator seed = spec seed + Reseed × 100003.</summary>
            public int Reseed;
            public bool InBand => Miss == 0;
        }

        private const int DecorateTries = 40, MaxReseeds = 12, TargetPlays = 200;
        private const long Reseed = 100_003;

        /// <summary>
        /// Put the spec's modifiers on a generated level. Places are drawn from <paramref name="rng"/>: hidden / locked queue
        /// rows never on a feeder's first row, hidden trays never at the front (they would show at once), a tray or row is
        /// never both hidden and locked. A placement that changes the rules must still win: the construction solution is
        /// replayed first, else the solver searches; a placement it can not prove, or whose random-play win rate is outside
        /// the spec's targetWin band, is drawn again. Returns the first placement in the band, else the provable one closest
        /// to it (the caller may reseed), else null.
        /// </summary>
        private static Candidate Decorate(LevelDefinition g, Modifiers m, IRandom rng)
        {
            Candidate best = null;
            int width = g.Loop.Width;
            var feeders = g.Loop.Feeders;
            for (int attempt = 1; attempt <= DecorateTries; attempt++)
            {
                var hiddenRows = feeders.Select(_ => new HashSet<int>()).ToList();
                var lockedRows = feeders.Select(_ => new Dictionary<int, int>()).ToList();
                var rowPool = new List<(int f, int row)>();
                for (int f = 0; f < feeders.Count; f++)
                    for (int r = 1; r < feeders[f].RowCount(width); r++) rowPool.Add((f, r));
                rng.Shuffle(rowPool);
                int next = 0;
                for (int i = 0; i < m.HiddenRows && next < rowPool.Count; i++, next++) hiddenRows[rowPool[next].f].Add(rowPool[next].row);
                // a lock waits at the merge point: keep it off the last few rows so it holds something back
                for (int i = 0; i < m.LockedRows && next < rowPool.Count; next++)
                {
                    var (f, r) = rowPool[next];
                    if (r < 2 || r > feeders[f].RowCount(width) - 3) continue;
                    lockedRows[f][r] = rng.NextInt(m.LockMin, m.LockMax + 1);
                    i++;
                }

                // R21 first: bigger containers are trays of one colour in one lane merged into the first of them, so the
                // bottle count per colour (V4) holds; the lanes shrink, so hidden / locked trays are placed afterwards
                var lanes = MergeTrays(g.Lanes, m, rng, out var sizes);
                var trayPool = new List<TrayRef>();
                for (int j = 0; j < lanes.Count; j++)
                    for (int t = 0; t < lanes[j].Count; t++) trayPool.Add(new TrayRef(j, t));
                rng.Shuffle(trayPool);
                var hiddenTrays = new List<TrayRef>();
                var locks = new List<TrayLock>();
                foreach (var t in trayPool)
                {
                    if (hiddenTrays.Count < m.HiddenTrays && t.Index > 0) hiddenTrays.Add(t);
                    else if (locks.Count < m.LockedTrays) locks.Add(new TrayLock(t, rng.NextInt(m.LockMin, m.LockMax + 1)));
                }

                var loop = new LoopDefinition(g.Conveyor, feeders.Select(f => f.Bottles).ToList(), g.Loop.Initial,
                    hiddenRows.Select(h => (IEnumerable<int>)h).ToList(), lockedRows.Select(d => (IReadOnlyDictionary<int, int>)d).ToList());
                LevelDefinition Build(IReadOnlyList<int> solution) => new LevelDefinition(g.Id, g.Slots, g.TrayCapacity, g.Colors, loop, lanes,
                    g.CameraPreset, g.Name, g.Difficulty, g.Notes, solution: solution, hiddenTrays: hiddenTrays, locks: locks,
                    extraSlots: g.ExtraSlots, traySizes: sizes, slotLocks: m.SlotLocks);

                var level = Build(g.Solution);
                if (LevelValidator.Validate(level).Count > 0) continue;
                var c = m.Rate(level, "replay");
                if (best != null && c.Miss >= best.Miss) continue;               // not closer: skip the (costly) proof
                if (m.ChangesRules && LevelSolver.Prove(level).Status != SolveStatus.Solvable)
                {
                    var search = LevelSolver.Solve(level);
                    if (search.Status != SolveStatus.Solvable) continue;
                    c.Level = Build(search.Solution);
                    c.Proof = "search";
                }
                best = c;
                if (best.InBand || !m.HasTarget) return best;
            }
            return best;
        }

        private const long RandomPlaySeed = 20260930;

        /// <summary>How many of <paramref name="plays"/> random players win with the open slots only (uniform over non-empty
        /// lanes, tapping when the board is quiet, fixed seed): a difficulty read-out, not a gate.</summary>
        private static int RandomWins(LevelDefinition level, int plays)
        {
            var rng = new Pcg32(RandomPlaySeed);
            int wins = 0;
            for (int p = 0; p < plays; p++)
            {
                var g = new CapChaosGame(level);
                g.Settle();
                var open = new List<int>();
                while (g.Status == GameStatus.Playing && !g.SlotsRanOut)   // needing a paid slot counts as not winning
                {
                    open.Clear();
                    for (int j = 0; j < g.LaneCount; j++) if (g.LaneRemaining(j) > 0) open.Add(j);
                    if (open.Count == 0) break;
                    g.Tap(open[rng.NextInt(0, open.Count)]);
                    g.Settle();
                }
                if (g.Status == GameStatus.Won) wins++;
            }
            return wins;
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
