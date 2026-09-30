using UnityEngine;

namespace Game.Editor.ArtGen
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
