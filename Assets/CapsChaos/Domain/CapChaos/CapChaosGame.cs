using System;
using System.Collections.Generic;
using System.Text;

namespace Game.Domain
{
    public enum GameStatus { Playing, Won, Lost }

    public enum TapOutcome
    {
        Accepted, RejectedEmptyLane, RejectedNoFreeSlot, RejectedGameOver, RejectedBadLane,
        /// <summary>R18: the front tray is still locked.</summary>
        RejectedLocked,
        /// <summary>R19: the front tray is linked and its partner is not at the front of its own lane yet.</summary>
        RejectedLinkNotReady,
        /// <summary>R19: the front position of the lane is empty — its belt is held until the belt it is linked to can move too.</summary>
        RejectedBeltHeld,
    }

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
        private readonly List<CapColor>[] _lanes;
        private readonly int[] _laneHead;
        // per-tray modifiers (R17–R19), immutable after construction and shared by clones; null = none in the level
        private bool[][] _hidden;
        private int[][] _lockTurns;
        private TrayRef?[][] _partner;
        private readonly int[] _lockLeft;          // per lane: placements the FRONT tray still waits for (R18)
        private readonly int[] _gap;               // per lane: empty positions at the front of a HELD belt (R19)
        private readonly CapColor[] _slotColor;   // None = empty slot
        private readonly int[] _slotFilled;

        /// <summary>Generator mode: trays come from <see cref="PlaceTray"/>, so empty lanes are not a dead end.</summary>
        internal bool EndlessSupply { get; set; }

        public int Capacity { get; }
        public int SlotCount => _slotColor.Length;
        public int LaneCount => _lanes.Length;
        public GameStatus Status { get; private set; }
        public BottleStack Stack => _stack;

        public CapChaosGame(LevelDefinition level)
            : this(BottleStack.FromDefinition(level.Stack), level.Lanes, level.Slots, level.TrayCapacity)
        {
            ApplyTrayModifiers(level);
        }

        internal CapChaosGame(BottleStack stack, IReadOnlyList<IReadOnlyList<CapColor>> lanes, int slots, int capacity)
        {
            _stack = stack;
            Capacity = capacity;
            _lanes = new List<CapColor>[lanes.Count];
            for (int j = 0; j < lanes.Count; j++) _lanes[j] = new List<CapColor>(lanes[j]);
            _laneHead = new int[lanes.Count];
            _lockLeft = new int[lanes.Count];
            _gap = new int[lanes.Count];
            _slotColor = new CapColor[slots];
            _slotFilled = new int[slots];
            Status = GameStatus.Playing;
        }

        private CapChaosGame(CapChaosGame src)
        {
            _stack = src._stack.Clone();
            Capacity = src.Capacity;
            _lanes = src._lanes;                          // lane CONTENT is immutable after construction…
            _laneHead = (int[])src._laneHead.Clone();     // …only the heads move
            _hidden = src._hidden; _lockTurns = src._lockTurns; _partner = src._partner;
            _lockLeft = (int[])src._lockLeft.Clone();
            _gap = (int[])src._gap.Clone();
            _slotColor = (CapColor[])src._slotColor.Clone();
            _slotFilled = (int[])src._slotFilled.Clone();
            Status = src.Status;
            EndlessSupply = src.EndlessSupply;
        }

        public CapChaosGame Clone() => new CapChaosGame(this);

        /// <summary>Copy the level's hidden / locked / linked trays in. A reference outside the lanes is skipped
        /// (V7 reports it; the rules never index past a lane).</summary>
        private void ApplyTrayModifiers(LevelDefinition level)
        {
            bool InRange(TrayRef t) => t.Lane >= 0 && t.Lane < _lanes.Length && t.Index >= 0 && t.Index < _lanes[t.Lane].Count;
            if (level.HiddenTrays.Count > 0)
            {
                _hidden = NewJagged<bool>();
                foreach (var t in level.HiddenTrays) if (InRange(t)) _hidden[t.Lane][t.Index] = true;
            }
            if (level.Locks.Count > 0)
            {
                _lockTurns = NewJagged<int>();
                foreach (var l in level.Locks) if (InRange(l.Tray) && l.Turns > 0) _lockTurns[l.Tray.Lane][l.Tray.Index] = l.Turns;
            }
            if (level.Links.Count > 0)
            {
                _partner = NewJagged<TrayRef?>();
                foreach (var l in level.Links)
                {
                    if (!InRange(l.A) || !InRange(l.B) || l.A.Equals(l.B)) continue;
                    _partner[l.A.Lane][l.A.Index] = l.B;
                    _partner[l.B.Lane][l.B.Index] = l.A;
                }
            }
            for (int j = 0; j < _lanes.Length; j++) _lockLeft[j] = LockTurnsAt(j, 0);
        }

