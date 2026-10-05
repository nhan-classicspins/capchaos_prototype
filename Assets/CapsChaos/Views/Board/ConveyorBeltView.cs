using System;
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Splines;
using SplineMesh = SplineMeshTools.Core.SplineMesh;

namespace Game.Views
{
    /// <summary>One knot of a belt's spline, as the conveyor file stores it (board units; heading in degrees round Y,
    /// 0 = +z, 90 = +x). <see cref="Linear"/> = a sharp corner.</summary>
    public readonly struct BeltNode
    {
        public readonly Vector2 Position;
        public readonly float YRotation;
        public readonly bool Linear;

        public BeltNode(float x, float z, float yRotation, bool linear = false)
        {
            Position = new Vector2(x, z); YRotation = yRotation; Linear = linear;
        }
    }

    /// <summary>
    /// One conveyor belt drawn along a spline — the ConveyorKit way (ported from its <c>ConveyorController</c>): the
    /// knots become a <see cref="Spline"/> whose smooth corners run along each knot's heading with handles sized from
    /// the distance to its neighbours, <see cref="SplineMesh"/> extrudes the belt mesh along it, and arrow decals run
    /// along the top. Root of the <c>ConveyorBelt</c> prefab; the loop and every feeder are one each.
    /// </summary>
    /// <remarks>
    /// Humble: it knows nothing about bottles. Its owner (<see cref="LoopBeltView"/>) moves them with
    /// <see cref="Sample"/> (by distance along the belt, in this transform's parent space — the belt sits at its
    /// parent's origin) and tells it how far the belt has travelled (<see cref="SetTravel"/>) so the arrows keep in step.
    /// Lookups go through an arc-length table baked once per <see cref="SetRoute"/>, so distances are true lengths.
    /// </remarks>
    public sealed class ConveyorBeltView : MonoBehaviour
    {
        [SerializeField] private SplineContainer _splineContainer;
        [SerializeField] private SplineMesh _splineMesh;

        [Tooltip("An arrow decal, inactive; cloned along the belt every Arrow Spacing.")]
        [SerializeField] private GameObject _arrowTemplate;
        [Tooltip("Board units between two arrows along the belt.")]
        [SerializeField, Min(0.1f)] private float _arrowSpacing = 1.2f;
        [Tooltip("Turns the arrow model round Y so its tip points along the belt (the model's own forward is not +Z).")]
        [SerializeField] private float _arrowYaw = -90f;
        [Tooltip("How high above the spline the arrows ride (board units).")]
        [SerializeField] private float _arrowHeight = 0.04f;
        [Tooltip("How high above the spline the belt's top is: where whatever rides it stands (board units).")]
        [SerializeField] private float _surfaceHeight = 0.03f;
        [Tooltip("Feeder belts only: moves the queue's head row along the belt from where it waits by default (a bottle " +
                 "clear of the loop). + = closer to the loop, − = further back up the belt. Full-size units (before the " +
                 "conveyor's scale). Read every frame, so it can be tuned in play mode on a Feeder/Belt instance.")]
        [SerializeField] private float _headOffset;

        /// <summary>Arc-length table density (samples per board unit of belt).</summary>
        private const float SamplesPerUnit = 40f;

        private readonly List<Transform> _arrows = new List<Transform>();
        private Vector3[] _points = Array.Empty<Vector3>();     // the baked centre line, parent space
        private float[] _arc = Array.Empty<float>();             // cumulative arc length per baked point
        private float _travel;

        /// <summary>The spline the belt runs along (authoring reads its knots back).</summary>
        public SplineContainer Spline => _splineContainer;
        public bool Closed { get; private set; }
        /// <summary>The belt's length (board units).</summary>
        public float Length { get; private set; }
        public float SurfaceHeight => _surfaceHeight;
        /// <summary>How far the queue's head row is moved along a feeder belt from its default place (+ = toward the loop).</summary>
        public float HeadOffset => _headOffset;

        /// <summary>
        /// Rebuild the belt along <paramref name="nodes"/> (board units, in this transform's parent space): spline, mesh,
        /// arc-length table, arrows. A smooth knot gets a Continuous tangent along its heading (TangentIn / TangentOut
        /// collinear, each half the larger X/Z distance to that neighbour); a Linear knot a sharp corner.
        /// </summary>
        public void SetRoute(IReadOnlyList<BeltNode> nodes, bool closed)
        {
            if (nodes == null || nodes.Count < 2) throw new ArgumentException("a belt needs at least two knots", nameof(nodes));
            Closed = closed;
            transform.localPosition = Vector3.zero;
            transform.localRotation = Quaternion.identity;
            transform.localScale = Vector3.one;

            var knots = new float3[nodes.Count];
            for (int i = 0; i < nodes.Count; i++) knots[i] = new float3(nodes[i].Position.x, 0f, nodes[i].Position.y);
            var spline = new Spline(knots, TangentMode.AutoSmooth, closed);
            for (int i = 0; i < nodes.Count; i++)
            {
                TangentLengths(nodes, closed, i, out float tin, out float tout);
                ApplyKnot(spline, i, nodes[i], tin, tout);
            }
            _splineContainer.Spline = spline;
            if (_splineMesh != null) _splineMesh.GenerateMeshAlongSpline();

            Bake(spline);
            BuildArrows();
            SetTravel(_travel);
        }

