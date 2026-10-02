using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using LitMotion;
using UnityEngine;
using B = Game.Views.DesignTokens.Board;
using M = Game.Views.DesignTokens.Motion;

namespace Game.Views
{
    /// <summary>The generated prop prefabs (art-direction §9.1) the board is built from.</summary>
    public sealed class BoardPrefabs
    {
        /// <summary>The tray model (<c>Container_S</c>, with a <see cref="ContainerView"/>).</summary>
        public GameObject Container;
        /// <summary>The containers' shared materials (one asset for the session).</summary>
        public ContainerPalette ContainerPalette;
        public GameObject Slot, Lane, Floor;
        /// <summary>The item drawn for each colour on the belt and in the trays: <c>Items[flavour − 1]</c>.</summary>
        public GameObject[] Items;
        /// <summary>Tray modifiers (GDD R18, R19): the padlock on a locked tray, the rope between linked trays. Optional — without one, that modifier just does not draw.</summary>
        public GameObject TrayLock, TrayLink;
    }

    /// <summary>
    /// The 3D board — the oval bottle belt and its feeders (<see cref="LoopBeltView"/>), slot bar, tray conveyors —
    /// and every animation on it (GDD §8, art §6–7). Humble view: it is told WHERE things are and WHAT to animate in
    /// primitives (belt row and track, lane, slot, colour code); it never sees a rule, a level or a service. Every method is callable in any
    /// order in a gallery scene (pass a no-op stamp), so it passes both View tests.
    /// Animations return when their visible beat is done; bottle flights overlap (stagger) and a pack
    /// waits for the flights of its own slot.
    /// </summary>
    public sealed class BoardView : MonoBehaviour
    {
        private BoardPrefabs _p;
        private Action<GameObject> _stamp;
        private LoopBeltView _loop;
        private readonly List<List<GameObject>> _lanes = new List<List<GameObject>>();
        private readonly List<Renderer> _belts = new List<Renderer>();
        private readonly List<float> _beltOffset = new List<float>();
        private readonly List<Transform> _laneRoots = new List<Transform>();
        // per lane: empty positions at the front of a HELD belt (GDD R19) — tray i of a lane's list stands at
        // belt position i + gap
        private readonly List<int> _laneGap = new List<int>();
        // Visual slots. The rules free a slot the instant its tray is full; on screen that slot is still busy
        // until the box lifts off. So a rules slot is mapped to a VISUAL slot when its tray arrives: the same
        // one if it is clear, else the left-most clear one — a tap never waits behind a leaving box.
        // Trays are addressed by a caller-chosen TRAY ID, not by slot index: the rules may reuse a slot for
        // the next tray while the previous tray's bottles and box are still animating on screen.
        private sealed class TrayRec { public GameObject Go; public int Visual = -1; public readonly List<UniTask> Flights = new List<UniTask>(); }
        private readonly Dictionary<int, TrayRec> _trays = new Dictionary<int, TrayRec>();
        private GameObject[] _slotTray = Array.Empty<GameObject>();       // per visual slot
        private bool[] _slotLeaving = Array.Empty<bool>();                // per visual slot: a box is still on it
        private GameObject[] _slotTile = Array.Empty<GameObject>();       // per slot: the tile
        private GameObject[] _slotPlus = Array.Empty<GameObject>();       // per LOCKED slot: its "+" (R20); null = open
        private int _slotCount;
        private readonly Dictionary<GameObject, TrayLockView> _locks = new Dictionary<GameObject, TrayLockView>();
        private readonly List<TrayLinkView> _links = new List<TrayLinkView>();
        // per tray on screen: its model's view and the look it was last given (so an unlock or a reveal keeps the rest)
        private readonly Dictionary<GameObject, ContainerView> _containers = new Dictionary<GameObject, ContainerView>();
        private readonly Dictionary<GameObject, TrayLook> _looks = new Dictionary<GameObject, TrayLook>();
        private float _slotScale = 1f, _slotSpacing = DesignTokens.Board.ColumnSpacing;

        private static readonly int BaseMapSt = Shader.PropertyToID("_BaseMap_ST");

