using System;
using System.Collections.Generic;
using UnityEngine;
using B = Game.Views.DesignTokens.Board;
using M = Game.Views.DesignTokens.Motion;

namespace Game.Views
{
    /// <summary>
    /// The belt loop and its feeder queues (GDD R1–R4, art §4.2b). Humble view: it is told the belt's size and SHAPE,
    /// which bottle stands on which (row, track), and the belt's PHASE (rows travelled, fractional) — it never sees a rule.
    /// A row's place on the loop is <c>(row + phase) mod rows</c> track positions from the start of the pick zone (on the
    /// front edge, nearest the slots); the belt runs clockwise seen from above, right→left along the front.
    /// <para>Shape: a rounded convex polygon (<see cref="LoopPath"/>, conveyor <c>shape</c>). Rows stand EVENLY
    /// round it: the shape is scaled so its length is <c>rows</c> pitches, the pitch never under
    /// <see cref="DesignTokens.Board.LoopRowPitch"/>, and large enough that at the tightest corner the INNER track's
    /// rows stay <see cref="DesignTokens.Board.LoopInnerPitch"/> apart (a little overlap is fine) round a hole of at least
    /// <see cref="DesignTokens.Board.LoopMinHole"/>. The pick zone is centred on the front edge. The whole loop then
    /// shrinks to fit <c>LoopMaxWidth × LoopMaxDepth</c>.</para>
    /// <para>A feeder is an on-ramp: a belt that comes in from off-screen and merges into the oval, tangent to it, a
    /// little past its merge row; the oval's outer rail opens where the two belts' rails meet and the ramp's outer rail
    /// runs on into it. The queue's head waits where the two belts first touch; a joining bottle slides across onto its
    /// track. Where it comes in from follows from where it merges, so feeders read in belt order, left to right, as
    /// columns coming down from the top edge — merging on the back straight or the right bend. The belt runs UP the left
    /// side, so a queue merging low on the left bend cannot come from above: it comes in from the left edge.</para>
    /// <para>Hierarchy: <c>Loop/Belt</c> (with its rails), <c>Loop/Bottles</c> (on the oval), and one
    /// <c>Loop/Feeder{i}</c> per feeder holding its <c>Belt</c> (with its rails) and its <c>Queue</c>.</para>
    /// </summary>
    public sealed class LoopBeltView : MonoBehaviour
    {
        private int _rows, _width;
        private float _pick;                        // rows in the pick zone
        private LoopPath _path;                     // loop-local units (before the fit scale)
        private float _pitch, _origin;              // path length per row; where along the path track position 0 is
        private float _fit;                         // loop-local → board units
        private Transform _belt, _bottles;
        private Action<GameObject> _stamp;
        private IReadOnlyList<GameObject> _items;                       // [flavour − 1]: the item drawn for that colour
        private Renderer _beltRenderer;
        private MaterialPropertyBlock _block;
        private static readonly int BaseMapSt = Shader.PropertyToID("_BaseMap_ST");

        private GameObject[] _spots = Array.Empty<GameObject>();        // [row * width + track]
        // a bottle that just stepped on from a feeder: it slides from where it waited to its moving spot
        // each walks to its own spot on its own: its own start delay and pace, chasing the spot as the belt carries it on
        private sealed class Joiner { public Vector3 Pos; public float Wait, Speed, Age; }
        private readonly Dictionary<GameObject, Joiner> _joining = new Dictionary<GameObject, Joiner>();

