using System.Collections.Generic;
using UnityEngine;

namespace Game.Editor.ArtGen
{
    /// <summary>
    /// The geometry of every procedural MVP prop (art-direction §4). World unit: 1 = one bottle's
    /// height. Pivots are bottom-centre unless noted. All dimensions live here as named constants so
    /// the prefab assembly and the Views can agree on them.
    /// </summary>
    internal static class ArtShapes
    {
        // ── shared dimensions ────────────────────────────────────────────────────────────────
        public const float BottleHeight = 1f;
        public const float BottleRadius = 0.19f;
        public const float CellPitch = 0.42f;             // bottle spacing in a tray / on the stack grid
        public const float TrayWidth = 0.9f, TrayDepth = 0.86f, TrayHeight = 0.07f;
        public const float CupRadius = 0.18f;
        public const float CapRadius = 0.085f, CapHeight = 0.075f;
        public const float CapOnTrayScale = 2f;           // caps read cell-filling on the belt [QS]
        public const float BoxWidth = 0.96f, BoxDepth = 0.92f, BoxHeight = 1.12f, BoxWall = 0.02f;
        public const float SlotWidth = 1.02f, SlotDepth = 0.98f, SlotHeight = 0.03f;
        public const float LaneWidth = 1.0f, LaneLength = 6f, LanePitch = 0.95f;

        /// <summary>Bottle — open neck with thread and support ring, two grooves, frosted band (submesh 1).</summary>
        public static Mesh Bottle()
        {
            var p = new List<Vector2>();
            // base: flat centre → rounded foot
            p.Add(new Vector2(0f, 0f));
            p.Add(new Vector2(0.12f, 0f));
            Arc(p, new Vector2(0.15f, 0.04f), 0.04f, -90f, 0f, 5);
            p.Add(new Vector2(0.19f, 0.06f));
            p.Add(new Vector2(0.19f, 0.17f));
            Groove(p, 0.20f);
            p.Add(new Vector2(0.19f, 0.30f));
            p.Add(new Vector2(0.186f, 0.37f));
            int bandStart = p.Count - 1;
            p.Add(new Vector2(0.186f, 0.63f));                 // frosted band 0.37..0.63
            int bandEnd = p.Count - 1;
            p.Add(new Vector2(0.19f, 0.67f));
            Groove(p, 0.70f);
            p.Add(new Vector2(0.19f, 0.745f));
            // shoulder
            p.Add(new Vector2(0.184f, 0.78f));
            p.Add(new Vector2(0.165f, 0.82f));
            p.Add(new Vector2(0.135f, 0.855f));
            p.Add(new Vector2(0.10f, 0.88f));
            p.Add(new Vector2(0.078f, 0.90f));
            p.Add(new Vector2(0.07f, 0.915f));
            // support ring
            p.Add(new Vector2(0.07f, 0.925f));
            p.Add(new Vector2(0.092f, 0.93f));
            p.Add(new Vector2(0.092f, 0.942f));
            p.Add(new Vector2(0.07f, 0.947f));
            // threads
            p.Add(new Vector2(0.068f, 0.955f));
            p.Add(new Vector2(0.077f, 0.963f));
            p.Add(new Vector2(0.068f, 0.972f));
            p.Add(new Vector2(0.077f, 0.980f));
            p.Add(new Vector2(0.068f, 0.988f));
            // lip, then down the inside so the neck reads OPEN [QS]
            p.Add(new Vector2(0.069f, 0.997f));
            p.Add(new Vector2(0.064f, 1f));
            p.Add(new Vector2(0.056f, 0.997f));
            p.Add(new Vector2(0.055f, 0.90f));
            p.Add(new Vector2(0.03f, 0.86f));

            var b = new MeshBuilder();
            b.Lathe(p, 40, Matrix4x4.identity, s => s >= bandStart && s < bandEnd ? 1 : 0);
            return b.ToMesh("Bottle");
        }

