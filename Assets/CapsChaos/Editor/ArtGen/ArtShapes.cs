using System.Collections.Generic;
using UnityEngine;

namespace Game.Editor
{
    /// <summary>
    /// The geometry of every procedural MVP prop (art-direction §4). World unit: 1 = one bottle's
    /// height. Pivots are bottom-centre unless noted. All dimensions live here as named constants so
    /// the prefab assembly and the Views can agree on them.
    /// </summary>
    internal static class ArtShapes
    {
        // ── shared dimensions ────────────────────────────────────────────────────────────────
        public const float SlotWidth = 1.02f, SlotDepth = 0.98f, SlotHeight = 0.03f;
        public const float LaneWidth = 1.0f, LaneLength = 6f, LanePitch = 0.95f;

        /// <summary>Slot — recessed rounded tile.</summary>
        public static Mesh Slot()
        {
            var b = new MeshBuilder();
            b.RoundedPrism(SlotWidth, SlotDepth, SlotHeight, 0.16f, 0.015f, Matrix4x4.identity);
            return b.ToMesh("Slot");
        }

        /// <summary>Lane frame — two side rails + the dark head step at the tappable end (z = 0), extending to -z.</summary>
        public static Mesh LaneRail()
        {
            var b = new MeshBuilder();
            var id = Matrix4x4.identity;
            float half = LaneWidth * 0.5f, L = LaneLength;
            b.Box(new Vector3(half + 0.03f, 0.04f, -L * 0.5f), new Vector3(0.06f, 0.08f, L), id);
            b.Box(new Vector3(-half - 0.03f, 0.04f, -L * 0.5f), new Vector3(0.06f, 0.08f, L), id);
            b.Box(new Vector3(0, 0.06f, 0.06f), new Vector3(LaneWidth + 0.12f, 0.12f, 0.12f), id);
            b.Box(new Vector3(0, -0.02f, -L * 0.5f), new Vector3(LaneWidth + 0.12f, 0.04f, L), id);
            return b.ToMesh("LaneRail");
        }

        /// <summary>Belt surface — one stripe texture repeat per tray pitch along the lane (V), scrolled by the View.</summary>
        public static Mesh LaneBelt()
        {
            var b = new MeshBuilder();
            float half = LaneWidth * 0.5f;
            b.Plane(new Vector2(-half, -LaneLength), new Vector2(half, 0f), 0.001f,
                new Vector2(1f / LaneWidth, 1f / LanePitch), Matrix4x4.identity);
            return b.ToMesh("LaneBelt");
        }

        /// <summary>Floor — large quad centred on the origin.</summary>
        public static Mesh Floor()
        {
            var b = new MeshBuilder();
            b.Plane(new Vector2(-15, -15), new Vector2(15, 15), 0f, new Vector2(0.1f, 0.1f), Matrix4x4.identity);
            return b.ToMesh("Floor");
        }

        // ── profile helpers ──────────────────────────────────────────────────────────────────
        private static void Arc(List<Vector2> p, Vector2 centre, float radius, float fromDeg, float toDeg, int steps)
        {
            for (int i = 1; i <= steps; i++)
            {
                float a = Mathf.Lerp(fromDeg, toDeg, (float)i / steps) * Mathf.Deg2Rad;
                p.Add(centre + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * radius);
            }
        }

        private static void Groove(List<Vector2> p, float y)
        {
            p.Add(new Vector2(0.19f, y - 0.022f));
            p.Add(new Vector2(0.178f, y - 0.008f));
            p.Add(new Vector2(0.178f, y + 0.008f));
            p.Add(new Vector2(0.19f, y + 0.022f));
        }
    }
}
