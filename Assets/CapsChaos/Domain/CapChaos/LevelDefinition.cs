using System;
using System.Collections.Generic;

namespace Game.Domain
{
    /// <summary>The colour codes of the level format (GDD §4). Hex values live in DesignTokens, never here.</summary>
    public static class CapColors
    {
        public const string Codes = "ROBGPYCN";
        public const char Empty = '.';

        public static bool IsCode(char c) => Codes.IndexOf(c) >= 0;

        /// <summary>A stack cell char: '.', an uppercase code (visible) or a lowercase code (hidden).</summary>
        public static bool IsCell(char c) => c == Empty || IsCode(c) || IsCode(char.ToUpperInvariant(c)) && char.IsLower(c);
    }

    /// <summary>The bottle stack exactly as authored: <c>Layers[k][row]</c> is a string of <c>Cols</c> chars;
    /// layer 0 is the ground; row 0 is the BACK row and row Rows-1 the FRONT row.</summary>
    public sealed class StackDefinition
    {
        public int Cols { get; }
        public int Rows { get; }
        public IReadOnlyList<IReadOnlyList<string>> Layers { get; }

        public StackDefinition(int cols, int rows, IReadOnlyList<IReadOnlyList<string>> layers)
        {
            Cols = cols; Rows = rows; Layers = layers ?? throw new ArgumentNullException(nameof(layers));
        }

        public char At(int layer, int row, int col) => Layers[layer][row][col];
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
        public IReadOnlyList<char> Colors { get; }
        public StackDefinition Stack { get; }
        /// <summary><c>Lanes[j][0]</c> is the tappable front tray of conveyor j.</summary>
        public IReadOnlyList<IReadOnlyList<char>> Lanes { get; }
        public string CameraPreset { get; }
        public double StackScale { get; }
        public string Name { get; }
        public string Difficulty { get; }
        public string Notes { get; }
        /// <summary>Optional winning tap sequence (lane indices) — V6 replays it as proof of solvability.</summary>
        public IReadOnlyList<int> Solution { get; }

        public LevelDefinition(string id, int slots, int trayCapacity, IReadOnlyList<char> colors,
            StackDefinition stack, IReadOnlyList<IReadOnlyList<char>> lanes,
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