        /// <summary>Prefabs to build from, and how to put a new object on the board's render layer.</summary>
        public void Bind(BoardPrefabs prefabs, Action<GameObject> stamp)
        {
            _p = prefabs ?? throw new ArgumentNullException(nameof(prefabs));
            _stamp = stamp ?? (_ => { });
            var sun = new GameObject("Sun").AddComponent<Light>();
            sun.transform.SetParent(transform, false);
            sun.transform.localRotation = Quaternion.Euler(55f, -35f, 0f);
            sun.type = LightType.Directional;
            sun.intensity = 1.25f;
            sun.shadows = LightShadows.Soft;
            sun.shadowStrength = 0.55f;
        }

        /// <summary>
        /// ADR-001 (rev. 2026-10-02, orthographic): the camera stays level; the board tilts in front of it and SCALES so
        /// that <see cref="DesignTokens.Board.ViewHeight"/> board units fill the safe rect's height (taller screens add
        /// bleed above and below, never crop the sides). <paramref name="safeHalfHeight"/> is the safe rect's half height
        /// in world units (the viewport's, read live — never a constant). Call again whenever it may have changed; it is cheap.
        /// </summary>
        public void PlaceInFrontOf(Camera cam, float safeHalfHeight)
        {
            var ct = cam.transform;
            float scale = 2f * safeHalfHeight / B.ViewHeight;
            var rot = ct.rotation * Quaternion.Euler(B.TiltDegrees, 0f, 0f);
            var parentScale = transform.parent != null ? transform.parent.lossyScale.x : 1f;
            transform.localScale = Vector3.one * (scale / Mathf.Max(parentScale, 1e-6f));
            transform.SetPositionAndRotation(ct.position + ct.forward * B.ViewDistance - rot * new Vector3(0f, 0f, B.FocusZ * scale), rot);
        }

        // ── build ────────────────────────────────────────────────────────────────────────────
        /// <summary>The floor, the slot bar — <paramref name="openSlots"/> slots, then <paramref name="lockedSlots"/> locked
        /// ones on the right (R20: a dim tile with a green "+", tappable) — and <paramref name="laneCount"/> tray belts.</summary>
        public void BuildTable(int openSlots, int lockedSlots, int laneCount)
        {
            Spawn(_p.Floor, transform, Vector3.zero);

            // The slot count comes from the level (open + locked, ≤ 6). The row keeps full-size slots up to
            // SlotRowMaxWidth and shrinks uniformly beyond it — spacing, tile, tray and box alike.
            int slotCount = openSlots + lockedSlots;
            _slotCount = Mathf.Max(1, slotCount);
            _slotScale = Mathf.Min(1f, B.SlotRowMaxWidth / (_slotCount * B.ColumnSpacing));
            _slotSpacing = B.ColumnSpacing * _slotScale;
            var band = Spawn(_p.Slot, transform, new Vector3(0f, 0.002f, B.SlotZ));
            band.transform.localScale = new Vector3(_slotCount * _slotSpacing + B.SlotBandMargin * _slotScale, 0.5f, B.SlotBandDepth * _slotScale);
            band.GetComponent<TokenTint>()?.SetToken(TintToken.SlotBand);

            _slotTray = new GameObject[_slotCount];
            _slotLeaving = new bool[_slotCount];
            _slotTile = new GameObject[_slotCount];
            _slotPlus = new GameObject[_slotCount];
            for (int s = 0; s < _slotCount; s++)
            {
                var tile = Spawn(_p.Slot, transform, SlotPos(s) + Vector3.up * (0.02f - B.SlotTop));
                tile.transform.localScale = Vector3.one * _slotScale;
                _slotTile[s] = tile;
                if (s >= openSlots) Lock(s);
            }

            for (int j = 0; j < laneCount; j++)
            {
                // the belt itself is NOT tappable — each tray on it carries its own hit box (TrayOnBelt)
                var lane = Spawn(_p.Lane, transform, new Vector3(ColumnX(j, laneCount), 0f, B.LaneFrontZ));
                lane.transform.localScale = Vector3.one * B.LaneScale;
                _laneRoots.Add(lane.transform);
                var belt = lane.transform.Find("Belt");
                _belts.Add(belt != null ? belt.GetComponent<Renderer>() : null);
                _beltOffset.Add(0f);
                _lanes.Add(new List<GameObject>());
                _laneGap.Add(0);
            }
        }

