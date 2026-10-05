using System;
using System.Collections.Generic;

namespace Game.Domain
{
    /// <summary>
    /// One knot of a conveyor's spline (the ConveyorKit node): a point on the board plane (x right, z away from the
    /// player, board units) and the heading the belt runs at there (<see cref="YRotation"/>, degrees round Y: 0 = +z,
    /// 90 = +x). A smooth knot's handles run along that heading, sized from the distance to its neighbours; a
    /// <see cref="Linear"/> knot is a sharp corner. Presentation data only — the rules never read it.
    /// </summary>
    public readonly struct ConveyorNode : IEquatable<ConveyorNode>
    {
        public readonly double X, Z, YRotation;
        public readonly bool Linear;

        public ConveyorNode(double x, double z, double yRotation, bool linear = false)
        {
            X = x; Z = z; YRotation = yRotation; Linear = linear;
        }

        public bool Equals(ConveyorNode o) => X == o.X && Z == o.Z && YRotation == o.YRotation && Linear == o.Linear;
        public override bool Equals(object obj) => obj is ConveyorNode o && Equals(o);
        public override int GetHashCode() => (X, Z, YRotation, Linear).GetHashCode();
    }

    /// <summary>Which feeder of a conveyor it is. Every conveyor file has all three, in this order; a level uses the
    /// first N (1 = right, 2 = right + left, 3 = all).</summary>
    public enum FeederSide : byte
    {
        Right = 0,
        Left = 1,
        Middle = 2,
    }

    /// <summary>One feeder conveyor of a layout: where its queue joins the loop (<see cref="MergeAt"/>, a track
    /// position) and the open spline it runs along, its last node where it lands on the loop.</summary>
    public sealed class FeederLayout
    {
        public FeederSide Side { get; }
        public int MergeAt { get; }
        /// <summary>The feeder's spline, far end first; empty for a feeder built in code.</summary>
        public IReadOnlyList<ConveyorNode> Nodes { get; }

        public FeederLayout(FeederSide side, int mergeAt, IReadOnlyList<ConveyorNode> nodes = null)
        {
            Side = side; MergeAt = mergeAt;
            Nodes = nodes ?? Array.Empty<ConveyorNode>();
        }
    }

    /// <summary>
    /// The TOP conveyor's layout (GDD R1–R4, §6.2b): one LOOP and three FEEDER conveyors (right, left, middle — in that
    /// order), each a spline of <see cref="ConveyorNode"/>s, plus the rule numbers: how many rows run round the loop,
    /// how many bottles stand in a row, how many rows make the pick zone, where each feeder's queue joins. NO bottles.
    /// One file per layout in <c>Content/LevelConfig/Conveyors/&lt;id&gt;.json</c>, SHARED: many levels name the same
    /// conveyor; a level supplies the bottles of the feeders it uses (the first N) — <see cref="LoopDefinition"/> joins
    /// the two. Immutable.
    /// <para>The rules never read the splines: a row is a row wherever it is drawn. The view spreads the rows evenly
    /// along the loop spline from its first node (track position 0 = the start of the pick zone).</para>
    /// </summary>
    public sealed class ConveyorDefinition
    {
        public const int CurrentFormatVersion = 2;
        /// <summary>Every conveyor file has exactly this many feeders: right, left, middle.</summary>
        public const int FeederSlots = 3;

        /// <summary>The file's name without <c>.json</c> (<see cref="IsConveyorId"/>); null for a layout built in code
        /// (tests, the old <see cref="LoopDefinition"/> constructor) — such a level can be played but not written.</summary>
        public string Id { get; }
        /// <summary>Rows of belt round the loop.</summary>
        public int Rows { get; }
        /// <summary>Bottle spots per row.</summary>
        public int Width { get; }
        /// <summary>The pick zone: track positions <c>0 .. PickRows−1</c>, from the loop's first node on.</summary>
        public int PickRows { get; }
        /// <summary>How big the belts and the bottles on them are drawn (1 = full size). The knots stay where they are
        /// (board units); a long loop that must fit the screen takes a smaller scale so its rows still fit. Drawing only.</summary>
        public double Scale { get; }
        /// <summary>The loop's closed spline, starting where the pick zone starts; empty for a layout built in code.</summary>
        public IReadOnlyList<ConveyorNode> Loop { get; }
        /// <summary>The feeders, in order (right, left, middle for a file).</summary>
        public IReadOnlyList<FeederLayout> Feeders { get; }
        public string Name { get; }
        public string Notes { get; }

