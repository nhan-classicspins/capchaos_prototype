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
        public string Colors { get; set; } = "ROBG";
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
                var layers = Paint(spec);
                var stackDef = new StackDefinition(spec.Shape[0][0].Length, spec.Shape[0].Count, layers);
                var lanes = new List<List<char>>();
                for (int j = 0; j < spec.Lanes; j++) lanes.Add(new List<char>());
                var solution = new List<int>();
                if (!Construct(spec, stackDef, lanes, solution)) continue;

                var laneViews = new List<IReadOnlyList<char>>();
                foreach (var l in lanes) laneViews.Add(l);
                var colors = new List<char>();
                foreach (char c in spec.Colors) colors.Add(c);
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
            if (cells / spec.TrayCapacity < spec.Colors.Length)
                throw new ArgumentException($"{spec.Id}: too few bottles for {spec.Colors.Length} colours");
        }

        /// <summary>Assign colours: per-colour totals are multiples of the capacity, spread as evenly as possible.</summary>
        private List<IReadOnlyList<string>> Paint(LevelSpec spec)
        {
            int cols = spec.Shape[0][0].Length, rows = spec.Shape[0].Count;
            var slots = new List<(int k, int r, int x)>();
            for (int k = 0; k < spec.Shape.Count; k++)
                for (int r = 0; r < rows; r++)
                    for (int x = 0; x < cols; x++)
                        if (spec.Shape[k][r][x] != '.') slots.Add((k, r, x));

            int traysTotal = slots.Count / spec.TrayCapacity;
            var bag = new List<char>();
            for (int t = 0; t < traysTotal; t++)
                for (int i = 0; i < spec.TrayCapacity; i++) bag.Add(spec.Colors[t % spec.Colors.Length]);
            _rng.Shuffle(bag);

            var grid = new char[spec.Shape.Count, rows, cols];
            // paint front→back, ground→up, so "copy the front neighbour" (clustering) sees painted cells
            slots.Sort((a, b) => a.k != b.k ? a.k.CompareTo(b.k) : a.r != b.r ? b.r.CompareTo(a.r) : a.x.CompareTo(b.x));
            foreach (var (k, r, x) in slots)
            {
                char pick = bag[bag.Count - 1];
                if (r + 1 < rows && grid[k, r + 1, x] != '\0' && _rng.NextFloat() < spec.Clustering)
                {
                    int i = bag.LastIndexOf(char.ToUpperInvariant(grid[k, r + 1, x]));
                    if (i >= 0) pick = bag[i];
                }
                bag.RemoveAt(bag.LastIndexOf(pick));
                grid[k, r, x] = spec.Shape[k][r][x] == '?' ? char.ToLowerInvariant(pick) : pick;
            }

            var layers = new List<IReadOnlyList<string>>();
            for (int k = 0; k < spec.Shape.Count; k++)
            {
                var rowsOut = new List<string>();
                for (int r = 0; r < rows; r++)
                {
                    var line = new char[cols];
                    for (int x = 0; x < cols; x++) line[x] = grid[k, r, x] == '\0' ? CapColors.Empty : grid[k, r, x];
                    rowsOut.Add(new string(line));
                }
                layers.Add(rowsOut);
            }
            return layers;
        }

        private bool Construct(LevelSpec spec, StackDefinition stackDef, List<List<char>> lanes, List<int> solution)
        {
            var stack = BottleStack.FromDefinition(stackDef);
            var quota = new Dictionary<char, int>();
            for (int x = 0; x < stack.Cols; x++)
                for (int z = 0; z < stack.Depth; z++)
                    for (int h = 0; h < stack.Height(x, z); h++)
                    {
                        char c = stack.At(x, z, h).Color;
                        quota[c] = (quota.TryGetValue(c, out var n) ? n : 0) + 1;
                    }
            var keys = new List<char>(quota.Keys);
            foreach (char c in keys) quota[c] /= spec.TrayCapacity;

            var game = new CapChaosGame(stack, Array.Empty<IReadOnlyList<char>>(), spec.Slots, spec.TrayCapacity) { EndlessSupply = true };
            while (game.Status == GameStatus.Playing)
            {
                var order = new List<char>();
                foreach (var kv in quota) if (kv.Value > 0) order.Add(kv.Key);
                if (order.Count == 0) return false;
                order.Sort();                         // dictionary order is not a contract; the rng must see a stable list
                _rng.Shuffle(order);
                if (_rng.NextFloat() < spec.Greed)
                {
                    var exposed = CountExposed(game.Stack);
                    order.Sort((a, b) => Get(exposed, b).CompareTo(Get(exposed, a)));
                }

                bool placed = false;
                foreach (char c in order)
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
        private int PickLane(List<List<char>> lanes)
        {
            int min = int.MaxValue;
            foreach (var l in lanes) min = Math.Min(min, l.Count);
            int slack = min == 0 ? 0 : 1;
            var candidates = new List<int>();
            for (int j = 0; j < lanes.Count; j++) if (lanes[j].Count <= min + slack) candidates.Add(j);
            return candidates[_rng.NextInt(0, candidates.Count)];
        }

        private static Dictionary<char, int> CountExposed(BottleStack s)
        {
            var d = new Dictionary<char, int>();
            for (int x = 0; x < s.Cols; x++)
            {
                int z = s.FrontZ(x);
                if (z < 0 || !s.IsExposed(x, z)) continue;
                char c = s.At(x, z, 0).Color;
                d[c] = Get(d, c) + 1;
            }
            return d;
        }

        private static int Get(Dictionary<char, int> d, char c) => d.TryGetValue(c, out var n) ? n : 0;
    }
}