        /// <summary>Slot <paramref name="slot"/> starts locked: dim tile, a green "+" on it, and a hit box for the tap.</summary>
        private void Lock(int slot)
        {
            var tile = _slotTile[slot];
            tile.GetComponent<TokenTint>()?.SetToken(TintToken.SlotLocked);
            var plus = new GameObject("Plus");
            plus.transform.SetParent(tile.transform, false);
            plus.transform.localPosition = Vector3.up * B.SlotPlusY;
            var mat = tile.GetComponent<Renderer>() != null ? tile.GetComponent<Renderer>().sharedMaterial : null;
            foreach (float yaw in new[] { 0f, 90f })
            {
                var bar = GameObject.CreatePrimitive(PrimitiveType.Cube);
                Destroy(bar.GetComponent<Collider>());
                bar.name = "Bar";
                bar.transform.SetParent(plus.transform, false);
                bar.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
                bar.transform.localScale = new Vector3(B.SlotPlusLength, B.SlotPlusThickness, B.SlotPlusWidth);
                if (mat != null) bar.GetComponent<Renderer>().sharedMaterial = mat;
                bar.AddComponent<TokenTint>().SetToken(TintToken.SlotPlus);
            }
            var hit = tile.AddComponent<BoxCollider>();
            hit.size = B.SlotHitSize;
            hit.center = new Vector3(0f, B.SlotHitSize.y * 0.5f, 0f);
            _stamp(plus);
            _slotPlus[slot] = plus;
        }

        /// <summary>Which LOCKED slot a collider is (R20). False if none.</summary>
        public bool TryGetLockedSlot(Collider c, out int slot)
        {
            for (slot = 0; slot < _slotTile.Length; slot++)
                if (_slotPlus[slot] != null && _slotTile[slot] != null && _slotTile[slot].GetComponent<Collider>() == c) return true;
            slot = -1;
            return false;
        }

        /// <summary>Locked slot <paramref name="slot"/> opens (R20): the "+" shrinks away, the tile lights up and pops.</summary>
        public async UniTask UnlockSlot(int slot)
        {
            if (slot < 0 || slot >= _slotPlus.Length || _slotPlus[slot] == null) return;
            var plus = _slotPlus[slot];
            _slotPlus[slot] = null;
            var tile = _slotTile[slot];
            var hit = tile.GetComponent<Collider>();
            if (hit != null) Destroy(hit);
            tile.GetComponent<TokenTint>()?.SetToken(TintToken.SlotEmpty);
            var pt = plus.transform;
            var tt = tile.transform;
            var baseScale = tt.localScale;
            await UniTask.WhenAll(
                LMotion.Create(1f, 0f, M.SlotUnlock).WithEase(Ease.InBack)
                    .Bind(k => { if (pt != null) pt.localScale = Vector3.one * k; }).AddTo(plus).ToUniTask(destroyCancellationToken),
                LMotion.Create(1.15f, 1f, M.SlotUnlock).WithEase(Ease.OutBack)
                    .Bind(k => { if (tt != null) tt.localScale = baseScale * k; }).AddTo(tile).ToUniTask(destroyCancellationToken));
            if (plus != null) Destroy(plus);
        }

        /// <summary>The belt loop: <paramref name="rows"/> rows of <paramref name="width"/> bottles; the first
        /// <paramref name="pickRows"/> track positions are the pick zone on its front edge, in front of the slots. Its shape
        /// is a rounded convex polygon: corners clockwise from above (corner 0 → 1 the front edge) with their radii.</summary>
        public void BuildLoop(int rows, int width, int pickRows, IReadOnlyList<float> xs, IReadOnlyList<float> zs, IReadOnlyList<float> radii)
        {
            _loop = new GameObject("Loop").AddComponent<LoopBeltView>();
            _loop.transform.SetParent(transform, false);
            _stamp(_loop.gameObject);
            _loop.Build(rows, width, pickRows, xs, zs, radii, _p.Items, _p.Lane, _stamp);
        }