        private T[][] NewJagged<T>()
        {
            var a = new T[_lanes.Length][];
            for (int j = 0; j < a.Length; j++) a[j] = new T[_lanes[j].Count];
            return a;
        }

        private int LockTurnsAt(int lane, int tray) =>
            _lockTurns != null && tray < _lockTurns[lane].Length ? _lockTurns[lane][tray] : 0;

        // ── queries (what a View / solver reads) ────────────────────────────────────────────
        public int LaneRemaining(int lane) => _lanes[lane].Count - _laneHead[lane];
        /// <summary>The front tray's colour, or <see cref="CapColor.None"/> when the lane is empty.</summary>
        public CapColor LaneFront(int lane) => HasFront(lane) ? _lanes[lane][_laneHead[lane]] : CapColor.None;
        public CapColor LaneAt(int lane, int offset) => _laneHead[lane] + offset < _lanes[lane].Count ? _lanes[lane][_laneHead[lane] + offset] : CapColor.None;
        /// <summary>The colour of the tray in <paramref name="slot"/>, or <see cref="CapColor.None"/> when it is free.</summary>
        public CapColor SlotColor(int slot) => _slotColor[slot];
        public int SlotFilled(int slot) => _slotFilled[slot];
        public bool HasFreeSlot => FreeSlot() >= 0;

        /// <summary>Authored index of the next tray of <paramref name="lane"/> (= how many trays have left it).</summary>
        public int LaneHead(int lane) => _laneHead[lane];
        /// <summary>R19: empty positions at the front of <paramref name="lane"/>'s belt while it is held; 0 = its next tray is at the front.</summary>
        public int LaneGap(int lane) => _gap[lane];
        /// <summary>Is authored tray <paramref name="tray"/> standing at the front position of its belt?</summary>
        public bool IsAtFront(int lane, int tray) => tray == _laneHead[lane] && HasFront(lane);
        private bool HasFront(int lane) => _gap[lane] == 0 && LaneRemaining(lane) > 0;
        /// <summary>The colour of authored tray <paramref name="tray"/> of <paramref name="lane"/>, hidden or not.</summary>
        public CapColor TrayColor(int lane, int tray) => _lanes[lane][tray];
        /// <summary>R18: placements the front tray of <paramref name="lane"/> still waits for; 0 = tappable.</summary>
        public int LockLeft(int lane) => _lockLeft[lane];
        /// <summary>The lock a tray starts with when it reaches the front; 0 = not locked.</summary>
        public int LockTurns(int lane, int tray) => LockTurnsAt(lane, tray);

        /// <summary>R17: a hidden tray shows its colour once it is at the front of its lane — or once the first tray
        /// of its same-lane link pair is (the pair is at the front together).</summary>
        public bool IsTrayHidden(int lane, int tray)
        {
            if (_hidden == null || tray >= _hidden[lane].Length || !_hidden[lane][tray]) return false;
            int head = _laneHead[lane];
            if (tray < head || (tray == head && _gap[lane] == 0)) return false;
            return !(tray == head + 1 && _gap[lane] == 0 && TryPartner(lane, head, out var p) && p.Lane == lane && p.Index == tray);
        }

        /// <summary>The tray linked to authored tray <paramref name="tray"/> of <paramref name="lane"/>, if any (R19).</summary>
        public bool TryPartner(int lane, int tray, out TrayRef partner)
        {
            var p = _partner != null && tray < _partner[lane].Length ? _partner[lane][tray] : null;
            partner = p ?? default;
            return p.HasValue;
        }

