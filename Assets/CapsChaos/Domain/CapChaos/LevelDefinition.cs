using System;
using System.Collections.Generic;

namespace Game.Domain
{
    /// <summary>A feeder as played (GDD R4): it joins the loop at track position <see cref="MergeAt"/> (from the
    /// conveyor); its <see cref="Bottles"/> (from the level) queue row by row — bottle <c>i</c> on track
    /// <c>i % width</c>, bottle 0 joins first. <see cref="HiddenRows"/>: queue rows (row r = bottles r × width …
    /// r × width + width − 1) whose bottles are drawn grey while they queue and show their colour once they join the
    /// loop (R23) — a look only, the rules never read it.</summary>
    public sealed class FeederDefinition
    {
        private readonly HashSet<int> _hidden;
        private readonly Dictionary<int, int> _locked;

        public int MergeAt { get; }
        public IReadOnlyList<CapColor> Bottles { get; }
        /// <summary>R23: queue rows hidden until they join the loop (0 = the first row to join).</summary>
        public IReadOnlyCollection<int> HiddenRows => _hidden;
        /// <summary>R24: locked queue rows → their lock turns. A locked row joins the loop like any other, but no tray
        /// takes its bottles until that many trays have flown to the slots while it is on the loop.</summary>
        public IReadOnlyDictionary<int, int> LockedRows => _locked;

        public FeederDefinition(int mergeAt, IReadOnlyList<CapColor> bottles, IEnumerable<int> hiddenRows = null,
            IReadOnlyDictionary<int, int> lockedRows = null)
        {
            MergeAt = mergeAt;
            Bottles = bottles ?? throw new ArgumentNullException(nameof(bottles));
            _hidden = hiddenRows != null ? new HashSet<int>(hiddenRows) : new HashSet<int>();
            _locked = lockedRows != null ? new Dictionary<int, int>(lockedRows) : new Dictionary<int, int>();
        }

        /// <summary>R24: the lock turns of queue row <paramref name="row"/>; 0 = not locked.</summary>
        public int LockTurns(int row) => _locked.TryGetValue(row, out var t) ? t : 0;

        /// <summary>Is queue row <paramref name="row"/> hidden until it joins the loop (R23)?</summary>
        public bool IsHiddenRow(int row) => _hidden.Contains(row);

        /// <summary>How many queue rows the bottles make at <paramref name="width"/> bottles per row.</summary>
        public int RowCount(int width) => width <= 0 ? 0 : (Bottles.Count + width - 1) / width;
    }

    /// <summary>
    /// The top conveyor as played (GDD R1–R4): a shared <see cref="ConveyorDefinition"/> (the layout — rows, pick zone,
    /// splines, merge points) filled with ONE level's bottles (the queues of the feeders it uses, optional initial
    /// rows). Immutable.
    /// </summary>
    public sealed class LoopDefinition
    {
        public const int DefaultWidth = 4;

        /// <summary>The layout this belt runs on — the conveyor file the level names.</summary>
        public ConveyorDefinition Conveyor { get; }
        /// <summary>Rows of belt round the loop.</summary>
        public int Rows => Conveyor.Rows;
        /// <summary>Bottle spots per row.</summary>
        public int Width => Conveyor.Width;
        /// <summary>The pick zone: track positions <c>0 .. PickRows−1</c>, the front straight in front of the slots.</summary>
        public int PickRows => Conveyor.PickRows;
        /// <summary>The feeders this level uses — the conveyor's first N, in its order (right, left, middle): the merge
        /// point and this level's bottles for it.</summary>
        public IReadOnlyList<FeederDefinition> Feeders { get; }
        /// <summary>Optional: the belt's content at the start, <c>Initial[row][track]</c> (None = empty spot). Null =
        /// the feeders fill the belt as it turns once round before the round starts.</summary>
        public IReadOnlyList<IReadOnlyList<CapColor>> Initial { get; }

        /// <summary>A level's bottles on a shared conveyor: <paramref name="feederBottles"/>[f] is what conveyor feeder
        /// f carries, <paramref name="feederHiddenRows"/>[f] which of its queue rows are hidden (R23; null = none),
        /// <paramref name="feederLockedRows"/>[f] which are locked and for how many turns (R24; null = none). A level
        /// uses the conveyor's first <c>feederBottles.Count</c> feeders; the rest stay empty and are not drawn.</summary>
        public LoopDefinition(ConveyorDefinition conveyor, IReadOnlyList<IReadOnlyList<CapColor>> feederBottles,
            IReadOnlyList<IReadOnlyList<CapColor>> initial = null, IReadOnlyList<IEnumerable<int>> feederHiddenRows = null,
            IReadOnlyList<IReadOnlyDictionary<int, int>> feederLockedRows = null)
        {
            Conveyor = conveyor ?? throw new ArgumentNullException(nameof(conveyor));
            if (feederBottles == null) throw new ArgumentNullException(nameof(feederBottles));
            if (feederBottles.Count > conveyor.FeederCount)
                throw new ArgumentException($"{feederBottles.Count} feeder queue(s) for a conveyor with {conveyor.FeederCount} feeder(s)", nameof(feederBottles));
            var feeders = new List<FeederDefinition>(feederBottles.Count);
            for (int f = 0; f < feederBottles.Count; f++)
                feeders.Add(new FeederDefinition(conveyor.MergeAt[f], feederBottles[f],
                    feederHiddenRows != null && f < feederHiddenRows.Count ? feederHiddenRows[f] : null,
                    feederLockedRows != null && f < feederLockedRows.Count ? feederLockedRows[f] : null));
            Feeders = feeders;
            Initial = initial;
        }

