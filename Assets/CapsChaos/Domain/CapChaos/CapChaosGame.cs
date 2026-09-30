using System;
using System.Collections.Generic;
using System.Text;

namespace Game.Domain
{
    public enum GameStatus { Playing, Won, Lost }

    public enum TapOutcome { Accepted, RejectedEmptyLane, RejectedNoFreeSlot, RejectedGameOver, RejectedBadLane }

    public sealed class TapResult
    {
        public TapOutcome Outcome { get; }
        public IReadOnlyList<GameFact> Facts { get; }
        public bool Accepted => Outcome == TapOutcome.Accepted;
        public TapResult(TapOutcome outcome, IReadOnlyList<GameFact> facts) { Outcome = outcome; Facts = facts; }
    }

    /// <summary>
    /// The Cap Chaos rules (GDD §5, R5–R16) over one level. Deterministic and instantaneous: a tap
    /// resolves the whole cascade (place → fill → drop/reveal → pack → win/lose) and returns the facts;
    /// nothing here waits for an animation. No randomness, no time, no engine (rules #14, #15).
    /// </summary>
    public sealed class CapChaosGame
    {
        private readonly BottleStack _stack;
        private readonly List<char>[] _lanes;
        private readonly int[] _laneHead;
        private readonly char[] _slotColor;   // '\0' = empty slot
        private readonly int[] _slotFilled;

        /// <summary>Generator mode: trays come from <see cref="PlaceTray"/>, so empty lanes are not a dead end.</summary>
        internal bool EndlessSupply { get; set; }

        public int Capacity { get; }
        public int SlotCount => _slotColor.Length;
        public int LaneCount => _lanes.Length;
        public GameStatus Status { get; private set; }
        public BottleStack Stack => _stack;

        public CapChaosGame(LevelDefinition level)
            : this(BottleStack.FromDefinition(level.Stack), level.Lanes, level.Slots, level.TrayCapacity) { }

        internal CapChaosGame(BottleStack stack, IReadOnlyList<IReadOnlyList<char>> lanes, int slots, int capacity)
        {
            _stack = stack;
            Capacity = capacity;
            _lanes = new List<char>[lanes.Count];
            for (int j = 0; j < lanes.Count; j++) _lanes[j] = new List<char>(lanes[j]);
            _laneHead = new int[lanes.Count];
            _slotColor = new char[slots];
            _slotFilled = new int[slots];
            Status = GameStatus.Playing;
        }

        private CapChaosGame(CapChaosGame src)
        {
            _stack = src._stack.Clone();
            Capacity = src.Capacity;
            _lanes = src._lanes;                          // lane CONTENT is immutable after construction…
            _laneHead = (int[])src._laneHead.Clone();     // …only the heads move
            _slotColor = (char[])src._slotColor.Clone();
            _slotFilled = (int[])src._slotFilled.Clone();
            Status = src.Status;
            EndlessSupply = src.EndlessSupply;
        }

        public CapChaosGame Clone() => new CapChaosGame(this);

        // ── queries (what a View / solver reads) ────────────────────────────────────────────
        public int LaneRemaining(int lane) => _lanes[lane].Count - _laneHead[lane];
        public char LaneFront(int lane) => LaneRemaining(lane) > 0 ? _lanes[lane][_laneHead[lane]] : '\0';
        public char LaneAt(int lane, int offset) => _laneHead[lane] + offset < _lanes[lane].Count ? _lanes[lane][_laneHead[lane] + offset] : '\0';
        public char SlotColor(int slot) => _slotColor[slot];
        public int SlotFilled(int slot) => _slotFilled[slot];
        public bool HasFreeSlot => FreeSlot() >= 0;

        // ── the one player action ───────────────────────────────────────────────────────────
        public TapResult Tap(int lane)
        {
            if (Status != GameStatus.Playing) return Reject(TapOutcome.RejectedGameOver);
            if (lane < 0 || lane >= _lanes.Length) return Reject(TapOutcome.RejectedBadLane);
            if (LaneRemaining(lane) == 0) return Reject(TapOutcome.RejectedEmptyLane);         // R5
            int slot = FreeSlot();
            if (slot < 0) return Reject(TapOutcome.RejectedNoFreeSlot);                         // R8

            var facts = new List<GameFact>();
            char color = _lanes[lane][_laneHead[lane]];
            _laneHead[lane]++;
            facts.Add(new TrayPlaced(lane, slot, color));                                       // R6
            facts.Add(new LaneAdvanced(lane, LaneRemaining(lane)));                             // R7
            _slotColor[slot] = color;
            _slotFilled[slot] = 0;
            Resolve(facts);
            return new TapResult(TapOutcome.Accepted, facts);
        }

