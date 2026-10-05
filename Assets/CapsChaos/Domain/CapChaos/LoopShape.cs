using System;
using System.Collections.Generic;

namespace Game.Domain
{
    /// <summary>
    /// The SHAPE of the belt's loop (GDD R1, conveyor <c>shape</c>) — presentation data only: the rules never read
    /// it (a row is a row wherever it is drawn). A shape is a ROUNDED CONVEX POLYGON on the board plane (x right, z away
    /// from the player): corners in belt order — clockwise seen from above — each rounded with its own radius. The
    /// first edge, corner 0 → corner 1, is the FRONT edge: the lowest, running right → left, in front of the slots; the
    /// pick zone is centred on it. Units are free: the view scales the shape so its rows stand evenly round it.
    /// <para>Presets: <c>oval</c> (the default; its straights fit the pick zone exactly), <c>circle</c>,
    /// <c>triangle</c> (apex up, the queues merge into its slanted sides).</para>
    /// </summary>
    public sealed class LoopShape
    {
        public const string Oval = "oval", Circle = "circle", Triangle = "triangle";
        public static readonly string[] Presets = { Oval, Circle, Triangle };
        public static readonly LoopShape Default = new LoopShape(Oval, null, null, null);

        /// <summary>The preset's name, or null for a custom shape.</summary>
        public string Preset { get; }
        /// <summary>A custom shape's corners and radii (null for a preset).</summary>
        public IReadOnlyList<double> Xs { get; }
        public IReadOnlyList<double> Zs { get; }
        public IReadOnlyList<double> Radii { get; }

        private LoopShape(string preset, IReadOnlyList<double> xs, IReadOnlyList<double> zs, IReadOnlyList<double> radii)
        {
            Preset = preset; Xs = xs; Zs = zs; Radii = radii;
        }

        public static LoopShape Named(string preset)
        {
            if (Array.IndexOf(Presets, preset) < 0) throw new ArgumentException($"unknown loop shape '{preset}'", nameof(preset));
            return preset == Oval ? Default : new LoopShape(preset, null, null, null);
        }

        public static LoopShape Custom(IReadOnlyList<double> xs, IReadOnlyList<double> zs, IReadOnlyList<double> radii) =>
            new LoopShape(null, xs, zs, radii);

        public bool IsDefault => Preset == Oval;

        /// <summary>The concrete corners for a loop of <paramref name="rows"/> rows with <paramref name="pickRows"/> in the
        /// pick zone. The oval's straights take the pick zone's share of the loop, so with rows evenly spaced the pick
        /// zone is exactly its front straight.</summary>
        public (double[] xs, double[] zs, double[] radii) Corners(int rows, int pickRows)
        {
            switch (Preset)
            {
                case Oval:
                {
                    // two straights of half-length a and two half circles of radius 1: straight share = 2a / (4a + 2π)
                    double share = Math.Min(0.45, Math.Max(0.05, (double)pickRows / rows));
                    double a = Math.PI * share / (1 - 2 * share);
                    return (new[] { a + 1, -a - 1, -a - 1, a + 1 }, new[] { -1.0, -1, 1, 1 }, new[] { 1.0, 1, 1, 1 });
                }
                case Circle:
                    return (new[] { 1.0, -1, -1, 1 }, new[] { -1.0, -1, 1, 1 }, new[] { 1.0, 1, 1, 1 });
                case Triangle:
                    return (new[] { 1.4, -1.4, 0 }, new[] { 0.0, 0, 1.6 }, new[] { 0.45, 0.45, 0.45 });
                default:
                    return (Copy(Xs), Copy(Zs), Copy(Radii));
            }
        }

        private static double[] Copy(IReadOnlyList<double> v)
        {
            var a = new double[v.Count];
            for (int i = 0; i < a.Length; i++) a[i] = v[i];
            return a;
        }

        /// <summary>V9: a rounded convex polygon in belt order whose first edge is the front one, with every corner's
        /// rounding fitting the edges either side of it. Problems are reported under <paramref name="path"/>.</summary>
        public static List<string> Check(IReadOnlyList<double> xs, IReadOnlyList<double> zs, IReadOnlyList<double> radii, string path)
        {
            var errors = new List<string>();
            int n = xs.Count;
            if (n < 3 || zs.Count != n) { errors.Add($"{path}.points: at least 3 corners [x, z]"); return errors; }
            if (radii.Count != n) { errors.Add($"{path}.radius: one radius, or one per corner ({n})"); return errors; }
            for (int i = 0; i < n; i++)
                if (!(radii[i] > 0)) errors.Add($"{path}.radius[{i}]: {radii[i]} must be > 0 (a sharp corner jams the inner track)");
            double area = 0;
            for (int i = 0; i < n; i++) area += xs[i] * zs[(i + 1) % n] - xs[(i + 1) % n] * zs[i];
            if (area >= 0) errors.Add($"{path}.points: the belt runs clockwise seen from above — list the corners that way");
            for (int i = 0; i < n; i++)
            {
                int a = (i + n - 1) % n, b = (i + 1) % n;
                double cross = (xs[i] - xs[a]) * (zs[b] - zs[i]) - (zs[i] - zs[a]) * (xs[b] - xs[i]);
                if (cross > 1e-9) errors.Add($"{path}.points[{i}]: the shape must be convex");
            }
            double minZ = double.MaxValue;
            for (int i = 0; i < n; i++) minZ = Math.Min(minZ, zs[i]);
            if (Math.Abs(zs[0] - zs[1]) > 1e-9 || xs[0] <= xs[1] || zs[0] > minZ + 1e-9)
                errors.Add($"{path}.points: the first edge (corner 0 → 1) is the front one — level, lowest, running right → left");
            if (errors.Count > 0) return errors;
            var trim = new double[n];
            for (int i = 0; i < n; i++) trim[i] = radii[i] * Math.Tan(Turn(xs, zs, i) * 0.5);
            for (int i = 0; i < n; i++)
            {
                int b = (i + 1) % n;
                double len = Math.Sqrt((xs[b] - xs[i]) * (xs[b] - xs[i]) + (zs[b] - zs[i]) * (zs[b] - zs[i]));
                if (trim[i] + trim[b] > len + 1e-6)
                    errors.Add($"{path}.radius: corners {i} and {b} are rounded more than their edge ({len:0.###}) allows");
            }
            return errors;
        }

        /// <summary>How far the belt turns at corner <paramref name="i"/> (radians, 0 = straight on).</summary>
        public static double Turn(IReadOnlyList<double> xs, IReadOnlyList<double> zs, int i)
        {
            int n = xs.Count, a = (i + n - 1) % n, b = (i + 1) % n;
            double ux = xs[i] - xs[a], uz = zs[i] - zs[a], vx = xs[b] - xs[i], vz = zs[b] - zs[i];
            return Math.Abs(Math.Atan2(ux * vz - uz * vx, ux * vx + uz * vz));
        }
    }
}
