using System;
using System.Collections.Generic;
using LitMotion;
using UnityEngine;
using B = Game.Views.DesignTokens.Board;
using M = Game.Views.DesignTokens.Motion;

namespace Game.Views
{
    /// <summary>
    /// The top conveyor (GDD R1–R4, art §4.2b): the LOOP the bottles ride and the FEEDER belts their queues come down,
    /// each a <see cref="ConveyorBeltView"/> built from the conveyor file's spline knots — the ConveyorKit way. Humble
    /// view: it is told the belt's size, the knots, which bottle stands on which (row, track) and the belt's PHASE (rows
    /// travelled, fractional) — it never sees a rule.
    /// <para><b>Rows on the loop.</b> The rows stand EVENLY along the loop spline from its first knot: row pitch = loop
    /// length / rows, and a row's place is <c>(row + phase) mod rows</c> pitches from that knot — the start of the pick
    /// zone. Track 0 is the outermost (left of travel on a clockwise loop), the last track the innermost.</para>
    /// <para><b>Feeders.</b> A feeder belt runs from its first knot (off-screen) to its last, where it lands on the
    /// loop. Its queue's head row waits where the queue's inner track is a bottle clear of the loop's outer track; the
    /// rows behind it stand <see cref="GameFeel.FeederRowPitch"/> apart back up the belt. A joining bottle walks
    /// from where it waited to its moving spot on the loop. Only the feeders the level uses are added (the conveyor's
    /// first N); the rest are not drawn.</para>
    /// <para>Knots are BOARD units, authored where they are drawn. The conveyor's SCALE sizes the belts and the
    /// bottles (a long loop that must fit the screen is drawn smaller): this transform sits at the board's origin scaled
    /// by it, and the knots are divided by it, so everything inside — belt meshes, track spacing, row pitch, bottles —
    /// is in full-size units and lands where it was authored. Hierarchy: <c>Loop/Belt</c>, <c>Loop/Bottles</c>, and one <c>Loop/Feeder{i}</c> per
    /// feeder holding its <c>Belt</c> and its <c>Queue</c> (a hair lower, so where the two belts overlap the loop's
    /// surface wins).</para>
    /// </summary>
    public sealed class LoopBeltView : MonoBehaviour
    {
        private int _rows, _width;
        private float _pick;                        // rows in the pick zone
        private float _pitch;                       // loop length per row (loop-local units)
        private float _scale = 1f;                  // board units per loop-local unit
        private ConveyorBeltView _loop;
        private GameObject _beltPrefab;
        private Transform _bottles;
        private Action<GameObject> _stamp;
        private IReadOnlyList<GameObject> _items;                       // [flavour − 1]: the item drawn for that colour
        private GameTime _time;                                          // the gameplay clock (GameSpeed); null = engine time

        /// <summary>Run on <paramref name="time"/> — the belt's drawing, the feeders and the joining bottles all move in
        /// game seconds, so they speed up with <see cref="GameTime.GameSpeed"/>.</summary>
        public void UseTime(GameTime time) => _time = time;

        private GameFeel _feel;                                          // row pitch / track spacing (GameFeel defaults until set)

        /// <summary>Space the rows and tracks by <paramref name="feel"/> (<see cref="GameFeel.FeederRowPitch"/>,
        /// <see cref="GameFeel.TrackSpacing"/>). Call before Build.</summary>
        public void UseFeel(GameFeel feel) => _feel = feel;

        private GameFeel Feel => _feel != null ? _feel : (_feel = GameFeel.CreateDefault());
        private float RowPitch => Feel.FeederRowPitch;
        private float TrackSpacing => Feel.TrackSpacing;
        private float Dt => _time != null ? _time.DeltaTime : Time.deltaTime;
        private double Now => _time != null ? _time.Now : Time.timeAsDouble;

