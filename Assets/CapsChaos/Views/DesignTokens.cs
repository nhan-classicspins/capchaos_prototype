using UnityEngine;

namespace Game.Views
{
    /// <summary>
    /// The SKU's single source of visual truth (rule 17). Every value here is transcribed from
    /// <c>docs/art-direction.md</c> §3 — that spec outranks this file; when they disagree, fix this
    /// file in the same change. Views index these tokens; nothing redeclares a colour.
    /// </summary>
    public static class DesignTokens
    {
        public static Color Hex(string rrggbb) =>
            ColorUtility.TryParseHtmlString("#" + rrggbb, out var c) ? c : Color.magenta;

        // ── Environment (art §3.1) ────────────────────────────────────────────────────────────
        public static readonly Color Ground     = Hex("4D5680");
        public static readonly Color GroundTop  = Hex("5A6090");
        public static readonly Color Floor      = Hex("525781");
        public static readonly Color SlotBand   = Hex("474F7A");
        public static readonly Color SlotEmpty  = Hex("3E4E78");
        public static readonly Color Divider    = Hex("3C4162");
        public static readonly Color LaneRail   = Hex("25272E");
        public static readonly Color LaneBeltA  = Hex("D5E0EE");
        public static readonly Color LaneBeltB  = Hex("CAD2DF");
        public static readonly Color Tape       = Hex("F4F1EA");

        // ── Gameplay colours (art §3.2) — indexed by the level-JSON colour code ──────────────
        public readonly struct FlavorColors
        {
            public readonly Color Body, Shade, Cap;
            public FlavorColors(string body, string shade, string cap)
            { Body = Hex(body); Shade = Hex(shade); Cap = Hex(cap); }
        }

        /// <summary>Colour codes in art-spec order: R O B G P Y C N.</summary>
        public const string FlavorCodes = "ROBGPYCN";

        private static readonly FlavorColors[] Flavors =
        {
            new("EF2B86", "B81F65", "F692C2"), // R pink-red
            new("E3761B", "A65100", "F4A15A"), // O orange
            new("0098FB", "076DBD", "5CC0FF"), // B blue
            new("2FA36B", "006636", "81CBA4"), // G green
            new("8E4BF0", "5E2BB0", "C29BFA"), // P purple
            new("F7C520", "B98A00", "FFE27A"), // Y yellow
            new("18C3C8", "0D8589", "8EE9EC"), // C cyan
            new("8B5A3A", "5A3620", "C08A66"), // N brown
        };

        /// <summary>Upper- or lower-case code (lower = hidden in level JSON; the colour is the same).</summary>
        public static FlavorColors Flavor(char code)
        {
            int i = FlavorCodes.IndexOf(char.ToUpperInvariant(code));
            return i >= 0 ? Flavors[i] : new FlavorColors("FF00FF", "FF00FF", "FF00FF");
        }

        /// <summary>The frosted band on a bottle body is its Body lifted this far toward white (art §4.1).</summary>
        public const float BottleBandLift = 0.15f;

        // ── Hidden bottle rainbow (art §4.2) — consumed by the rainbow material at runtime ────
        public const float RainbowScrollPerSecond = 0.15f;

        // ── UI (art §3.3) ─────────────────────────────────────────────────────────────────────
        public static readonly Color HudPill    = Hex("2EBCFB");
        public static readonly Color HudPillLip = Hex("1E8FD0");
        public static readonly Color HudButton  = Hex("0C345B");
        public static readonly Color TextOnFill = Hex("FFFFFF");

        public readonly struct ResultTheme
        {
            public readonly Color Dim, Header, Body, Band, ButtonTop, ButtonBottom, ButtonLip;
            public ResultTheme(string dim, string header, string body, string band, string top, string bottom, string lip)
            {
                Dim = Hex(dim); Dim.a = 0.85f;
                Header = Hex(header); Body = Hex(body); Band = Hex(band);
                ButtonTop = Hex(top); ButtonBottom = Hex(bottom); ButtonLip = Hex(lip);
            }
        }

        public static readonly ResultTheme Win  = new("57935B", "4B9B8B", "5DB987", "89D851", "86D64F", "75C34C", "428C66");
        public static readonly ResultTheme Lose = new("5733A2", "984BF5", "B34DFF", "D972FE", "B34FE6", "8434AB", "5E2A86");
    }
}
