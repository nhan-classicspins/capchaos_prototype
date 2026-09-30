using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Game.Editor
{
    /// <summary>
    /// Minimal deterministic mesh builder for the procedural MVP art (art-direction §9).
    /// Three primitives cover every gameplay prop: a lathe (bottle, cap, tray cups), a bevelled
    /// rounded-rectangle prism (tray, slot) and a flat-shaded box (carton, rails).
    /// Winding follows Unity's clockwise front face; normals are written explicitly.
    /// </summary>
    internal sealed class MeshBuilder
    {
        private readonly List<Vector3> _v = new();
        private readonly List<Vector3> _n = new();
        private readonly List<Vector2> _uv = new();
        private readonly List<List<int>> _subs = new() { new List<int>() };

        public int VertexCount => _v.Count;

        private List<int> Sub(int index)
        {
            while (_subs.Count <= index) _subs.Add(new List<int>());
            return _subs[index];
        }

        private int Add(Vector3 p, Vector3 n, Vector2 uv)
        {
            _v.Add(p); _n.Add(n.normalized); _uv.Add(uv);
            return _v.Count - 1;
        }

        private void Tri(int sub, int a, int b, int c) { var s = Sub(sub); s.Add(a); s.Add(b); s.Add(c); }

        /// <summary>
        /// Revolve a (radius, height) profile around +Y. The profile is walked in order; its
        /// outward side is to the right of the walking direction (walking up an outer wall faces +r).
        /// <paramref name="subOfSegment"/> picks the submesh per profile segment;
        /// <paramref name="radiusMod"/> (theta, y) → Δr adds surface detail such as knurling.
        /// UV: u = around (0..1), v = height (y / vScale).
        /// </summary>
        public void Lathe(IReadOnlyList<Vector2> profile, int segments, Matrix4x4 xf,
            Func<int, int> subOfSegment = null, Func<float, float, float> radiusMod = null, float vScale = 1f)
        {
            int n = profile.Count;
            var rot = xf.rotation;
            // per-point 2D normals: average of the adjacent segment normals
            var pn = new Vector2[n];
            for (int i = 0; i < n; i++)
            {
                Vector2 acc = Vector2.zero;
                if (i > 0) acc += SegNormal(profile[i - 1], profile[i]);
                if (i < n - 1) acc += SegNormal(profile[i], profile[i + 1]);
                pn[i] = acc.sqrMagnitude > 1e-12f ? acc.normalized : Vector2.up;
            }

            // one vertex ring per profile SEGMENT END, so a submesh boundary never shares a vertex
            for (int s = 0; s < n - 1; s++)
            {
                int sub = subOfSegment?.Invoke(s) ?? 0;
                int baseA = _v.Count;
                for (int k = 0; k < 2; k++)
                {
                    var p = profile[s + k];
                    var nn = pn[s + k];
                    for (int j = 0; j <= segments; j++)
                    {
                        float t = (float)j / segments;
                        float th = t * Mathf.PI * 2f;
                        float r = p.x + (radiusMod?.Invoke(th, p.y) ?? 0f);
                        float c = Mathf.Cos(th), sn = Mathf.Sin(th);
                        var pos = new Vector3(r * c, p.y, r * sn);
                        var nor = new Vector3(nn.x * c, nn.y, nn.x * sn);
                        Add(xf.MultiplyPoint3x4(pos), rot * nor, new Vector2(t, p.y / vScale));
                    }
                }
                int row = segments + 1;
                for (int j = 0; j < segments; j++)
                {
                    int a = baseA + j, b = baseA + row + j, c1 = baseA + j + 1, d = baseA + row + j + 1;
                    Tri(sub, a, b, c1);
                    Tri(sub, b, d, c1);
                }
            }
        }

        // outward normal of a profile segment walked from a to b: rotate the direction by -90°
        private static Vector2 SegNormal(Vector2 a, Vector2 b)
        {
            var d = b - a;
            return new Vector2(d.y, -d.x).normalized;
        }

        /// <summary>Rounded-rectangle outline in XZ (counter-clockwise seen from +Y), centred at origin.</summary>
        public static List<Vector2> RoundedRect(float width, float depth, float radius, int cornerSegments)
        {
            radius = Mathf.Min(radius, Mathf.Min(width, depth) * 0.5f);
            var pts = new List<Vector2>();
            float hx = width * 0.5f - radius, hz = depth * 0.5f - radius;
            var centres = new[] { new Vector2(hx, hz), new Vector2(-hx, hz), new Vector2(-hx, -hz), new Vector2(hx, -hz) };
            for (int c = 0; c < 4; c++)
                for (int i = 0; i <= cornerSegments; i++)
                {
                    float a = (c * 90f + 90f * i / cornerSegments) * Mathf.Deg2Rad;
                    pts.Add(centres[c] + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * radius);
                }
            return pts;
        }

        /// <summary>
        /// Bevelled rounded-rect prism from y=0 to y=height: bottom face, vertical side, a top bevel
        /// of size <paramref name="bevel"/>, top face.
        /// </summary>
        public void RoundedPrism(float width, float depth, float height, float radius, float bevel,
            Matrix4x4 xf, int sub = 0, int cornerSegments = 6)
        {
            var outer = RoundedRect(width, depth, radius, cornerSegments);
            var inner = RoundedRect(width - 2 * bevel, depth - 2 * bevel, Mathf.Max(0.001f, radius - bevel), cornerSegments);
            int m = outer.Count;
            var rot = xf.rotation;
            Vector3 P(Vector2 q, float y) => xf.MultiplyPoint3x4(new Vector3(q.x, y, q.y));
            Vector2 UV(Vector2 q) => new(q.x / width + 0.5f, q.y / depth + 0.5f);

            // outward normals of the outline
            var on = new Vector3[m];
            for (int i = 0; i < m; i++)
            {
                var prev = outer[(i - 1 + m) % m]; var next = outer[(i + 1) % m];
                var d = next - prev;
                on[i] = new Vector3(d.y, 0, -d.x).normalized;
            }

            // side ring (bottom→top-minus-bevel) and bevel ring
            int sideB = _v.Count;
            for (int i = 0; i <= m; i++)
            {
                int k = i % m; float u = (float)i / m;
                Add(P(outer[k], 0f), rot * on[k], new Vector2(u, 0));
                Add(P(outer[k], height - bevel), rot * on[k], new Vector2(u, 0.9f));
                Add(P(inner[k], height), rot * (on[k] + Vector3.up).normalized, new Vector2(u, 1));
            }
            for (int i = 0; i < m; i++)
            {
                int a = sideB + i * 3, b = a + 3;
                // side quad (walking CCW from above, outward faces are clockwise from outside)
                Tri(sub, a, a + 1, b); Tri(sub, a + 1, b + 1, b);
                Tri(sub, a + 1, a + 2, b + 1); Tri(sub, a + 2, b + 2, b + 1);
            }

            // top face fan
            int topC = Add(P(Vector2.zero, height), rot * Vector3.up, new Vector2(0.5f, 0.5f));
            int topB = _v.Count;
            for (int i = 0; i < m; i++) Add(P(inner[i], height), rot * Vector3.up, UV(inner[i]));
            for (int i = 0; i < m; i++) Tri(sub, topC, topB + (i + 1) % m, topB + i);

            // bottom face fan
            int botC = Add(P(Vector2.zero, 0f), rot * Vector3.down, new Vector2(0.5f, 0.5f));
            int botB = _v.Count;
            for (int i = 0; i < m; i++) Add(P(outer[i], 0f), rot * Vector3.down, UV(outer[i]));
            for (int i = 0; i < m; i++) Tri(sub, botC, botB + i, botB + (i + 1) % m);
        }

        /// <summary>Flat-shaded axis-aligned box, centre <paramref name="c"/>, full size <paramref name="size"/>.</summary>
        public void Box(Vector3 c, Vector3 size, Matrix4x4 xf, int sub = 0)
        {
            var h = size * 0.5f;
            var rot = xf.rotation;
            // (normal, right, up) per face so each quad is clockwise when seen from outside
            var faces = new (Vector3 n, Vector3 r, Vector3 u)[]
            {
                (Vector3.right,   Vector3.forward, Vector3.up),
                (Vector3.left,    Vector3.back,    Vector3.up),
                (Vector3.up,      Vector3.right,   Vector3.forward),
                (Vector3.down,    Vector3.right,   Vector3.back),
                (Vector3.forward, Vector3.left,    Vector3.up),
                (Vector3.back,    Vector3.right,   Vector3.up),
            };
            foreach (var (n, r, u) in faces)
            {
                var fc = c + Vector3.Scale(n, h);
                var rs = Vector3.Scale(r, h); var us = Vector3.Scale(u, h);
                float ru = Mathf.Abs(Vector3.Dot(r, size)), uu = Mathf.Abs(Vector3.Dot(u, size));
                int i0 = Add(xf.MultiplyPoint3x4(fc - rs - us), rot * n, new Vector2(0, 0));
                int i1 = Add(xf.MultiplyPoint3x4(fc - rs + us), rot * n, new Vector2(0, uu));
                int i2 = Add(xf.MultiplyPoint3x4(fc + rs + us), rot * n, new Vector2(ru, uu));
                int i3 = Add(xf.MultiplyPoint3x4(fc + rs - us), rot * n, new Vector2(ru, 0));
                Tri(sub, i0, i1, i2); Tri(sub, i0, i2, i3);
            }
        }

        /// <summary>Upward-facing quad in XZ (belt surface, floor). UV tiled by <paramref name="uvPerUnit"/>.</summary>
        public void Plane(Vector2 min, Vector2 max, float y, Vector2 uvPerUnit, Matrix4x4 xf, int sub = 0)
        {
            var rot = xf.rotation; var up = rot * Vector3.up;
            Vector2 UV(float x, float z) => new(x * uvPerUnit.x, z * uvPerUnit.y);
            int a = Add(xf.MultiplyPoint3x4(new Vector3(min.x, y, min.y)), up, UV(min.x, min.y));
            int b = Add(xf.MultiplyPoint3x4(new Vector3(min.x, y, max.y)), up, UV(min.x, max.y));
            int c = Add(xf.MultiplyPoint3x4(new Vector3(max.x, y, max.y)), up, UV(max.x, max.y));
            int d = Add(xf.MultiplyPoint3x4(new Vector3(max.x, y, min.y)), up, UV(max.x, min.y));
            Tri(sub, a, b, c); Tri(sub, a, c, d);
        }

        public Mesh ToMesh(string name)
        {
            var mesh = new Mesh { name = name };
            if (_v.Count > 65535) mesh.indexFormat = IndexFormat.UInt32;
            mesh.SetVertices(_v);
            mesh.SetNormals(_n);
            mesh.SetUVs(0, _uv);
            mesh.subMeshCount = _subs.Count;
            for (int i = 0; i < _subs.Count; i++) mesh.SetTriangles(_subs[i], i, false);
            mesh.RecalculateBounds();
            mesh.RecalculateTangents();
            return mesh;
        }
    }
}
