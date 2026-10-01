using System;
using System.Collections.Generic;
using System.Text;

namespace Game.Domain
{
    /// <summary>
    /// A flavour colour (GDD §4). ONE colour is shared by everything that matches: a bottle, the tray that
    /// takes it, the caps on that tray and the box it ships in. Hex values live in DesignTokens, never here.
    /// <see cref="None"/> is "no colour" (an empty slot, an empty lane, an empty stack cell).
    /// </summary>
    public enum CapColor : byte
    {
        None = 0,
        Red,
        Orange,
        Blue,
        Green,
        Purple,
        Yellow,
        Cyan,
        Brown,
    }

    /// <summary>
    /// The level format's spelling of <see cref="CapColor"/>: one uppercase letter per colour. Only the
    /// JSON codec, the level tool and the solver's state key speak codes — everything else speaks the enum.
    /// </summary>
    public static class CapColorCodes
    {
        /// <summary>Colour codes in enum order: <c>Codes[i]</c> is <c>(CapColor)(i + 1)</c>.</summary>
        public const string Codes = "ROBGPYCN";

        /// <summary>An empty stack cell in a level row string.</summary>
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

    /// <summary>One authored stack cell: empty, or a bottle of <see cref="Color"/> that may start hidden.</summary>
    public readonly struct StackCell : IEquatable<StackCell>
    {
        public readonly CapColor Color;
        public readonly bool Hidden;

        public StackCell(CapColor color, bool hidden) { Color = color; Hidden = hidden && color != CapColor.None; }

        public static StackCell Empty => default;
        public bool IsEmpty => Color == CapColor.None;

        /// <summary>'.' empty · uppercase code visible · lowercase code hidden.</summary>
        public char ToCode()
        {
            char c = CapColorCodes.ToCode(Color);
            return Hidden ? char.ToLowerInvariant(c) : c;
        }

        /// <summary>The inverse of <see cref="ToCode"/>.</summary>
        public static bool TryParse(char code, out StackCell cell)
        {
            cell = Empty;
            if (code == CapColorCodes.Empty) return true;
            if (!CapColorCodes.TryParse(char.ToUpperInvariant(code), out var color)) return false;
            cell = new StackCell(color, char.IsLower(code));
            return true;
        }

        public bool Equals(StackCell other) => Color == other.Color && Hidden == other.Hidden;
        public override bool Equals(object obj) => obj is StackCell o && Equals(o);
        public override int GetHashCode() => ((int)Color << 1) | (Hidden ? 1 : 0);
        public override string ToString() => ToCode().ToString();
    }
}