        /// <summary>Where each feeder joins the loop (a track position), in feeder order.</summary>
        public IReadOnlyList<int> MergeAt { get; }
        public int FeederCount => Feeders.Count;

        public ConveyorDefinition(string id, int rows, int width, int pickRows, IReadOnlyList<ConveyorNode> loop,
            IReadOnlyList<FeederLayout> feeders, string name = null, string notes = null, double scale = 1.0)
        {
            Id = id;
            Rows = rows; Width = width; PickRows = pickRows; Scale = scale;
            Loop = loop ?? Array.Empty<ConveyorNode>();
            Feeders = feeders ?? throw new ArgumentNullException(nameof(feeders));
            var merge = new int[feeders.Count];
            for (int f = 0; f < merge.Length; f++) merge[f] = feeders[f].MergeAt;
            MergeAt = merge;
            Name = name; Notes = notes;
        }

        /// <summary>A layout built in code (tests): the rule numbers only, no splines; feeder f is side f.</summary>
        public ConveyorDefinition(string id, int rows, int width, int pickRows, IReadOnlyList<int> mergeAt)
            : this(id, rows, width, pickRows, null, CodeFeeders(mergeAt)) { }

        private static IReadOnlyList<FeederLayout> CodeFeeders(IReadOnlyList<int> mergeAt)
        {
            if (mergeAt == null) throw new ArgumentNullException(nameof(mergeAt));
            var list = new List<FeederLayout>(mergeAt.Count);
            for (int f = 0; f < mergeAt.Count; f++) list.Add(new FeederLayout((FeederSide)Math.Min(f, (int)FeederSide.Middle), mergeAt[f]));
            return list;
        }

        /// <summary>A conveyor id: lowercase letters, digits and <c>_</c>, starting with a letter, at most 48 long
        /// (it is a file name and an Addressables key).</summary>
        public static bool IsConveyorId(string id)
        {
            if (string.IsNullOrEmpty(id) || id.Length > 48 || id[0] < 'a' || id[0] > 'z') return false;
            foreach (char c in id)
                if (!(c >= 'a' && c <= 'z' || c >= '0' && c <= '9' || c == '_')) return false;
            return true;
        }
    }

    /// <summary>Every conveyor a set of levels may name, by id. Filled once (boot, LevelTool, tests), then read.</summary>
    public sealed class ConveyorLibrary
    {
        private readonly Dictionary<string, ConveyorDefinition> _byId = new Dictionary<string, ConveyorDefinition>(StringComparer.Ordinal);

        public ConveyorLibrary() { }

        public ConveyorLibrary(IEnumerable<ConveyorDefinition> conveyors)
        {
            foreach (var c in conveyors) Add(c);
        }

        public int Count => _byId.Count;
        public IEnumerable<ConveyorDefinition> All => _byId.Values;

        public void Add(ConveyorDefinition conveyor)
        {
            if (conveyor == null) throw new ArgumentNullException(nameof(conveyor));
            if (conveyor.Id == null) throw new ArgumentException("a library conveyor needs an id", nameof(conveyor));
            if (_byId.ContainsKey(conveyor.Id)) throw new ArgumentException($"conveyor '{conveyor.Id}' is already in the library", nameof(conveyor));
            _byId.Add(conveyor.Id, conveyor);
        }

        public bool TryGet(string id, out ConveyorDefinition conveyor)
        {
            conveyor = null;
            return id != null && _byId.TryGetValue(id, out conveyor);
        }
    }
}
