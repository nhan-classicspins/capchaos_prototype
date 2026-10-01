using System.Collections.Generic;

namespace Game.Domain
{
    /// <summary>
    /// Semantic level checks V2–V5 (GDD §6.4). V1 (structure) is <see cref="LevelJson.Parse"/>; V6
    /// (solvability) is <see cref="LevelSolver"/>. Every problem is reported, not just the first.
    /// </summary>
    public static class LevelValidator
    {
        public static List<string> Validate(LevelDefinition level)
        {
            var errors = new List<string>();
            var st = level.Stack;
            var declared = new HashSet<CapColor>(level.Colors);
            var bottles = new Dictionary<CapColor, int>();
            var trays = new Dictionary<CapColor, int>();

            for (int k = 0; k < st.LayerCount; k++)
                for (int r = 0; r < st.Rows; r++)
                    for (int x = 0; x < st.Cols; x++)
                    {
                        var c = st.At(k, r, x);
                        if (c.IsEmpty) continue;
                        string where = $"stack.layers[{k}][{r}][{x}]";
                        if (k > 0 && st.At(k - 1, r, x).IsEmpty)
                            errors.Add($"V2 {where}: floating bottle — layer {k - 1} is empty below it");
                        if (k == 0 && c.Hidden)
                            errors.Add($"V3 {where}: hidden bottle on the ground (it could never be revealed)");
                        if (!declared.Contains(c.Color)) errors.Add($"V5 {where}: colour '{Code(c.Color)}' is not in colors");
                        bottles[c.Color] = (bottles.TryGetValue(c.Color, out var n) ? n : 0) + 1;
                    }

            for (int j = 0; j < level.Lanes.Count; j++)
                for (int t = 0; t < level.Lanes[j].Count; t++)
                {
                    var c = level.Lanes[j][t];
                    if (!declared.Contains(c)) errors.Add($"V5 lanes[{j}][{t}]: colour '{Code(c)}' is not in colors");
                    trays[c] = (trays.TryGetValue(c, out var n) ? n : 0) + 1;
                }

            foreach (var c in level.Colors)
            {
                int b = bottles.TryGetValue(c, out var nb) ? nb : 0;
                int t = trays.TryGetValue(c, out var nt) ? nt : 0;
                if (b == 0 && t == 0) errors.Add($"V5 colors: '{Code(c)}' is declared but never used");
                else if (b != t * level.TrayCapacity)
                    errors.Add($"V4 colour {Code(c)}: {b} bottles vs {t} trays × {level.TrayCapacity} = {t * level.TrayCapacity}");
            }
            return errors;
        }

        // messages name colours by their level-file code, so an error points straight at the JSON
        private static char Code(CapColor c) => CapColorCodes.ToCode(c);
    }

    public enum SolveStatus { Solvable, Unsolvable, Unknown }

    public sealed class SolveReport
    {
        public SolveStatus Status { get; }
        /// <summary>One winning tap sequence (lane indices), when <see cref="Status"/> is Solvable.</summary>
        public IReadOnlyList<int> Solution { get; }
        public int NodesExplored { get; }
        public int DeadEnds { get; }
        public SolveReport(SolveStatus status, IReadOnlyList<int> solution, int nodes, int deadEnds)
        { Status = status; Solution = solution; NodesExplored = nodes; DeadEnds = deadEnds; }

        /// <summary>A rough difficulty proxy: the share of explored states that lead nowhere.</summary>
        public double DeadEndRatio => NodesExplored == 0 ? 0 : (double)DeadEnds / NodesExplored;
    }

    /// <summary>
    /// V6 — depth-first search over lane taps with a visited-state memo. The rules are deterministic, so
    /// a state is fully described by <see cref="CapChaosGame.StateKey"/>; a state seen once is never
    /// expanded again. Bounded by a node budget: over budget ⇒ <see cref="SolveStatus.Unknown"/>, never a
    /// guess.
    /// </summary>
    public static class LevelSolver
    {
        public const int DefaultNodeBudget = 200_000;

        public static SolveReport Solve(LevelDefinition level, int nodeBudget = DefaultNodeBudget)
            => Solve(new CapChaosGame(level), nodeBudget);

        /// <summary>
        /// V6 proof: replay <see cref="LevelDefinition.Solution"/> when the level carries one (exact and
        /// instant — generated levels always do); otherwise search. A recorded solution that does NOT win is
        /// reported as Unsolvable-by-that-proof, never silently re-searched.
        /// </summary>
        public static SolveReport Prove(LevelDefinition level, int nodeBudget = DefaultNodeBudget)
        {
            if (level.Solution == null) return Solve(level, nodeBudget);
            var g = new CapChaosGame(level);
            foreach (int lane in level.Solution)
                if (!g.Tap(lane).Accepted) break;
            return g.Status == GameStatus.Won
                ? new SolveReport(SolveStatus.Solvable, level.Solution, level.Solution.Count, 0)
                : new SolveReport(SolveStatus.Unsolvable, System.Array.Empty<int>(), level.Solution.Count, 1);
        }

        public static SolveReport Solve(CapChaosGame start, int nodeBudget = DefaultNodeBudget)
        {
            var visited = new HashSet<string>();
            var path = new List<int>();
            int nodes = 0, dead = 0;
            bool overBudget = false;

            bool Dfs(CapChaosGame g)
            {
                if (g.Status == GameStatus.Won) return true;
                if (g.Status == GameStatus.Lost) { dead++; return false; }
                if (!visited.Add(g.StateKey())) return false;
                if (++nodes > nodeBudget) { overBudget = true; return false; }
                bool anyMove = false;
                for (int lane = 0; lane < g.LaneCount; lane++)
                {
                    if (g.LaneRemaining(lane) == 0) continue;
                    var next = g.Clone();
                    if (!next.Tap(lane).Accepted) continue;
                    anyMove = true;
                    path.Add(lane);
                    if (Dfs(next)) return true;
                    path.RemoveAt(path.Count - 1);
                    if (overBudget) return false;
                }
                if (!anyMove) dead++;
                return false;
            }

            bool ok = Dfs(start);
            var status = ok ? SolveStatus.Solvable : overBudget ? SolveStatus.Unknown : SolveStatus.Unsolvable;
            return new SolveReport(status, ok ? path.ToArray() : System.Array.Empty<int>(), nodes, dead);
        }
    }
}