        // ── the one player action ───────────────────────────────────────────────────────────
        /// <summary>
        /// Release the front tray of <paramref name="lane"/> (R5–R7). A linked front tray takes its partner with it
        /// (R19) — both leave in authored order (same lane: front first; two lanes: the left lane first), each to the
        /// left-most free slot. Then the belts step forward (<see cref="AdvanceBelts"/>). Every placement counts down
        /// the locks of the trays that were already waiting at the front (R18); a tray that only just reached the
        /// front starts counting with the next placement.
        /// </summary>
        public TapResult Tap(int lane)
        {
            var outcome = Check(lane, out var group, out int count);
            if (outcome != TapOutcome.Accepted) return Reject(outcome);

            var frontBefore = new int[_lanes.Length];
            for (int j = 0; j < _lanes.Length; j++) frontBefore[j] = HasFront(j) ? _laneHead[j] : -1;

            var facts = new List<GameFact>();
            for (int i = 0; i < count; i++)
            {
                int slot = FreeSlot();
                CapColor color = _lanes[group[i].Lane][group[i].Index];
                facts.Add(new TrayPlaced(group[i].Lane, slot, color));                          // R6
                _slotColor[slot] = color;
                _slotFilled[slot] = 0;
                _laneHead[group[i].Lane]++;                                                     // the tray left; its position is empty
                _gap[group[i].Lane]++;
                _lockLeft[group[i].Lane] = 0;
            }
            AdvanceBelts(facts);                                                                // R7, R19

            for (int j = 0; j < _lanes.Length; j++)
            {
                int front = HasFront(j) ? _laneHead[j] : -1;
                if (front < 0) continue;
                if (front != frontBefore[j])                                                    // just arrived at the front
                {
                    _lockLeft[j] = LockTurnsAt(j, front);                                       // R18: counts from the next placement
                    RevealAt(j, front, facts);                                                  // R17
                    if (TryPartner(j, front, out var p) && p.Lane == j && p.Index == front + 1) RevealAt(j, front + 1, facts);
                }
                else if (_lockLeft[j] > 0)
                {
                    _lockLeft[j] = Math.Max(0, _lockLeft[j] - count);
                    facts.Add(new TrayLockTicked(j, front, _lockLeft[j]));
                }
            }

            Resolve(facts);
            return new TapResult(TapOutcome.Accepted, facts);
        }

        /// <summary>
        /// R7 + R19: a belt with an empty front position steps one tray forward — unless it carries a tray linked to a
        /// tray on ANOTHER belt: linked trays stay side by side, so those belts only step together, when every one of
        /// them has an empty front. A belt that can not step yet is HELD (its front stays empty, nothing on it can be
        /// tapped) until the belts it is linked to can step too. Repeats until no belt can step; one
        /// <see cref="LaneAdvanced"/> per belt per step.
        /// </summary>
        private void AdvanceBelts(List<GameFact> facts)
        {
            var moves = new bool[_lanes.Length];
            while (true)
            {
                for (int j = 0; j < _lanes.Length; j++) moves[j] = _gap[j] > 0;
                bool changed = true;
                while (changed)                                                                 // drop belts tied to a belt that can not step
                {
                    changed = false;
                    for (int j = 0; j < _lanes.Length; j++)
                        if (moves[j] && TiedToStuckBelt(j, moves)) { moves[j] = false; changed = true; }
                }
                bool any = false;
                for (int j = 0; j < _lanes.Length; j++)
                {
                    if (!moves[j]) continue;
                    _gap[j]--;
                    facts.Add(new LaneAdvanced(j, LaneRemaining(j)));
                    any = true;
                }
                if (!any) return;
            }
        }

        /// <summary>Does a tray still on belt <paramref name="lane"/> have a partner on a belt that is not stepping?</summary>
        private bool TiedToStuckBelt(int lane, bool[] moves)
        {
            if (_partner == null) return false;
            for (int t = _laneHead[lane]; t < _lanes[lane].Count; t++)
            {
                var p = _partner[lane][t];
                if (p.HasValue && p.Value.Lane != lane && !moves[p.Value.Lane]) return true;
            }
            return false;
        }