        private GameObject[] _spots = Array.Empty<GameObject>();        // [row * width + track]
        // a bottle that just stepped on from a feeder: it slides from where it waited to its moving spot over
        // GameFeel.MergeSeconds (eased, chasing the spot as the belt carries it on), turning to face along the loop;
        // the bottles of one row go one after another, MergeStagger apart
        private sealed class Joiner { public Vector3 Start; public Quaternion StartRot; public float Wait, Age, Dist; }
        private readonly Dictionary<GameObject, Joiner> _joining = new Dictionary<GameObject, Joiner>();

        private sealed class Feeder
        {
            public int MergeAt;
            public ConveyorBeltView Belt;
            public Transform Root, Queue;
            /// <summary>+1, or −1 when the feeder's own outward side faces the loop's inward side at the join: tracks
            /// swap sides so track k of the queue lines up with track k of the loop.</summary>
            public float Side = 1f;
            public float Head;                                         // distance along the feeder of the head row
            public float Travel, TravelTarget;                         // belt travel (board units): eased toward the target
            public List<GameObject>[] Visible;                         // per track: the queued bottles drawn, head first
            public Queue<(TintFlavor color, bool masked)>[] Pending;   // per track: the rest of the queue, not drawn yet
            public float[] Shift;                                      // per track: rows still to slide forward (eased to 0)
            public int[] Joined;                                       // per track: bottles that stepped onto the loop
            public float Intro, IntroFrom;                             // run-in: rows the whole queue still stands back (→ 0)
            public int RowsJoined;                                     // queue rows on the loop (the head row = this one)
            public readonly List<Joiner> Batch = new List<Joiner>();   // the bottles that stepped on this frame (one row)
            public int BatchFrame = -1;
            public readonly Dictionary<int, TrayLockView> Locks = new Dictionary<int, TrayLockView>();   // R24: queue row → its padlock
        }
        private readonly List<Feeder> _feeders = new List<Feeder>();

        private float _phase, _rowsPerSecond;
        private double _phaseStamp;                 // game time of the last SetPhase

        /// <summary>Rows round the loop, bottles per row, rows in the pick zone; how big the belts and bottles are drawn;
        /// the loop's knots (board units, the first = the start of the pick zone); the ConveyorBelt prefab every belt is
        /// made from.</summary>
        public void Build(int rows, int width, int pickRows, float scale, IReadOnlyList<BeltNode> loop, GameObject beltPrefab,
            IReadOnlyList<GameObject> itemPrefabs, Action<GameObject> stamp)
        {
            _rows = rows; _width = width; _pick = pickRows;
            _scale = Mathf.Max(0.01f, scale);
            _stamp = stamp ?? (_ => { });
            _items = itemPrefabs ?? Array.Empty<GameObject>();
            _beltPrefab = beltPrefab;
            _spots = new GameObject[rows * width];
            transform.localPosition = Vector3.zero;
            transform.localRotation = Quaternion.identity;
            transform.localScale = Vector3.one * _scale;

            _loop = NewBelt("Belt", transform, loop, closed: true);
            _pitch = _loop.Length / rows;
            _bottles = Group("Bottles", transform);
        }

        /// <summary>A feeder joining at track position <paramref name="mergeAt"/>, running along <paramref name="path"/>
        /// (far end first, its last knot on the loop); <paramref name="tracks"/>[k] is the queue on track k, head first.
        /// <paramref name="masked"/>[k][d] (null = none): that queued bottle is drawn grey (<see cref="DesignTokens.ItemHidden"/>,
        /// GDD R23) until it steps onto the loop, where every bottle shows its colour. Its head row is placed in
        /// <see cref="Finish"/>.</summary>
        public void AddFeeder(int mergeAt, IReadOnlyList<BeltNode> path, IReadOnlyList<IReadOnlyList<TintFlavor>> tracks,
            IReadOnlyList<IReadOnlyList<bool>> masked = null)
        {
            var f = new Feeder
            {
                MergeAt = mergeAt,
                Visible = new List<GameObject>[_width], Pending = new Queue<(TintFlavor, bool)>[_width], Shift = new float[_width],
                Joined = new int[_width],
            };
            f.Root = Group("Feeder" + _feeders.Count, transform);
            f.Root.localPosition = Vector3.down * B.FeederBeltSink;
            f.Belt = NewBelt("Belt", f.Root, path, closed: false);
            f.Queue = Group("Queue", f.Root);
            for (int k = 0; k < _width; k++)
            {
                f.Visible[k] = new List<GameObject>();
                f.Pending[k] = new Queue<(TintFlavor, bool)>();
                var queue = k < tracks.Count ? tracks[k] : Array.Empty<TintFlavor>();
                var hide = masked != null && k < masked.Count ? masked[k] : null;
                for (int d = 0; d < queue.Count; d++)
                {
                    bool m = hide != null && d < hide.Count && hide[d];
                    if (d < B.FeederVisibleRows) f.Visible[k].Add(NewBottle(queue[d], f.Queue, m));
                    else f.Pending[k].Enqueue((queue[d], m));
                }
            }
            _feeders.Add(f);
        }