        /// <summary>A feeder joining the oval at track position <paramref name="mergeAt"/>; <paramref name="tracks"/>[k] is
        /// the queue on track k, head first.</summary>
        public void AddFeeder(int mergeAt, IReadOnlyList<IReadOnlyList<TintFlavor>> tracks) => _loop.AddFeeder(mergeAt, tracks);

        /// <summary>Editor gizmos only: how far either side of its entrance a feeder looks for a free row (R4).</summary>
        public void SetFeedReach(int rows) => _loop.SetFeedReach(rows);

        /// <summary>Close the oval once every feeder is added: lay out the feeders, then its outer rail, open where each joins.</summary>
        public void FinishLoop() => _loop.Finish();

        /// <summary>A bottle on belt (row, track) as the round starts.</summary>
        public void AddBeltBottle(int row, int track, TintFlavor color) => _loop.AddBottle(row, track, color);

        /// <summary>The belt has travelled <paramref name="phase"/> rows and runs at <paramref name="rowsPerSecond"/>.</summary>
        public void SetBeltPhase(float phase, float rowsPerSecond) => _loop.SetPhase(phase, rowsPerSecond);

        /// <summary>The next bottle of feeder <paramref name="feeder"/>'s track <paramref name="track"/> steps onto belt row
        /// <paramref name="row"/>; that track of the queue moves up.</summary>
        public void FeedBottle(int feeder, int track, int row) => _loop.Feed(feeder, track, row);

        /// <summary>Append a tray to the visible tail of <paramref name="lane"/>.</summary>
        public void AddLaneTray(int lane, TrayLook look)
        {
            var list = _lanes[lane];
            if (list.Count >= B.VisibleTraysPerLane) return;
            var tray = TrayOnBelt(lane, list.Count + _laneGap[lane], look);
            list.Add(tray);
        }

        /// <summary>Tie belt trays (laneA, indexA) and (laneB, indexB) with a rope (R19). It follows both until either
        /// leaves the belt. Nothing happens if either is not on the belt or there is no rope prefab.</summary>
        public void LinkTrays(int laneA, int indexA, int laneB, int indexB)
        {
            if (_p.TrayLink == null || !TryBeltTray(laneA, indexA, out var a) || !TryBeltTray(laneB, indexB, out var b)) return;
            foreach (var l in _links) if (l != null && l.Joins(a.transform) && l.Joins(b.transform)) return;
            var go = Spawn(_p.TrayLink, transform, Vector3.zero);
            var view = go.GetComponent<TrayLinkView>() ?? go.AddComponent<TrayLinkView>();
            view.Bind(a.transform, b.transform);
            _links.Add(view);
        }

        /// <summary>A hidden tray on the belt turns out to be <paramref name="color"/> (R17): the "?" goes, the colour pops in.</summary>
        public async UniTask RevealLaneTray(int lane, int index, TintFlavor color)
        {
            if (!TryBeltTray(lane, index, out var tray)) return;
            ApplyLook(tray, new TrayLook(color, false, _looks.TryGetValue(tray, out var was) ? was.LockLabel : null));
            var t = tray.transform;
            var belt = _laneRoots[lane];
            // the pop stops the moment the tray is tapped away — PlaceTray owns its scale from then on
            await LMotion.Create(1.2f, 1f, M.TrayReveal).WithEase(Ease.OutBack)
                .Bind(k => { if (t != null && t.parent == belt) t.localScale = Vector3.one * k; }).AddTo(tray).ToUniTask(destroyCancellationToken);
        }

        /// <summary>The lock on belt tray (lane, index) now reads <paramref name="label"/> (R18).</summary>
        public UniTask SetTrayLock(int lane, int index, string label)
        {
            if (!TryBeltTray(lane, index, out var tray) || !_locks.TryGetValue(tray, out var lockView) || lockView == null) return UniTask.CompletedTask;
            return lockView.PlayTickAsync(label, destroyCancellationToken);
        }