        /// <summary>Would a tap on <paramref name="lane"/> be accepted? The trays it would release land in
        /// <paramref name="group"/>[0..<paramref name="count"/>).</summary>
        private TapOutcome Check(int lane, out TrayRef[] group, out int count)
        {
            group = null; count = 0;
            if (Status != GameStatus.Playing) return TapOutcome.RejectedGameOver;
            if (lane < 0 || lane >= _lanes.Length) return TapOutcome.RejectedBadLane;
            if (LaneRemaining(lane) == 0) return TapOutcome.RejectedEmptyLane;                   // R5
            if (_gap[lane] > 0) return TapOutcome.RejectedBeltHeld;                              // R19
            if (_lockLeft[lane] > 0) return TapOutcome.RejectedLocked;                           // R18
            int head = _laneHead[lane];
            var front = new TrayRef(lane, head);
            if (TryPartner(lane, head, out var partner))                                         // R19
            {
                bool ready = partner.Lane == lane
                    ? partner.Index == head + 1
                    : IsAtFront(partner.Lane, partner.Index) && _lockLeft[partner.Lane] == 0;
                if (!ready) return TapOutcome.RejectedLinkNotReady;
                if (FreeSlotCount() < 2) return TapOutcome.RejectedNoFreeSlot;
                bool frontFirst = partner.Lane == lane || lane < partner.Lane;
                group = frontFirst ? new[] { front, partner } : new[] { partner, front };
                count = 2;
                return TapOutcome.Accepted;
            }
            if (FreeSlotCount() < 1) return TapOutcome.RejectedNoFreeSlot;                       // R8
            group = new[] { front };
            count = 1;
            return TapOutcome.Accepted;
        }

        private void RevealAt(int lane, int tray, List<GameFact> facts)
        {
            if (_hidden != null && tray < _hidden[lane].Length && _hidden[lane][tray])
                facts.Add(new TrayRevealed(lane, tray, _lanes[lane][tray]));
        }

        /// <summary>Generator hook: drop a tray of <paramref name="color"/> straight into a free slot (no lane).</summary>
        internal IReadOnlyList<GameFact> PlaceTray(CapColor color)
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
            for (int s = 0; s < _slotColor.Length; s++) if (_slotColor[s] == CapColor.None) return s;
            return -1;
        }

        private int FreeSlotCount()
        {
            int n = 0;
            for (int s = 0; s < _slotColor.Length; s++) if (_slotColor[s] == CapColor.None) n++;
            return n;
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
                    if (_slotColor[s] == CapColor.None) continue;
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
                        _slotColor[s] = CapColor.None;
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
            CapColor color = _slotColor[slot];
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
            for (int s = 0; s < _slotColor.Length; s++) if (_slotColor[s] != CapColor.None) anyTray = true;
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
            // a tap is the only thing that changes the state, so "no tap is accepted now" means "never again"
            bool anyMove = EndlessSupply;
            for (int j = 0; j < _lanes.Length && !anyMove; j++) anyMove = Check(j, out _, out _) == TapOutcome.Accepted;
            if (!anyMove)
            {
                Status = GameStatus.Lost;
                facts.Add(new LevelFailed(FailReason.NoMovesLeft));
            }
        }

        /// <summary>Exact state fingerprint for the solver's memo.</summary>
        public string StateKey()
        {
            var sb = new StringBuilder(64 + _stack.Count * 2);
            for (int j = 0; j < _laneHead.Length; j++) sb.Append(_laneHead[j]).Append(':').Append(_gap[j]).Append(':').Append(_lockLeft[j]).Append(',');
            sb.Append('#');
            for (int s = 0; s < _slotColor.Length; s++) sb.Append(_slotColor[s] == CapColor.None ? '_' : CapColorCodes.ToCode(_slotColor[s])).Append(_slotFilled[s]);
            sb.Append('#');
            _stack.AppendKey(sb);
            return sb.ToString();
        }
    }
}
