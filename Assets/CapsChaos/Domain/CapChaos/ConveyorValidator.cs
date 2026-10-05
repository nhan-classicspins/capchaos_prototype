using System;
using System.Collections.Generic;

namespace Game.Domain
{
    /// <summary>
    /// The semantic checks of a conveyor layout: V8 (the loop's rule numbers and its merge points) and V9 (its splines)
    /// — GDD §6.4. V1 (structure) is <see cref="ConveyorJson.Parse"/>. <see cref="LevelValidator"/> runs these too, so a
    /// level built in memory is held to the same rules as a level read from files.
    /// </summary>
    public static class ConveyorValidator
    {
        /// <summary>Rows the two bends of the loop take at least, so the belt can turn round its own width.</summary>
        public const int MinBendRows = 6;
        /// <summary>Two neighbouring knots closer than this (board units) make a degenerate belt piece.</summary>
        public const double MinKnotGap = 0.01;

        /// <summary>How a message names the conveyor: <c>conveyor oval_16</c>, or <c>conveyor</c> for one built in code.</summary>
        public static string Where(ConveyorDefinition c) => c.Id != null ? "conveyor " + c.Id : "conveyor";

        /// <summary>V8: the loop has a straight on each side of its two bends (the pick zone is less than half of it);
        /// every merge point is on the track, outside the pick zone and used by one feeder only. V9: the loop spline
        /// closes over at least 3 knots, each feeder spline runs over at least 2, no two neighbouring knots coincide and
        /// every number is finite. A layout built in code has no splines: V9 has nothing to check.</summary>
        public static List<string> Validate(ConveyorDefinition c)
        {
            var errors = new List<string>();
            string where = Where(c);
            if (c.PickRows * 2 + MinBendRows > c.Rows)
                errors.Add($"V8 {where}.pickRows: {c.PickRows} pick rows need rows ≥ {c.PickRows * 2 + MinBendRows} (two straights + the bends), not {c.Rows}");
            var merges = new HashSet<int>();
            for (int f = 0; f < c.Feeders.Count; f++)
            {
                int at = c.Feeders[f].MergeAt;
                string fp = $"V8 {where}.feeders[{f}].mergeAt";
                if (at < 0 || at >= c.Rows) errors.Add($"{fp}: {at} is not a track position 0..{c.Rows - 1}");
                else if (at < c.PickRows) errors.Add($"{fp}: {at} is inside the pick zone 0..{c.PickRows - 1}");
                if (!merges.Add(at)) errors.Add($"{fp}: {at} is another feeder's merge point");
            }

            if (c.Loop.Count > 0) CheckSpline(c.Loop, closed: true, 3, $"V9 {where}.loop", errors);
            for (int f = 0; f < c.Feeders.Count; f++)
                if (c.Feeders[f].Nodes.Count > 0) CheckSpline(c.Feeders[f].Nodes, closed: false, 2, $"V9 {where}.feeders[{f}]", errors);
            return errors;
        }

        private static void CheckSpline(IReadOnlyList<ConveyorNode> nodes, bool closed, int min, string path, List<string> errors)
        {
            if (nodes.Count < min) { errors.Add($"{path}.nodes: at least {min} nodes ({(closed ? "a closed loop" : "an open belt")})"); return; }
            for (int i = 0; i < nodes.Count; i++)
            {
                var n = nodes[i];
                if (!Finite(n.X) || !Finite(n.Z) || !Finite(n.YRotation)) { errors.Add($"{path}.nodes[{i}]: x, z and yRotation must be finite numbers"); continue; }
                if (!closed && i == nodes.Count - 1) continue;
                var b = nodes[(i + 1) % nodes.Count];
                double dx = b.X - n.X, dz = b.Z - n.Z;
                if (Math.Sqrt(dx * dx + dz * dz) < MinKnotGap)
                    errors.Add($"{path}.nodes[{i}]: it and node {(i + 1) % nodes.Count} stand on the same spot (closer than {MinKnotGap})");
            }
        }

        private static bool Finite(double v) => !double.IsNaN(v) && !double.IsInfinity(v);
    }
}