        /// <summary>A belt whose layout is built in code (tests): an anonymous oval conveyor — playable, not writable.</summary>
        public LoopDefinition(int rows, int width, int pickRows, IReadOnlyList<FeederDefinition> feeders,
            IReadOnlyList<IReadOnlyList<CapColor>> initial = null)
        {
            Feeders = feeders ?? throw new ArgumentNullException(nameof(feeders));
            var mergeAt = new List<int>(feeders.Count);
            foreach (var f in feeders) mergeAt.Add(f.MergeAt);
            Conveyor = new ConveyorDefinition(null, rows, width, pickRows, mergeAt);
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

    /// <summary>
    /// A tray's container size (GDD R21). The level file writes the NUMBER (1 S · 2 M · 3 L · 4 XL — never renumbered);
    /// a tray takes <c>size × trayCapacity</c> items, so with the usual trayCapacity 4: S 4, M 8, L 12, XL 16.
    /// </summary>
    public enum TraySize : byte
    {
        S = 1,
        M = 2,
        L = 3,
        XL = 4,
    }

    /// <summary>A locked tray (GDD R18): it can not be tapped until <see cref="Turns"/> trays have flown to the slots
    /// while it stands at the front of its lane.</summary>
    public sealed class TrayLock
    {
        public TrayRef Tray { get; }
        public int Turns { get; }
        public TrayLock(TrayRef tray, int turns) { Tray = tray; Turns = turns; }
    }

    /// <summary>A slot locked for turns (GDD R22): slot <see cref="Slot"/> (one of the open slots, 0 = left-most) takes no
    /// tray until <see cref="Turns"/> trays have flown to the other slots.</summary>
    public sealed class SlotLock
    {
        public int Slot { get; }
        public int Turns { get; }
        public SlotLock(int slot, int turns) { Slot = slot; Turns = turns; }
    }

    /// <summary>Two linked trays (GDD R19) on two DIFFERENT lanes, at any positions. Each lane moves on its own; a linked
    /// tray at the front can not be released until its partner reaches the front of its lane too — then the link breaks
    /// and both are ordinary trays.</summary>
    public sealed class TrayLink
    {
        public TrayRef A { get; }
        public TrayRef B { get; }
        public TrayLink(TrayRef a, TrayRef b) { A = a; B = b; }
    }

    /// <summary>One level, fully data-driven (GDD §6). Immutable; build it with <see cref="LevelJson.Parse"/>.</summary>
    public sealed class LevelDefinition
    {
        public const int CurrentFormatVersion = 4;
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
        /// <summary>The shared top-conveyor layout this level runs on (<c>ConveyorConfig/&lt;id&gt;.json</c>).</summary>
        public ConveyorDefinition Conveyor => Loop.Conveyor;
        public string Name { get; }
        public string Difficulty { get; }
        public string Notes { get; }
        /// <summary>Optional winning tap sequence (lane indices) — V6 replays it as proof of solvability.</summary>
        public IReadOnlyList<int> Solution { get; }
        /// <summary>Hidden trays (GDD R17): tray and caps show no colour until the tray reaches the front of its lane.</summary>
        public IReadOnlyCollection<TrayRef> HiddenTrays { get; }
        public IReadOnlyList<TrayLock> Locks { get; }
        public IReadOnlyList<TrayLink> Links { get; }
        /// <summary>R22: open slots that take no tray until enough trays have flown to the others.</summary>
        public IReadOnlyList<SlotLock> SlotLocks { get; }

        /// <summary>Trays bigger than <see cref="TraySize.S"/> (R21); every other tray is S.</summary>
        public IReadOnlyDictionary<TrayRef, TraySize> TraySizes { get; }

        private readonly HashSet<TrayRef> _hidden;

        public LevelDefinition(string id, int slots, int trayCapacity, IReadOnlyList<CapColor> colors,
            LoopDefinition loop, IReadOnlyList<IReadOnlyList<CapColor>> lanes,
            string cameraPreset = "default",
            string name = null, string difficulty = null, string notes = null,
            int formatVersion = CurrentFormatVersion, IReadOnlyList<int> solution = null,
            IEnumerable<TrayRef> hiddenTrays = null, IReadOnlyList<TrayLock> locks = null, IReadOnlyList<TrayLink> links = null,
            int extraSlots = 0, IReadOnlyDictionary<TrayRef, TraySize> traySizes = null, IReadOnlyList<SlotLock> slotLocks = null)
        {
            SlotLocks = slotLocks ?? Array.Empty<SlotLock>();
            TraySizes = traySizes ?? new Dictionary<TrayRef, TraySize>();
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

        public TraySize SizeOf(TrayRef tray) => TraySizes.TryGetValue(tray, out var s) ? s : TraySize.S;

        /// <summary>How many items <paramref name="tray"/> takes before it is full (R13, R21).</summary>
        public int CapacityOf(TrayRef tray) => (int)SizeOf(tray) * TrayCapacity;

        /// <summary>How many placements <paramref name="tray"/> stays locked for at the front; 0 = not locked.</summary>
        public int LockTurns(TrayRef tray)
        {
            foreach (var l in Locks) if (l.Tray.Equals(tray)) return l.Turns;
            return 0;
        }

        /// <summary>How many placements slot <paramref name="slot"/> stays locked for (R22); 0 = not locked.</summary>
        public int SlotLockTurns(int slot)
        {
            foreach (var l in SlotLocks) if (l.Slot == slot) return l.Turns;
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
