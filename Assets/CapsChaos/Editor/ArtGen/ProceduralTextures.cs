using UnityEngine;

namespace Game.Editor
{
    /// <summary>
    /// Procedural textures (art-direction §9). Everything is GREYSCALE: the colour
    /// comes from a design token at runtime (TokenTint), so no palette value is duplicated here.
    /// The rainbow's hues are the spectrum itself (art §4.2), not a palette choice.
    /// Deterministic: a fixed seed, so a regeneration yields identical pixels.
    /// </summary>
    internal static class ProceduralTextures
    {
        /// <summary>Belt stripes: two grey tones (ratio LaneBeltB/LaneBeltA ≈ 0.95) with a thin seam line. One repeat = one tray pitch.</summary>
        public static Texture2D BeltStripes(int size = 64)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, true) { name = "T_BeltStripes" };
            var px = new Color[size * size];
            for (int y = 0; y < size; y++)
            {
                float v = (float)y / size;
                float g = v < 0.5f ? 1f : 0.95f;
                if (v < 0.03f || Mathf.Abs(v - 0.5f) < 0.015f) g = 0.86f;
                for (int x = 0; x < size; x++) px[y * size + x] = new Color(g, g, g, 1f);
            }
            tex.SetPixels(px); tex.Apply(true);
            return tex;
        }

        /// <summary>
        /// A white rounded rectangle with an anti-aliased edge, for 9-sliced uGUI surfaces (pills, tiles,
        /// panels). White so the colour comes from a UiTint token; the corner radius is DesignTokens.Ui.CornerRadius
        /// (mirrored here — Game.Editor may not reference Game.Views).
        /// </summary>
        public static Texture2D RoundedRect(int size = 128, float radius = 40f)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { name = "T_UiRounded" };
            var px = new Color[size * size];
            float half = size * 0.5f;
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    // signed distance to a rounded box centred in the texture (pixel centres at +0.5)
                    float qx = Mathf.Abs(x + 0.5f - half) - (half - radius);
                    float qy = Mathf.Abs(y + 0.5f - half) - (half - radius);
                    float outside = new Vector2(Mathf.Max(qx, 0f), Mathf.Max(qy, 0f)).magnitude;
                    float d = outside + Mathf.Min(Mathf.Max(qx, qy), 0f) - radius;
                    float a = Mathf.Clamp01(0.5f - d);
                    px[y * size + x] = new Color(1f, 1f, 1f, a);
                }
            tex.SetPixels(px); tex.Apply(false);
            return tex;
        }

        /// <summary>A white disc filling the texture (HUD round buttons, their rim and shadow).</summary>
        public static Texture2D Disc(int size = 128)
            => Shape("T_UiDisc", size, (x, y) => x * x + y * y <= 1f);

        /// <summary>↻ Restart icon (art §5): an open ring with an arrowhead closing it clockwise from the top.</summary>
        public static Texture2D IconRetry(int size = 128)
        {
            const float r = 0.56f, half = 0.12f;                      // ring radius and half thickness
            const float gapFrom = 55f, gapTo = 100f;                  // degrees, measured CCW from +x
            var a = Polar(r - 0.27f, gapFrom); var b = Polar(r + 0.27f, gapFrom); var tip = Polar(r, gapFrom + 36f);
            return Shape("T_IconRetry", size, (x, y) =>
            {
                float d = Mathf.Sqrt(x * x + y * y);
                float ang = Mathf.Repeat(Mathf.Atan2(y, x) * Mathf.Rad2Deg, 360f);
                bool ring = Mathf.Abs(d - r) <= half && (ang < gapFrom || ang > gapTo);
                return ring || InTriangle(new Vector2(x, y), a, b, tip);
            });
        }

        /// <summary>⌂ Home icon (art §5): a roof over a house body with a door cut out.</summary>
        public static Texture2D IconHome(int size = 128)
        {
            var roofL = new Vector2(-0.80f, 0.02f); var roofR = new Vector2(0.80f, 0.02f); var roofTop = new Vector2(0f, 0.78f);
            return Shape("T_IconHome", size, (x, y) =>
            {
                var p = new Vector2(x, y);
                bool roof = InTriangle(p, roofL, roofR, roofTop);
                bool body = x >= -0.52f && x <= 0.52f && y >= -0.70f && y <= 0.10f;
                bool door = x >= -0.16f && x <= 0.16f && y >= -0.70f && y <= -0.22f;
                return roof || (body && !door);
            });
        }

        // shape rasteriser: unit square [-1, 1]², y up, 4 × 4 supersampling for the anti-aliased edge
        /// <summary>Five-point star (the coin's face, art: R20 coin pill / offer button).</summary>
        public static Texture2D IconStar(int size = 128)
        {
            var pts = new Vector2[10];
            for (int i = 0; i < 10; i++) pts[i] = Polar(i % 2 == 0 ? 0.9f : 0.4f, 90f + i * 36f);
            return Shape("T_IconStar", size, (x, y) =>
            {
                var p = new Vector2(x, y);
                for (int i = 0; i < 10; i += 2)
                    if (InTriangle(p, Vector2.zero, pts[i], pts[(i + 1) % 10]) || InTriangle(p, Vector2.zero, pts[i], pts[(i + 9) % 10])) return true;
                return false;
            });
        }

        /// <summary>A rounded plus (an extra slot, R20).</summary>
        public static Texture2D IconPlus(int size = 128)
            => Shape("T_IconPlus", size, (x, y) => (Mathf.Abs(x) < 0.22f && Mathf.Abs(y) < 0.8f) || (Mathf.Abs(y) < 0.22f && Mathf.Abs(x) < 0.8f));

        /// <summary>A play triangle (the rewarded ad, drawn on a rounded clapper body).</summary>
        public static Texture2D IconPlay(int size = 128)
            => Shape("T_IconPlay", size, (x, y) => InTriangle(new Vector2(x, y), new Vector2(-0.45f, -0.6f), new Vector2(-0.45f, 0.6f), new Vector2(0.65f, 0f)));

        /// <summary>A bold ✕ (the offer's close button).</summary>
        public static Texture2D IconClose(int size = 128)
            => Shape("T_IconClose", size, (x, y) =>
                (Mathf.Abs(x - y) < 0.3f || Mathf.Abs(x + y) < 0.3f) && Mathf.Abs(x) < 0.62f && Mathf.Abs(y) < 0.62f);

        private static Texture2D Shape(string name, int size, System.Func<float, float, bool> inside)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { name = name };
            var px = new Color[size * size];
            const int ss = 4;
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    int hits = 0;
                    for (int sy = 0; sy < ss; sy++)
                        for (int sx = 0; sx < ss; sx++)
                        {
                            float u = (x + (sx + 0.5f) / ss) / size * 2f - 1f;
                            float v = (y + (sy + 0.5f) / ss) / size * 2f - 1f;
                            if (inside(u, v)) hits++;
                        }
                    px[y * size + x] = new Color(1f, 1f, 1f, hits / (float)(ss * ss));
                }
            tex.SetPixels(px); tex.Apply(false);
            return tex;
        }

        private static Vector2 Polar(float r, float degrees)
            => new Vector2(Mathf.Cos(degrees * Mathf.Deg2Rad), Mathf.Sin(degrees * Mathf.Deg2Rad)) * r;

        private static bool InTriangle(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
        {
            float d1 = Cross(p, a, b), d2 = Cross(p, b, c), d3 = Cross(p, c, a);
            bool neg = d1 < 0 || d2 < 0 || d3 < 0, pos = d1 > 0 || d2 > 0 || d3 > 0;
            return !(neg && pos);
        }

        private static float Cross(Vector2 p, Vector2 a, Vector2 b) => (p.x - b.x) * (a.y - b.y) - (a.x - b.x) * (p.y - b.y);

        // ── tray modifiers (GDD R17–R19): white fill + dark outline, tinted by a token like everything else ──
        /// <summary>Padlock shackle (R18): the upper half-ring and its two legs. Same frame as <see cref="LockBody"/> — the two quads overlap exactly.</summary>
        public static Texture2D LockShackle(int size = 256)
        {
            const float r = 0.36f, w = 0.1f;
            var c = new Vector2(0f, 0.2f);
            return Outlined("T_LockShackle", size, 0.08f, p => Mathf.Min(
                Arc(p, c, r, 0f, 180f) - w,
                Mathf.Min(Segment(p, new Vector2(-r, 0.2f), new Vector2(-r, -0.1f)), Segment(p, new Vector2(r, 0.2f), new Vector2(r, -0.1f))) - w));
        }

        /// <summary>Padlock body (R18): a rounded block the count is printed on.</summary>
        public static Texture2D LockBody(int size = 256)
            => Outlined("T_LockBody", size, 0.08f, p => RoundedBox(p - new Vector2(0f, -0.38f), new Vector2(0.6f, 0.42f), 0.16f));

        /// <summary>Rope (R19): twisted diagonal strands, darker towards both edges so the line reads round. U runs along the rope.</summary>
        public static Texture2D Rope(int width = 64, int height = 16)
        {
            var tex = new Texture2D(width, height, TextureFormat.RGBA32, true) { name = "T_Rope" };
            var px = new Color[width * height];
            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                {
                    float u = (float)x / width, v = (y + 0.5f) / height;
                    float strand = Mathf.Repeat(u * 2f + v * 0.5f, 1f);
                    float g = Mathf.Lerp(0.78f, 1f, Mathf.SmoothStep(0f, 1f, Mathf.Abs(strand - 0.5f) * 2f));
                    g *= Mathf.Lerp(0.72f, 1f, Mathf.Sin(v * Mathf.PI));
                    px[y * width + x] = new Color(g, g, g, 1f);
                }
            tex.SetPixels(px); tex.Apply(true);
            return tex;
        }

        // signed-distance rasteriser: unit square [-1, 1]², y up, 4 × 4 supersampling. d < 0 → white fill,
        // 0 ≤ d < outline → dark outline, beyond → transparent.
        private static Texture2D Outlined(string name, int size, float outline, System.Func<Vector2, float> sdf)
        {
            const float lineGrey = 0.18f;
            const int ss = 4;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, true) { name = name };
            var px = new Color[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float grey = 0f, alpha = 0f;
                    for (int sy = 0; sy < ss; sy++)
                        for (int sx = 0; sx < ss; sx++)
                        {
                            var p = new Vector2((x + (sx + 0.5f) / ss) / size * 2f - 1f, (y + (sy + 0.5f) / ss) / size * 2f - 1f);
                            float d = sdf(p);
                            if (d < 0f) { grey += 1f; alpha += 1f; }
                            else if (d < outline) { grey += lineGrey; alpha += 1f; }
                        }
                    float a = alpha / (ss * ss);
                    float g = alpha > 0f ? grey / alpha : 1f;
                    px[y * size + x] = new Color(g, g, g, a);
                }
            tex.SetPixels(px); tex.Apply(true);
            return tex;
        }

        private static float Segment(Vector2 p, Vector2 a, Vector2 b)
        {
            var ab = b - a;
            float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude);
            return (p - (a + ab * t)).magnitude;
        }

        /// <summary>Distance to the arc of radius <paramref name="r"/> round <paramref name="c"/>, from <paramref name="fromDeg"/> CCW to <paramref name="toDeg"/>.</summary>
        private static float Arc(Vector2 p, Vector2 c, float r, float fromDeg, float toDeg)
        {
            var q = p - c;
            float ang = Mathf.Atan2(q.y, q.x) * Mathf.Rad2Deg;
            if (Mathf.Repeat(ang - fromDeg, 360f) <= toDeg - fromDeg) return Mathf.Abs(q.magnitude - r);
            return Mathf.Min((p - (c + Polar(r, fromDeg))).magnitude, (p - (c + Polar(r, toDeg))).magnitude);
        }

        private static float RoundedBox(Vector2 p, Vector2 half, float radius)
        {
            var q = new Vector2(Mathf.Abs(p.x) - half.x + radius, Mathf.Abs(p.y) - half.y + radius);
            return new Vector2(Mathf.Max(q.x, 0f), Mathf.Max(q.y, 0f)).magnitude + Mathf.Min(Mathf.Max(q.x, q.y), 0f) - radius;
        }
    }
}