        /// <summary>Place every feeder's head row and lay its queue out. Call once, after the feeders.</summary>
        public void Finish()
        {
            foreach (var f in _feeders)
            {
                // the closest the queue's inner track gets to the loop's outer track while still a bottle apart
                float clear = 2f * Across(0) + TrackSpacing;
                f.Head = 0f;
                for (float s = f.Belt.Length; s > 0f; s -= HeadProbeStep)
                    if (_loop.DistanceTo(f.Belt.Sample(s).point) >= clear) { f.Head = s; break; }
                var (_, ft) = f.Belt.Sample(f.Belt.Length);
                var (_, lt) = _loop.Sample(Entrance(f) * _pitch);
                f.Side = Vector3.Dot(ConveyorBeltView.Outward(ft), ConveyorBeltView.Outward(lt)) < 0f ? -1f : 1f;
                // the run-in: the head row starts at the far end of the belt and the queue runs in behind it
                f.IntroFrom = f.Intro = Feel.FeederIntroSeconds > 0f ? Mathf.Max(0f, HeadAt(f) / RowPitch) : 0f;
                LayoutFeeder(f);
            }
            _introAge = 0f;
            _introArmed = false;
        }

        /// <summary>Start the feeders' run-in now (the board is on screen). Until then the queues wait at the far end.</summary>
        public void StartRunIn() => _introArmed = true;

        private bool _introArmed;

        private float _introAge;                                    // game seconds since the feeders' run-in began

        /// <summary>Rows short of its waiting place a queue may already start feeding: the head row runs straight on into
        /// the loop instead of stopping at the end of the run-in.</summary>
        private const float JoinBeforeInRows = 0.5f;

        private bool IntroDone => _introAge >= Feel.FeederIntroSeconds;

        /// <summary>True while the feeders' queues are still running in from the far end of their belts (false from half a
        /// row before they are in: the head row then steps on without a stop).</summary>
        public bool FeedersRunningIn
        {
            get
            {
                if (_feeders.Count == 0) return false;
                if (!_introArmed) return true;
                if (IntroDone) return false;
                foreach (var f in _feeders) if (f.Intro > JoinBeforeInRows) return true;
                return false;
            }
        }

        /// <summary>Put a bottle of <paramref name="color"/> on (row, track) — the belt as the round starts.</summary>
        public void AddBottle(int row, int track, TintFlavor color)
        {
            var go = NewBottle(color, _bottles);
            _spots[row * _width + track] = go;
            Place(go, row, track, _phase);
        }

        /// <summary>The belt has travelled <paramref name="phase"/> rows; it keeps moving at <paramref name="rowsPerSecond"/>
        /// until the next call (for a few frames at most — the fixed tick and the render frame do not line up).</summary>
        public void SetPhase(float phase, float rowsPerSecond)
        {
            _phase = phase;
            _phaseStamp = Now;
            _rowsPerSecond = rowsPerSecond;
        }

        /// <summary>The bottle on (row, track) leaves the belt (to fly to a tray). Null if the spot is empty.</summary>
        public GameObject Take(int row, int track)
        {
            int i = row * _width + track;
            var go = _spots[i];
            _spots[i] = null;
            if (go != null) _joining.Remove(go);
            return go;
        }

