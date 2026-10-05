using System;
using System.Collections.Generic;
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
    /// rows behind it stand <see cref="DesignTokens.Board.LoopRowPitch"/> apart back up the belt. A joining bottle walks
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

        private GameObject[] _spots = Array.Empty<GameObject>();        // [row * width + track]
        // a bottle that just stepped on from a feeder: it slides from where it waited to its moving spot
        // each walks to its own spot on its own: its own start delay and pace, chasing the spot as the belt carries it on
        private sealed class Joiner { public Vector3 Pos; public float Wait, Speed, Age; }
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
            public Queue<TintFlavor>[] Hidden;                         // per track: the rest of the queue, not drawn yet
            public float[] Shift;                                      // per track: rows still to slide forward (eased to 0)
        }
        private readonly List<Feeder> _feeders = new List<Feeder>();

        private float _phase, _phaseStamp, _rowsPerSecond;

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
        /// Its head row is placed in <see cref="Finish"/>.</summary>
        public void AddFeeder(int mergeAt, IReadOnlyList<BeltNode> path, IReadOnlyList<IReadOnlyList<TintFlavor>> tracks)
        {
            var f = new Feeder
            {
                MergeAt = mergeAt,
                Visible = new List<GameObject>[_width], Hidden = new Queue<TintFlavor>[_width], Shift = new float[_width],
            };
            f.Root = Group("Feeder" + _feeders.Count, transform);
            f.Root.localPosition = Vector3.down * B.FeederBeltSink;
            f.Belt = NewBelt("Belt", f.Root, path, closed: false);
            f.Queue = Group("Queue", f.Root);
            for (int k = 0; k < _width; k++)
            {
                f.Visible[k] = new List<GameObject>();
                f.Hidden[k] = new Queue<TintFlavor>();
                var queue = k < tracks.Count ? tracks[k] : Array.Empty<TintFlavor>();
                for (int d = 0; d < queue.Count; d++)
                {
                    if (d < B.FeederVisibleRows) f.Visible[k].Add(NewBottle(queue[d], f.Queue));
                    else f.Hidden[k].Enqueue(queue[d]);
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
                float clear = 2f * Across(0) + B.LoopTrackSpacing;
                f.Head = 0f;
                for (float s = f.Belt.Length; s > 0f; s -= HeadProbeStep)
                    if (_loop.DistanceTo(f.Belt.Sample(s).point) >= clear) { f.Head = s; break; }
                var (_, ft) = f.Belt.Sample(f.Belt.Length);
                var (_, lt) = _loop.Sample(Entrance(f) * _pitch);
                f.Side = Vector3.Dot(ConveyorBeltView.Outward(ft), ConveyorBeltView.Outward(lt)) < 0f ? -1f : 1f;
                LayoutFeeder(f);
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
            _phaseStamp = Time.time;
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
            list.RemoveAt(0);
            if (f.Hidden[track].Count > 0) list.Add(NewBottle(f.Hidden[track].Dequeue(), f.Queue));
            f.Shift[track] += 1f;
            f.TravelTarget += B.LoopRowPitch / _width;
            go.transform.SetParent(_bottles, false);                    // every group sits at the loop's origin (within a hair)
            _joining[go] = new Joiner
            {
                Pos = go.transform.localPosition,
                Wait = UnityEngine.Random.Range(0f, M.BottleJoinDelayMax),
                Speed = M.BottleJoinSpeed / _scale * UnityEngine.Random.Range(1f - M.BottleJoinSpeedSpread, 1f + M.BottleJoinSpeedSpread),
            };
            _spots[row * _width + track] = go;
        }

        private void LateUpdate()
        {
            if (_rows == 0 || _loop == null) return;
            float phase = _phase + _rowsPerSecond * Mathf.Min(Time.time - _phaseStamp, M.BeltExtrapolateMax);
            for (int row = 0; row < _rows; row++)
                for (int k = 0; k < _width; k++)
                {
                    var go = _spots[row * _width + k];
                    if (go != null) Place(go, row, k, phase);
                }
            _loop.SetTravel(phase * _pitch);
            foreach (var f in _feeders)
            {
                for (int k = 0; k < _width; k++)
                    f.Shift[k] = Mathf.MoveTowards(f.Shift[k], 0f, Time.deltaTime / M.FeederStep);
                f.Travel = Mathf.MoveTowards(f.Travel, f.TravelTarget, Time.deltaTime * B.LoopRowPitch / M.FeederStep);
                f.Belt.SetTravel(f.Travel);
                LayoutFeeder(f);
            }
        }

        // ── geometry ────────────────────────────────────────────────────────────────────────
        private const float HeadProbeStep = 0.02f;

        /// <summary>Track k's offset along the outward normal: track 0 outermost, the last track innermost.</summary>
        private float Across(int track) => ((_width - 1) * 0.5f - track) * B.LoopTrackSpacing;

        /// <summary>Where the feeder's head row waits along its belt: a bottle clear of the loop, moved by the belt's
        /// <see cref="ConveyorBeltView.HeadOffset"/> (tuned on the ConveyorBelt prefab).</summary>
        private static float HeadAt(Feeder f) => f.Head + f.Belt.HeadOffset;

        /// <summary>Where a feeder's queue joins, in rows from the loop's first knot (the middle of its merge row).</summary>
        private static float Entrance(Feeder f) => f.MergeAt + 0.5f;

        private Vector3 SpotLocal(int row, int track, float phase)
        {
            var (p, t) = _loop.Sample((Mathf.Repeat(row + phase, _rows) + 0.5f) * _pitch);
            return p + ConveyorBeltView.Outward(t) * Across(track) + Vector3.up * _loop.SurfaceHeight;
        }

        private void Place(GameObject go, int row, int track, float phase)
        {
            var target = SpotLocal(row, track, phase);
            if (_joining.TryGetValue(go, out var j))
            {
                float dt = Time.deltaTime;
                j.Age += dt;
                if (j.Wait > 0f) j.Wait -= dt;
                else
                {
                    float pace = j.Speed * Mathf.Clamp01(j.Age / M.BottleJoinRamp);          // a step off, then full pace
                    j.Pos = Vector3.MoveTowards(j.Pos, target, pace * dt);
                }
                if ((j.Pos - target).sqrMagnitude < 1e-6f || j.Age > M.BottleJoinMax) _joining.Remove(go);
                else target = j.Pos;
            }
            go.transform.localPosition = target;
        }

        private void LayoutFeeder(Feeder f)
        {
            for (int k = 0; k < _width; k++)
            {
                var list = f.Visible[k];
                for (int d = 0; d < list.Count; d++)
                {
                    if (list[d] == null) continue;
                    var (p, t) = f.Belt.Sample(HeadAt(f) - (d + f.Shift[k]) * B.LoopRowPitch);
                    list[d].transform.localPosition = p + ConveyorBeltView.Outward(t) * (Across(k) * f.Side) + Vector3.up * f.Belt.SurfaceHeight;
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
        private GameObject NewBottle(TintFlavor color, Transform parent)
        {
            var go = new GameObject("Item_" + color);
            go.transform.SetParent(parent, false);
            go.transform.localRotation = Quaternion.Euler(0f, UnityEngine.Random.Range(0f, 360f), 0f);
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
            return go;
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
