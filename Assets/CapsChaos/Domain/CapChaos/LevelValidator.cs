using System.Collections.Generic;

namespace Game.Domain
{
    /// <summary>
    /// Semantic level checks V4, V5, V7, V8 and V9 (GDD §6.4). V1 (structure) is <see cref="LevelJson.Parse"/>; V6
    /// (solvability) is <see cref="LevelSolver"/>. Every problem is reported, not just the first.
    /// </summary>
    public static class LevelValidator
    {
        public static List<string> Validate(LevelDefinition level)
        {
            var errors = new List<string>();
            var declared = new HashSet<CapColor>(level.Colors);
            var bottles = new Dictionary<CapColor, int>();
            var trays = new Dictionary<CapColor, int>();

            var lp = level.Loop;
            if (lp.Initial != null)
                for (int r = 0; r < lp.Initial.Count; r++)
                    for (int k = 0; k < lp.Initial[r].Count; k++)
                        Count(lp.Initial[r][k], $"loop.initial[{r}][{k}]");
            for (int f = 0; f < lp.Feeders.Count; f++)
                for (int i = 0; i < lp.Feeders[f].Bottles.Count; i++)
                    Count(lp.Feeders[f].Bottles[i], $"loop.feeders[{f}].bottles[{i}]");

            void Count(CapColor c, string where)
            {
                if (c == CapColor.None) return;
                if (!declared.Contains(c)) errors.Add($"V5 {where}: colour {Code(c)} is not in colors");
                bottles[c] = (bottles.TryGetValue(c, out var n) ? n : 0) + 1;
            }

            for (int j = 0; j < level.Lanes.Count; j++)
                for (int t = 0; t < level.Lanes[j].Count; t++)
                {
                    var c = level.Lanes[j][t];
                    if (!declared.Contains(c)) errors.Add($"V5 lanes[{j}][{t}]: colour {Code(c)} is not in colors");
                    trays[c] = (trays.TryGetValue(c, out var n) ? n : 0) + level.CapacityOf(new TrayRef(j, t));   // R21: by size
                }

            foreach (var c in level.Colors)
            {
                int b = bottles.TryGetValue(c, out var nb) ? nb : 0;
                int t = trays.TryGetValue(c, out var nt) ? nt : 0;
                if (b == 0 && t == 0) errors.Add($"V5 colors: {Code(c)} is declared but never used");
                else if (b != t)
                    errors.Add($"V4 colour {Code(c)}: {b} bottles vs {t} tray places (trays × size × trayCapacity {level.TrayCapacity})");
            }
            ValidateLoop(lp, errors);
            var shape = level.Shape;
            if (shape.Preset == null) errors.AddRange(LoopShape.Check(shape.Xs, shape.Zs, shape.Radii, "V9 view.loopShape"));
            ValidateTrayModifiers(level, errors);
            return errors;
        }

        /// <summary>V8: the oval has a straight on each side of its two bends (the pick zone is less than half of it);
        /// every merge point is on the track, outside the pick zone and used by one feeder only; a feeder carries bottles.</summary>
        private static void ValidateLoop(LoopDefinition lp, List<string> errors)
        {
            if (lp.PickRows * 2 + MinBendRows > lp.Rows)
                errors.Add($"V8 loop.pickRows: {lp.PickRows} pick rows need rows ≥ {lp.PickRows * 2 + MinBendRows} (two straights + the bends), not {lp.Rows}");
            var merges = new HashSet<int>();
            for (int f = 0; f < lp.Feeders.Count; f++)
            {
                var fd = lp.Feeders[f];
                string where = $"V8 loop.feeders[{f}]";
                if (fd.MergeAt < 0 || fd.MergeAt >= lp.Rows) errors.Add($"{where}.mergeAt: {fd.MergeAt} is not a track position 0..{lp.Rows - 1}");
                else if (fd.MergeAt < lp.PickRows) errors.Add($"{where}.mergeAt: {fd.MergeAt} is inside the pick zone 0..{lp.PickRows - 1}");
                if (!merges.Add(fd.MergeAt)) errors.Add($"{where}.mergeAt: {fd.MergeAt} is another feeder's merge point");
                if (fd.Bottles.Count == 0) errors.Add($"{where}.bottles: a feeder carries at least one bottle");
            }
        }