        /// <summary>The head bottle of <paramref name="feeder"/>'s track <paramref name="track"/> steps onto (row, track);
        /// the rest of that track's queue moves up one row.</summary>
        public void Feed(int feeder, int track, int row)
        {
            if (feeder < 0 || feeder >= _feeders.Count) return;
            var f = _feeders[feeder];
            var list = f.Visible[track];
            if (list.Count == 0) return;
            var go = list[0];
            if (!go.activeSelf) go.SetActive(true);                      // a fed bottle is always drawn
            list.RemoveAt(0);
            if (f.Pending[track].Count > 0)
            {
                var (color, masked) = f.Pending[track].Dequeue();
                list.Add(NewBottle(color, f.Queue, masked));
            }
            Unmask(go);                                                 // R23: on the loop every bottle shows its colour
            f.RowsJoined = Math.Max(f.RowsJoined, ++f.Joined[track]);
            f.Shift[track] += 1f;
            f.TravelTarget += RowPitch / _width;
            go.transform.SetParent(_bottles, false);                    // every group sits at the loop's origin (within a hair)
            go.transform.GetLocalPositionAndRotation(out var from, out var fromRot);
            var j = new Joiner { Start = from, StartRot = fromRot, Dist = (SpotLocal(row, track, _phase, out _) - from).sqrMagnitude };
            _joining[go] = j;
            _spots[row * _width + track] = go;
            // the row steps on one bottle after another, the one nearest its spot first
            if (f.BatchFrame != Time.frameCount) { f.BatchFrame = Time.frameCount; f.Batch.Clear(); }
            f.Batch.Add(j);
            f.Batch.Sort((a, b) => a.Dist.CompareTo(b.Dist));
            for (int i = 0; i < f.Batch.Count; i++) f.Batch[i].Wait = i * Feel.MergeStagger;
        }

        private void LateUpdate()
        {
            if (_rows == 0 || _loop == null) return;
            float phase = _phase + _rowsPerSecond * Mathf.Min((float)(Now - _phaseStamp), M.BeltExtrapolateMax);
            for (int row = 0; row < _rows; row++)
                for (int k = 0; k < _width; k++)
                {
                    var go = _spots[row * _width + k];
                    if (go != null) Place(go, row, k, phase);
                }
            _loop.SetTravel(phase * _pitch);
            if (_introArmed && !IntroDone) _introAge += Dt;
            float introLeft = _introArmed ? 1f - Mathf.Clamp01(_introAge / Mathf.Max(Feel.FeederIntroSeconds, 1e-4f)) : 1f;   // steady pace
            if (_feeders.Count == 0 || Feel.FeederIntroSeconds <= 0f) introLeft = 0f;
            const float rowsPerSecond = M.BeltRowsPerSecond;
            foreach (var f in _feeders)
            {
                f.Intro = f.IntroFrom * introLeft;
                // the queue moves up at the belt's pace (one row per step), so a feeding queue never stops between rows;
                // a backlog (two rows in one frame) catches up faster
                for (int k = 0; k < _width; k++)
                    f.Shift[k] = Mathf.MoveTowards(f.Shift[k], 0f, Dt * rowsPerSecond * Mathf.Max(1f, f.Shift[k]));
                float behind = (f.TravelTarget - f.Travel) / RowPitch;
                f.Travel = Mathf.MoveTowards(f.Travel, f.TravelTarget, Dt * RowPitch * rowsPerSecond * Mathf.Max(1f, behind));
                f.Belt.SetTravel(f.Travel - f.Intro * RowPitch);          // the belt surface runs in with its queue
                LayoutFeeder(f);
                LayoutRowLocks(f);
            }
        }

        // ── geometry ────────────────────────────────────────────────────────────────────────
        private const float HeadProbeStep = 0.02f;

        /// <summary>Track k's offset along the outward normal: track 0 outermost, the last track innermost.</summary>
        private float Across(int track) => ((_width - 1) * 0.5f - track) * TrackSpacing;

