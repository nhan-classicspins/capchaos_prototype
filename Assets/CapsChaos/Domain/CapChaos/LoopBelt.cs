using System;
using System.Collections.Generic;
using System.Text;

namespace Game.Domain
{
    /// <summary>
    /// The oval conveyor (GDD R1–R4): <see cref="Rows"/> rows of <see cref="Width"/> bottle spots that go round
    /// without stopping, one row per <see cref="Advance"/>. A row is a fixed piece of belt (its id never changes);
    /// <see cref="PositionOf"/> says where on the track it is now. Track position 0 is the first row of the pick zone
    /// (the front straight, in front of the slots); positions grow in the direction of travel.
    /// <para>A picked bottle leaves an empty spot that travels with the belt (R3). Feeders (R4) are queues of bottles
    /// that join the oval at a fixed position, ROW BY ROW: bottle <c>i</c> of a feeder stands in queue row
    /// <c>i / Width</c> on track <c>i % Width</c>, and the front queue row steps onto a belt row only when that row is
    /// empty on every track it needs — whole rows join, never single bottles. The merge position is the queue's
    /// ENTRANCE: the row taken is the free one nearest to it, within <see cref="FeedReach"/> rows either way (the one at
    /// the entrance first, then the one about to arrive before the one just gone by), never one in the pick zone.</para>
    /// </summary>
    public sealed class LoopBelt
    {
        public int Rows { get; }
        public int Width { get; }
        /// <summary>How many rows from track position 0 bottles are picked from (R2).</summary>
        public int PickRows { get; }
        /// <summary>How many rows the belt has moved since the round started, modulo <see cref="Rows"/>.</summary>
        public int Offset { get; private set; }
        /// <summary>Bottles on the belt (not in the feeders).</summary>
        public int Count { get; private set; }
        public int FeederCount => _mergeAt.Length;

        private readonly CapColor[] _spots;          // [row * Width + track]
        private readonly int[] _mergeAt;             // per feeder: the track position it joins at
        private readonly CapColor[][][] _feeders;    // [feeder][track] → queue, immutable after construction
        private readonly int[] _feederRow;           // [feeder] → queue rows already on the belt
        private readonly IReadOnlyDictionary<int, int>[] _rowLocks;   // [feeder] → locked queue row → authored turns (R24), shared by clones
        // R24, live: queue row key (LockKey) → turns left, for every locked row that has joined the loop (it counts down)
        // or was let go (0). A locked row still queued is not here: it keeps its authored turns and does not count.
        private readonly Dictionary<int, int> _lockLeft = new Dictionary<int, int>();
        private readonly int[] _spotLock;            // [row * Width + track] → LockKey of the locked queue row its bottle came from, 0 = none

        private LoopBelt(int rows, int width, int pickRows, int[] mergeAt, CapColor[][][] feeders,
            IReadOnlyDictionary<int, int>[] rowLocks = null)
        {
            Rows = rows; Width = width; PickRows = pickRows;
            _spots = new CapColor[rows * width];
            _mergeAt = mergeAt;
            _feeders = feeders;
            _feederRow = new int[feeders.Length];
            _rowLocks = rowLocks ?? new IReadOnlyDictionary<int, int>[feeders.Length];
            _spotLock = new int[rows * width];
        }

        private int RowLock(int f, int row) => _rowLocks[f] != null && _rowLocks[f].TryGetValue(row, out var t) ? t : 0;

        // one key per (feeder, queue row); never 0, so 0 means "no lock" in _spotLock
        private static int LockKey(int f, int row) => f * 1_000_000 + row + 1;

        /// <summary>
        /// The belt as the round starts: the authored <see cref="LoopDefinition.Initial"/> rows, or — when the level
        /// authors none — empty: every bottle starts in a feeder and joins while the belt runs.
        /// </summary>
        public static LoopBelt FromDefinition(LoopDefinition def)
        {
            var merge = new int[def.Feeders.Count];
            var queues = new CapColor[def.Feeders.Count][][];
            var locks = new IReadOnlyDictionary<int, int>[def.Feeders.Count];
            for (int f = 0; f < def.Feeders.Count; f++)
            {
                merge[f] = def.Feeders[f].MergeAt;
                locks[f] = def.Feeders[f].LockedRows.Count > 0 ? def.Feeders[f].LockedRows : null;
                var perTrack = new List<CapColor>[def.Width];
                for (int k = 0; k < def.Width; k++) perTrack[k] = new List<CapColor>();
                var bottles = def.Feeders[f].Bottles;
                for (int i = 0; i < bottles.Count; i++) perTrack[i % def.Width].Add(bottles[i]);
                queues[f] = new CapColor[def.Width][];
                for (int k = 0; k < def.Width; k++) queues[f][k] = perTrack[k].ToArray();
            }
            var belt = new LoopBelt(def.Rows, def.Width, def.PickRows, merge, queues, locks);
            if (def.Initial != null)
            {
                for (int r = 0; r < def.Rows; r++)
                    for (int k = 0; k < def.Width; k++)
                    {
                        var c = def.Initial[r][k];
                        if (c == CapColor.None) continue;
                        belt._spots[r * def.Width + k] = c;
                        belt.Count++;
                    }
            }
            return belt;
        }