        /// <summary>The lock on belt tray (lane, index) opens and goes away (R18).</summary>
        public UniTask UnlockTray(int lane, int index)
        {
            if (!TryBeltTray(lane, index, out var tray) || !_locks.TryGetValue(tray, out var lockView)) return UniTask.CompletedTask;
            _locks.Remove(tray);
            if (_looks.TryGetValue(tray, out var look)) ApplyLook(tray, new TrayLook(look.Color, look.Hidden));   // its own colour again
            return lockView != null ? lockView.PlayUnlockAsync(destroyCancellationToken) : UniTask.CompletedTask;
        }

        // ── animations (the fact replay) ─────────────────────────────────────────────────────
        /// <summary>
        /// The front tray of <paramref name="lane"/> becomes tray <paramref name="trayId"/> and flies to a slot:
        /// <paramref name="preferredSlot"/> if it is clear on screen, else the left-most clear one. Claimed
        /// synchronously when a slot is clear (the controller only releases a tray when
        /// <see cref="HasClearSlot"/>), so bottles can already fly to it and the next tap sees the slot taken.
        /// </summary>
        public async UniTask PlaceTray(int trayId, int lane, int preferredSlot)
        {
            var list = _lanes[lane];
            if (list.Count == 0) return;
            var tray = list[0];
            list.RemoveAt(0);
            _laneGap[lane]++;                                         // its position stays empty until the belt steps
            var hit = tray.GetComponent<Collider>();
            if (hit != null) hit.enabled = false;                     // off the belt: no longer tappable
            ReleaseLinks(tray.transform);                             // R19: the link ends when the trays fly
            if (_locks.TryGetValue(tray, out var lockView)) { _locks.Remove(tray); if (lockView != null) Destroy(lockView.gameObject); }
            tray.transform.SetParent(transform, true);
            var rec = new TrayRec { Go = tray };
            _trays[trayId] = rec;
            int v = await ClaimVisualSlot(preferredSlot);
            rec.Visual = v;
            _slotTray[v] = tray;
            var tt = tray.transform;
            var fromScale = tt.localScale;
            await UniTask.WhenAll(
                Move(tt, tt.localPosition, SlotPos(v), M.TrayToSlot, Ease.OutBack),
                LMotion.Create(fromScale, Vector3.one * _slotScale, M.TrayToSlot).WithEase(Ease.OutCubic)
                    .Bind(k => { if (tt != null) tt.localScale = k; }).AddTo(tray).ToUniTask(destroyCancellationToken));
        }

        /// <summary>The belt steps <paramref name="steps"/> positions forward into the empty front the trays that left
        /// opened; <paramref name="newTails"/> slide in at the back, in order. One call per lane per tap. A belt that is
        /// HELD (R19) simply gets no call: its trays stay put and its front stays empty.</summary>
        public async UniTask AdvanceLane(int lane, int steps, IReadOnlyList<TrayLook> newTails)
        {
            var list = _lanes[lane];
            _laneGap[lane] = Mathf.Max(0, _laneGap[lane] - steps);
            int gap = _laneGap[lane];
            foreach (var look in newTails)                             // each starts as far back as the belt moves
            {
                if (list.Count >= B.VisibleTraysPerLane) break;
                list.Add(TrayOnBelt(lane, list.Count + gap + steps, look));
            }
            var moves = new List<UniTask>();
            for (int i = 0; i < list.Count; i++)
                moves.Add(Move(list[i].transform, list[i].transform.localPosition, TrayOnLane(i + gap), M.LaneAdvance, Ease.OutCubic));
            moves.Add(ScrollBelt(lane, steps));
            await UniTask.WhenAll(moves);
        }

        /// <summary>The bottle on belt (row, track) leaves for tray <paramref name="trayId"/>'s cell <paramref name="cell"/>
        /// (0..3) after <paramref name="delay"/> seconds; the cell's cap pops up to meet it. A pack of that tray waits for
        /// the flight.</summary>
        public void FlyBottle(int row, int track, int trayId, int cell, float delay)
        {
            var bottle = _loop.Take(row, track);
            if (bottle == null) return;
            if (!_trays.TryGetValue(trayId, out var rec) || rec.Go == null) { Destroy(bottle); return; }
            rec.Flights.Add(Flight(bottle, rec.Go, Mathf.Clamp(cell, 0, 3), delay));
        }