        /// <summary>Where the feeder's head row waits along its belt: a bottle clear of the loop, moved by the belt's
        /// <see cref="ConveyorBeltView.HeadOffset"/> (tuned on the ConveyorBelt prefab).</summary>
        private static float HeadAt(Feeder f) => f.Head + f.Belt.HeadOffset;

        /// <summary>Where a feeder's queue joins, in rows from the loop's first knot (the middle of its merge row).</summary>
        private static float Entrance(Feeder f) => f.MergeAt + 0.5f;

        private Vector3 SpotLocal(int row, int track, float phase, out Quaternion facing)
        {
            var (p, t) = _loop.Sample((Mathf.Repeat(row + phase, _rows) + 0.5f) * _pitch);
            facing = RowFacing(t);
            return p + ConveyorBeltView.Outward(t) * Across(track) + Vector3.up * _loop.SurfaceHeight;
        }

        /// <summary>Every item of a belt row faces the same way — along the belt where the row stands (sampled once per
        /// row, never per track), so a row keeps one rotation and turns as one through the bends.</summary>
        private static Quaternion RowFacing(Vector3 tangent)
        {
            tangent.y = 0f;
            return tangent.sqrMagnitude > 1e-8f ? Quaternion.LookRotation(tangent.normalized, Vector3.up) : Quaternion.identity;
        }

        private void Place(GameObject go, int row, int track, float phase)
        {
            var target = SpotLocal(row, track, phase, out var facing);
            if (_joining.TryGetValue(go, out var j))
            {
                if (j.Wait > 0f)
                {
                    j.Wait -= Dt;                                       // its turn in the row has not come yet
                    target = j.Start;
                    facing = j.StartRot;
                }
                else
                {
                    j.Age += Dt;
                    float t = Mathf.Clamp01(j.Age / Mathf.Max(Feel.MergeSeconds, 1e-4f));
                    if (t >= 1f) _joining.Remove(go);                   // on its spot: from now on it faces with its row
                    else
                    {
                        float e = EaseUtility.Evaluate(t, Feel.MergeEase);
                        target = Vector3.LerpUnclamped(j.Start, target, e);
                        facing = Quaternion.SlerpUnclamped(j.StartRot, facing, e);
                    }
                }
            }
            go.transform.SetLocalPositionAndRotation(target, facing);
        }

        private void LayoutFeeder(Feeder f)
        {
            // one facing per queue row: the tracks of a row slide together, so the row is sampled at their mean shift
            float rowShift = 0f;
            for (int k = 0; k < _width; k++) rowShift += f.Shift[k];
            rowShift /= Mathf.Max(1, _width);
            for (int k = 0; k < _width; k++)
            {
                var list = f.Visible[k];
                for (int d = 0; d < list.Count; d++)
                {
                    if (list[d] == null) continue;
                    float along = HeadAt(f) - (d + f.Shift[k] + f.Intro) * RowPitch;
                    bool onBelt = along >= 0f;                          // still beyond the belt's far end (run-in): not drawn
                    if (list[d].activeSelf != onBelt) list[d].SetActive(onBelt);
                    if (!onBelt) continue;
                    var (p, t) = f.Belt.Sample(along);
                    var (_, rowT) = f.Belt.Sample(HeadAt(f) - (d + rowShift + f.Intro) * RowPitch);
                    list[d].transform.SetLocalPositionAndRotation(
                        p + ConveyorBeltView.Outward(t) * (Across(k) * f.Side) + Vector3.up * f.Belt.SurfaceHeight, RowFacing(rowT));
                }
            }
        }