        /// <summary>The centre line <paramref name="distance"/> along the belt from its first knot (parent space): point
        /// and direction of travel. A loop wraps; an open belt runs on straight past either end.</summary>
        public (Vector3 point, Vector3 tangent) Sample(float distance)
        {
            int n = _points.Length;
            if (n < 2) return (Vector3.zero, Vector3.forward);
            if (Closed) distance = Mathf.Repeat(distance, Length);
            else if (distance <= 0f) { var t0 = (_points[1] - _points[0]).normalized; return (_points[0] + t0 * distance, t0); }
            else if (distance >= Length) { var t1 = (_points[n - 1] - _points[n - 2]).normalized; return (_points[n - 1] + t1 * (distance - Length), t1); }
            int lo = 0, hi = n - 1;
            while (hi - lo > 1)
            {
                int mid = (lo + hi) / 2;
                if (_arc[mid] <= distance) lo = mid; else hi = mid;
            }
            float k = Mathf.InverseLerp(_arc[lo], _arc[hi], distance);
            var d = _points[hi] - _points[lo];
            return (Vector3.Lerp(_points[lo], _points[hi], k), d.sqrMagnitude > 1e-12f ? d.normalized : Vector3.forward);
        }

        /// <summary>The outward side of the belt at a sample: right of travel for a clockwise loop seen from above is its
        /// inside, so outward is the left (same handedness the rules' track 0 = outermost uses).</summary>
        public static Vector3 Outward(Vector3 tangent) => new Vector3(-tangent.z, 0f, tangent.x);

        /// <summary>How far <paramref name="p"/> (parent space) is from the belt's centre line.</summary>
        public float DistanceTo(Vector3 p)
        {
            float best = float.MaxValue;
            for (int i = 1; i < _points.Length; i++)
            {
                Vector3 a = _points[i - 1], ab = _points[i] - a;
                float k = Mathf.Clamp01(Vector3.Dot(p - a, ab) / Mathf.Max(ab.sqrMagnitude, 1e-8f));
                best = Mathf.Min(best, (a + ab * k - p).sqrMagnitude);
            }
            return Mathf.Sqrt(best);
        }

        /// <summary>The belt has moved <paramref name="distance"/> (board units, any origin): the arrows follow.</summary>
        public void SetTravel(float distance)
        {
            _travel = distance;
            for (int i = 0; i < _arrows.Count; i++)
            {
                float s = distance + i * _arrowSpacing;
                if (Closed) s = Mathf.Repeat(s, Length);
                else s = Mathf.Repeat(s, Mathf.Max(Length, 1e-3f));
                var (p, t) = Sample(s);
                _arrows[i].localPosition = p + Vector3.up * _arrowHeight;
                _arrows[i].localRotation = Quaternion.LookRotation(t, Vector3.up) * Quaternion.Euler(0f, _arrowYaw, 0f);
            }
        }

        // ── spline (ConveyorKit's ComputeBezierTangentLengths / ApplyTangentMode) ──
        private static void TangentLengths(IReadOnlyList<BeltNode> nodes, bool closed, int i, out float tin, out float tout)
        {
            int n = nodes.Count;
            bool hasPrev = closed || i > 0, hasNext = closed || i < n - 1;
            tin = hasPrev ? HalfMaxAxis(nodes[i].Position, nodes[(i - 1 + n) % n].Position) : 0f;
            tout = hasNext ? HalfMaxAxis(nodes[i].Position, nodes[(i + 1) % n].Position) : 0f;
            if (!hasPrev) tin = tout;
            if (!hasNext) tout = tin;
        }

        private static float HalfMaxAxis(Vector2 a, Vector2 b) => Mathf.Max(Mathf.Abs(a.x - b.x), Mathf.Abs(a.y - b.y)) * 0.5f;

        private static void ApplyKnot(Spline spline, int i, BeltNode node, float tin, float tout)
        {
            if (node.Linear) { spline.SetTangentMode(i, TangentMode.Linear); return; }
            // Continuous knots keep TangentIn / TangentOut in the knot's own space: the heading goes into Rotation,
            // the handles along local ±Z
            spline.SetTangentMode(i, TangentMode.Continuous);
            var q = Quaternion.Euler(0f, node.YRotation, 0f);
            var knot = spline[i];
            knot.Rotation = new quaternion(q.x, q.y, q.z, q.w);
            knot.TangentIn = new float3(0f, 0f, -tin);
            knot.TangentOut = new float3(0f, 0f, tout);
            spline.SetKnot(i, knot);
        }

        private void Bake(Spline spline)
        {
            float rough = spline.GetLength();
            int count = Mathf.Max(64, Mathf.CeilToInt(rough * SamplesPerUnit)) + 1;
            _points = new Vector3[count];
            _arc = new float[count];
            for (int i = 0; i < count; i++)
            {
                _points[i] = (Vector3)spline.EvaluatePosition((float)i / (count - 1));
                if (i > 0) _arc[i] = _arc[i - 1] + (_points[i] - _points[i - 1]).magnitude;
            }
            Length = _arc[count - 1];
        }

        private void BuildArrows()
        {
            foreach (var a in _arrows) if (a != null) Kill(a.gameObject);
            _arrows.Clear();
            if (_arrowTemplate == null || Length <= 0f) return;
            int count = Mathf.Max(1, Mathf.FloorToInt(Length / _arrowSpacing));
            for (int i = 0; i < count; i++)
            {
                var a = Instantiate(_arrowTemplate, transform, false);     // the belt sits at its parent's origin: same space
                a.name = "Arrow" + i;
                a.SetActive(true);
                _arrows.Add(a.transform);
            }
        }

        // the authoring tool rebuilds belts in edit mode, where Destroy is not allowed
        private static void Kill(GameObject go)
        {
            if (Application.isPlaying) Destroy(go); else DestroyImmediate(go);
        }
    }
}