        private sealed class Feeder
        {
            public int MergeAt;
            /// <summary>+1, or −1 for a mirrored (left-bend) feeder: its "outward" is the mirror of the right side's, so
            /// tracks and rails swap sides along the curve's own (tangent, outward) frame.</summary>
            public float Side = 1f;
            public Vector3[] Curve;                                    // the queue's centre line, far end → merged into the oval
            public float Head;                                         // arc length of the head row (where the belts first touch)
            public float[] CurveLength;                                // cumulative arc length per curve point
            public Renderer Belt;
            public Transform Root, Queue;
            public float Scroll;
            public List<GameObject>[] Visible;                         // per track: the queued bottles drawn, head first
            public Queue<TintFlavor>[] Hidden;                         // per track: the rest of the queue, not drawn yet
            public float[] Shift;                                      // per track: rows still to slide forward (eased to 0)
        }
        private readonly List<Feeder> _feeders = new List<Feeder>();
        private readonly List<Mesh> _meshes = new List<Mesh>();          // built here, so destroyed here
        private readonly List<Vector2> _railGaps = new List<Vector2>();  // [from, to) positions where the outer rail opens

        private float _phase, _phaseStamp, _rowsPerSecond;
        private Material _beltMat, _railMat;
        private Vector2[] _beltProfile;

        /// <summary>Rows round the loop, bottles per row, rows in the pick zone; the shape's corners (clockwise from above,
        /// corner 0 → 1 the front edge) and their rounding radii; materials from the straight lane prefab.</summary>
        public void Build(int rows, int width, int pickRows, IReadOnlyList<float> xs, IReadOnlyList<float> zs, IReadOnlyList<float> radii,
            IReadOnlyList<GameObject> itemPrefabs, GameObject lanePrefab, Action<GameObject> stamp)
        {
            _rows = rows; _width = width;
            _stamp = stamp ?? (_ => { });
            _items = itemPrefabs ?? Array.Empty<GameObject>();
            _spots = new GameObject[rows * width];
            _pick = pickRows;
            // scale the shape: rows evenly round it at no less than the row pitch; at the tightest corner the inner track
            // keeps its rows LoopInnerPitch apart, round a hole of at least LoopMinHole
            float innerTrack = (width - 1) * 0.5f * B.LoopTrackSpacing;                    // the inner track's distance in
            var unit = new LoopPath(xs, zs, radii, 1f);
            float scale = Mathf.Max(rows * B.LoopRowPitch / unit.Length,
                (rows * B.LoopInnerPitch + unit.Length * innerTrack / unit.MinRadius) / unit.Length,
                (innerTrack + B.LoopMinHole) / unit.MinRadius);
            _path = new LoopPath(xs, zs, radii, scale);
            _pitch = _path.Length / rows;
            _origin = (_path.FrontLength - pickRows * _pitch) * 0.5f;                       // the pick zone centred on the front edge

            float beltHalf = BeltHalfWidth, edge = beltHalf + B.LoopRailWidth;
            float wide = _path.Max.x - _path.Min.x + 2f * edge, deep = _path.Max.y - _path.Min.y + 2f * edge;
            float fit = Mathf.Min(1f, B.LoopMaxWidth / wide, B.LoopMaxDepth / deep);
            _fit = fit;
            transform.localScale = Vector3.one * fit;
            transform.localPosition = new Vector3(0f, 0f, B.LoopFrontZ + (edge - _path.Min.y) * fit);   // centred; the front edge sits on LoopFrontZ

            _beltMat = Material(lanePrefab, "Belt");
            _railMat = Material(lanePrefab, "Rail");
            _beltProfile = new[] { new Vector2(-beltHalf, 0f), new Vector2(-beltHalf, B.LoopBeltTop), new Vector2(beltHalf, B.LoopBeltTop), new Vector2(beltHalf, 0f) };
            var belt = Strip("Belt", transform, Path, 0f, _rows, _beltMat, _beltProfile, TintToken.LaneBelt, uvPerUnit: 1f);
            _belt = belt.transform;
            _beltRenderer = belt.GetComponent<Renderer>();
            Strip("RailInner", _belt, Path, 0f, _rows, _railMat, RailProfile(-(beltHalf + B.LoopRailWidth * 0.5f)), TintToken.LaneRail, 0f);
            _bottles = Group("Bottles", transform);
            // the outer rail is built in BuildOuterRail, once the feeders have cut their gaps
        }

