using System;
using System.Collections.Generic;
using System.Text;

namespace Game.Domain
{
    /// <summary>
    /// A flavour colour (GDD §4). ONE colour is shared by everything that matches: a bottle, the tray that
    /// takes it, the caps on that tray and the box it ships in. Hex values live in DesignTokens, never here.
    /// <see cref="None"/> is "no colour" (an empty slot, an empty lane, an empty belt spot).
    /// <para>The NUMBERS are the level file's spelling (GDD §6.2) — designers' tools write them, so a value
    /// is never renumbered or reused; a new colour takes the next number.</para>
    /// </summary>
    public enum CapColor : byte
    {
        None = 0,
        Red = 1,
        Orange = 2,
        Blue = 3,
        Green = 4,
        Purple = 5,
        Yellow = 6,
        Cyan = 7,
        Brown = 8,
    }

    /// <summary>
    /// One uppercase letter per <see cref="CapColor"/>: the
    /// LevelTool spec, the solver's state key and terse test levels. Level files use the enum's numbers.
    /// </summary>
    public static class CapColorCodes
    {
        /// <summary>Colour codes in enum order: <c>Codes[i]</c> is <c>(CapColor)(i + 1)</c>.</summary>
        public const string Codes = "ROBGPYCN";

        /// <summary>An empty belt spot in a state key or a terse test row.</summary>
        public const char Empty = '.';

        private static readonly CapColor[] AllColors =
            { CapColor.Red, CapColor.Orange, CapColor.Blue, CapColor.Green, CapColor.Purple, CapColor.Yellow, CapColor.Cyan, CapColor.Brown };

        /// <summary>Every real colour (no <see cref="CapColor.None"/>), in code order.</summary>
        public static IReadOnlyList<CapColor> All => AllColors;

        /// <summary>The uppercase code of <paramref name="color"/>; <see cref="Empty"/> for <see cref="CapColor.None"/>.</summary>
        public static char ToCode(CapColor color)
        {
            int i = (int)color - 1;
            if (color == CapColor.None) return Empty;
            if (i < 0 || i >= Codes.Length) throw new ArgumentOutOfRangeException(nameof(color), color, "not a CapColor");
            return Codes[i];
        }

        /// <summary>Uppercase code → colour. Lowercase, '.', or anything else is false.</summary>
        public static bool TryParse(char code, out CapColor color)
        {
            int i = Codes.IndexOf(code);
            color = i >= 0 ? (CapColor)(i + 1) : CapColor.None;
            return i >= 0;
        }

        public static CapColor Parse(char code) =>
            TryParse(code, out var c) ? c : throw new FormatException($"'{code}' is not a colour code ({Codes})");

        /// <summary>"ROBG" → [Red, Orange, Blue, Green].</summary>
        public static List<CapColor> ParseList(string codes)
        {
            var list = new List<CapColor>(codes.Length);
            foreach (char c in codes) list.Add(Parse(c));
            return list;
        }

        /// <summary>[Red, Orange] → "RO".</summary>
        public static string ToCodes(IEnumerable<CapColor> colors)
        {
            var sb = new StringBuilder();
            foreach (var c in colors) sb.Append(ToCode(c));
            return sb.ToString();
        }
    }
}