        // ── building blocks ─────────────────────────────────────────────────────────────────
        private ConveyorBeltView NewBelt(string name, Transform parent, IReadOnlyList<BeltNode> nodes, bool closed)
        {
            if (_beltPrefab == null) throw new InvalidOperationException("[LoopBeltView] no ConveyorBelt prefab");
            var go = Instantiate(_beltPrefab, parent, false);
            go.name = name;
            var belt = go.GetComponent<ConveyorBeltView>()
                       ?? throw new InvalidOperationException("[LoopBeltView] the ConveyorBelt prefab carries no ConveyorBeltView");
            var local = new BeltNode[nodes.Count];                      // board units → this (scaled) transform's units
            for (int i = 0; i < local.Length; i++)
                local[i] = new BeltNode(nodes[i].Position.x / _scale, nodes[i].Position.y / _scale, nodes[i].YRotation, nodes[i].Linear);
            belt.SetRoute(local, closed);
            foreach (var t in go.GetComponentsInChildren<Transform>(true)) _stamp(t.gameObject);
            return belt;
        }

        /// <summary>The item of <paramref name="color"/>, wrapped: the wrapper is what rides the belt and flies to a tray;
        /// the model inside is scaled so its widest footprint is <see cref="DesignTokens.Board.ItemSize"/>, centred,
        /// standing on y = 0 — whatever the prefab's own scale and pivot.</summary>
        private GameObject NewBottle(TintFlavor color, Transform parent, bool masked = false)
        {
            var go = new GameObject("Item_" + color);
            go.transform.SetParent(parent, false);
            go.transform.localRotation = Quaternion.identity;          // faces along its belt row once laid out (RowFacing)
            int i = (int)color - 1;
            var prefab = i >= 0 && i < _items.Count ? _items[i] : null;
            if (prefab != null)
            {
                var (scale, offset) = PrefabFit.Footprint(prefab, B.ItemSize);
                var model = Instantiate(prefab, go.transform, false);
                model.transform.localScale = model.transform.localScale * scale;
                model.transform.localPosition = offset;
                foreach (var t in model.GetComponentsInChildren<TokenTint>(true)) t.SetFlavor(color);
            }
            else Debug.LogWarning($"[LoopBeltView] no item prefab for {color}");
            _stamp(go);
            if (masked) Mask(go);
            return go;
        }

        // ── locked items (GDD R24) ──────────────────────────────────────────────────────────
        /// <summary>Queue row <paramref name="row"/> of <paramref name="feeder"/> is locked: <paramref name="padlock"/> (a
        /// TrayLock the board made) rides over the middle of that row, shown while the row is drawn.</summary>
        public void AttachRowLock(int feeder, int row, TrayLockView padlock)
        {
            if (feeder < 0 || feeder >= _feeders.Count || padlock == null) return;
            var f = _feeders[feeder];
            if (f.Locks.TryGetValue(row, out var old) && old != null) Destroy(old.gameObject);
            padlock.transform.SetParent(f.Queue, false);
            padlock.transform.localScale = Vector3.one * (B.RowLockScale / _scale);
            f.Locks[row] = padlock;
            LayoutRowLocks(f);
        }

        /// <summary>The padlock of a locked queue row, or null.</summary>
        public TrayLockView RowLock(int feeder, int row) =>
            feeder >= 0 && feeder < _feeders.Count && _feeders[feeder].Locks.TryGetValue(row, out var v) ? v : null;

        /// <summary>The row's lock opened: it no longer rides the queue (the caller plays its unlock).</summary>
        public TrayLockView DetachRowLock(int feeder, int row)
        {
            var padlock = RowLock(feeder, row);
            if (padlock != null) _feeders[feeder].Locks.Remove(row);
            return padlock;
        }

        /// <summary>Each padlock over the middle of its row, as the queue is laid out; hidden while the row is not drawn.</summary>
        private void LayoutRowLocks(Feeder f)
        {
            foreach (var kv in f.Locks)
            {
                if (kv.Value == null) continue;
                int d = kv.Key - f.RowsJoined;
                float along = HeadAt(f) - (d + f.Shift[0] + f.Intro) * RowPitch;
                bool drawn = d >= 0 && d < B.FeederVisibleRows && along >= 0f;
                if (kv.Value.gameObject.activeSelf != drawn) kv.Value.gameObject.SetActive(drawn);
                if (!drawn) continue;
                var (p, _) = f.Belt.Sample(along);
                kv.Value.transform.localPosition = p + Vector3.up * (f.Belt.SurfaceHeight + B.RowLockY / _scale);
            }
        }

