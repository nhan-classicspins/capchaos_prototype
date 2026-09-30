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
        public GameObject Bottle, BottleHidden, Cap, CapTray, Box, Slot, Lane, Floor;
    }

    /// <summary>
    /// The 3D board — stack, slot bar, conveyors — and every animation on it (GDD §8, art §6–7).
    /// Humble view: it is told WHERE things are and WHAT to animate in primitives (column, depth, lane,
    /// slot, colour code); it never sees a rule, a level or a service. Every method is callable in any
    /// order in a gallery scene (pass a no-op stamp), so it passes both View tests.
    /// Animations return when their visible beat is done; bottle flights overlap (stagger) and a pack
    /// waits for the flights of its own slot.
    /// </summary>
    public sealed class BoardView : MonoBehaviour
    {
        private BoardPrefabs _p;
        private Action<GameObject> _stamp;
        private Transform _stackRoot;
        private int _cols, _depth;
        private readonly Dictionary<(int x, int z), List<GameObject>> _piles = new Dictionary<(int, int), List<GameObject>>();
        private readonly List<List<GameObject>> _lanes = new List<List<GameObject>>();
        private readonly List<Renderer> _belts = new List<Renderer>();
        private readonly List<float> _beltOffset = new List<float>();
        private readonly List<Transform> _laneRoots = new List<Transform>();
        // Visual slots. The rules free a slot the instant its tray is full; on screen that slot is still busy
        // until the box lifts off. So a rules slot is mapped to a VISUAL slot when its tray arrives: the same
        // one if it is clear, else the left-most clear one — a tap never waits behind a leaving box.
        // Trays are addressed by a caller-chosen TRAY ID, not by slot index: the rules may reuse a slot for
        // the next tray while the previous tray's bottles and box are still animating on screen.
        private sealed class TrayRec { public GameObject Go; public int Visual = -1; public readonly List<UniTask> Flights = new List<UniTask>(); }
        private readonly Dictionary<int, TrayRec> _trays = new Dictionary<int, TrayRec>();
        private GameObject[] _slotTray = Array.Empty<GameObject>();       // per visual slot
        private bool[] _slotLeaving = Array.Empty<bool>();                // per visual slot: a box is still on it
        private int _slotCount;
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

        /// <summary>ADR-001 §5: the camera stays level; the board tilts in front of it.</summary>
        public void PlaceInFrontOf(Camera cam)
        {
            var ct = cam.transform;
            var rot = ct.rotation * Quaternion.Euler(B.TiltDegrees, 0f, 0f);
            transform.SetPositionAndRotation(ct.position + ct.forward * B.ViewDistance - rot * new Vector3(0f, 0f, B.FocusZ), rot);
        }

        // ── build ────────────────────────────────────────────────────────────────────────────
        public void BuildTable(int slotCount, int laneCount)
        {
            Spawn(_p.Floor, transform, Vector3.zero);

            // The slot count comes from the level (1..5). The row keeps full-size slots up to
            // SlotRowMaxWidth and shrinks uniformly beyond it — spacing, tile, tray and box alike.
            _slotCount = Mathf.Max(1, slotCount);
            _slotScale = Mathf.Min(1f, B.SlotRowMaxWidth / (_slotCount * B.ColumnSpacing));
            _slotSpacing = B.ColumnSpacing * _slotScale;
            var band = Spawn(_p.Slot, transform, new Vector3(0f, 0.002f, B.SlotZ));
            band.transform.localScale = new Vector3(_slotCount * _slotSpacing + B.SlotBandMargin * _slotScale, 0.5f, B.SlotBandDepth * _slotScale);
            band.GetComponent<TokenTint>()?.SetToken(TintToken.SlotBand);

            _slotTray = new GameObject[_slotCount];
            _slotLeaving = new bool[_slotCount];
            for (int s = 0; s < _slotCount; s++)
            {
                var tile = Spawn(_p.Slot, transform, SlotPos(s) + Vector3.up * (0.02f - B.SlotTop));
                tile.transform.localScale = Vector3.one * _slotScale;
            }

            for (int j = 0; j < laneCount; j++)
            {
                // the belt itself is NOT tappable — each tray on it carries its own hit box (TrayOnBelt)
                var lane = Spawn(_p.Lane, transform, new Vector3(ColumnX(j, laneCount), 0f, B.LaneFrontZ));
                _laneRoots.Add(lane.transform);
                var belt = lane.transform.Find("Belt");
                _belts.Add(belt != null ? belt.GetComponent<Renderer>() : null);
                _beltOffset.Add(0f);
                _lanes.Add(new List<GameObject>());
            }
        }

        /// <summary>Stack grid: <paramref name="depth"/> rows, z = 0 is the front. Auto-fits, then applies <paramref name="scale"/>.</summary>
        public void BuildStack(int cols, int depth, float scale)
        {
            _cols = cols; _depth = depth;
            _stackRoot = new GameObject("Stack").transform;
            _stackRoot.SetParent(transform, false);
            _stackRoot.localPosition = new Vector3(0f, 0f, B.StackFrontZ);
            float fit = Mathf.Min(1f, B.StackMaxWidth / (cols * B.CellPitch), B.StackMaxDepth / (depth * B.CellPitch));
            _stackRoot.localScale = Vector3.one * fit * scale;
            _stamp(_stackRoot.gameObject);
        }

        public void AddBottle(int x, int z, int height, char color, bool hidden)
        {
            var go = Spawn(hidden ? _p.BottleHidden : _p.Bottle, _stackRoot, CellLocal(x, z, height));
            go.transform.localRotation = Quaternion.Euler(0f, (x * 7 + z * 3 + height) * 37f, 0f);
            if (!hidden) Tint(go, color);
            Pile(x, z).Add(go);
        }

        /// <summary>Append a tray to the visible tail of <paramref name="lane"/>.</summary>
        public void AddLaneTray(int lane, char color)
        {
            var list = _lanes[lane];
            if (list.Count >= B.VisibleTraysPerLane) return;
            var tray = TrayOnBelt(lane, list.Count, color);
            list.Add(tray);
        }

        // ── animations (the fact replay) ─────────────────────────────────────────────────────
        /// <summary>
        /// The front tray of <paramref name="lane"/> becomes tray <paramref name="trayId"/> and flies to a slot:
        /// <paramref name="preferredSlot"/> if it is clear on screen, else the left-most clear one (a leaving box
        /// never makes a tap wait). Registered synchronously, so bottles can already fly to it.
        /// </summary>
        public async UniTask PlaceTray(int trayId, int lane, int preferredSlot)
        {
            var list = _lanes[lane];
            if (list.Count == 0) return;
            var tray = list[0];
            list.RemoveAt(0);
            var hit = tray.GetComponent<Collider>();
            if (hit != null) hit.enabled = false;                     // off the belt: no longer tappable
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

        /// <summary>The belt steps one tray forward; <paramref name="newTail"/> ('\0' = none) slides in at the back.</summary>
        public async UniTask AdvanceLane(int lane, char newTail)
        {
            var list = _lanes[lane];
            if (newTail != '\0' && list.Count < B.VisibleTraysPerLane)
            {
                var tray = TrayOnBelt(lane, list.Count + 1, newTail);
                list.Add(tray);
            }
            var moves = new List<UniTask>();
            for (int i = 0; i < list.Count; i++)
                moves.Add(Move(list[i].transform, list[i].transform.localPosition, TrayOnLane(i), M.LaneAdvance, Ease.OutCubic));
            moves.Add(ScrollBelt(lane));
            await UniTask.WhenAll(moves);
        }

        /// <summary>A bottle leaves the stack for tray <paramref name="trayId"/>'s cell <paramref name="cell"/> (0..3);
        /// the cell's cap pops up to meet it. Returns after the stagger — the flight continues.</summary>
        public async UniTask FlyBottle(int x, int z, int trayId, int cell)
        {
            var pile = Pile(x, z);
            if (pile.Count == 0 || !_trays.TryGetValue(trayId, out var rec) || rec.Go == null) return;
            var bottle = pile[0];
            pile.RemoveAt(0);
            rec.Flights.Add(Flight(bottle, rec.Go, Mathf.Clamp(cell, 0, 3)));
            await UniTask.Delay(TimeSpan.FromSeconds(M.BottleStagger), cancellationToken: destroyCancellationToken);
        }

        private async UniTask Flight(GameObject bottle, GameObject tray, int cell)
        {
            var t = bottle.transform;
            t.SetParent(tray.transform, true);
            var from = t.localPosition;
            var to = CellOnTray(cell);
            var fromScale = t.localScale;
            var fromRot = t.localRotation;
            var cap = tray.transform.Find("Cap_" + cell);
            Vector3 capFrom = cap != null ? cap.localPosition : Vector3.zero;
            var capTo = to + Vector3.up * B.CapOnNeckY;
            await LMotion.Create(0f, 1f, M.BottleFlight).WithEase(Ease.InOutQuad).Bind(k =>
            {
                if (t == null) return;
                var p = Vector3.Lerp(from, to, k);
                p.y += Mathf.Sin(k * Mathf.PI) * M.BottleArcHeight;
                t.localPosition = p;
                t.localScale = Vector3.Lerp(fromScale, Vector3.one, k);
                t.localRotation = Quaternion.Slerp(fromRot, Quaternion.identity, k);
                if (cap != null)
                {
                    var c = Vector3.Lerp(capFrom, capTo, k);
                    c.y += Mathf.Sin(k * Mathf.PI) * M.BottleArcHeight * 0.5f;
                    cap.localPosition = c;
                    cap.localScale = Vector3.one * Mathf.Lerp(B.CapOnTrayScale, 1f, k);
                    cap.localRotation = Quaternion.Euler(0f, k * 360f, 0f);
                }
            }).AddTo(gameObject).ToUniTask(destroyCancellationToken);
        }

        /// <summary>The pile at (x, z) settles one level (R3) with a small squash.</summary>
        public async UniTask DropPile(int x, int z)
        {
            var pile = Pile(x, z);
            var moves = new List<UniTask>();
            for (int h = 0; h < pile.Count; h++)
                moves.Add(Move(pile[h].transform, pile[h].transform.localPosition, CellLocal(x, z, h), M.StackDrop, Ease.OutBack));
            await UniTask.WhenAll(moves);
        }

        /// <summary>The hidden (rainbow) ground bottle at (x, z) turns out to be <paramref name="color"/> (R4).</summary>
        public async UniTask Reveal(int x, int z, char color)
        {
            var pile = Pile(x, z);
            if (pile.Count == 0) return;
            var old = pile[0];
            var go = Spawn(_p.Bottle, _stackRoot, old.transform.localPosition);
            go.transform.localRotation = old.transform.localRotation;
            Tint(go, color);
            pile[0] = go;
            Destroy(old);
            var t = go.transform;
            await LMotion.Create(1.25f, 1f, M.Reveal).WithEase(Ease.OutBack)
                .Bind(s => { if (t != null) t.localScale = Vector3.one * s; }).AddTo(go).ToUniTask(destroyCancellationToken);
        }

        /// <summary>Box full tray <paramref name="trayId"/> and ship it (R13): drop, fold, tape, fly away.</summary>
        public async UniTask PackTray(int trayId, char color)
        {
            if (!_trays.TryGetValue(trayId, out var rec) || rec.Go == null) return;
            _trays.Remove(trayId);
            while (rec.Visual < 0) await UniTask.Yield(PlayerLoopTiming.Update, destroyCancellationToken);   // still claiming a slot
            int v = rec.Visual;
            var tray = rec.Go;
            _slotLeaving[v] = true;                                    // busy on screen until the box lifts off
            _slotTray[v] = null;
            await UniTask.WhenAll(rec.Flights);
            await UniTask.Delay(TimeSpan.FromSeconds(M.BoxHold), cancellationToken: destroyCancellationToken);

            var box = Spawn(_p.Box, transform, SlotPos(v) + Vector3.up * 3f);
            box.transform.localScale = Vector3.one * _slotScale;
            Tint(box, color);
            await Move(box.transform, box.transform.localPosition, SlotPos(v), M.BoxDrop, Ease.OutBounce);
            tray.transform.SetParent(box.transform, true);

            var folds = new List<UniTask>();
            foreach (var name in new[] { "Flap_Left", "Flap_Right", "Flap_Front", "Flap_Back" })
            {
                var pivot = box.transform.Find(name);
                if (pivot == null) continue;
                var open = pivot.localRotation;
                var closed = open * Quaternion.Euler(125f, 0f, 0f);   // −35° open lean → +90° closed (ArtGenerator)
                folds.Add(LMotion.Create(0f, 1f, M.BoxFlaps).WithEase(Ease.InOutQuad)
                    .Bind(k => { if (pivot != null) pivot.localRotation = Quaternion.Slerp(open, closed, k); }).AddTo(box).ToUniTask(destroyCancellationToken));
            }
            await UniTask.WhenAll(folds);
            var tape = box.transform.Find("Tape");
            if (tape != null) tape.gameObject.SetActive(true);

            _slotLeaving[v] = false;                                   // lifting off: the slot is free on screen
            var bt = box.transform;
            var from = bt.localPosition;
            var to = new Vector3(B.BoxExitX, B.BoxExitY, from.z + 1f);
            await LMotion.Create(0f, 1f, M.BoxExit).WithEase(Ease.InBack).Bind(k =>
            {
                if (bt == null) return;
                bt.localPosition = Vector3.LerpUnclamped(from, to, k);
                bt.localScale = Vector3.one * (_slotScale * Mathf.Lerp(1f, 0.6f, k));
                bt.localRotation = Quaternion.Euler(0f, 0f, -15f * k);
            }).AddTo(box).ToUniTask(destroyCancellationToken);
            Destroy(box);
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
        private bool VisualFree(int v) => _slotTray[v] == null && !_slotLeaving[v];

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
            var rest = TrayOnLane(index);
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

        private GameObject TrayOnBelt(int lane, int index, char color)
        {
            var tray = Spawn(_p.CapTray, _laneRoots[lane], TrayOnLane(index));
            Tint(tray, color);
            var hit = tray.AddComponent<BoxCollider>();
            hit.size = B.TrayHitSize;
            hit.center = new Vector3(0f, B.TrayHitCenterY, 0f);
            return tray;
        }

        private static void Tint(GameObject go, char color)
        {
            foreach (var t in go.GetComponentsInChildren<TokenTint>(true)) t.SetFlavor(color);
        }

        private List<GameObject> Pile(int x, int z)
        {
            if (!_piles.TryGetValue((x, z), out var list)) _piles[(x, z)] = list = new List<GameObject>();
            return list;
        }

        private Vector3 CellLocal(int x, int z, int h) =>
            new Vector3((x - (_cols - 1) * 0.5f) * B.CellPitch, h * B.LayerHeight, z * B.CellPitch);

        private static float ColumnX(int i, int count) => (i - (count - 1) * 0.5f) * B.ColumnSpacing;
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

        private UniTask ScrollBelt(int lane)
        {
            var r = _belts[lane];
            if (r == null) return UniTask.CompletedTask;
            float from = _beltOffset[lane], to = from - 1f;
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