        /// <summary>Generator hook: drop a tray of <paramref name="color"/> straight into a free slot (no lane).</summary>
        internal IReadOnlyList<GameFact> PlaceTray(char color)
        {
            int slot = FreeSlot();
            if (Status != GameStatus.Playing || slot < 0) throw new InvalidOperationException("no free slot");
            var facts = new List<GameFact> { new TrayPlaced(-1, slot, color) };
            _slotColor[slot] = color;
            _slotFilled[slot] = 0;
            Resolve(facts);
            return facts;
        }

        private static TapResult Reject(TapOutcome o) => new TapResult(o, Array.Empty<GameFact>());

        private int FreeSlot()                                                                  // R6: left-most free
        {
            for (int s = 0; s < _slotColor.Length; s++) if (_slotColor[s] == '\0') return s;
            return -1;
        }

        /// <summary>R11: fill to a fixpoint, slots left→right, one bottle at a time, restarting after each pick.</summary>
        private void Resolve(List<GameFact> facts)
        {
            bool progress = true;
            while (progress)
            {
                progress = false;
                for (int s = 0; s < _slotColor.Length && !progress; s++)
                {
                    if (_slotColor[s] == '\0') continue;
                    if (!TryBest(s, out int bx, out int bz)) continue;
                    // the drop/reveal facts belong AFTER the pick in replay order
                    var tail = new List<GameFact>(2);
                    var b = _stack.TakeGround(bx, bz, tail);
                    facts.Add(new BottlePicked(bx, bz, s, b.Color));
                    facts.AddRange(tail);
                    _slotFilled[s]++;
                    facts.Add(new BottleCapped(s, _slotFilled[s]));
                    if (_slotFilled[s] >= Capacity)                                             // R13
                    {
                        facts.Add(new TrayPacked(s, _slotColor[s]));
                        _slotColor[s] = '\0';
                        _slotFilled[s] = 0;
                    }
                    progress = true;
                }
            }
            Judge(facts);
        }

        /// <summary>
        /// Best exposed bottle of the slot's colour: smallest z (front-most), then the column nearest the
        /// slot's centre, then the smaller x. Deterministic (R11).
        /// </summary>
        private bool TryBest(int slot, out int bestX, out int bestZ)
        {
            bestX = bestZ = -1;
            char color = _slotColor[slot];
            double centre = (slot + 0.5) * _stack.Cols / _slotColor.Length - 0.5;
            double bestDist = double.MaxValue;
            for (int x = 0; x < _stack.Cols; x++)
            {
                int z = _stack.FrontZ(x);
                if (z < 0 || !_stack.IsExposed(x, z) || _stack.At(x, z, 0).Color != color) continue;
                double d = Math.Abs(x - centre);
                if (bestZ < 0 || z < bestZ || (z == bestZ && (d < bestDist || (d == bestDist && x < bestX))))
                { bestX = x; bestZ = z; bestDist = d; }
            }
            return bestX >= 0;
        }

        private void Judge(List<GameFact> facts)
        {
            bool anyTray = false;
            for (int s = 0; s < _slotColor.Length; s++) if (_slotColor[s] != '\0') anyTray = true;
            if (_stack.IsEmpty && !anyTray)
            {
                Status = GameStatus.Won;                                                        // R14
                facts.Add(new LevelCompleted());
                return;
            }
            if (FreeSlot() < 0)                                                                 // R15 (fill already reached its fixpoint)
            {
                Status = GameStatus.Lost;
                facts.Add(new LevelFailed(FailReason.SlotsJammed));
                return;
            }
            bool anyLane = EndlessSupply;
            for (int j = 0; j < _lanes.Length; j++) if (LaneRemaining(j) > 0) anyLane = true;
            if (!anyLane)
            {
                Status = GameStatus.Lost;
                facts.Add(new LevelFailed(FailReason.NoMovesLeft));
            }
        }

        /// <summary>Exact state fingerprint for the solver's memo.</summary>
        public string StateKey()
        {
            var sb = new StringBuilder(64 + _stack.Count * 2);
            foreach (var h in _laneHead) sb.Append(h).Append(',');
            sb.Append('#');
            for (int s = 0; s < _slotColor.Length; s++) sb.Append(_slotColor[s] == '\0' ? '_' : _slotColor[s]).Append(_slotFilled[s]);
            sb.Append('#');
            _stack.AppendKey(sb);
            return sb.ToString();
        }
    }
}
