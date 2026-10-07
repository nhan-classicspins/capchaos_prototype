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
        /// <summary>R19: the front tray is still linked — its partner has not reached the front of its own lane yet.</summary>
        RejectedLinkNotReady,
    }

    public sealed class TapResult
    {
        public TapOutcome Outcome { get; }
        public IReadOnlyList<GameFact> Facts { get; }
        public bool Accepted => Outcome == TapOutcome.Accepted;
        public TapResult(TapOutcome outcome, IReadOnlyList<GameFact> facts) { Outcome = outcome; Facts = facts; }
    }

    /// <summary>
    /// The Cap Chaos rules (GDD §5, R1–R19) over one level. Deterministic and discrete: the oval belt moves in
    /// whole rows (<see cref="Step"/>), and the controller decides WHEN — it calls Step on a fixed cadence from the
    /// gameplay tick, so no clock lives here. A tap or a step resolves its whole cascade (place → pick → pack → feed →
    /// win/lose) and returns the facts; nothing here waits for an animation. No randomness, no time, no engine
    /// (rules #14, #15).
    /// </summary>
    public sealed class CapChaosGame
    {
        private readonly LoopBelt _belt;
        private readonly List<CapColor>[] _lanes;
        private readonly int[] _laneHead;
        // per-tray modifiers (R17–R19), immutable after construction and shared by clones; null = none in the level
        private bool[][] _hidden;
        private int[][] _lockTurns;
        private TraySize[][] _size;                // R21; null = every tray is S
        private int[][] _linkOf;                   // R19: [lane][tray] → index into _links, −1 = not linked
        private TrayLink[] _links;
        private bool[] _linkBroken;                // R19, per link: both trays reached the front — they are free trays now
        private readonly int[] _lockLeft;          // per lane: placements the FRONT tray still waits for (R18)
        private readonly CapColor[] _slotColor;   // None = empty slot
        private readonly int[] _slotFilled;
        private readonly int[] _slotCapacity;      // items the tray in the slot takes before it is full (R21)
        private readonly bool[] _slotOpen;        // R20: false = an extra slot still locked (it takes no tray)
        private readonly int[] _slotLockLeft;     // R22: placements an open slot still waits for before it takes a tray
        private bool _ranOut;                     // R20: SlotsRanOut was reported for the current stall

        /// <summary>Generator mode: trays come from <see cref="PlaceTray"/>, so empty lanes are not a dead end.</summary>
        internal bool EndlessSupply { get; set; }

        public int Capacity { get; }
        /// <summary>Every slot of the bar, open or locked (R20).</summary>
        public int SlotCount => _slotColor.Length;
        public bool IsSlotOpen(int slot) => _slotOpen[slot];
        /// <summary>R22: trays that must still fly to the slots before <paramref name="slot"/> takes one; 0 = not locked.</summary>
        public int SlotLockLeft(int slot) => _slotLockLeft[slot];
        public int LockedSlotCount
        {
            get
            {
                int n = 0;
                foreach (bool open in _slotOpen) if (!open) n++;
                return n;
            }
        }
        /// <summary>R20: the open slots ran out (stall reported) and an extra slot can still be unlocked; the round waits
        /// for <see cref="UnlockSlot"/> or for the player to restart.</summary>
        public bool SlotsRanOut => _ranOut;
        public int LaneCount => _lanes.Length;
        public GameStatus Status { get; private set; }
        public LoopBelt Belt => _belt;

        public CapChaosGame(LevelDefinition level)
            : this(LoopBelt.FromDefinition(level.Loop), level.Lanes, level.Slots, level.TrayCapacity, level.ExtraSlots)
        {
            ApplyTrayModifiers(level);
            foreach (var l in level.SlotLocks)                                                  // R22; V10 reports a bad one
                if (l.Slot >= 0 && l.Slot < level.Slots && l.Turns > 0) _slotLockLeft[l.Slot] = l.Turns;
        }

        internal CapChaosGame(LoopBelt belt, IReadOnlyList<IReadOnlyList<CapColor>> lanes, int slots, int capacity, int extraSlots = 0)
        {
            _belt = belt;
            Capacity = capacity;
            _lanes = new List<CapColor>[lanes.Count];
            for (int j = 0; j < lanes.Count; j++) _lanes[j] = new List<CapColor>(lanes[j]);
            _laneHead = new int[lanes.Count];
            _lockLeft = new int[lanes.Count];
            _slotColor = new CapColor[slots + extraSlots];
            _slotFilled = new int[slots + extraSlots];
            _slotCapacity = new int[slots + extraSlots];
            _slotOpen = new bool[slots + extraSlots];
            _slotLockLeft = new int[slots + extraSlots];
            for (int s = 0; s < slots; s++) _slotOpen[s] = true;
            Status = GameStatus.Playing;
        }

        private CapChaosGame(CapChaosGame src)
        {
            _belt = src._belt.Clone();
            Capacity = src.Capacity;
            _lanes = src._lanes;                          // lane CONTENT is immutable after construction…
            _laneHead = (int[])src._laneHead.Clone();     // …only the heads move
            _hidden = src._hidden; _lockTurns = src._lockTurns; _size = src._size;
            _linkOf = src._linkOf; _links = src._links;
            _linkBroken = (bool[])src._linkBroken?.Clone();
            _lockLeft = (int[])src._lockLeft.Clone();
            _slotColor = (CapColor[])src._slotColor.Clone();
            _slotFilled = (int[])src._slotFilled.Clone();
            _slotCapacity = (int[])src._slotCapacity.Clone();
            _slotOpen = (bool[])src._slotOpen.Clone();
            _slotLockLeft = (int[])src._slotLockLeft.Clone();
            _ranOut = src._ranOut;
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
                _linkOf = NewJagged<int>();
                foreach (var row in _linkOf) for (int t = 0; t < row.Length; t++) row[t] = -1;
                var links = new List<TrayLink>();
                foreach (var l in level.Links)
                {
                    if (!InRange(l.A) || !InRange(l.B) || l.A.Lane == l.B.Lane) continue;          // V7 reports a bad one
                    _linkOf[l.A.Lane][l.A.Index] = _linkOf[l.B.Lane][l.B.Index] = links.Count;
                    links.Add(l);
                }
                _links = links.ToArray();
                _linkBroken = new bool[_links.Length];
                BreakLinks(null);                                                               // V7 forbids it, but never start stuck
            }
            if (level.TraySizes.Count > 0)
            {
                _size = NewJagged<TraySize>();
                foreach (var kv in level.TraySizes) if (InRange(kv.Key)) _size[kv.Key.Lane][kv.Key.Index] = kv.Value;
            }
            for (int j = 0; j < _lanes.Length; j++) _lockLeft[j] = LockTurnsAt(j, 0);
        }

        private T[][] NewJagged<T>()
        {
            var a = new T[_lanes.Length][];
            for (int j = 0; j < a.Length; j++) a[j] = new T[_lanes[j].Count];
            return a;
        }

        /// <summary>R21: items authored tray <paramref name="tray"/> of <paramref name="lane"/> takes — its size × <see cref="Capacity"/>.</summary>
        public int TrayCapacity(int lane, int tray) => (int)TraySizeOf(lane, tray) * Capacity;

        public TraySize TraySizeOf(int lane, int tray) =>
            _size != null && tray < _size[lane].Length && _size[lane][tray] != 0 ? _size[lane][tray] : TraySize.S;

        /// <summary>Items the tray in <paramref name="slot"/> takes before it is full; 0 = no tray there.</summary>
        public int SlotCapacity(int slot) => _slotCapacity[slot];

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
        /// <summary>Is authored tray <paramref name="tray"/> standing at the front position of its belt?</summary>
        public bool IsAtFront(int lane, int tray) => tray == _laneHead[lane] && HasFront(lane);
        private bool HasFront(int lane) => LaneRemaining(lane) > 0;

        /// <summary>Where authored tray <paramref name="tray"/> of <paramref name="lane"/> stands on its belt — 0 is the
        /// front position, −1 = it already left. Every lane moves up on its own (R7; a link does not hold a belt, R19).</summary>
        public int TrayPosition(int lane, int tray) => tray < _laneHead[lane] ? -1 : tray - _laneHead[lane];
        /// <summary>The colour of authored tray <paramref name="tray"/> of <paramref name="lane"/>, hidden or not.</summary>
        public CapColor TrayColor(int lane, int tray) => _lanes[lane][tray];
        /// <summary>R18: placements the front tray of <paramref name="lane"/> still waits for; 0 = tappable.</summary>
        public int LockLeft(int lane) => _lockLeft[lane];
        /// <summary>The lock a tray starts with when it reaches the front; 0 = not locked.</summary>
        public int LockTurns(int lane, int tray) => LockTurnsAt(lane, tray);

        /// <summary>R17: a hidden tray shows its colour once it is at the front of its lane.</summary>
        public bool IsTrayHidden(int lane, int tray)
        {
            if (_hidden == null || tray >= _hidden[lane].Length || !_hidden[lane][tray]) return false;
            return tray > _laneHead[lane];
        }

        /// <summary>The tray authored tray <paramref name="tray"/> of <paramref name="lane"/> is still linked to (R19) —
        /// false once the link broke (both reached the front) or when it never had one.</summary>
        public bool TryPartner(int lane, int tray, out TrayRef partner)
        {
            partner = default;
            int i = _linkOf != null && tray < _linkOf[lane].Length ? _linkOf[lane][tray] : -1;
            if (i < 0 || _linkBroken[i]) return false;
            var l = _links[i];
            partner = l.A.Lane == lane && l.A.Index == tray ? l.B : l.A;
            return true;
        }

        /// <summary>R19: every link whose two trays both stand at the front of their lanes breaks — from now on each is a
        /// free tray, released by its own tap.</summary>
        private void BreakLinks(List<GameFact> facts)
        {
            if (_links == null) return;
            for (int i = 0; i < _links.Length; i++)
            {
                if (_linkBroken[i]) continue;
                var l = _links[i];
                if (!IsAtFront(l.A.Lane, l.A.Index) || !IsAtFront(l.B.Lane, l.B.Index)) continue;
                _linkBroken[i] = true;
                facts?.Add(new TrayLinkBroken(l.A, l.B));
            }
        }

        // ── the one player action ───────────────────────────────────────────────────────────
        /// <summary>
        /// Release the front tray of <paramref name="lane"/> (R5–R7) to the left-most free slot; its belt steps forward.
        /// A tray still linked (R19) is not released: its link breaks only once its partner reaches the front of its own
        /// lane too — the step that brings the second one there breaks it, and both are free trays from then on. The
        /// placement counts down the locks of the trays that were already waiting at the front (R18); a tray that only
        /// just reached the front starts counting with the next placement. It counts down the locked slots (R22) once it
        /// has landed: a slot this tap opens takes trays from the next tap on.
        /// </summary>
        public TapResult Tap(int lane)
        {
            var outcome = Check(lane);
            if (outcome != TapOutcome.Accepted) return Reject(outcome);

            var frontBefore = new int[_lanes.Length];
            for (int j = 0; j < _lanes.Length; j++) frontBefore[j] = HasFront(j) ? _laneHead[j] : -1;

            var facts = new List<GameFact>();
            int head = _laneHead[lane];
            int slot = FreeSlot();
            CapColor color = _lanes[lane][head];
            int capacity = TrayCapacity(lane, head);
            facts.Add(new TrayPlaced(lane, slot, color, capacity));                             // R6
            _slotColor[slot] = color;
            _slotFilled[slot] = 0;
            _slotCapacity[slot] = capacity;
            _laneHead[lane]++;
            _lockLeft[lane] = 0;
            TickSlotLocks(1, facts);                                                            // R22
            _belt.TickRowLocks(1, facts);                                                       // R24
            facts.Add(new LaneAdvanced(lane, LaneRemaining(lane)));                             // R7: the lane moves up on its own
            if (NoTrayLeftInLanes() && _belt.AnyRowLocked) _belt.ReleaseRowLocks(facts);        // R24: never strand a locked row

            for (int j = 0; j < _lanes.Length; j++)
            {
                int front = HasFront(j) ? _laneHead[j] : -1;
                if (front < 0) continue;
                if (front != frontBefore[j])                                                    // just arrived at the front
                {
                    _lockLeft[j] = LockTurnsAt(j, front);                                       // R18: counts from the next placement
                    RevealAt(j, front, facts);                                                  // R17
                }
                else if (_lockLeft[j] > 0)
                {
                    _lockLeft[j] = Math.Max(0, _lockLeft[j] - 1);
                    facts.Add(new TrayLockTicked(j, front, _lockLeft[j]));
                }
            }
            BreakLinks(facts);                                                                  // R19

            Resolve(facts);
            return new TapResult(TapOutcome.Accepted, facts);
        }

        /// <summary>
        /// R1–R4: the oval moves one row; the bottles now in the pick zone fly to the trays waiting for them, then every
        /// feeder fills the empty spots passing its merge point. Nothing happens once the round is over.
        /// <paramref name="feed"/> false: the feeders do not join this step (the round's opening, while their queues are
        /// still on their way in on screen) — the belt still turns and picks, and a feeder that could join keeps the
        /// board from counting as quiet, so no round is judged on it.
        /// </summary>
        public IReadOnlyList<GameFact> Step(bool feed = true)
        {
            if (Status != GameStatus.Playing) return Array.Empty<GameFact>();
            var facts = new List<GameFact>();
            _belt.Advance();
            Pick(facts);
            if (feed) _belt.Feed(facts);
            Judge(facts);
            return facts;
        }

        /// <summary>
        /// Nothing can change any more without a tap: no tray in a slot has a bottle of its colour on the belt (so no
        /// pick will happen, R2), and no feeder can move (R4). The belt still turns, but turning changes nothing.
        /// </summary>
        public bool IsQuiescent
        {
            get
            {
                for (int s = 0; s < _slotColor.Length; s++)
                    if (_slotColor[s] != CapColor.None && _belt.Contains(_slotColor[s])) return false;
                return !_belt.CanFeed;
            }
        }

        /// <summary>Step until <see cref="IsQuiescent"/> or the round is over — "tap, then wait until the board is
        /// quiet" (the solver's and the generator's player). Returns every fact of those steps.</summary>
        public IReadOnlyList<GameFact> Settle()
        {
            var facts = new List<GameFact>();
            // every full turn without a change is quiescent, and each pick or feed moves a bottle for good
            int budget = (_belt.Count + _belt.FeederRemainingTotal + 2) * _belt.Rows;
            while (Status == GameStatus.Playing && !IsQuiescent && budget-- > 0) facts.AddRange(Step());
            return facts;
        }

        private bool NoTrayLeftInLanes()
        {
            for (int j = 0; j < _lanes.Length; j++) if (LaneRemaining(j) > 0) return false;
            return true;
        }

        /// <summary>Would a tap on <paramref name="lane"/> be accepted?</summary>
        private TapOutcome Check(int lane)
        {
            if (Status != GameStatus.Playing) return TapOutcome.RejectedGameOver;
            if (lane < 0 || lane >= _lanes.Length) return TapOutcome.RejectedBadLane;
            if (LaneRemaining(lane) == 0) return TapOutcome.RejectedEmptyLane;                   // R5
            if (_lockLeft[lane] > 0) return TapOutcome.RejectedLocked;                           // R18
            if (TryPartner(lane, _laneHead[lane], out _)) return TapOutcome.RejectedLinkNotReady; // R19: partner not at the front yet
            if (FreeSlot() < 0) return TapOutcome.RejectedNoFreeSlot;                            // R8
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
            var facts = new List<GameFact> { new TrayPlaced(-1, slot, color, Capacity) };
            _slotColor[slot] = color;
            _slotFilled[slot] = 0;
            _slotCapacity[slot] = Capacity;
            TickSlotLocks(1, facts);
            _belt.TickRowLocks(1, facts);
            Resolve(facts);
            return facts;
        }

        /// <summary>R22: <paramref name="placed"/> trays just flew to the slots — every locked slot counts them down.</summary>
        private void TickSlotLocks(int placed, List<GameFact> facts)
        {
            for (int s = 0; s < _slotLockLeft.Length; s++)
            {
                if (_slotLockLeft[s] == 0) continue;
                _slotLockLeft[s] = Math.Max(0, _slotLockLeft[s] - placed);
                facts.Add(new SlotLockTicked(s, _slotLockLeft[s]));
            }
        }

        private bool TakesTray(int slot) => _slotOpen[slot] && _slotLockLeft[slot] == 0 && _slotColor[slot] == CapColor.None;

        private static TapResult Reject(TapOutcome o) => new TapResult(o, Array.Empty<GameFact>());

        private int FreeSlot()                                                                  // R6: left-most free
        {
            for (int s = 0; s < _slotColor.Length; s++) if (TakesTray(s)) return s;
            return -1;
        }

        /// <summary>A tap's cascade after the placement: the trays take what is in the pick zone now (R10), then win/lose.</summary>
        private void Resolve(List<GameFact> facts)
        {
            Pick(facts);
            Judge(facts);
        }

        /// <summary>
        /// R2 + R11: every bottle in the pick zone whose colour a tray is waiting for flies to the left-most such slot.
        /// The zone is read front-most row first (the row that leaves it soonest), tracks left→right. A full tray
        /// packs at once and frees its slot (R13).
        /// </summary>
        private void Pick(List<GameFact> facts)
        {
            for (int pos = _belt.PickRows - 1; pos >= 0; pos--)
            {
                int row = _belt.RowAt(pos);
                for (int k = 0; k < _belt.Width; k++)
                {
                    var c = _belt.At(row, k);
                    if (c == CapColor.None || _belt.IsLocked(row, k)) continue;                // R24: locked bottles ride on
                    int s = SlotWaitingFor(c);
                    if (s < 0) continue;
                    _belt.Take(row, k);
                    facts.Add(new BottlePicked(row, k, s, c));
                    _slotFilled[s]++;
                    facts.Add(new BottleCapped(s, _slotFilled[s]));
                    if (_slotFilled[s] >= _slotCapacity[s])                                    // R13, R21
                    {
                        facts.Add(new TrayPacked(s, _slotColor[s]));
                        _slotColor[s] = CapColor.None;
                        _slotFilled[s] = 0;
                        _slotCapacity[s] = 0;
                    }
                }
            }
        }

        private int SlotWaitingFor(CapColor color)
        {
            for (int s = 0; s < _slotColor.Length; s++) if (_slotColor[s] == color) return s;
            return -1;
        }

        /// <summary>Would one more open slot let the round go on — are the slots full?</summary>
        private bool NeedsASlot() => FreeSlot() < 0;

        /// <summary>
        /// R20: open the left-most locked slot — the player paid for it (coins or a rewarded ad); the rules do not care
        /// which. Clears a reported stall. Nothing happens once the round is over or when every slot is open.
        /// </summary>
        public IReadOnlyList<GameFact> UnlockSlot()
        {
            if (Status != GameStatus.Playing) return Array.Empty<GameFact>();
            int s = Array.IndexOf(_slotOpen, false);
            if (s < 0) return Array.Empty<GameFact>();
            _slotOpen[s] = true;
            _ranOut = false;
            var facts = new List<GameFact> { new SlotUnlocked(s) };
            Judge(facts);
            return facts;
        }

        private void Judge(List<GameFact> facts)
        {
            bool anyTray = false;
            for (int s = 0; s < _slotColor.Length; s++) if (_slotColor[s] != CapColor.None) anyTray = true;
            if (_belt.IsEmpty && !anyTray)
            {
                Status = GameStatus.Won;                                                        // R14
                facts.Add(new LevelCompleted());
                return;
            }
            if (!IsQuiescent) { _ranOut = false; return; }                                       // something will still change
            if (LockedSlotCount > 0 && NeedsASlot())                                            // R20: offer a slot first
            {
                if (!_ranOut) facts.Add(new SlotsRanOut());
                _ranOut = true;
                return;
            }
            if (FreeSlot() < 0)                                                                 // R15
            {
                Status = GameStatus.Lost;
                facts.Add(new LevelFailed(FailReason.SlotsJammed));
                return;
            }
            // quiescent: a tap is the only thing that changes the state, so "no tap is accepted now" means "never again"
            bool anyMove = EndlessSupply;
            for (int j = 0; j < _lanes.Length && !anyMove; j++) anyMove = Check(j) == TapOutcome.Accepted;
            if (!anyMove)
            {
                Status = GameStatus.Lost;
                facts.Add(new LevelFailed(FailReason.NoMovesLeft));
            }
        }

        /// <summary>Exact state fingerprint for the solver's memo.</summary>
        public string StateKey()
        {
            var sb = new StringBuilder(64 + _belt.Rows * _belt.Width);
            for (int j = 0; j < _laneHead.Length; j++) sb.Append(_laneHead[j]).Append(':').Append(_lockLeft[j]).Append(',');
            if (_linkBroken != null) foreach (bool b in _linkBroken) sb.Append(b ? '1' : '0');
            sb.Append('#');
            for (int s = 0; s < _slotColor.Length; s++)
                sb.Append(!_slotOpen[s] ? 'x' : _slotColor[s] == CapColor.None ? '_' : CapColorCodes.ToCode(_slotColor[s])).Append(_slotFilled[s]).Append('/').Append(_slotCapacity[s])
                  .Append('l').Append(_slotLockLeft[s]);
            sb.Append('#');
            _belt.AppendKey(sb);
            return sb.ToString();
        }
    }
}
