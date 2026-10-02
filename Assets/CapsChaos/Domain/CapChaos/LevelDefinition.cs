using System;
using System.Collections.Generic;

namespace Game.Domain
{
    /// <summary>A feeder as authored (GDD R4): it joins the oval at track position <see cref="MergeAt"/>; its
    /// <see cref="Bottles"/> queue row by row — bottle <c>i</c> on track <c>i % width</c>, bottle 0 joins first.</summary>
    public sealed class FeederDefinition
    {
        public int MergeAt { get; }
        public IReadOnlyList<CapColor> Bottles { get; }
        public FeederDefinition(int mergeAt, IReadOnlyList<CapColor> bottles)
        {
            MergeAt = mergeAt;
            Bottles = bottles ?? throw new ArgumentNullException(nameof(bottles));
        }
    }

    /// <summary>The oval conveyor as authored (GDD R1–R4). Immutable.</summary>
    public sealed class LoopDefinition
    {
        public const int DefaultWidth = 4;

        /// <summary>Rows of belt round the oval.</summary>
        public int Rows { get; }
        /// <summary>Bottle spots per row.</summary>
        public int Width { get; }
        /// <summary>The pick zone: track positions <c>0 .. PickRows−1</c>, the front straight in front of the slots.</summary>
        public int PickRows { get; }
        public IReadOnlyList<FeederDefinition> Feeders { get; }
        /// <summary>Optional: the belt's content at the start, <c>Initial[row][track]</c> (None = empty spot). Null =
        /// the feeders fill the belt as it turns once round before the round starts.</summary>
        public IReadOnlyList<IReadOnlyList<CapColor>> Initial { get; }

        public LoopDefinition(int rows, int width, int pickRows, IReadOnlyList<FeederDefinition> feeders,
            IReadOnlyList<IReadOnlyList<CapColor>> initial = null)
        {
            Rows = rows; Width = width; PickRows = pickRows;
            Feeders = feeders ?? throw new ArgumentNullException(nameof(feeders));
            Initial = initial;
        }

        /// <summary>Every bottle of the level: the authored belt plus every feeder.</summary>
        public IEnumerable<CapColor> AllBottles()
        {
            if (Initial != null) foreach (var row in Initial) foreach (var c in row) if (c != CapColor.None) yield return c;
            foreach (var f in Feeders) foreach (var c in f.Bottles) yield return c;
        }
    }

    /// <summary>A tray on a conveyor as authored: lane <see cref="Lane"/>, position <see cref="Index"/> in that lane's
    /// queue (0 = the tray that starts at the front).</summary>
    public readonly struct TrayRef : IEquatable<TrayRef>
    {
        public readonly int Lane, Index;
        public TrayRef(int lane, int index) { Lane = lane; Index = index; }
        public bool Equals(TrayRef o) => Lane == o.Lane && Index == o.Index;
        public override bool Equals(object obj) => obj is TrayRef o && Equals(o);
        public override int GetHashCode() => (Lane << 16) ^ Index;
        public override string ToString() => $"lanes[{Lane}][{Index}]";
    }

    /// <summary>A locked tray (GDD R18): it can not be tapped until <see cref="Turns"/> trays have flown to the slots
    /// while it stands at the front of its lane.</summary>
    public sealed class TrayLock
    {
        public TrayRef Tray { get; }
        public int Turns { get; }
        public TrayLock(TrayRef tray, int turns) { Tray = tray; Turns = turns; }
    }

    /// <summary>Two linked trays (GDD R19): they leave the belt together or not at all. Either two neighbours in one
    /// lane, or the trays at the same position of two neighbouring lanes.</summary>
    public sealed class TrayLink
    {
        public TrayRef A { get; }
        public TrayRef B { get; }
        public TrayLink(TrayRef a, TrayRef b) { A = a; B = b; }
    }