        public LoopBelt Clone()
        {
            var c = new LoopBelt(Rows, Width, PickRows, _mergeAt, _feeders, _rowLocks) { Offset = Offset, Count = Count };
            Array.Copy(_spots, c._spots, _spots.Length);
            Array.Copy(_feederRow, c._feederRow, _feederRow.Length);
            Array.Copy(_spotLock, c._spotLock, _spotLock.Length);
            foreach (var kv in _lockLeft) c._lockLeft[kv.Key] = kv.Value;
            return c;
        }

        // ── geometry ────────────────────────────────────────────────────────────────────────
        /// <summary>The track position row <paramref name="row"/> is at now.</summary>
        public int PositionOf(int row) => (row + Offset) % Rows;
        /// <summary>The row standing at track position <paramref name="position"/> now.</summary>
        public int RowAt(int position) => ((position - Offset) % Rows + Rows) % Rows;
        public bool InPickZone(int position) => position >= 0 && position < PickRows;
        public int MergeAt(int feeder) => _mergeAt[feeder];

        // ── contents ────────────────────────────────────────────────────────────────────────
        public CapColor At(int row, int track) => _spots[row * Width + track];
        public bool IsEmpty => Count == 0 && FeederRemainingTotal == 0;

        /// <summary>Bottles of <paramref name="feeder"/>'s track <paramref name="track"/> that are still queued.</summary>
        public int FeederRemaining(int feeder, int track) => Math.Max(0, _feeders[feeder][track].Length - _feederRow[feeder]);
        /// <summary>Queue rows of <paramref name="feeder"/> already on the belt: the bottle at depth d of any of its
        /// tracks is in queue row <c>FeederRowsJoined + d</c> (R23 hides by queue row).</summary>
        public int FeederRowsJoined(int feeder) => _feederRow[feeder];
        /// <summary>R24: turns queue row <paramref name="row"/> of <paramref name="feeder"/> is locked for NOW — counting
        /// down once it is on the loop, its authored turns while still queued; 0 = not (or no longer) locked.</summary>
        public int FeederRowLockLeft(int feeder, int row) =>
            _lockLeft.TryGetValue(LockKey(feeder, row), out var left) ? left : RowLock(feeder, row);
        /// <summary>R24: the lock queue row <paramref name="row"/> of <paramref name="feeder"/> starts with; 0 = none.</summary>
        public int FeederRowLockTurns(int feeder, int row) => RowLock(feeder, row);
        /// <summary>R24: the bottle on (row, track) came from a locked queue row that is still locked — it rides the loop
        /// but no tray takes it.</summary>
        public bool IsLocked(int row, int track)
        {
            int key = _spotLock[row * Width + track];
            return key != 0 && _lockLeft.TryGetValue(key, out var left) && left > 0;
        }
        /// <summary>Is any locked queue row still locked — on the loop or still queued?</summary>
        public bool AnyRowLocked
        {
            get
            {
                for (int f = 0; f < _rowLocks.Length; f++)
                {
                    if (_rowLocks[f] == null) continue;
                    foreach (var kv in _rowLocks[f]) if (FeederRowLockLeft(f, kv.Key) > 0) return true;
                }
                return false;
            }
        }
        /// <summary>The <paramref name="depth"/>-th queued bottle of a feeder track (0 = the next to join).</summary>
        public CapColor FeederAt(int feeder, int track, int depth) => _feeders[feeder][track][_feederRow[feeder] + depth];

        public int FeederRemainingTotal
        {
            get
            {
                int n = 0;
                for (int f = 0; f < _feeders.Length; f++) for (int k = 0; k < Width; k++) n += FeederRemaining(f, k);
                return n;
            }
        }

        /// <summary>Is a bottle of <paramref name="color"/> on the belt that a tray could take (not locked, R24)?</summary>
        public bool Contains(CapColor color)
        {
            for (int i = 0; i < _spots.Length; i++)
                if (_spots[i] == color && !IsLocked(i / Width, i % Width)) return true;
            return false;
        }

        /// <summary>Can any feeder ever move again — does some belt row have room for a feeder's front queue row?</summary>
        public bool CanFeed
        {
            get
            {
                for (int f = 0; f < _feeders.Length; f++)
                    for (int r = 0; r < Rows; r++)
                        if (FrontRowFits(f, r)) return true;
                return false;
            }
        }

