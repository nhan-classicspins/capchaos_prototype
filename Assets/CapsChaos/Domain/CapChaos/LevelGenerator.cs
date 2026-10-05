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
        public int ExtraSlots { get; set; } = LevelDefinition.DefaultExtraSlots;
        public int TrayCapacity { get; set; } = LevelDefinition.DefaultTrayCapacity;
        public int Lanes { get; set; } = 3;
        public IReadOnlyList<CapColor> Colors { get; set; } = CapColorCodes.ParseList("ROBG");
        /// <summary>The top conveyor the level runs on (a shared <c>Conveyors/</c> file): rows, width, pick zone, shape
        /// and merge points. The generator fills its feeders; it never changes the layout.</summary>
        public ConveyorDefinition Conveyor { get; set; }
        /// <summary>How many bottles each feeder carries — the level uses the conveyor's first N feeders (right, left,
        /// middle), N = this list's length. The belt starts filled from the feeders (no authored initial rows).</summary>
        public List<int> FeederBottles { get; set; } = new List<int>();
        internal int Width => Conveyor.Width;
        /// <summary>0..1 — chance each tray is the colour with the most bottles on the belt (easy) rather than any
        /// colour that keeps the level alive (tense).</summary>
        public double Greed { get; set; } = 0.6;
        /// <summary>0..1 — chance a bottle copies the colour of the bottle queued just before it (blocks of one colour
        /// = easy).</summary>
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
                var loop = Paint(spec);
                var lanes = new List<List<CapColor>>();
                for (int j = 0; j < spec.Lanes; j++) lanes.Add(new List<CapColor>());
                var solution = new List<int>();
                if (!Construct(spec, loop, lanes, solution)) continue;

                var laneViews = new List<IReadOnlyList<CapColor>>();
                foreach (var l in lanes) laneViews.Add(l);
                var colors = new List<CapColor>(spec.Colors);
                var level = new LevelDefinition(spec.Id, spec.Slots, spec.TrayCapacity, colors, loop, laneViews,
                    name: spec.Name, difficulty: spec.Difficulty, notes: spec.Notes, solution: solution, extraSlots: spec.ExtraSlots);
                return new GeneratedLevel(level, solution, attempt);
            }
            throw new InvalidOperationException($"{spec.Id}: no valid level in {maxAttempts} attempts — relax the spec");
        }

        private static void Check(LevelSpec spec)
        {
            if (spec.Conveyor == null) throw new ArgumentException($"{spec.Id}: no conveyor");
            if (spec.FeederBottles.Count == 0) throw new ArgumentException($"{spec.Id}: no feeders");
            if (spec.FeederBottles.Count > spec.Conveyor.FeederCount)
                throw new ArgumentException($"{spec.Id}: {spec.FeederBottles.Count} feeders, but conveyor '{spec.Conveyor.Id}' has {spec.Conveyor.FeederCount}");
            int bottles = 0;
            foreach (int count in spec.FeederBottles)
            {
                if (count % spec.Width != 0)
                    throw new ArgumentException($"{spec.Id}: a feeder of {count} bottles is not whole rows of {spec.Width}");
                bottles += count;
            }
            int unit = ColourUnit(spec);
            if (bottles % unit != 0)
                throw new ArgumentException($"{spec.Id}: {bottles} bottles is not a multiple of {unit} (whole rows of whole trays)");
            if (spec.Colors.Count == 0) throw new ArgumentException($"{spec.Id}: no colours");
            if (bottles / unit < spec.Colors.Count)
                throw new ArgumentException($"{spec.Id}: too few bottles for {spec.Colors.Count} colours");
        }

        /// <summary>The smallest bottle count a colour comes in: whole queue rows (R4 feeds row by row, one colour a
        /// row) that also fill whole trays.</summary>
        private static int ColourUnit(LevelSpec spec)
        {
            int a = spec.Width, b = spec.TrayCapacity;
            while (b != 0) { int t = a % b; a = b; b = t; }
            return spec.Width / a * spec.TrayCapacity;
        }

        /// <summary>Assign colours ROW BY ROW — every queue row is one colour (R4). Per-colour totals are whole colour
        /// units, spread as evenly as possible; the rows queue feeder after feeder, each copying the colour of the row
        /// before it with probability Clustering.</summary>
        private LoopDefinition Paint(LevelSpec spec)
        {
            int total = 0;
            foreach (int count in spec.FeederBottles) total += count;
            int rowsPerUnit = ColourUnit(spec) / spec.Width;
            int units = total / ColourUnit(spec);
            var bag = new List<CapColor>();
            for (int u = 0; u < units; u++)
                for (int i = 0; i < rowsPerUnit; i++) bag.Add(spec.Colors[u % spec.Colors.Count]);
            _rng.Shuffle(bag);

            var feeders = new List<IReadOnlyList<CapColor>>();
            CapColor previous = CapColor.None;
            foreach (int count in spec.FeederBottles)
            {
                var bottles = new List<CapColor>(count);
                for (int row = 0; row < count / spec.Width; row++)
                {
                    var pick = bag[bag.Count - 1];
                    if (previous != CapColor.None && _rng.NextFloat() < spec.Clustering)
                    {
                        int at = bag.LastIndexOf(previous);
                        if (at >= 0) pick = bag[at];
                    }
                    bag.RemoveAt(bag.LastIndexOf(pick));
                    for (int k = 0; k < spec.Width; k++) bottles.Add(pick);
                    previous = pick;
                }
                feeders.Add(bottles);
            }
            return new LoopDefinition(spec.Conveyor, feeders);
        }

        private bool Construct(LevelSpec spec, LoopDefinition loop, List<List<CapColor>> lanes, List<int> solution)
        {
            var quota = new Dictionary<CapColor, int>();
            foreach (var c in loop.AllBottles()) quota[c] = (quota.TryGetValue(c, out var n) ? n : 0) + 1;
            var keys = new List<CapColor>(quota.Keys);
            foreach (var c in keys) quota[c] /= spec.TrayCapacity;

            var game = new CapChaosGame(LoopBelt.FromDefinition(loop), Array.Empty<IReadOnlyList<CapColor>>(), spec.Slots, spec.TrayCapacity) { EndlessSupply = true };
            game.Settle();
            while (game.Status == GameStatus.Playing)
            {
                var order = new List<CapColor>();
                foreach (var kv in quota) if (kv.Value > 0) order.Add(kv.Key);
                if (order.Count == 0) return false;
                // dictionary order is not a contract; the rng must see a stable list
                order.Sort(ByCode);
                _rng.Shuffle(order);
                if (_rng.NextFloat() < spec.Greed)
                {
                    var onBelt = CountOnBelt(game.Belt);
                    order.Sort((a, b) => Get(onBelt, b).CompareTo(Get(onBelt, a)));
                }

                bool placed = false;
                foreach (var c in order)
                {
                    var next = game.Clone();
                    next.PlaceTray(c);
                    next.Settle();
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

        private static Dictionary<CapColor, int> CountOnBelt(LoopBelt belt)
        {
            var d = new Dictionary<CapColor, int>();
            for (int r = 0; r < belt.Rows; r++)
                for (int k = 0; k < belt.Width; k++)
                {
                    var c = belt.At(r, k);
                    if (c != CapColor.None) d[c] = Get(d, c) + 1;
                }
            return d;
        }

        private static int Get(Dictionary<CapColor, int> d, CapColor c) => d.TryGetValue(c, out var n) ? n : 0;

        private static int ByCode(CapColor a, CapColor b) => CapColorCodes.ToCode(a).CompareTo(CapColorCodes.ToCode(b));
    }
}