        /// <summary>Screw cap — knurled skirt, concentric ring on the top. Pivot bottom-centre.</summary>
        public static Mesh Cap()
        {
            const float r = CapRadius, h = CapHeight;
            var p = new List<Vector2>
            {
                new(0.056f, 0.012f),                            // inside lip (seen from below)
                new(0.06f, 0f),
                new(r - 0.004f, 0f),
                new(r, 0.006f),
                new(r, h - 0.012f),
            };
            Arc(p, new Vector2(r - 0.01f, h - 0.012f), 0.01f, 0f, 90f, 4);
            p.Add(new Vector2(0.066f, h - 0.002f));
            p.Add(new Vector2(0.06f, h + 0.004f));             // concentric ring
            p.Add(new Vector2(0.052f, h + 0.004f));
            p.Add(new Vector2(0.046f, h - 0.001f));
            p.Add(new Vector2(0f, h - 0.001f));

            var b = new MeshBuilder();
            // knurl only on the vertical skirt
            b.Lathe(p, 96, Matrix4x4.identity, null,
                (th, y) => y > 0.008f && y < h - 0.012f ? 0.0035f * Mathf.Cos(th * 32f) : 0f);
            return b.ToMesh("Cap");
        }

        /// <summary>Cap tray — rounded slab with a 2×2 grid of shallow cups.</summary>
        public static Mesh CapTray()
        {
            var b = new MeshBuilder();
            b.RoundedPrism(TrayWidth, TrayDepth, TrayHeight, 0.14f, 0.022f, Matrix4x4.identity);
            var cup = new List<Vector2>
            {
                new(CupRadius, TrayHeight - 0.002f),
                new(CupRadius, TrayHeight + 0.014f),
                new(CupRadius - 0.012f, TrayHeight + 0.02f),
                new(CupRadius - 0.022f, TrayHeight + 0.012f),
                new(CupRadius - 0.03f, TrayHeight - 0.012f),
                new(CupRadius - 0.045f, TrayHeight - 0.018f),
                new(0f, TrayHeight - 0.018f),
            };
            foreach (var c in CellCentres())
                b.Lathe(cup, 32, Matrix4x4.Translate(new Vector3(c.x, 0f, c.y)));
            return b.ToMesh("CapTray");
        }

        /// <summary>The 4 cell centres (XZ) of a tray, front-left first.</summary>
        public static Vector2[] CellCentres()
        {
            float h = CellPitch * 0.5f;
            return new[] { new Vector2(-h, -h), new Vector2(h, -h), new Vector2(-h, h), new Vector2(h, h) };
        }

        /// <summary>Carton body — open-top box with walls.</summary>
        public static Mesh BoxBody()
        {
            var b = new MeshBuilder();
            float w = BoxWidth, d = BoxDepth, hgt = BoxHeight, t = BoxWall;
            var id = Matrix4x4.identity;
            b.Box(new Vector3(0, t * 0.5f, 0), new Vector3(w, t, d), id);
            b.Box(new Vector3(0, hgt * 0.5f, d * 0.5f - t * 0.5f), new Vector3(w, hgt, t), id);
            b.Box(new Vector3(0, hgt * 0.5f, -d * 0.5f + t * 0.5f), new Vector3(w, hgt, t), id);
            b.Box(new Vector3(w * 0.5f - t * 0.5f, hgt * 0.5f, 0), new Vector3(t, hgt, d - 2 * t), id);
            b.Box(new Vector3(-w * 0.5f + t * 0.5f, hgt * 0.5f, 0), new Vector3(t, hgt, d - 2 * t), id);
            return b.ToMesh("BoxBody");
        }

        /// <summary>One carton flap, hinge at local origin, standing up along +Y (open), thickness along Z.</summary>
        public static Mesh BoxFlap(string name, float width, float length)
        {
            var b = new MeshBuilder();
            b.Box(new Vector3(0, length * 0.5f, 0), new Vector3(width, length, BoxWall), Matrix4x4.identity);
            return b.ToMesh(name);
        }

        /// <summary>Packing tape strip across the closed top, running front↔back.</summary>
        public static Mesh Tape()
        {
            var b = new MeshBuilder();
            b.Box(new Vector3(0, 0.004f, 0), new Vector3(BoxWidth * 0.22f, 0.008f, BoxDepth + 0.02f), Matrix4x4.identity);
            b.Box(new Vector3(0, -0.12f, BoxDepth * 0.5f + 0.006f), new Vector3(BoxWidth * 0.22f, 0.24f, 0.008f), Matrix4x4.identity);
            b.Box(new Vector3(0, -0.12f, -BoxDepth * 0.5f - 0.006f), new Vector3(BoxWidth * 0.22f, 0.24f, 0.008f), Matrix4x4.identity);
            return b.ToMesh("BoxTape");
        }

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