    /// <summary>One level, fully data-driven (GDD §6). Immutable; build it with <see cref="LevelJson.Parse"/>.</summary>
    public sealed class LevelDefinition
    {
        public const int CurrentFormatVersion = 3;
        public const int DefaultSlots = 4;
        public const int DefaultExtraSlots = 2;
        /// <summary>Open plus locked slots never exceed this (the slot bar's width).</summary>
        public const int MaxSlots = 6;
        public const int DefaultTrayCapacity = 4;

        public int FormatVersion { get; }
        public string Id { get; }
        /// <summary>Slots open from the start.</summary>
        public int Slots { get; }
        /// <summary>R20: locked slots right of the open ones; each opens for coins or a rewarded ad, for this round only.</summary>
        public int ExtraSlots { get; }
        public int TrayCapacity { get; }
        public IReadOnlyList<CapColor> Colors { get; }
        public LoopDefinition Loop { get; }
        /// <summary><c>Lanes[j][0]</c> is the tappable front tray of conveyor j.</summary>
        public IReadOnlyList<IReadOnlyList<CapColor>> Lanes { get; }
        public string CameraPreset { get; }
        /// <summary>How the loop is drawn (presentation only; <see cref="LoopShape.Default"/> = the oval).</summary>
        public LoopShape Shape { get; }
        public string Name { get; }
        public string Difficulty { get; }
        public string Notes { get; }
        /// <summary>Optional winning tap sequence (lane indices) — V6 replays it as proof of solvability.</summary>
        public IReadOnlyList<int> Solution { get; }
        /// <summary>Hidden trays (GDD R17): tray and caps show no colour until the tray reaches the front of its lane.</summary>
        public IReadOnlyCollection<TrayRef> HiddenTrays { get; }
        public IReadOnlyList<TrayLock> Locks { get; }
        public IReadOnlyList<TrayLink> Links { get; }

        private readonly HashSet<TrayRef> _hidden;

        public LevelDefinition(string id, int slots, int trayCapacity, IReadOnlyList<CapColor> colors,
            LoopDefinition loop, IReadOnlyList<IReadOnlyList<CapColor>> lanes,
            string cameraPreset = "default",
            string name = null, string difficulty = null, string notes = null,
            int formatVersion = CurrentFormatVersion, IReadOnlyList<int> solution = null,
            IEnumerable<TrayRef> hiddenTrays = null, IReadOnlyList<TrayLock> locks = null, IReadOnlyList<TrayLink> links = null,
            int extraSlots = 0, LoopShape loopShape = null)
        {
            Shape = loopShape ?? LoopShape.Default;
            ExtraSlots = extraSlots;
            _hidden = hiddenTrays != null ? new HashSet<TrayRef>(hiddenTrays) : new HashSet<TrayRef>();
            HiddenTrays = _hidden;
            Locks = locks ?? Array.Empty<TrayLock>();
            Links = links ?? Array.Empty<TrayLink>();
            Solution = solution;
            FormatVersion = formatVersion;
            Id = id ?? throw new ArgumentNullException(nameof(id));
            Slots = slots; TrayCapacity = trayCapacity;
            Colors = colors ?? throw new ArgumentNullException(nameof(colors));
            Loop = loop ?? throw new ArgumentNullException(nameof(loop));
            Lanes = lanes ?? throw new ArgumentNullException(nameof(lanes));
            CameraPreset = cameraPreset ?? "default";
            Name = name; Difficulty = difficulty; Notes = notes;
        }

        public bool IsHiddenTray(TrayRef tray) => _hidden.Contains(tray);

        /// <summary>How many placements <paramref name="tray"/> stays locked for at the front; 0 = not locked.</summary>
        public int LockTurns(TrayRef tray)
        {
            foreach (var l in Locks) if (l.Tray.Equals(tray)) return l.Turns;
            return 0;
        }

        /// <summary>The tray linked to <paramref name="tray"/>, if any.</summary>
        public bool TryGetLinkPartner(TrayRef tray, out TrayRef partner)
        {
            foreach (var l in Links)
            {
                if (l.A.Equals(tray)) { partner = l.B; return true; }
                if (l.B.Equals(tray)) { partner = l.A; return true; }
            }
            partner = default;
            return false;
        }
    }
}