        /// <summary>A feeder joining at track position <paramref name="mergeAt"/>; <paramref name="tracks"/>[k] is the queue
        /// on track k, head first. Its belt is laid out in <see cref="Finish"/>, once every feeder is known.</summary>
        public void AddFeeder(int mergeAt, IReadOnlyList<IReadOnlyList<TintFlavor>> tracks)
        {
            var f = new Feeder
            {
                MergeAt = mergeAt,
                Visible = new List<GameObject>[_width], Hidden = new Queue<TintFlavor>[_width], Shift = new float[_width],
            };
            f.Root = Group("Feeder" + _feeders.Count, transform);
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

        /// <summary>Lay out every feeder's belt and rails, then the oval's outer rail, open where each joins. Call once,
        /// after the feeders.</summary>
        public void Finish()
        {
            // the queues that come down from the top stand as evenly spread columns, left to right in belt order
            var fromTop = new List<Feeder>();
            foreach (var f in _feeders) fromTop.Add(f);
            fromTop.Sort((a, b) => Mathf.Repeat(EndPos(a), _rows).CompareTo(Mathf.Repeat(EndPos(b), _rows)));
            float edge = B.ViewHeight * B.SafeAspect * 0.5f - BeltHalfWidth * _fit - B.FeederEdgeMargin;
            foreach (var f in _feeders)
            {
                int i = fromTop.IndexOf(f);
                float? column = i < 0 || fromTop.Count < 2 ? (float?)null : Mathf.Lerp(-edge, edge, (float)i / (fromTop.Count - 1));
                BuildFeeder(f, column);
                LayoutFeeder(f);
            }
            BuildOuterRail();
        }

        /// <summary>Where its ramp lands on the loop: the merge position is the queue's ENTRANCE (its head row stands
        /// there, R4 feeds the free row nearest it), and the ramp lands FeederEntryRows further on — downstream for a
        /// right-side feeder, upstream for a mirrored one (its mirror image).</summary>
        private float EndPos(Feeder f)
        {
            float entrance = f.MergeAt + 0.5f;
            return entrance + B.FeederMergeRows + (Mirrored(entrance) ? -B.FeederEntryRows : B.FeederEntryRows);
        }

        /// <summary>A feeder merging on the left side (of a symmetric shape) is drawn as the mirror image of a right-side
        /// one (art §4.2b).</summary>
        private bool Mirrored(float endPos) => _path.Symmetric && Path(endPos).outward.x < -MirrorSide;

        /// <summary>The track position mirrored left↔right (a symmetric shape with the pick zone centred on its front).</summary>
        private float MirrorPos(float pos) => Mathf.Repeat(_rows + _pick - pos, _rows);

        private void BuildFeeder(Feeder f, float? column)
        {
            // An on-ramp: the queue comes in from off-screen and merges INTO the oval, tangent to it, a little past the
            // merge row. The queue's head row waits where the belts first touch, side by side.
            float join = f.MergeAt + 0.5f;
            float endPos = EndPos(f);
            BuildRamp(f, endPos, column);
            float total = f.CurveLength[f.CurveLength.Length - 1];
            float rail = B.LoopRailWidth;
            float half = BeltHalfWidth + rail * 0.5f;                                   // a rail's centre off its belt's centre
            // the closest the queue's inner track gets to the oval's outer track while still a bottle apart — the head
            // row waits there
            f.Head = ArcWhereOffsetReaches(f, join, 2f * Across(0) + B.LoopTrackSpacing);

            // Rails open wherever they would stand on the other belt or in a joining bottle's way, whatever the ramp's
            // shape: the queue's inner rail stops where it reaches the oval's belt; its outer rail stops before it crosses
            // the path a head bottle slides along to its spot; the oval's outer rail opens over the stretch the queue's
            // belt covers, on to where the bottles land.
            float innerEnd = total;
            for (float arc = 0f; arc < total; arc += RailProbeStep)
            {
                var (rp, _, rn) = CurveAt(f, arc);
                if (OffCentreLine(rp - rn * (half * f.Side)) < half - RailProbeStep) { innerEnd = arc; break; }
            }
            float outerEnd = total;
            for (float arc = f.Head; arc < total; arc += RailProbeStep)
            {
                var (rp, _, rn) = CurveAt(f, arc);
                if (InSlidePath(f, join, rp + rn * (half * f.Side))) { outerEnd = Mathf.Max(f.Head, arc - rail); break; }
            }
            // the covered stretch lies upstream of the merge on the right side, downstream of it on a mirrored feeder
            float gapFrom = endPos, gapTo = endPos;
            for (float pos = endPos - CoverSearchRows; pos < endPos + CoverSearchRows; pos += RailProbeStep)
            {
                var (op, _, on) = Path(pos);
                if (OffCurve(f, op + on * half) >= half - RailProbeStep) continue;
                gapFrom = Mathf.Min(gapFrom, pos);
                gapTo = Mathf.Max(gapTo, pos);
            }
            if (outerEnd < total)
            {
                if (f.Side > 0f) gapTo = Mathf.Max(gapTo, endPos + B.FeederLandRows);
                else gapFrom = Mathf.Min(gapFrom, endPos - B.FeederLandRows);          // the mirror image of it
            }
            _railGaps.Add(new Vector2(gapFrom, gapTo));

            (Vector3, Vector3, Vector3) Along(float s) => CurveAt(f, s);
            var beltProfile = new[] { new Vector2(-BeltHalfWidth, 0f), new Vector2(-BeltHalfWidth, B.LoopBeltTop - B.FeederBeltSink),
                new Vector2(BeltHalfWidth, B.LoopBeltTop - B.FeederBeltSink), new Vector2(BeltHalfWidth, 0f) };   // under the oval's where they overlap
            var belt = Strip("Belt", f.Root, Along, 0f, total, _beltMat, beltProfile, TintToken.LaneBelt, 1f / B.LoopRowPitch,
                segmentLength: B.LoopMeshStep);
            belt.transform.SetSiblingIndex(0);
            f.Belt = belt.GetComponent<Renderer>();
            Strip("RailOuter", belt.transform, Along, 0f, outerEnd, _railMat, RailProfile(half * f.Side), TintToken.LaneRail, 0f, segmentLength: B.LoopMeshStep);
            Strip("RailInner", belt.transform, Along, 0f, innerEnd, _railMat, RailProfile(-half * f.Side), TintToken.LaneRail, 0f, segmentLength: B.LoopMeshStep);
        }

        /// <summary>Whether a rail at <paramref name="p"/> would stand in the way of a head bottle sliding to its spot on
        /// the oval (the spot keeps moving while it slides, so the path runs a little past the merge row).</summary>
        private bool InSlidePath(Feeder f, float join, Vector3 p)
        {
            var (hp, _, hn) = CurveAt(f, f.Head);
            var (op, _, on) = Path(join + B.FeederLandRows);
            float clear = B.LoopRailWidth * 0.5f + B.BottleRadius;
            for (int k = 0; k < _width; k++)
            {
                Vector3 a = hp + hn * (Across(k) * f.Side), ab = op + on * Across(k) - a;
                float u = Mathf.Clamp01(Vector3.Dot(p - a, ab) / Mathf.Max(ab.sqrMagnitude, 1e-8f));
                if ((a + ab * u - p).magnitude < clear) return true;
            }
            return false;
        }

        /// <summary>The oval's outer rail, open where each feeder joins.</summary>
        private void BuildOuterRail()
        {
            var profile = RailProfile(BeltHalfWidth + B.LoopRailWidth * 0.5f);
            var gaps = new List<Vector2>(_railGaps);
            gaps.Sort((a, b) => a.x.CompareTo(b.x));
            if (gaps.Count == 0) { Strip("RailOuter", _belt, Path, 0f, _rows, _railMat, profile, TintToken.LaneRail, 0f); return; }
            // walk round from the end of the last gap to the start of each next gap (positions wrap)
            for (int i = 0; i < gaps.Count; i++)
            {
                float from = gaps[i].y, to = gaps[(i + 1) % gaps.Count].x;
                if (to <= from) to += _rows;
                Strip("RailOuter", _belt, Path, from, to, _railMat, profile, TintToken.LaneRail, 0f);
            }
        }

        /// <summary>
        /// The queue's centre line, ending merged into the oval at <paramref name="endPos"/>. It starts off-screen and runs
        /// in straight, then a cubic curve turns it onto the oval, landing tangent to the belt over a short run (a junction
        /// mouth about a belt wide — the rails open over it). It comes down from the top edge, upstream of the merge and out
        /// to the oval's outer side. A feeder merging on the left bend is the exact mirror image of a right-side one at the
        /// mirrored position (it meets a belt climbing that side: the product owner's call, for symmetry). Screen edges are
        /// the safe rect's (board units).
        /// </summary>
        private void BuildRamp(Feeder f, float endPos, float? column)
        {
            bool mirrored = Mirrored(endPos);
            if (mirrored) { endPos = MirrorPos(endPos); column = -column; f.Side = -1f; }
            var (m, t, n) = Path(endPos);
            var centre = transform.localPosition;
            Vector3 ToLocal(Vector3 board) => (board - centre) / _fit;
            Vector3 ToBoard(Vector3 local) => centre + local * _fit;
            var mb = ToBoard(m);
            float halfW = B.ViewHeight * B.SafeAspect * 0.5f;
            float topZ = B.FocusZ + B.ViewHeight * 0.5f / Mathf.Sin(-B.TiltDegrees * Mathf.Deg2Rad);
            float beltB = BeltHalfWidth * _fit;                                         // the queue's half width, board units
            var dir = Vector3.back;
            // its own column when it shares the top edge; alone, it comes down upstream of the merge, out to the side
            float x = column ?? mb.x + (n.x * (2f * beltB + B.FeederSwing) - t.x * B.FeederTopLead);
            var entry = new Vector3(Mathf.Clamp(x, -halfW + beltB, halfW - beltB), 0f, topZ + beltB);
            var p0 = ToLocal(entry);
            float reach = (m - p0).magnitude;
            var p1 = p0 + dir * reach * 0.5f;
            var p2 = m - t * (B.FeederMergeRun / _fit);                                  // a short run in, tangent to the belt
            // the straight lead-in: long enough that every drawn queue row has belt under it, off-screen
            float lead = (B.FeederVisibleRows + 2f) * B.LoopRowPitch;
            const int leadSamples = 8, samples = 64;
            var pts = new List<Vector3>(leadSamples + samples + 1);
            for (int i = 0; i < leadSamples; i++) pts.Add(p0 - dir * lead * (1f - (float)i / leadSamples));
            for (int i = 0; i <= samples; i++)
            {
                float u = (float)i / samples, v = 1f - u;
                pts.Add(v * v * v * p0 + 3f * v * v * u * p1 + 3f * v * u * u * p2 + u * u * u * m);
            }
            if (mirrored) for (int i = 0; i < pts.Count; i++) pts[i] = new Vector3(-pts[i].x, pts[i].y, pts[i].z);
            f.Curve = pts.ToArray();
            f.CurveLength = new float[f.Curve.Length];
            for (int i = 1; i < f.Curve.Length; i++) f.CurveLength[i] = f.CurveLength[i - 1] + (f.Curve[i] - f.Curve[i - 1]).magnitude;
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
            f.Scroll -= 1f / _width;
            go.transform.SetParent(_bottles, false);                    // every group sits at the loop's origin
            _joining[go] = new Joiner
            {
                Pos = go.transform.localPosition,
                Wait = UnityEngine.Random.Range(0f, M.BottleJoinDelayMax),
                Speed = M.BottleJoinSpeed / _fit * UnityEngine.Random.Range(1f - M.BottleJoinSpeedSpread, 1f + M.BottleJoinSpeedSpread),
            };
            _spots[row * _width + track] = go;
        }

        private void LateUpdate()
        {
            if (_rows == 0) return;
            float phase = _phase + _rowsPerSecond * Mathf.Min(Time.time - _phaseStamp, M.BeltExtrapolateMax);
            for (int row = 0; row < _rows; row++)
                for (int k = 0; k < _width; k++)
                {
                    var go = _spots[row * _width + k];
                    if (go != null) Place(go, row, k, phase);
                }
            ScrollTo(_beltRenderer, -phase);
            foreach (var f in _feeders)
            {
                if (f.Curve == null) continue;                                  // not laid out yet (Finish)
                for (int k = 0; k < _width; k++)
                    f.Shift[k] = Mathf.MoveTowards(f.Shift[k], 0f, Time.deltaTime / M.FeederStep);
                LayoutFeeder(f);
                ScrollTo(f.Belt, f.Scroll);
            }
        }

        // ── geometry ────────────────────────────────────────────────────────────────────────
        private float BeltHalfWidth => _width * B.LoopTrackSpacing * 0.5f + B.LoopBeltMargin;

        /// <summary>The centre line at track position <paramref name="pos"/> (rows, wraps): point, direction of travel,
        /// outward normal (loop-local, y = 0).</summary>
        private (Vector3 p, Vector3 tangent, Vector3 outward) Path(float pos) => _path.At(_origin + Mathf.Repeat(pos, _rows) * _pitch);

        /// <summary>Track k's offset along the outward normal: track 0 outermost, the last track innermost.</summary>
        private float Across(int track) => ((_width - 1) * 0.5f - track) * B.LoopTrackSpacing;

        private Vector3 SpotLocal(int row, int track, float phase)
        {
            var (p, _, outward) = Path(Mathf.Repeat(row + phase, _rows) + 0.5f);
            return p + outward * Across(track) + Vector3.up * B.LoopBeltTop;
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

        /// <summary>The feeder's centre line at arc length <paramref name="s"/> from its far end: point, direction of flow
        /// (toward the oval), and the side the oval's outward normal is on at the join.</summary>
        private static (Vector3 p, Vector3 tangent, Vector3 outward) CurveAt(Feeder f, float s)
        {
            var len = f.CurveLength;
            int i = 1;
            while (i < len.Length - 1 && len[i] < s) i++;
            float k = Mathf.InverseLerp(len[i - 1], len[i], s);
            var p = Vector3.Lerp(f.Curve[i - 1], f.Curve[i], k);
            var t = (f.Curve[i] - f.Curve[i - 1]).normalized;
            return (p, t, new Vector3(-t.z, 0f, t.x));                    // same handedness as the oval's (tangent, outward)
        }

        private const float RailProbeStep = 0.02f;
        /// <summary>A merge counts as on the left side when the outward normal points this far left.</summary>
        private const float MirrorSide = 0.3f;
        /// <summary>How far either side of a merge (rows) the oval's outer rail is checked for a feeder covering it.</summary>
        private const float CoverSearchRows = 10f;

        /// <summary>How far <paramref name="p"/> is from the loop's centre line.</summary>
        private float OffCentreLine(Vector3 p) => _path.Distance(p);

        /// <summary>How far <paramref name="p"/> is from the feeder's centre line.</summary>
        private static float OffCurve(Feeder f, Vector3 p)
        {
            float best = float.MaxValue;
            for (int i = 1; i < f.Curve.Length; i++)
            {
                Vector3 a = f.Curve[i - 1], ab = f.Curve[i] - a;
                float k = Mathf.Clamp01(Vector3.Dot(p - a, ab) / Mathf.Max(ab.sqrMagnitude, 1e-8f));
                best = Mathf.Min(best, (a + ab * k - p).magnitude);
            }
            return best;
        }

        /// <summary>The oval position (searched in [from, to]) whose centre point is nearest <paramref name="p"/>.</summary>
        private float NearestPosition(Vector3 p, float from, float to)
        {
            float best = from, bestD = float.MaxValue;
            for (float pos = from; pos <= to; pos += 0.05f)
            {
                float d = (Path(pos).p - p).sqrMagnitude;
                if (d < bestD) { bestD = d; best = pos; }
            }
            return best;
        }

        /// <summary>Walking the feeder from its merged end back out, the first arc length where its centre line is
        /// <paramref name="offset"/> from the oval's centre line.</summary>
        private float ArcWhereOffsetReaches(Feeder f, float near, float offset)
        {
            for (int i = f.Curve.Length - 1; i > 0; i--)
            {
                var c = f.Curve[i];
                // both sides of the merge: a mirrored feeder runs alongside the oval downstream of it
                float d = (Path(NearestPosition(c, near - CoverSearchRows, near + CoverSearchRows)).p - c).magnitude;
                if (d >= offset) return f.CurveLength[i];
            }
            return 0f;
        }

        private void LayoutFeeder(Feeder f)
        {
            for (int k = 0; k < _width; k++)
            {
                var list = f.Visible[k];
                for (int d = 0; d < list.Count; d++)
                {
                    if (list[d] == null) continue;
                    var (p, _, n) = CurveAt(f, f.Head - (d + f.Shift[k]) * B.LoopRowPitch);
                    list[d].transform.localPosition = p + n * (Across(k) * f.Side) + Vector3.up * B.LoopBeltTop;
                }
            }
        }

        // ── building blocks ─────────────────────────────────────────────────────────────────
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

        private static Material Material(GameObject lanePrefab, string child)
        {
            var t = lanePrefab != null ? lanePrefab.transform.Find(child) : null;
            var r = t != null ? t.GetComponent<Renderer>() : null;
            return r != null ? r.sharedMaterial : null;
        }

        private static Vector2[] RailProfile(float centre)
        {
            float w = B.LoopRailWidth * 0.5f, h = B.LoopRailHeight;
            return new[] { new Vector2(centre - w, 0f), new Vector2(centre - w, h), new Vector2(centre + w, h), new Vector2(centre + w, 0f) };
        }

        private void ScrollTo(Renderer r, float v)
        {
            if (r == null) return;
            _block ??= new MaterialPropertyBlock();
            r.GetPropertyBlock(_block);
            _block.SetVector(BaseMapSt, new Vector4(1f, 1f, 0f, v));
            r.SetPropertyBlock(_block);
        }

        /// <summary>
        /// Extrude <paramref name="profile"/> (x = across the path, along the outward normal; y = up) along a path from
        /// <paramref name="from"/> to <paramref name="to"/> (path units: rows on the oval, arc length on a feeder).
        /// UV: u across the profile, v = path units × <paramref name="uvPerUnit"/>.
        /// </summary>
        private GameObject Strip(string name, Transform parent, Func<float, (Vector3 p, Vector3 tangent, Vector3 outward)> path, float from, float to,
            Material material, Vector2[] profile, TintToken token, float uvPerUnit, float segmentLength = 1f / B.LoopRowSegments)
        {
            int segments = Mathf.Max(4, Mathf.CeilToInt((to - from) / segmentLength));
            int rings = segments + 1, n = profile.Length;
            var verts = new Vector3[rings * n];
            var uvs = new Vector2[rings * n];
            for (int i = 0; i < rings; i++)
            {
                float s = Mathf.Lerp(from, to, (float)i / segments);
                var (p, _, outward) = path(s);
                for (int j = 0; j < n; j++)
                {
                    verts[i * n + j] = p + outward * profile[j].x + Vector3.up * profile[j].y;
                    uvs[i * n + j] = new Vector2((float)j / (n - 1), s * uvPerUnit);
                }
            }
            var tris = new List<int>(segments * (n - 1) * 6);
            for (int i = 0; i < segments; i++)
                for (int j = 0; j < n - 1; j++)
                {
                    int a = i * n + j, b = a + 1, c = a + n, d = c + 1;
                    tris.Add(a); tris.Add(b); tris.Add(c);                 // front face up / outward (Unity winds clockwise)
                    tris.Add(b); tris.Add(d); tris.Add(c);
                }
            var mesh = new Mesh { name = name };
            _meshes.Add(mesh);
            mesh.SetVertices(verts);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            var go = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer));
            go.transform.SetParent(parent, false);
            go.GetComponent<MeshFilter>().sharedMesh = mesh;
            go.GetComponent<MeshRenderer>().sharedMaterial = material;
            go.AddComponent<TokenTint>().SetToken(token);
            _stamp(go);
            return go;
        }