        /// <summary>Feeder <paramref name="f"/> has a queue row left and belt row <paramref name="row"/> is empty on every
        /// track it needs.</summary>
        private bool FrontRowFits(int f, int row)
        {
            bool any = false;
            for (int k = 0; k < Width; k++)
            {
                if (_feederRow[f] >= _feeders[f][k].Length) continue;
                if (_spots[row * Width + k] != CapColor.None) return false;
                any = true;
            }
            return any;
        }

        // ── change ──────────────────────────────────────────────────────────────────────────
        /// <summary>The belt moves one row forward.</summary>
        public void Advance() => Offset = (Offset + 1) % Rows;

        public CapColor Take(int row, int track)
        {
            int i = row * Width + track;
            var c = _spots[i];
            if (c == CapColor.None) throw new InvalidOperationException($"no bottle at row {row} track {track}");
            _spots[i] = CapColor.None;
            _spotLock[i] = 0;
            Count--;
            return c;
        }

        /// <summary>How far (rows) either side of its entrance a feeder looks for a free belt row.</summary>
        public const int FeedReach = 2;

        /// <summary>R4: a feeder puts its whole front queue row on the free belt row nearest its entrance (within
        /// <see cref="FeedReach"/>); feeders in order, one row each per step.</summary>
        public void Feed(List<GameFact> facts)
        {
            for (int f = 0; f < _feeders.Length; f++)
            {
                int row = NearestFreeRow(f);
                if (row < 0) continue;
                int q = _feederRow[f]++;
                int key = LockKey(f, q);
                int locked = FeederRowLockLeft(f, q);                              // R24: a locked row joins locked
                if (locked > 0) _lockLeft[key] = locked;
                for (int k = 0; k < Width; k++)
                {
                    if (q >= _feeders[f][k].Length) continue;
                    var c = _feeders[f][k][q];
                    _spots[row * Width + k] = c;
                    _spotLock[row * Width + k] = locked > 0 ? key : 0;
                    Count++;
                    facts?.Add(new BottleFed(f, k, row, c));
                }
            }
        }

        /// <summary>R24: <paramref name="placed"/> trays just flew to the slots — every locked row ON THE LOOP counts them
        /// down. A locked row still queued has not started counting.</summary>
        internal void TickRowLocks(int placed, List<GameFact> facts)
        {
            if (_lockLeft.Count == 0) return;
            foreach (int key in SortedKeys())
            {
                int left = _lockLeft[key];
                if (left == 0) continue;
                _lockLeft[key] = left = Math.Max(0, left - placed);
                facts?.Add(new FeederRowLockTicked((key - 1) / 1_000_000, (key - 1) % 1_000_000, left));
            }
        }

        /// <summary>R24: no tray is left in the lanes — every row still locked (on the loop or queued) opens now, or its
        /// bottles could never be taken.</summary>
        internal void ReleaseRowLocks(List<GameFact> facts)
        {
            for (int f = 0; f < _rowLocks.Length; f++)
            {
                if (_rowLocks[f] == null) continue;
                var rows = new List<int>(_rowLocks[f].Keys);
                rows.Sort();
                foreach (int row in rows)
                {
                    if (FeederRowLockLeft(f, row) == 0) continue;
                    _lockLeft[LockKey(f, row)] = 0;
                    facts?.Add(new FeederRowLockTicked(f, row, 0));
                }
            }
        }

        private List<int> SortedKeys()
        {
            var keys = new List<int>(_lockLeft.Keys);
            keys.Sort();
            return keys;
        }

        /// <summary>The belt row nearest feeder <paramref name="f"/>'s entrance that its front queue row fits: the one at
        /// the entrance, then −1, +1, −2, +2 … (upstream first: it is about to pass); −1 if none.</summary>
        private int NearestFreeRow(int f)
        {
            for (int d = 0; d <= 2 * FeedReach; d++)
            {
                int offset = (d + 1) / 2 * (d % 2 == 1 ? -1 : 1);
                int pos = ((_mergeAt[f] + offset) % Rows + Rows) % Rows;
                if (InPickZone(pos)) continue;
                int row = RowAt(pos);
                if (FrontRowFits(f, row)) return row;
            }
            return -1;
        }

        public void AppendKey(StringBuilder sb)
        {
            sb.Append(Offset).Append('@');
            foreach (var c in _spots) sb.Append(CapColorCodes.ToCode(c));
            sb.Append('|');
            for (int f = 0; f < _feeders.Length; f++) sb.Append(_feederRow[f]).Append(',');
            foreach (int key in SortedKeys()) sb.Append(key).Append('=').Append(_lockLeft[key]).Append(',');
            for (int i = 0; i < _spotLock.Length; i++) if (_spotLock[i] != 0 && IsLocked(i / Width, i % Width)) sb.Append('L').Append(i);
        }
    }
}
