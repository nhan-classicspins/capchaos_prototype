using System;
using System.Collections.Generic;

namespace Game.Domain
{
    /// <summary>The bottle stack as authored: <c>At(layer, row, col)</c>; layer 0 is the ground; row 0 is the
    /// BACK row and row Rows-1 the FRONT row. Immutable.</summary>
    public sealed class StackDefinition
    {
        public int Cols { get; }
        public int Rows { get; }
        public int LayerCount { get; }
        private readonly StackCell[,,] _cells;   // [layer, row, col]

        /// <param name="cells">Indexed <c>[layer, row, col]</c>; copied, so the caller may reuse it.</param>
        public StackDefinition(StackCell[,,] cells)
        {
            if (cells == null) throw new ArgumentNullException(nameof(cells));
            LayerCount = cells.GetLength(0); Rows = cells.GetLength(1); Cols = cells.GetLength(2);
            _cells = (StackCell[,,])cells.Clone();
        }

        /// <summary>From the level format's row strings (<c>layers[k][row]</c>, one cell code per column).</summary>
        /// <exception cref="FormatException">A row has the wrong length or a char that is not a cell code.</exception>
        public static StackDefinition FromRows(int cols, int rows, IReadOnlyList<IReadOnlyList<string>> layers)
        {
            if (layers == null) throw new ArgumentNullException(nameof(layers));
            var cells = new StackCell[layers.Count, rows, cols];
            for (int k = 0; k < layers.Count; k++)
            {
                if (layers[k].Count != rows) throw new FormatException($"layer {k}: {layers[k].Count} rows ≠ {rows}");
                for (int r = 0; r < rows; r++)
                {
                    string line = layers[k][r];
                    if (line.Length != cols) throw new FormatException($"layer {k} row {r}: length {line.Length} ≠ {cols}");
                    for (int x = 0; x < cols; x++)
                        if (!StackCell.TryParse(line[x], out cells[k, r, x]))
                            throw new FormatException($"layer {k} row {r} col {x}: '{line[x]}' is not a cell code");
                }
            }
            return new StackDefinition(cells);
        }

        public StackCell At(int layer, int row, int col) => _cells[layer, row, col];

        /// <summary>One row in the level format (the writer's view).</summary>
        public string RowCodes(int layer, int row)
        {
            var line = new char[Cols];
            for (int x = 0; x < Cols; x++) line[x] = _cells[layer, row, x].ToCode();
            return new string(line);
        }
    }

    /// <summary>One level, fully data-driven (GDD §6). Immutable; build it with <see cref="LevelJson.Parse"/>.</summary>
    public sealed class LevelDefinition
    {
        public const int CurrentFormatVersion = 1;
        public const int DefaultSlots = 3;
        public const int DefaultTrayCapacity = 4;

        public int FormatVersion { get; }
        public string Id { get; }
        public int Slots { get; }
        public int TrayCapacity { get; }
        public IReadOnlyList<CapColor> Colors { get; }
        public StackDefinition Stack { get; }
        /// <summary><c>Lanes[j][0]</c> is the tappable front tray of conveyor j.</summary>
        public IReadOnlyList<IReadOnlyList<CapColor>> Lanes { get; }
        public string CameraPreset { get; }
        public double StackScale { get; }
        public string Name { get; }
        public string Difficulty { get; }
        public string Notes { get; }
        /// <summary>Optional winning tap sequence (lane indices) — V6 replays it as proof of solvability.</summary>
        public IReadOnlyList<int> Solution { get; }

        public LevelDefinition(string id, int slots, int trayCapacity, IReadOnlyList<CapColor> colors,
            StackDefinition stack, IReadOnlyList<IReadOnlyList<CapColor>> lanes,
            string cameraPreset = "default", double stackScale = 1.0,
            string name = null, string difficulty = null, string notes = null,
            int formatVersion = CurrentFormatVersion, IReadOnlyList<int> solution = null)
        {
            Solution = solution;
            FormatVersion = formatVersion;
            Id = id ?? throw new ArgumentNullException(nameof(id));
            Slots = slots; TrayCapacity = trayCapacity;
            Colors = colors ?? throw new ArgumentNullException(nameof(colors));
            Stack = stack ?? throw new ArgumentNullException(nameof(stack));
            Lanes = lanes ?? throw new ArgumentNullException(nameof(lanes));
            CameraPreset = cameraPreset ?? "default"; StackScale = stackScale;
            Name = name; Difficulty = difficulty; Notes = notes;
        }
    }
}