        private int _feedReach;

        /// <summary>Gizmos only: the rows either side of an entrance a feeder looks at (R4's FeedReach).</summary>
        public void SetFeedReach(int rows) => _feedReach = rows;

        /// <summary>
        /// Scene-view markers per feeder (art §4.2b): the ENTRANCE (its merge position — the rules feed the free row
        /// nearest it), the rows it looks at either side, where its head row stands on its ramp, and where the ramp lands
        /// on the loop. Editor only; the game view never shows them.
        /// </summary>
        private void OnDrawGizmos()
        {
            if (_path == null) return;
            var g = DesignTokens.Gizmo.Marker;
            var up = Vector3.up * DesignTokens.Gizmo.Lift;
            Gizmos.matrix = transform.localToWorldMatrix;
            for (int i = 0; i < _feeders.Count; i++)
            {
                var f = _feeders[i];
                float entrance = f.MergeAt + 0.5f;
                Gizmos.color = DesignTokens.Gizmo.Reach;
                for (float s = entrance - _feedReach; s < entrance + _feedReach; s += 0.1f)
                    Gizmos.DrawLine(Path(s).p + up, Path(s + 0.1f).p + up);
                for (int d = -_feedReach; d <= _feedReach; d++) Gizmos.DrawWireSphere(Path(entrance + d).p + up, g * 0.4f);
                Gizmos.color = DesignTokens.Gizmo.Entrance;
                var e = Path(entrance).p + up;
                Gizmos.DrawSphere(e, g);
                Gizmos.DrawLine(Path(entrance).p, e);
                Gizmos.color = DesignTokens.Gizmo.Land;
                float land = EndPos(f);
                Gizmos.DrawWireSphere(Path(land).p + up, g);
                Vector3 h = default;
                if (f.Curve != null)
                {
                    Gizmos.color = DesignTokens.Gizmo.Head;
                    h = CurveAt(f, f.Head).p + up;
                    Gizmos.DrawWireCube(h, Vector3.one * g * 1.6f);
                    Gizmos.DrawLine(h, e);
                }
#if UNITY_EDITOR
                var m = transform.localToWorldMatrix;
                UnityEditor.Handles.Label(m.MultiplyPoint3x4(e), $"F{i} entrance {f.MergeAt} (±{_feedReach})");
                UnityEditor.Handles.Label(m.MultiplyPoint3x4(Path(land).p + up), $"F{i} lands {land:0.#}");
                if (f.Curve != null) UnityEditor.Handles.Label(m.MultiplyPoint3x4(h), $"F{i} head");
#endif
            }
            Gizmos.matrix = Matrix4x4.identity;
        }

        private void OnDestroy()
        {
            foreach (var m in _meshes) if (m != null) Destroy(m);
        }
    }
}