        private async UniTask Flight(GameObject bottle, GameObject tray, int cell, float delay)
        {
            var t = bottle.transform;
            t.SetParent(transform, true);                                           // off the belt: it stops riding it
            // collected: the item goes under its cell's anchor (the container's ItemAnchors) and flies to it; with no
            // anchor, to the cell's place on the tray root. Looked up NOW — a tray filled by this very pick starts packing
            // at once, while this item may still wait out its stagger
            var anchor = _containers.TryGetValue(tray, out var view) && view != null ? view.Anchor(cell) : null;
            if (delay > 0f) await UniTask.Delay(TimeSpan.FromSeconds(delay), cancellationToken: destroyCancellationToken);
            if (t == null || tray == null) return;
            var parent = anchor != null ? anchor : tray.transform;
            t.SetParent(parent, true);
            var from = t.localPosition;
            var to = anchor != null ? Vector3.zero : CellOnTray(cell);
            // the anchor sits inside the scaled model: keep the item's size and arc in tray units
            float rel = Mathf.Max(parent.lossyScale.y / Mathf.Max(tray.transform.lossyScale.y, 1e-6f), 1e-6f);
            var fromScale = t.localScale;
            var toScale = Vector3.one * (B.ItemInTray / rel);
            float arc = M.BottleArcHeight / rel;
            var fromRot = t.localRotation;
            await LMotion.Create(0f, 1f, M.BottleFlight).WithEase(Ease.InOutQuad).Bind(k =>
            {
                if (t == null) return;
                var p = Vector3.Lerp(from, to, k);
                p.y += Mathf.Sin(k * Mathf.PI) * arc;
                t.localPosition = p;
                t.localScale = Vector3.Lerp(fromScale, toScale, k);
                t.localRotation = Quaternion.Slerp(fromRot, Quaternion.identity, k);
            }).AddTo(gameObject).ToUniTask(destroyCancellationToken);
        }

        /// <summary>Ship full tray <paramref name="trayId"/> (R13): once its items are in, the container closes its lid and
        /// flies away.</summary>
        public async UniTask PackTray(int trayId)
        {
            if (!_trays.TryGetValue(trayId, out var rec) || rec.Go == null) return;
            _trays.Remove(trayId);
            _containers.TryGetValue(rec.Go, out var container);       // kept in the table until the tray is gone
            while (rec.Visual < 0) await UniTask.Yield(PlayerLoopTiming.Update, destroyCancellationToken);   // still claiming a slot
            int v = rec.Visual;
            var tray = rec.Go;
            _slotLeaving[v] = true;                                    // busy on screen until the box lifts off
            _slotTray[v] = null;
            await UniTask.WhenAll(rec.Flights);
            await UniTask.Delay(TimeSpan.FromSeconds(M.BoxHold), cancellationToken: destroyCancellationToken);

            // the container closes its own lid, then lifts off the way the carton used to
            if (container != null) await container.CloseLidAsync(M.LidClose, B.LidDrop, B.LidTilt, destroyCancellationToken);

            _slotLeaving[v] = false;                                   // lifting off: the slot is free on screen
            var tt = tray.transform;
            var from = tt.localPosition;
            var fromScale = tt.localScale;
            var to = new Vector3(B.BoxExitX, B.BoxExitY, from.z + 1f);
            await LMotion.Create(0f, 1f, M.BoxExit).WithEase(Ease.InBack).Bind(k =>
            {
                if (tt == null) return;
                tt.localPosition = Vector3.LerpUnclamped(from, to, k);
                tt.localScale = fromScale * Mathf.Lerp(1f, 0.6f, k);
                tt.localRotation = Quaternion.Euler(0f, 0f, -15f * k);
            }).AddTo(tray).ToUniTask(destroyCancellationToken);
            _containers.Remove(tray);
            _looks.Remove(tray);
            Destroy(tray);
        }

        /// <summary>Every tray in the slots shakes — the jam that precedes the lose result (art §6).</summary>
        public async UniTask Jam()
        {
            var shakes = new List<UniTask>();
            foreach (var tray in _slotTray)
            {
                if (tray == null) continue;
                var t = tray.transform;
                var basePos = t.localPosition;
                shakes.Add(LMotion.Create(0f, 1f, 0.5f).Bind(k =>
                {
                    if (t != null) t.localPosition = basePos + Vector3.right * Mathf.Sin(k * Mathf.PI * 6f) * 0.06f * (1f - k);
                }).AddTo(tray).ToUniTask(destroyCancellationToken));
            }
            await UniTask.WhenAll(shakes);
        }

