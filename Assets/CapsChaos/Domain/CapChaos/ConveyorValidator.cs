using System.Collections.Generic;

namespace Game.Domain
{
    /// <summary>
    /// The semantic checks of a conveyor layout: V8 (the loop's geometry and its merge points) and V9 (its drawn shape)
    /// — GDD §6.4. V1 (structure) is <see cref="ConveyorJson.Parse"/>. <see cref="LevelValidator"/> runs these too, so a
    /// level built in memory is held to the same rules as a level read from files.
    /// </summary>
    public static class ConveyorValidator
    {
        /// <summary>Rows the two bends of the loop take at least, so the belt can turn round its own width.</summary>
        public const int MinBendRows = 6;

        /// <summary>How a message names the conveyor: <c>conveyor oval_16_1f</c>, or <c>conveyor</c> for one built in code.</summary>
        public static string Where(ConveyorDefinition c) => c.Id != null ? "conveyor " + c.Id : "conveyor";

        /// <summary>V8: the loop has a straight on each side of its two bends (the pick zone is less than half of it);
        /// every merge point is on the track, outside the pick zone and used by one feeder only. V9: a custom shape is a
        /// rounded convex polygon whose first edge is the front one.</summary>
        public static List<string> Validate(ConveyorDefinition c)
        {
            var errors = new List<string>();
            string where = Where(c);
            if (c.PickRows * 2 + MinBendRows > c.Rows)
                errors.Add($"V8 {where}.pickRows: {c.PickRows} pick rows need rows ≥ {c.PickRows * 2 + MinBendRows} (two straights + the bends), not {c.Rows}");
            var merges = new HashSet<int>();
            for (int f = 0; f < c.MergeAt.Count; f++)
            {
                int at = c.MergeAt[f];
                string fp = $"V8 {where}.feeders[{f}].mergeAt";
                if (at < 0 || at >= c.Rows) errors.Add($"{fp}: {at} is not a track position 0..{c.Rows - 1}");
                else if (at < c.PickRows) errors.Add($"{fp}: {at} is inside the pick zone 0..{c.PickRows - 1}");
                if (!merges.Add(at)) errors.Add($"{fp}: {at} is another feeder's merge point");
            }
            var shape = c.Shape;
            if (shape.Preset == null) errors.AddRange(LoopShape.Check(shape.Xs, shape.Zs, shape.Radii, $"V9 {where}.shape"));
            return errors;
        }
    }
}