        // ── hidden items (GDD R23) ──────────────────────────────────────────────────────────
        private static readonly int BaseMapId = Shader.PropertyToID("_BaseMap");
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private readonly HashSet<GameObject> _masked = new HashSet<GameObject>();
        private MaterialPropertyBlock _maskBlock;

        /// <summary>Draw the bottle flat <see cref="DesignTokens.ItemHidden"/> grey: its colour texture swapped for white,
        /// its base colour for the grey — a property block on each renderer, the shared materials untouched.</summary>
        private void Mask(GameObject bottle)
        {
            if (_maskBlock == null)
            {
                _maskBlock = new MaterialPropertyBlock();
                _maskBlock.SetTexture(BaseMapId, Texture2D.whiteTexture);
                _maskBlock.SetColor(BaseColorId, DesignTokens.ItemHidden);
            }
            foreach (var r in bottle.GetComponentsInChildren<Renderer>(true)) r.SetPropertyBlock(_maskBlock);
            _masked.Add(bottle);
        }

        /// <summary>The bottle shows its own colour again (its renderers' property blocks cleared).</summary>
        private void Unmask(GameObject bottle)
        {
            if (!_masked.Remove(bottle)) return;
            foreach (var r in bottle.GetComponentsInChildren<Renderer>(true)) r.SetPropertyBlock(null);
        }

        /// <summary>An empty grouping node at the loop's origin (so loop-local positions hold inside it).</summary>
        private Transform Group(string name, Transform parent)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            _stamp(go);
            return go.transform;
        }

        private int _feedReach;

        /// <summary>Gizmos only: the rows either side of an entrance a feeder looks at (R4's FeedReach).</summary>
        public void SetFeedReach(int rows) => _feedReach = rows;

        /// <summary>
        /// Scene-view markers (art §4.2b): the pick zone, and per feeder its ENTRANCE (its merge position — the rules feed
        /// the free row nearest it), the rows it looks at either side and where its head row waits. Editor only.
        /// </summary>
        private void OnDrawGizmos()
        {
            if (_loop == null || _pitch <= 0f) return;
            var g = DesignTokens.Gizmo.Marker;
            var up = Vector3.up * DesignTokens.Gizmo.Lift;
            Gizmos.matrix = transform.localToWorldMatrix;
            Vector3 At(float rows) => _loop.Sample(rows * _pitch).point + up;
            Gizmos.color = DesignTokens.Gizmo.Land;
            for (float s = 0f; s < _pick; s += 0.1f) Gizmos.DrawLine(At(s), At(s + 0.1f));
            for (int i = 0; i < _feeders.Count; i++)
            {
                var f = _feeders[i];
                float entrance = Entrance(f);
                Gizmos.color = DesignTokens.Gizmo.Reach;
                for (float s = entrance - _feedReach; s < entrance + _feedReach; s += 0.1f) Gizmos.DrawLine(At(s), At(s + 0.1f));
                for (int d = -_feedReach; d <= _feedReach; d++) Gizmos.DrawWireSphere(At(entrance + d), g * 0.4f);
                Gizmos.color = DesignTokens.Gizmo.Entrance;
                var e = At(entrance);
                Gizmos.DrawSphere(e, g);
                Gizmos.color = DesignTokens.Gizmo.Head;
                var h = f.Root.localPosition + f.Belt.Sample(HeadAt(f)).point + up;
                Gizmos.DrawWireCube(h, Vector3.one * g * 1.6f);
                Gizmos.DrawLine(h, e);
#if UNITY_EDITOR
                var m = transform.localToWorldMatrix;
                UnityEditor.Handles.Label(m.MultiplyPoint3x4(e), $"F{i} entrance {f.MergeAt} (±{_feedReach})");
                UnityEditor.Handles.Label(m.MultiplyPoint3x4(h), $"F{i} head");
#endif
            }
            Gizmos.matrix = Matrix4x4.identity;
        }
    }
}