        /// <summary>The visual slot a tray for rules slot <paramref name="preferred"/> lands in: that slot if it is
        /// clear on screen, else the left-most clear one; waits only if every slot is busy.</summary>
        private async UniTask<int> ClaimVisualSlot(int preferred)
        {
            while (true)
            {
                if (VisualFree(preferred)) return preferred;
                for (int v = 0; v < _slotCount; v++) if (VisualFree(v)) return v;
                await UniTask.Yield(PlayerLoopTiming.Update, destroyCancellationToken);
            }
        }

        // claim and assignment happen with no await between them, so "no tray and no box" is the whole test
        private bool VisualFree(int v) => _slotTray[v] == null && !_slotLeaving[v] && _slotPlus[v] == null;

        /// <summary>
        /// True when at least one slot is clear ON SCREEN: no tray sits on it and no box is still being packed
        /// there. A slot stays taken for the tray's whole collect cycle — bottles flying in, the box dropping and
        /// folding — and frees the moment the box lifts off.
        /// </summary>
        public bool HasClearSlot
        {
            get
            {
                for (int v = 0; v < _slotCount; v++) if (VisualFree(v)) return true;
                return false;
            }
        }

        /// <summary>How many slots are clear ON SCREEN (see <see cref="HasClearSlot"/>) — a linked pair needs two.</summary>
        public int ClearSlotCount
        {
            get
            {
                int n = 0;
                for (int v = 0; v < _slotCount; v++) if (VisualFree(v)) n++;
                return n;
            }
        }

        // ── input support (BoardInputView) ───────────────────────────────────────────────────
        /// <summary>The lane a collider belongs to, or −1.</summary>
        /// <summary>Which belt tray a collider is: lane and position in the queue (0 = front). False if none.</summary>
        public bool TryGetBeltTray(Collider c, out int lane, out int index)
        {
            for (lane = 0; lane < _lanes.Count; lane++)
            {
                index = _lanes[lane].FindIndex(t => t != null && t.GetComponent<Collider>() == c);
                if (index >= 0) return true;
            }
            lane = index = -1;
            return false;
        }

        /// <summary>Shake belt tray <paramref name="index"/> of <paramref name="lane"/> side to side on the ground
        /// plane (a "not this one" answer). Rests at its belt position afterwards.</summary>
        public async UniTask ShakeTray(int lane, int index)
        {
            if (lane < 0 || lane >= _lanes.Count || index < 0 || index >= _lanes[lane].Count) return;
            var t = _lanes[lane][index].transform;
            var rest = TrayOnLane(index + _laneGap[lane]);
            await LMotion.Create(0f, 1f, M.TrayShake).WithEase(Ease.Linear).Bind(k =>
            {
                if (t == null) return;
                float side = Mathf.Sin(k * Mathf.PI * 2f * M.TrayShakeCycles) * B.TrayShakeAmplitude * (1f - k);
                t.localPosition = rest + Vector3.right * side;          // lane-local X = along the ground, across the belt
            }).AddTo(t.gameObject).ToUniTask(destroyCancellationToken);
            if (t != null) t.localPosition = rest;
        }

        // ── helpers ──────────────────────────────────────────────────────────────────────────
        private GameObject Spawn(GameObject prefab, Transform parent, Vector3 localPos)
        {
            var go = Instantiate(prefab, parent, false);
            go.transform.localPosition = localPos;
            _stamp(go);
            return go;
        }

