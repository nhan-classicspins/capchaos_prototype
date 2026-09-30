using System;
using System.Collections.Generic;
using System.Text;

namespace Game.Domain
{
    public readonly struct Bottle
    {
        public readonly char Color;   // uppercase code
        public readonly bool Hidden;
        public Bottle(char color, bool hidden) { Color = color; Hidden = hidden; }
    }

    /// <summary>
    /// The 3D bottle grid with gravity (GDD R1–R4). Coordinates: <c>x</c> = column (0 = left),
    /// <c>z</c> = depth (0 = FRONT, nearest the player), height = index in the cell's pile (0 = ground).
    /// Authored rows run back→front, so row <c>r</c> maps to <c>z = Rows − 1 − r</c>.
    /// </summary>
    public sealed class BottleStack
    {
        public int Cols { get; }
        public int Depth { get; }
        private readonly List<Bottle>[] _cells;   // index x * Depth + z; element 0 = ground
        public int Count { get; private set; }
        public bool IsEmpty => Count == 0;

        private BottleStack(int cols, int depth)
        {
            Cols = cols; Depth = depth;
            _cells = new List<Bottle>[cols * depth];
            for (int i = 0; i < _cells.Length; i++) _cells[i] = new List<Bottle>();
        }

        public static BottleStack FromDefinition(StackDefinition def)
        {
            var s = new BottleStack(def.Cols, def.Rows);
            for (int k = 0; k < def.Layers.Count; k++)
                for (int r = 0; r < def.Rows; r++)
                    for (int x = 0; x < def.Cols; x++)
                    {
                        char c = def.At(k, r, x);
                        if (c == CapColors.Empty) continue;
                        // gravity at load: a bottle over a gap settles onto whatever is below it (the validator
                        // rejects that authoring, V2; the model still never floats a bottle)
                        s._cells[x * s.Depth + (def.Rows - 1 - r)].Add(new Bottle(char.ToUpperInvariant(c), char.IsLower(c)));
                        s.Count++;
                    }
            return s;
        }

        public BottleStack Clone()
        {
            var c = new BottleStack(Cols, Depth) { Count = Count };
            for (int i = 0; i < _cells.Length; i++) c._cells[i].AddRange(_cells[i]);
            return c;
        }

        public int Height(int x, int z) => _cells[x * Depth + z].Count;
        public Bottle At(int x, int z, int height) => _cells[x * Depth + z][height];

        /// <summary>The front-most occupied depth of column <paramref name="x"/>, or −1 (R2).</summary>
        public int FrontZ(int x)
        {
            for (int z = 0; z < Depth; z++) if (_cells[x * Depth + z].Count > 0) return z;
            return -1;
        }

        /// <summary>R2: ground-level AND front-most in its column. A hidden bottle is never exposed.</summary>
        public bool IsExposed(int x, int z)
        {
            if (FrontZ(x) != z) return false;
            return !_cells[x * Depth + z][0].Hidden;
        }

        /// <summary>
        /// Remove the ground bottle at (x, z): the pile above drops one level (R3) and a hidden bottle that
        /// reaches the ground is revealed (R4). Facts are appended in the order they happen.
        /// </summary>
        public Bottle TakeGround(int x, int z, List<GameFact> facts)
        {
            var cell = _cells[x * Depth + z];
            if (cell.Count == 0) throw new InvalidOperationException($"no bottle at ({x},{z})");
            var taken = cell[0];
            cell.RemoveAt(0);
            Count--;
            if (cell.Count > 0)
            {
                facts?.Add(new StackDropped(x, z, cell.Count));
                if (cell[0].Hidden)
                {
                    cell[0] = new Bottle(cell[0].Color, false);
                    facts?.Add(new BottleRevealed(x, z, cell[0].Color));
                }
            }
            return taken;
        }

        /// <summary>Exact state fingerprint (solver memo).</summary>
        public void AppendKey(StringBuilder sb)
        {
            foreach (var cell in _cells)
            {
                foreach (var b in cell) sb.Append(b.Hidden ? char.ToLowerInvariant(b.Color) : b.Color);
                sb.Append('|');
            }
        }
    }
}
