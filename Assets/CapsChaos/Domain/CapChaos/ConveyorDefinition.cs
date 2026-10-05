using System;
using System.Collections.Generic;

namespace Game.Domain
{
    /// <summary>
    /// The TOP conveyor's layout (GDD R1–R4, §6.2b): how many rows run round the loop, how many bottles stand in a row,
    /// how many rows make the pick zone, how the loop is drawn and where each feeder joins it — and NO bottles. One
    /// file per layout in <c>Content/LevelConfig/Conveyors/&lt;id&gt;.json</c>, SHARED: many levels name the same
    /// conveyor; a level supplies the bottles each of its feeders carries (<see cref="LoopDefinition"/> joins the two).
    /// Immutable.
    /// </summary>
    public sealed class ConveyorDefinition
    {
        public const int CurrentFormatVersion = 1;

        /// <summary>The file's name without <c>.json</c> (<see cref="IsConveyorId"/>); null for a layout built in code
        /// (tests, the old <see cref="LoopDefinition"/> constructor) — such a level can be played but not written.</summary>
        public string Id { get; }
        /// <summary>Rows of belt round the loop.</summary>
        public int Rows { get; }
        /// <summary>Bottle spots per row.</summary>
        public int Width { get; }
        /// <summary>The pick zone: track positions <c>0 .. PickRows−1</c>, the front edge in front of the slots.</summary>
        public int PickRows { get; }
        /// <summary>Where each feeder joins the loop (a track position), in feeder order.</summary>
        public IReadOnlyList<int> MergeAt { get; }
        /// <summary>How the loop is drawn (presentation only; <see cref="LoopShape.Default"/> = the oval).</summary>
        public LoopShape Shape { get; }
        public string Name { get; }
        public string Notes { get; }

        public int FeederCount => MergeAt.Count;

        public ConveyorDefinition(string id, int rows, int width, int pickRows, IReadOnlyList<int> mergeAt,
            LoopShape shape = null, string name = null, string notes = null)
        {
            Id = id;
            Rows = rows; Width = width; PickRows = pickRows;
            MergeAt = mergeAt ?? throw new ArgumentNullException(nameof(mergeAt));
            Shape = shape ?? LoopShape.Default;
            Name = name; Notes = notes;
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
