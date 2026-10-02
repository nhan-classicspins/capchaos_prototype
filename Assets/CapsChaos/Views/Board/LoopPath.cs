using System.Collections.Generic;
using UnityEngine;

namespace Game.Views
{
    /// <summary>
    /// The belt's centre line as a closed ROUNDED CONVEX POLYGON (art §4.2b, level <c>view.loopShape</c>): straight
    /// edges joined by circular arcs, walked clockwise seen from above, starting at the front edge (corner 0 → 1). Plain
    /// geometry — it knows nothing of rows or bottles; the caller scales it and picks where along it a row stands.
    /// Coordinates are on the board plane (x, z), centred on the shape's bounding box.
    /// </summary>
    public sealed class LoopPath
    {
        private struct Segment
        {
            public float Start, Length;
            public bool Arc;
            public Vector3 From, Dir;          // a straight
            public Vector3 Centre;             // an arc: centre, radius, the angle at its start (radians, x-z plane)
            public float Radius, Angle0;
        }

        private readonly List<Segment> _segments = new List<Segment>();
        private readonly Vector3[] _samples;
        public float Length { get; }
        /// <summary>The tightest rounding (the arc radius the inner track has to fit inside).</summary>
        public float MinRadius { get; }
        public float FrontLength { get; }
        public Vector2 Min { get; }
        public Vector2 Max { get; }
        /// <summary>Mirror-symmetric about x = 0 (corner by corner).</summary>
        public bool Symmetric { get; }

        /// <summary>Corners in belt order (clockwise from above), corner 0 → 1 the front edge, each with its rounding
        /// radius; <paramref name="scale"/> multiplies everything. The caller has checked the shape (V9).</summary>
        public LoopPath(IReadOnlyList<float> xs, IReadOnlyList<float> zs, IReadOnlyList<float> radii, float scale)
        {
            int n = xs.Count;
            var p = new Vector3[n];
            for (int i = 0; i < n; i++) p[i] = new Vector3(xs[i], 0f, zs[i]) * scale;
            var trim = new float[n];
            var turn = new float[n];
            for (int i = 0; i < n; i++)
            {
                var u = (p[i] - p[(i + n - 1) % n]).normalized;
                var v = (p[(i + 1) % n] - p[i]).normalized;
                turn[i] = Mathf.Abs(Mathf.Atan2(u.x * v.z - u.z * v.x, Vector3.Dot(u, v)));
                trim[i] = radii[i] * scale * Mathf.Tan(turn[i] * 0.5f);
            }
            float s = 0f, minR = float.MaxValue;
            for (int i = 0; i < n; i++)
            {
                int b = (i + 1) % n;
                var dir = (p[b] - p[i]).normalized;
                float len = Mathf.Max(0f, (p[b] - p[i]).magnitude - trim[i] - trim[b]);
                _segments.Add(new Segment { Start = s, Length = len, From = p[i] + dir * trim[i], Dir = dir });
                if (i == 0) FrontLength = len;
                s += len;
                // the arc rounding corner b: from the end of this edge, turning right (clockwise) by turn[b]
                float r = radii[b] * scale;
                minR = Mathf.Min(minR, r);
                var a = p[b] - dir * trim[b];
                var centre = a + new Vector3(dir.z, 0f, -dir.x) * r;          // right of travel = the inside
                var rel = a - centre;
                _segments.Add(new Segment { Start = s, Length = r * turn[b], Arc = true, Centre = centre, Radius = r, Angle0 = Mathf.Atan2(rel.z, rel.x) });
                s += r * turn[b];
            }
            Length = s;
            MinRadius = minR;

            // sample, then centre on the bounding box
            int count = Mathf.Max(64, Mathf.CeilToInt(Length / SampleStep));
            _samples = new Vector3[count];
            var lo = new Vector2(float.MaxValue, float.MaxValue);
            var hi = new Vector2(float.MinValue, float.MinValue);
            for (int i = 0; i < count; i++)
            {
                var q = Raw(Length * i / count).p;
                _samples[i] = q;
                lo = Vector2.Min(lo, new Vector2(q.x, q.z));
                hi = Vector2.Max(hi, new Vector2(q.x, q.z));
            }
            _offset = new Vector3((lo.x + hi.x) * 0.5f, 0f, (lo.y + hi.y) * 0.5f);
            for (int i = 0; i < count; i++) _samples[i] -= _offset;
            Min = new Vector2(lo.x - _offset.x, lo.y - _offset.z);
            Max = new Vector2(hi.x - _offset.x, hi.y - _offset.z);

            bool symmetric = true;
            for (int i = 0; i < n && symmetric; i++)
            {
                var c = p[i] - _offset;
                bool found = false;
                for (int j = 0; j < n && !found; j++)
                {
                    var d = p[j] - _offset;
                    found = Mathf.Abs(d.x + c.x) < 1e-3f && Mathf.Abs(d.z - c.z) < 1e-3f && Mathf.Abs(radii[i] - radii[j]) < 1e-4f;
                }
                symmetric = found;
            }
            Symmetric = symmetric;
        }

        private const float SampleStep = 0.05f;
        private readonly Vector3 _offset;

        /// <summary>The centre line at <paramref name="s"/> along it (wraps): point, direction of travel, outward normal.</summary>
        public (Vector3 p, Vector3 tangent, Vector3 outward) At(float s)
        {
            var r = Raw(s);
            return (r.p - _offset, r.t, new Vector3(-r.t.z, 0f, r.t.x));
        }

        private (Vector3 p, Vector3 t) Raw(float s)
        {
            s = Mathf.Repeat(s, Length);
            int lo = 0, hi = _segments.Count - 1;
            while (lo < hi)                                                     // the last segment starting at or before s
            {
                int mid = (lo + hi + 1) / 2;
                if (_segments[mid].Start <= s) lo = mid; else hi = mid - 1;
            }
            var g = _segments[lo];
            float u = s - g.Start;
            if (!g.Arc) return (g.From + g.Dir * u, g.Dir);
            float ang = g.Angle0 - u / g.Radius;                                // clockwise
            var radial = new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang));
            return (g.Centre + radial * g.Radius, new Vector3(radial.z, 0f, -radial.x));
        }

        /// <summary>How far <paramref name="q"/> is from the centre line.</summary>
        public float Distance(Vector3 q)
        {
            float best = float.MaxValue;
            for (int i = 0; i < _samples.Length; i++)
            {
                Vector3 a = _samples[i], ab = _samples[(i + 1) % _samples.Length] - a;
                float k = Mathf.Clamp01(Vector3.Dot(q - a, ab) / Mathf.Max(ab.sqrMagnitude, 1e-8f));
                best = Mathf.Min(best, ((a + ab * k) - q).sqrMagnitude);
            }
            return Mathf.Sqrt(best);
        }
    }
}