        /// <summary>A tray on a lane: an empty root (what moves, scales, flies and is tapped) holding the container model,
        /// scaled to <see cref="DesignTokens.Board.ContainerSize"/> across and standing on the belt.</summary>
        private GameObject TrayOnBelt(int lane, int index, TrayLook look)
        {
            var tray = new GameObject("Tray");
            tray.transform.SetParent(_laneRoots[lane], false);
            tray.transform.localPosition = TrayOnLane(index);
            if (_p.Container != null)
            {
                var (scale, offset) = PrefabFit.Footprint(_p.Container, B.ContainerSize);
                var model = Instantiate(_p.Container, tray.transform, false);
                model.transform.localScale = model.transform.localScale * scale;
                model.transform.localPosition = offset;
                foreach (var c in model.GetComponentsInChildren<Collider>(true)) Destroy(c);   // the root's hit box is the tap target
                var view = model.GetComponent<ContainerView>() ?? model.AddComponent<ContainerView>();
                view.SetLidOpen(true);                                   // open while it waits and fills
                _containers[tray] = view;
            }
            _stamp(tray);
            ApplyLook(tray, look);
            if (look.Locked && _p.TrayLock != null)
            {
                var lockGo = Spawn(_p.TrayLock, tray.transform, Vector3.up * B.LockY);
                var lockView = lockGo.GetComponent<TrayLockView>();
                if (lockView != null) lockView.SetCount(look.LockLabel);
                _locks[tray] = lockView;
            }
            var hit = tray.AddComponent<BoxCollider>();
            hit.size = B.TrayHitSize;
            hit.center = new Vector3(0f, B.TrayHitCenterY, 0f);
            return tray;
        }

        /// <summary>The tray's look: its colour, or hidden (R17) / locked (R18) — the container swaps its material.</summary>
        private void ApplyLook(GameObject tray, TrayLook look)
        {
            _looks[tray] = look;
            if (_containers.TryGetValue(tray, out var view) && view != null)
                view.SetLook(_p.ContainerPalette != null ? _p.ContainerPalette.For(look.Color, look.Hidden, look.Locked) : null, look.Hidden);
        }

        private bool TryBeltTray(int lane, int index, out GameObject tray)
        {
            tray = lane >= 0 && lane < _lanes.Count && index >= 0 && index < _lanes[lane].Count ? _lanes[lane][index] : null;
            return tray != null;
        }

        private void ReleaseLinks(Transform tray)
        {
            for (int i = _links.Count - 1; i >= 0; i--)
            {
                var l = _links[i];
                if (l != null && !l.Joins(tray)) continue;
                _links.RemoveAt(i);
                if (l != null) l.ReleaseAsync(destroyCancellationToken).Forget();
            }
        }

        private static void Tint(GameObject go, TintFlavor color)
        {
            foreach (var t in go.GetComponentsInChildren<TokenTint>(true)) t.SetFlavor(color);
        }

        private static float ColumnX(int i, int count) => (i - (count - 1) * 0.5f) * B.LaneSpacing;
        private Vector3 SlotPos(int s) => new Vector3((s - (_slotCount - 1) * 0.5f) * _slotSpacing, B.SlotTop, B.SlotZ);
        private static Vector3 TrayOnLane(int index) => new Vector3(0f, 0.01f, -B.TrayOnBeltOffset - index * B.LanePitch);

        private static Vector3 CellOnTray(int cell)
        {
            float h = B.TrayCellHalf;
            float x = (cell % 2 == 0) ? -h : h, z = (cell < 2) ? -h : h;
            return new Vector3(x, B.TrayCupY, z);
        }

        private UniTask Move(Transform t, Vector3 from, Vector3 to, float seconds, Ease ease) =>
            LMotion.Create(from, to, seconds).WithEase(ease)
                .Bind(p => { if (t != null) t.localPosition = p; }).AddTo(t.gameObject).ToUniTask(destroyCancellationToken);

        private UniTask ScrollBelt(int lane, int steps)
        {
            var r = _belts[lane];
            if (r == null) return UniTask.CompletedTask;
            float from = _beltOffset[lane], to = from - steps;
            _beltOffset[lane] = to;
            var block = new MaterialPropertyBlock();
            return LMotion.Create(from, to, M.LaneAdvance).WithEase(Ease.OutCubic).Bind(v =>
            {
                if (r == null) return;
                r.GetPropertyBlock(block);
                block.SetVector(BaseMapSt, new Vector4(1f, 1f, 0f, v));
                r.SetPropertyBlock(block);
            }).AddTo(r.gameObject).ToUniTask(destroyCancellationToken);
        }
    }
}
