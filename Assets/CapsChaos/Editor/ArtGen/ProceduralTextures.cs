using UnityEngine;

namespace Game.Editor
{
    /// <summary>
    /// Procedural textures (art-direction §9). Everything except the rainbow is GREYSCALE: the colour
    /// comes from a design token at runtime (TokenTint), so no palette value is duplicated here.
    /// The rainbow's hues are the spectrum itself (art §4.2), not a palette choice.
    /// Deterministic: a fixed seed, so a regeneration yields identical pixels.
    /// </summary>
    internal static class ProceduralTextures
    {
        /// <summary>7-hue diagonal bands, periodic in U and V so it wraps around a lathe seamlessly, plus sparkles.</summary>
        public static Texture2D Rainbow(int size = 512)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, true) { name = "T_Rainbow" };
            var px = new Color[size * size];
            const int bandsU = 2, bandsV = 3;             // integer ⇒ seamless wrap; slope ≈ 30°
            const float saturation = 0.8f, value = 0.95f, edge = 0.08f;
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float u = (float)x / size, v = (float)y / size;
                    float t = Mathf.Repeat(u * bandsU + v * bandsV, 1f) * 7f;
                    int band = Mathf.FloorToInt(t) % 7;
                    float f = t - Mathf.Floor(t);
                    // crisp bands with a short blend into the next hue
                    float blend = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(1f - edge, 1f, f));
                    float hue = Mathf.Lerp(band, band + 1, blend) / 7f;
                    px[y * size + x] = Color.HSVToRGB(Mathf.Repeat(hue, 1f), saturation, value);
                }
            var rng = new System.Random(20260930);
            for (int i = 0; i < 90; i++)
            {
                int cx = rng.Next(size), cy = rng.Next(size); int r = 2 + rng.Next(3);
                for (int dy = -r; dy <= r; dy++)
                    for (int dx = -r; dx <= r; dx++)
                    {
                        float d = Mathf.Sqrt(dx * dx + dy * dy) / r; if (d > 1f) continue;
                        int ix = (cx + dx + size) % size, iy = (cy + dy + size) % size;
                        px[iy * size + ix] = Color.Lerp(px[iy * size + ix], Color.white, 1f - d * d);
                    }
            }
            tex.SetPixels(px); tex.Apply(true);
            return tex;
        }

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
        /// <summary>"?" for a hidden tray: a hooked arc, a short stem and a dot.</summary>
        public static Texture2D MysteryMark(int size = 256)
        {
            const float w = 0.15f;
            var c = new Vector2(0f, 0.32f);
            return Outlined("T_MysteryMark", size, 0.09f, p => Mathf.Min(
                Arc(p, c, 0.36f, -90f, 180f) - w,
                Segment(p, new Vector2(0f, -0.04f), new Vector2(0f, -0.30f)) - w,
                (p - new Vector2(0f, -0.66f)).magnitude - 0.16f));
        }

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

        /// <summary>Cardboard: light value noise + vertical fibres, greyscale around 0.9.</summary>
        public static Texture2D Cardboard(int size = 256)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, true) { name = "T_Cardboard" };
            var px = new Color[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float n = Mathf.PerlinNoise(x * 0.05f + 11.3f, y * 0.05f + 7.1f) * 0.06f
                            + Mathf.PerlinNoise(x * 0.9f + 3.7f, y * 0.02f) * 0.05f;
                    float g = 0.87f + n;
                    px[y * size + x] = new Color(g, g, g, 1f);
                }
            tex.SetPixels(px); tex.Apply(true);
            return tex;
        }
    }
}
