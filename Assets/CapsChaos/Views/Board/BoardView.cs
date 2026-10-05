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
        /// <summary>The tray models by size (R21): <c>Containers[size − 1]</c> = Container_S, _M, _L, _XL, each with a
        /// <see cref="ContainerView"/>. A missing size falls back to the next smaller one.</summary>
        public GameObject[] Containers;
        /// <summary>The containers' shared materials (one asset for the session).</summary>
        public ContainerPalette ContainerPalette;
        public GameObject Slot, Lane;
        /// <summary>One spline conveyor belt (<see cref="ConveyorBeltView"/>): the top loop and every feeder are one each.</summary>
        public GameObject ConveyorBelt;
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
        private readonly List<List<int>> _lanePos = new List<List<int>>();   // per lane: the belt position of each tray on it (0 = front)
        // Visual slots. The rules free a slot the instant its tray is full; on screen that slot is still busy
        // until the box lifts off. So a rules slot is mapped to a VISUAL slot when its tray arrives: the same
        // one if it is clear, else the left-most clear one — a tap never waits behind a leaving box.
        // Trays are addressed by a caller-chosen TRAY ID, not by slot index: the rules may reuse a slot for
        // the next tray while the previous tray's bottles and box are still animating on screen.
        private sealed class TrayRec
        {
            public GameObject Go; public int Visual = -1;
            public readonly List<UniTask> Flights = new List<UniTask>();
            /// <summary>Done once the tray has landed in its slot and played its impact: items fly in only after it.</summary>
            public readonly UniTaskCompletionSource Landed = new UniTaskCompletionSource();
            /// <summary>When (Time.time) the next item may launch into this tray: items go in one at a time.</summary>
            public float NextLaunch;
        }
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
        /// ADR-001 §8 (2026-10-05): the board lies FLAT on the world XZ plane and the camera looks down at it. The camera
        /// keeps its rig position and pitches down <see cref="DesignTokens.Board.CameraPitchDegrees"/>; the board puts its
        /// focus point on the view axis, <see cref="DesignTokens.Board.ViewDistance"/> away, and SCALES so that
        /// <see cref="DesignTokens.Board.ViewHeight"/> board units fill the safe rect's height (taller screens add bleed
        /// above and below, never crop the sides). Camera and board keep the very pose they had relative to each other when
        /// the camera stayed level and the board tilted, so the picture is the same. <paramref name="safeHalfHeight"/> is
        /// the safe rect's half height in world units (the viewport's, read live — never a constant). Call again whenever it
        /// may have changed; it is cheap. The screen puts the camera's own rotation back when it leaves.
        /// </summary>
        public void Frame(Camera cam, float safeHalfHeight)
        {
            var ct = cam.transform;
            ct.rotation = Quaternion.Euler(B.CameraPitchDegrees, 0f, 0f);
            float scale = 2f * safeHalfHeight / B.ViewHeight;
            var parentScale = transform.parent != null ? transform.parent.lossyScale.x : 1f;
            transform.localScale = Vector3.one * (scale / Mathf.Max(parentScale, 1e-6f));
            transform.SetPositionAndRotation(ct.position + ct.forward * B.ViewDistance - new Vector3(0f, 0f, B.FocusZ * scale), Quaternion.identity);
            if (_floor != null) _floor.FollowBoard(transform);
        }

        private BoardFloorView _floor;

        /// <summary>The scene's floor (Gameplay.unity): not spawned, not owned — every <see cref="Frame"/> puts it under
        /// the board's origin, as the spawned floor stood.</summary>
        public void UseFloor(BoardFloorView floor)
        {
            _floor = floor;
            if (_floor != null) _floor.FollowBoard(transform);
        }

        // ── build ────────────────────────────────────────────────────────────────────────────
        /// <summary>The floor, the slot bar — <paramref name="openSlots"/> slots, then <paramref name="lockedSlots"/> locked
        /// ones on the right (R20: a dim tile with a green "+", tappable) — and <paramref name="laneCount"/> tray belts.</summary>
        public void BuildTable(int openSlots, int lockedSlots, int laneCount)
        {

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
                _lanePos.Add(new List<int>());
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

        /// <summary>The belt loop: <paramref name="rows"/> rows of <paramref name="width"/> bottles spread evenly along the
        /// spline through <paramref name="loop"/> (board units, clockwise from above); the first <paramref name="pickRows"/>
        /// track positions, from its first knot on, are the pick zone in front of the slots. <paramref name="scale"/>: how
        /// big the belts and bottles are drawn.</summary>
        public void BuildLoop(int rows, int width, int pickRows, float scale, IReadOnlyList<BeltNode> loop)
        {
            _loop = new GameObject("Loop").AddComponent<LoopBeltView>();
            _loop.transform.SetParent(transform, false);
            _stamp(_loop.gameObject);
            _loop.Build(rows, width, pickRows, scale, loop, _p.ConveyorBelt, _p.Items, _stamp);
        }

        /// <summary>A feeder belt along <paramref name="path"/> (far end first, landing on the loop) whose queue joins at
        /// track position <paramref name="mergeAt"/>; <paramref name="tracks"/>[k] is the queue on track k, head first.</summary>
        public void AddFeeder(int mergeAt, IReadOnlyList<BeltNode> path, IReadOnlyList<IReadOnlyList<TintFlavor>> tracks) =>
            _loop.AddFeeder(mergeAt, path, tracks);

        /// <summary>Editor gizmos only: how far either side of its entrance a feeder looks for a free row (R4).</summary>
        public void SetFeedReach(int rows) => _loop.SetFeedReach(rows);

        /// <summary>Close the loop once every feeder is added: place each feeder's queue.</summary>
        public void FinishLoop() => _loop.Finish();

        /// <summary>A bottle on belt (row, track) as the round starts.</summary>
        public void AddBeltBottle(int row, int track, TintFlavor color) => _loop.AddBottle(row, track, color);

        /// <summary>The belt has travelled <paramref name="phase"/> rows and runs at <paramref name="rowsPerSecond"/>.</summary>
        public void SetBeltPhase(float phase, float rowsPerSecond) => _loop.SetPhase(phase, rowsPerSecond);

        /// <summary>The next bottle of feeder <paramref name="feeder"/>'s track <paramref name="track"/> steps onto belt row
        /// <paramref name="row"/>; that track of the queue moves up.</summary>
        public void FeedBottle(int feeder, int track, int row) => _loop.Feed(feeder, track, row);

        /// <summary>Append a tray to the visible tail of <paramref name="lane"/>, standing at belt <paramref name="position"/>
        /// (0 = front). The controller decides which trays are on the visible stretch of the belt.</summary>
        public void AddLaneTray(int lane, TrayLook look, int position)
        {
            _lanes[lane].Add(TrayOnBelt(lane, position, look));
            _lanePos[lane].Add(position);
        }

        /// <summary>Tie belt trays (laneA, indexA) and (laneB, indexB) with a rope (R19). It follows both until either
        /// leaves the belt. Nothing happens if either is not on the belt or there is no rope prefab.</summary>
        public void LinkTrays(int laneA, int indexA, int laneB, int indexB)
        {
            if (_p.TrayLink == null || !TryBeltTray(laneA, indexA, out var a) || !TryBeltTray(laneB, indexB, out var b)) return;
            foreach (var l in _links) if (l != null && l.Joins(a.transform) && l.Joins(b.transform)) return;
            var go = Spawn(_p.TrayLink, transform, Vector3.zero);
            var view = go.GetComponent<TrayLinkView>() ?? go.AddComponent<TrayLinkView>();
            view.Bind(a.transform, b.transform, LidOf(a), LidOf(b));
            _links.Add(view);
        }

        private Renderer LidOf(GameObject tray) => _containers.TryGetValue(tray, out var c) && c != null ? c.Lid : null;

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
            _lanePos[lane].RemoveAt(0);                               // its position stays empty until LayoutLane moves the rest
            var hit = tray.GetComponent<Collider>();
            if (hit != null) hit.enabled = false;                     // off the belt: no longer tappable
            ReleaseLinks(tray.transform);                             // R19: the link ends when the trays fly
            if (_containers.TryGetValue(tray, out var opening) && opening != null)   // closed on the belt; opens as it flies
                opening.OpenLidAsync(M.TrayToSlot, B.LidDrop, B.LidTilt, destroyCancellationToken).Forget();
            if (_locks.TryGetValue(tray, out var lockView)) { _locks.Remove(tray); if (lockView != null) Destroy(lockView.gameObject); }
            tray.transform.SetParent(transform, true);
            var rec = new TrayRec { Go = tray };
            _trays[trayId] = rec;
            try
            {
                int v = await ClaimVisualSlot(preferredSlot);
                rec.Visual = v;
                _slotTray[v] = tray;
                var tt = tray.transform;
                // scale 1 on the belt and in the slot (SKU owner, 2026-10-02) — no longer the slot row's shrink; it flies
                // on a Bézier arc (the scale lerp only matters if LaneScale is ever set back above 1)
                var p0 = tt.localPosition;
                var p2 = SlotPos(v);
                var p1 = (p0 + p2) * 0.5f + Vector3.up * M.TrayArcHeight;
                var s0 = tt.localScale;
                await LMotion.Create(0f, 1f, M.TrayToSlot).WithEase(Ease.InOutQuad).Bind(k =>
                {
                    if (tt == null) return;
                    float u = 1f - k;
                    tt.localPosition = u * u * p0 + 2f * u * k * p1 + k * k * p2;
                    tt.localScale = Vector3.Lerp(s0, Vector3.one, k);
                }).AddTo(tray).ToUniTask(destroyCancellationToken);
                if (tt != null) { tt.localPosition = p2; tt.localScale = Vector3.one; }
                if (_containers.TryGetValue(tray, out var landed) && landed != null)
                    await landed.PlayImpactAsync(M.ImpactSquash, M.ImpactRecover, M.ImpactWide, M.ImpactFlat, destroyCancellationToken);
            }
            finally { rec.Landed.TrySetResult(); }                    // items may fly in now (or the round was torn down)
        }

        /// <summary>
        /// Move every tray of <paramref name="lane"/> to its belt position — <paramref name="positions"/> holds one per
        /// tray, front first, the trays already on the belt followed by <paramref name="newTails"/>, which slide in from
        /// behind. Trays may stand apart: a held linked tray (R19) keeps a hole in front of it while the trays ahead of
        /// it move up. The belt surface scrolls as far as the furthest-moving tray.
        /// </summary>
        public async UniTask LayoutLane(int lane, IReadOnlyList<int> positions, IReadOnlyList<TrayLook> newTails)
        {
            var list = _lanes[lane];
            var pos = _lanePos[lane];
            int steps = 0;
            for (int i = 0; i < pos.Count && i < positions.Count; i++) steps = Mathf.Max(steps, pos[i] - positions[i]);
            int existing = list.Count;
            for (int k = 0; k < newTails.Count && existing + k < positions.Count; k++)   // each starts as far back as the belt moves
                list.Add(TrayOnBelt(lane, positions[existing + k] + Mathf.Max(1, steps), newTails[k]));
            pos.Clear();
            for (int i = 0; i < list.Count && i < positions.Count; i++) pos.Add(positions[i]);

            var moves = new List<UniTask>();
            for (int i = 0; i < pos.Count; i++)
                moves.Add(Move(list[i].transform, list[i].transform.localPosition, TrayOnLane(pos[i]), M.LaneAdvance, Ease.OutCubic));
            if (steps > 0) moves.Add(ScrollBelt(lane, steps));
            await UniTask.WhenAll(moves);
        }

        /// <summary>The bottle on belt (row, track) leaves for tray <paramref name="trayId"/>'s cell <paramref name="cell"/>
        /// (0 … the container's anchors − 1) after <paramref name="delay"/> seconds. When it lands, the container's count
        /// shows <paramref name="countAfter"/> (null hides it — the tray is full). A pack of that tray waits for the flight.</summary>
        public void FlyBottle(int row, int track, int trayId, int cell, float delay, string countAfter)
        {
            var bottle = _loop.Take(row, track);
            if (bottle == null) return;
            if (!_trays.TryGetValue(trayId, out var rec) || rec.Go == null) { Destroy(bottle); return; }
            int cells = _containers.TryGetValue(rec.Go, out var view) && view != null ? Mathf.Max(1, view.AnchorCount) : 4;
            rec.Flights.Add(Flight(bottle, rec, Mathf.Clamp(cell, 0, cells - 1), delay, countAfter));
        }

        /// <summary>Tray <paramref name="trayId"/>'s container shows <paramref name="count"/> (how many items it still
        /// misses, already localized); null hides it.</summary>
        public void SetTrayCount(int trayId, string count)
        {
            if (_trays.TryGetValue(trayId, out var rec) && rec.Go != null && _containers.TryGetValue(rec.Go, out var view) && view != null)
                view.SetCount(count);
        }

        private async UniTask Flight(GameObject bottle, TrayRec rec, int cell, float delay, string countAfter)
        {
            var tray = rec.Go;
            var t = bottle.transform;
            t.SetParent(transform, true);                                           // off the belt: it stops riding it
            // collected: the item goes under its cell's anchor (the container's ItemAnchors) and flies to it; with no
            // anchor, to the cell's place on the tray root. Looked up NOW — a tray filled by this very pick starts packing
            // at once, while this item may still wait out its stagger
            var anchor = _containers.TryGetValue(tray, out var view) && view != null ? view.Anchor(cell) : null;
            if (delay > 0f) await UniTask.Delay(TimeSpan.FromSeconds(delay), cancellationToken: destroyCancellationToken);
            await rec.Landed.Task;                                                  // the container takes items once it has landed
            // one at a time: wait for this tray's next launch time (items queued while it flew go in order, a stagger apart)
            float start = Mathf.Max(Time.time, rec.NextLaunch);
            rec.NextLaunch = start + M.ItemIntoBoxStagger;
            if (start > Time.time) await UniTask.Delay(TimeSpan.FromSeconds(start - Time.time), cancellationToken: destroyCancellationToken);
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
            // a quadratic Bézier: the control point halfway, 2 × arc up, so the path peaks arc above the midpoint
            var control = (from + to) * 0.5f + Vector3.up * (2f * arc);
            await LMotion.Create(0f, 1f, M.BottleFlight).WithEase(Ease.InOutQuad).Bind(k =>
            {
                if (t == null) return;
                float u = 1f - k;
                t.localPosition = u * u * from + 2f * u * k * control + k * k * to;
                t.localScale = Vector3.Lerp(fromScale, toScale, k);
                t.localRotation = Quaternion.Slerp(fromRot, Quaternion.identity, k);
            }).AddTo(gameObject).ToUniTask(destroyCancellationToken);
            if (view != null) await view.PlayCountAsync(countAfter, M.CountPop, destroyCancellationToken);   // landed: one fewer missing
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

            // the items shrink away while the container closes its own lid, then it lifts off the way the carton used to
            if (container != null)
                await UniTask.WhenAll(
                    container.CloseLidAsync(M.LidClose, B.LidDrop, B.LidTilt, destroyCancellationToken),
                    container.ShrinkItemsAsync(M.LidClose, destroyCancellationToken));

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
            var rest = TrayOnLane(index < _lanePos[lane].Count ? _lanePos[lane][index] : index);
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
        /// <summary>The container model for size <paramref name="size"/> (1 S … 4 XL); a missing one falls back to the next smaller.</summary>
        private GameObject ContainerFor(int size)
        {
            if (_p.Containers == null) return null;
            for (int i = Mathf.Clamp(size, 1, _p.Containers.Length) - 1; i >= 0; i--)
                if (_p.Containers[i] != null) return _p.Containers[i];
            return null;
        }

        private GameObject TrayOnBelt(int lane, int index, TrayLook look)
        {
            var tray = new GameObject("Tray");
            tray.transform.SetParent(_laneRoots[lane], false);
            tray.transform.localPosition = TrayOnLane(index);
            var prefab = ContainerFor(look.Size);
            if (prefab != null)
            {
                var (scale, offset) = PrefabFit.Footprint(prefab, B.ContainerSize);
                var model = Instantiate(prefab, tray.transform, false);
                model.transform.localScale = model.transform.localScale * scale;
                model.transform.localPosition = offset;
                foreach (var c in model.GetComponentsInChildren<Collider>(true)) Destroy(c);   // the root's hit box is the tap target
                var view = model.GetComponent<ContainerView>() ?? model.AddComponent<ContainerView>();
                view.SetLidOpen(false);                                  // closed while it waits on the belt; opens in the slot
                view.SetCount(null);
                _containers[tray] = view;
            }
            _stamp(tray);
            ApplyLook(tray, look);
            if (look.Locked)
            {
                // the padlock wired into the container prefab (placed per size by the art); a prefab without one gets a
                // TrayLock spawned over it
                var lockView = _containers.TryGetValue(tray, out var lockOn) && lockOn != null ? lockOn.Lock : null;
                if (lockView != null) lockView.gameObject.SetActive(true);
                else if (_p.TrayLock != null)
                {
                    float lockRise = lockOn != null ? lockOn.Rise : 0f;
                    lockView = Spawn(_p.TrayLock, tray.transform, Vector3.up * (B.LockY + lockRise)).GetComponent<TrayLockView>();
                }
                if (lockView != null) { lockView.SetCount(look.LockLabel); _locks[tray] = lockView; }
            }
            float rise = _containers.TryGetValue(tray, out var sized) && sized != null ? sized.Rise : 0f;   // taller sizes (R21)
            var hit = tray.AddComponent<BoxCollider>();
            hit.size = B.TrayHitSize + Vector3.up * rise;
            hit.center = new Vector3(0f, B.TrayHitCenterY + rise * 0.5f, 0f);
            return tray;
        }

        /// <summary>The tray's look: its colour, or hidden (R17) — the container swaps its material. A locked tray (R18)
        /// keeps its own colour (SKU owner, 2026-10-02): only its padlock shows the lock.</summary>
        private void ApplyLook(GameObject tray, TrayLook look)
        {
            _looks[tray] = look;
            if (_containers.TryGetValue(tray, out var view) && view != null)
                view.SetLook(_p.ContainerPalette != null ? _p.ContainerPalette.For(look.Color, look.Hidden, locked: false) : null, look.Hidden);
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