        /// <summary>Rows the two bends of the oval take at least, so the belt can turn round its own width.</summary>
        public const int MinBendRows = 6;

        /// <summary>V7: every lock / link names a real tray; a link joins two neighbours (same lane, consecutive — or
        /// two neighbouring lanes, same position); a tray is in at most one link and is never both linked and locked.</summary>
        private static void ValidateTrayModifiers(LevelDefinition level, List<string> errors)
        {
            bool Exists(TrayRef t) => t.Lane >= 0 && t.Lane < level.Lanes.Count && t.Index >= 0 && t.Index < level.Lanes[t.Lane].Count;

            var locked = new HashSet<TrayRef>();
            for (int i = 0; i < level.Locks.Count; i++)
            {
                var l = level.Locks[i];
                if (!Exists(l.Tray)) errors.Add($"V7 locks[{i}]: {l.Tray} does not exist");
                else if (!locked.Add(l.Tray)) errors.Add($"V7 locks[{i}]: {l.Tray} is locked twice");
            }

            var linked = new HashSet<TrayRef>();
            for (int i = 0; i < level.Links.Count; i++)
            {
                var a = level.Links[i].A; var b = level.Links[i].B;
                string where = $"V7 links[{i}]";
                if (!Exists(a) || !Exists(b)) { errors.Add($"{where}: {(Exists(a) ? b : a)} does not exist"); continue; }
                bool sameLane = a.Lane == b.Lane && System.Math.Abs(a.Index - b.Index) == 1;
                bool sideBySide = System.Math.Abs(a.Lane - b.Lane) == 1 && a.Index == b.Index;
                if (!sameLane && !sideBySide)
                    errors.Add($"{where}: {a} and {b} are not neighbours (same lane one apart, or neighbouring lanes at the same position)");
                foreach (var t in new[] { a, b })
                {
                    if (!linked.Add(t)) errors.Add($"{where}: {t} is already in another link");
                    if (locked.Contains(t)) errors.Add($"{where}: {t} is locked — a linked tray can not also be locked");
                }
            }
        }

        // messages name colours by their level-file number (and name), so an error points straight at the JSON
        private static string Code(CapColor c) => LevelJson.Name(c);
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
    /// V6 — depth-first search over lane taps with a visited-state memo. The belt never stops, so the searched
    /// player taps only when the board is quiet (<see cref="CapChaosGame.Settle"/> after every tap): a winning
    /// sequence for that player is one a real player can play too. The rules are deterministic, so a state is fully
    /// described by <see cref="CapChaosGame.StateKey"/>; a state seen once is never expanded again. Bounded by a node budget: over budget ⇒ <see cref="SolveStatus.Unknown"/>, never a
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
            g.Settle();
            foreach (int lane in level.Solution)
            {
                if (!g.Tap(lane).Accepted) break;
                g.Settle();
            }
            return g.Status == GameStatus.Won && !g.SlotsRanOut
                ? new SolveReport(SolveStatus.Solvable, level.Solution, level.Solution.Count, 0)
                : new SolveReport(SolveStatus.Unsolvable, System.Array.Empty<int>(), level.Solution.Count, 1);
        }

        public static SolveReport Solve(CapChaosGame start, int nodeBudget = DefaultNodeBudget)
        {
            start = start.Clone();
            start.Settle();
            var visited = new HashSet<string>();
            var path = new List<int>();
            int nodes = 0, dead = 0;
            bool overBudget = false;

            bool Dfs(CapChaosGame g)
            {
                if (g.Status == GameStatus.Won) return true;
                if (g.Status == GameStatus.Lost || g.SlotsRanOut) { dead++; return false; }   // V6 proves it with the open slots only
                if (!visited.Add(g.StateKey())) return false;
                if (++nodes > nodeBudget) { overBudget = true; return false; }
                bool anyMove = false;
                for (int lane = 0; lane < g.LaneCount; lane++)
                {
                    if (g.LaneRemaining(lane) == 0) continue;
                    var next = g.Clone();
                    if (!next.Tap(lane).Accepted) continue;
                    next.Settle();
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
