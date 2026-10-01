using System;
using System.Collections.Generic;
using ClassicSpins.PrototypeFramework.Domain;

namespace Game.Domain
{
    /// <summary>What a level should look like; the generator fills in colours and the conveyor queues.</summary>
    public sealed class LevelSpec
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string Difficulty { get; set; }
        public string Notes { get; set; }
        public int Slots { get; set; } = LevelDefinition.DefaultSlots;
        public int TrayCapacity { get; set; } = LevelDefinition.DefaultTrayCapacity;
        public int Lanes { get; set; } = 3;
        public IReadOnlyList<CapColor> Colors { get; set; } = CapColorCodes.ParseList("ROBG");
        /// <summary>Stack SHAPE, layers[k][row] like the level format but with '#' = visible bottle,
        /// '?' = hidden bottle, '.' = empty. Row 0 is the back row.</summary>
        public List<List<string>> Shape { get; set; } = new List<List<string>>();
        /// <summary>0..1 — chance each tray is the colour with the most exposed matches (easy) rather than any
        /// colour that keeps the level alive (tense).</summary>
        public double Greed { get; set; } = 0.6;
        /// <summary>0..1 — chance a bottle copies its front neighbour's colour (columns of one colour = easy).</summary>
        public double Clustering { get; set; } = 0.3;
    }

    public sealed class GeneratedLevel
    {
        public LevelDefinition Level { get; }
        /// <summary>The construction order = one winning tap sequence (lane indices).</summary>
        public IReadOnlyList<int> Solution { get; }
        public int Attempts { get; }
        public GeneratedLevel(LevelDefinition level, IReadOnlyList<int> solution, int attempts)
        { Level = level; Solution = solution; Attempts = attempts; }
    }

    /// <summary>
    /// Builds levels that are solvable BY CONSTRUCTION: it plays the real rules forward, deciding each
    /// next tray's colour (one that does not jam the slots) and the lane it goes on. Every tray is
    /// "tapped" the moment it is queued, so it is always its lane's front tray — the queueing order is
    /// a valid solution. Randomness only through the injected <see cref="IRandom"/> (rule #14); the
    /// composition point (Tools/LevelTool) owns and logs the seed.
    /// </summary>
    public sealed class LevelGenerator
    {
        private readonly IRandom _rng;
        public LevelGenerator(IRandom rng) { _rng = rng ?? throw new ArgumentNullException(nameof(rng)); }

        public GeneratedLevel Generate(LevelSpec spec, int maxAttempts = 400)
        {
            Check(spec);
            for (int attempt = 1; attempt <= maxAttempts; attempt++)
            {
                var stackDef = new StackDefinition(Paint(spec));
                var lanes = new List<List<CapColor>>();
                for (int j = 0; j < spec.Lanes; j++) lanes.Add(new List<CapColor>());
                var solution = new List<int>();
                if (!Construct(spec, stackDef, lanes, solution)) continue;

                var laneViews = new List<IReadOnlyList<CapColor>>();
                foreach (var l in lanes) laneViews.Add(l);
                var colors = new List<CapColor>(spec.Colors);
                var level = new LevelDefinition(spec.Id, spec.Slots, spec.TrayCapacity, colors, stackDef, laneViews,
                    name: spec.Name, difficulty: spec.Difficulty, notes: spec.Notes, solution: solution);
                return new GeneratedLevel(level, solution, attempt);
            }
            throw new InvalidOperationException($"{spec.Id}: no valid level in {maxAttempts} attempts — relax the spec");
        }

        private static void Check(LevelSpec spec)
        {
            if (spec.Shape.Count == 0) throw new ArgumentException($"{spec.Id}: empty shape");
            int cells = 0;
            foreach (var layer in spec.Shape) foreach (var row in layer) foreach (char c in row) if (c == '#' || c == '?') cells++;
            if (cells % spec.TrayCapacity != 0)
                throw new ArgumentException($"{spec.Id}: {cells} bottles is not a multiple of trayCapacity {spec.TrayCapacity}");
            if (spec.Colors.Count == 0) throw new ArgumentException($"{spec.Id}: no colours");
            if (cells / spec.TrayCapacity < spec.Colors.Count)
                throw new ArgumentException($"{spec.Id}: too few bottles for {spec.Colors.Count} colours");
        }

        /// <summary>Assign colours: per-colour totals are multiples of the capacity, spread as evenly as possible.</summary>
        private StackCell[,,] Paint(LevelSpec spec)
        {
            int cols = spec.Shape[0][0].Length, rows = spec.Shape[0].Count;
            var slots = new List<(int k, int r, int x)>();
            for (int k = 0; k < spec.Shape.Count; k++)
                for (int r = 0; r < rows; r++)
                    for (int x = 0; x < cols; x++)
                        if (spec.Shape[k][r][x] != '.') slots.Add((k, r, x));

            int traysTotal = slots.Count / spec.TrayCapacity;
            var bag = new List<CapColor>();
            for (int t = 0; t < traysTotal; t++)
                for (int i = 0; i < spec.TrayCapacity; i++) bag.Add(spec.Colors[t % spec.Colors.Count]);
            _rng.Shuffle(bag);

            var grid = new StackCell[spec.Shape.Count, rows, cols];
            // paint front→back, ground→up, so "copy the front neighbour" (clustering) sees painted cells
            slots.Sort((a, b) => a.k != b.k ? a.k.CompareTo(b.k) : a.r != b.r ? b.r.CompareTo(a.r) : a.x.CompareTo(b.x));
            foreach (var (k, r, x) in slots)
            {
                var pick = bag[bag.Count - 1];
                if (r + 1 < rows && !grid[k, r + 1, x].IsEmpty && _rng.NextFloat() < spec.Clustering)
                {
                    int i = bag.LastIndexOf(grid[k, r + 1, x].Color);
                    if (i >= 0) pick = bag[i];
                }
                bag.RemoveAt(bag.LastIndexOf(pick));
                grid[k, r, x] = new StackCell(pick, spec.Shape[k][r][x] == '?');
            }
            return grid;
        }

        private bool Construct(LevelSpec spec, StackDefinition stackDef, List<List<CapColor>> lanes, List<int> solution)
        {
            var stack = BottleStack.FromDefinition(stackDef);
            var quota = new Dictionary<CapColor, int>();
            for (int x = 0; x < stack.Cols; x++)
                for (int z = 0; z < stack.Depth; z++)
                    for (int h = 0; h < stack.Height(x, z); h++)
                    {
                        var c = stack.At(x, z, h).Color;
                        quota[c] = (quota.TryGetValue(c, out var n) ? n : 0) + 1;
                    }
            var keys = new List<CapColor>(quota.Keys);
            foreach (var c in keys) quota[c] /= spec.TrayCapacity;

            var game = new CapChaosGame(stack, Array.Empty<IReadOnlyList<CapColor>>(), spec.Slots, spec.TrayCapacity) { EndlessSupply = true };
            while (game.Status == GameStatus.Playing)
            {
                var order = new List<CapColor>();
                foreach (var kv in quota) if (kv.Value > 0) order.Add(kv.Key);
                if (order.Count == 0) return false;
                // dictionary order is not a contract; the rng must see a stable list. Sorted by CODE, not by
                // enum value: that is the order the seeds were tuned against, so every shipped level stays
                // byte-identical under `LevelTool generate --check`.
                order.Sort(ByCode);
                _rng.Shuffle(order);
                if (_rng.NextFloat() < spec.Greed)
                {
                    var exposed = CountExposed(game.Stack);
                    order.Sort((a, b) => Get(exposed, b).CompareTo(Get(exposed, a)));
                }

                bool placed = false;
                foreach (var c in order)
                {
                    var next = game.Clone();
                    next.PlaceTray(c);
                    if (next.Status == GameStatus.Lost) continue;
                    quota[c]--;
                    int lane = PickLane(lanes);
                    lanes[lane].Add(c);
                    solution.Add(lane);
                    game = next;
                    placed = true;
                    break;
                }
                if (!placed) return false;
            }
            return game.Status == GameStatus.Won;
        }

        /// <summary>Every lane gets a tray before any gets a second (the schema forbids an empty lane);
        /// after that shorter lanes are likelier, so the queues stay roughly even.</summary>
        private int PickLane(List<List<CapColor>> lanes)
        {
            int min = int.MaxValue;
            foreach (var l in lanes) min = Math.Min(min, l.Count);
            int slack = min == 0 ? 0 : 1;
            var candidates = new List<int>();
            for (int j = 0; j < lanes.Count; j++) if (lanes[j].Count <= min + slack) candidates.Add(j);
            return candidates[_rng.NextInt(0, candidates.Count)];
        }

        private static Dictionary<CapColor, int> CountExposed(BottleStack s)
        {
            var d = new Dictionary<CapColor, int>();
            for (int x = 0; x < s.Cols; x++)
            {
                int z = s.FrontZ(x);
                if (z < 0 || !s.IsExposed(x, z)) continue;
                var c = s.At(x, z, 0).Color;
                d[c] = Get(d, c) + 1;
            }
            return d;
        }

        private static int Get(Dictionary<CapColor, int> d, CapColor c) => d.TryGetValue(c, out var n) ? n : 0;

        private static int ByCode(CapColor a, CapColor b) => CapColorCodes.ToCode(a).CompareTo(CapColorCodes.ToCode(b));
    }
}
