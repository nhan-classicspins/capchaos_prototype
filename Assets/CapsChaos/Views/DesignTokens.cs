using UnityEngine;

namespace Game.Views
{
    /// <summary>
    /// Which flavour palette a bottle / tray / cap / box is painted with (art §3.2). The View-side twin of
    /// <c>Game.Domain.CapColor</c> — same members, same order — because Game.Views may not reference
    /// Game.Domain; the Gameplay controller maps one to the other (<c>CapColorTint</c>).
    /// </summary>
    public enum TintFlavor
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
        White,
        /// <summary>A hidden tray (GDD R17): tray and caps in the mystery slate instead of a flavour. View-only — the
        /// rules have no such colour.</summary>
        Mystery,
    }

    /// <summary>
    /// The SKU's single source of visual truth (rule 17) — except the live uGUI colours, which the SKU owner moved
    /// to the <c>UiPalette</c> asset (2026-10-01) so they can be tuned without a recompile; the UI colours below are
    /// that palette's defaults. Every value here is transcribed from
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

        // ── Gameplay colours (art §3.2) — indexed by TintFlavor ────────────────────────────────
        public readonly struct FlavorColors
        {
            public readonly Color Body, Shade, Cap;
            public FlavorColors(string body, string shade, string cap)
            { Body = Hex(body); Shade = Hex(shade); Cap = Hex(cap); }
        }

        /// <summary>Art-spec order R O B G P Y C N — <c>Flavors[i]</c> is <c>(TintFlavor)(i + 1)</c>.</summary>
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

        private static readonly FlavorColors Missing = new("FF00FF", "FF00FF", "FF00FF");

        /// <summary>Hidden tray (R17): a slate no flavour uses, dark enough to stand off the light belt; the white
        /// "?" mark on top carries the meaning. Decided here (rule 17) — not in the art spec yet.</summary>
        private static readonly FlavorColors MysteryColors = new("5F6A8F", "434C6E", "8792B8");

        /// <summary>The palette of <paramref name="flavor"/>; magenta for <see cref="TintFlavor.None"/> (a bug made visible).</summary>
        public static FlavorColors Flavor(TintFlavor flavor)
        {
            if (flavor == TintFlavor.Mystery) return MysteryColors;
            int i = (int)flavor - 1;
            return i >= 0 && i < Flavors.Length ? Flavors[i] : Missing;
        }

        /// <summary>The frosted band on a bottle body is its Body lifted this far toward white (art §4.1).</summary>
        public const float BottleBandLift = 0.15f;

        // ── Tray modifiers (GDD R17–R19) — decided here (rule 17), not in the art spec yet ─────
        /// <summary>The "?" printed on a hidden tray.</summary>
        public static readonly Color MysteryMark  = Hex("FFFFFF");
        /// <summary>Lock (R18): a charcoal body that keeps the white count readable on any tray colour, under a
        /// light steel shackle that keeps the icon's silhouette on the dark belt rail.</summary>
        public static readonly Color LockBody     = Hex("2F3554");
        public static readonly Color LockShackle  = Hex("D4DBEA");
        /// <summary>Link (R19): a hemp rope with a dark-brown outline — a neutral no flavour or tray uses.</summary>
        public static readonly Color Rope         = Hex("EBD5A4");
        public static readonly Color RopeOutline  = Hex("5A4630");

        // ── Hidden bottle rainbow (art §4.2) — consumed by the rainbow material at runtime ────
        public const float RainbowScrollPerSecond = 0.15f;

        // ── 3D board layout (ADR-001 §5, art §2) — board-local units, 1 = one bottle's height ───
        // Mirrors Editor/ArtGen/ArtShapes + ArtPreview (Game.Editor may not reference Game.Views, so the
        // generator keeps its own copy of the prop dimensions; the LAYOUT lives only here).
        public static class Board
        {
            /// <summary>Camera → board pose: the board tilts instead of the camera (GamePlay camera stays level).</summary>
            public const float ViewDistance = 19.5f, TiltDegrees = -60f, FocusZ = 1.2f;
            public const float CellPitch = 0.42f;          // bottle spacing on the stack grid
            public const float LayerHeight = 0.92f;        // a stacked bottle stands on the one below's shoulders
            public const float StackFrontZ = 3.45f;        // front row of the stack
            public const float StackMaxWidth = 3.8f, StackMaxDepth = 2.9f;   // auto-fit bounds before level stackScale
            public const float ColumnSpacing = 1.12f;      // slot / lane spacing along X
            public const float SlotZ = 1.55f, SlotTop = 0.05f;
            public const float SlotBandDepth = 1.35f;
            /// <summary>The slot row never grows wider than this (4 slots at full size); 5 slots shrink to fit.</summary>
            public const float SlotRowMaxWidth = 4.48f;
            /// <summary>Band = slot row + this margin (3 slots → 3 × 1.12 + 0.84 = 4.2, the art-preview band).</summary>
            public const float SlotBandMargin = 0.84f;
            public const float LaneFrontZ = 0.62f, LanePitch = 0.95f, TrayOnBeltOffset = 0.5f;
            public const int VisibleTraysPerLane = 6;
            /// <summary>A tapped-but-not-front tray shakes side to side on the ground plane by this much.</summary>
            public const float TrayShakeAmplitude = 0.07f;
            /// <summary>Tray hit box (tray-local): the slab plus the cell-filling caps on top.</summary>
            public static readonly Vector3 TrayHitSize = new Vector3(0.9f, 0.25f, 0.86f);
            public const float TrayHitCenterY = 0.12f;
            public const float TrayCellHalf = 0.21f, TrayCupY = 0.052f, CapOnNeckY = 0.925f, CapOnTrayScale = 2f;
            public const float BoxExitX = 3.5f, BoxExitY = 4.5f;
            /// <summary>Tray-local heights over the caps' tops (≈ 0.2): the lock icon, the rope's ends; and how far
            /// the rope arcs up between them.</summary>
            public const float LockY = 0.42f, RopeY = 0.24f, RopeArc = 0.16f;
            public const int RopeSegments = 14;
        }

        // ── Motion (GDD §2 timings measured off the reference video) ────────────────────────────
        public static class Motion
        {
            public const float TrayToSlot = 0.20f, LaneAdvance = 0.20f;
            public const float BottleFlight = 0.30f, BottleStagger = 0.12f, BottleArcHeight = 1.2f;
            public const float StackDrop = 0.18f, Reveal = 0.25f;
            public const float TrayShake = 0.35f, TrayShakeCycles = 3f;
            public const float BoxHold = 0.35f, BoxDrop = 0.25f, BoxFlaps = 0.25f, BoxExit = 0.40f;
            public const float RoundEndPause = 1.0f;
            /// <summary>Tray modifiers: the hidden tray's colour pop, a lock's count pop, the unlock (shackle lift + vanish), the rope letting go.</summary>
            public const float TrayReveal = 0.25f, LockTick = 0.2f, Unlock = 0.3f, LinkRelease = 0.15f;
        }

        // ── UI (art §3.3) ─────────────────────────────────────────────────────────────────────
        public static readonly Color HudPill    = Hex("2EBCFB");
        public static readonly Color HudPillLip = Hex("1E8FD0");
        public static readonly Color HudButton  = Hex("0C345B");
        /// <summary>The lighter ring round a HUD button and its hard drop shadow (ref 02_gameplay_start). Not in the
        /// art spec's table — decided here (rule 17): the rim is HudButton lifted toward HudPillLip, the shadow is
        /// HudButton pushed toward black.</summary>
        public static readonly Color HudButtonRim    = Hex("2F6FA8");
        public static readonly Color HudButtonShadow = Hex("06192C");
        public static readonly Color TextOnFill = Hex("FFFFFF");
        /// <summary>
        /// Default text colour for a <see cref="TintFlavor"/> key (TextTint): <see cref="TintFlavor.None"/> is the
        /// display white; a flavour reads as its Body colour. Defaults only — the live values are in
        /// <c>Content/UI/UiPalette.asset</c> (see UiPalette).
        /// </summary>
        public static Color TextColor(TintFlavor flavor) =>
            flavor == TintFlavor.None || flavor == TintFlavor.White ? TextOnFill : Flavor(flavor).Body;
        /// <summary>A raycast-only surface (the board hit-catcher): receives pointer events, draws nothing.</summary>
        public static readonly Color Invisible  = new Color(0f, 0f, 0f, 0f);

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

        /// <summary>
        /// Screen UI rhythm on the 1 px = 1 unit rig (canvas reference 1080 × 1920, safe rect x ∈ ±540). ONE base
        /// unit; every gap is a multiple of it. Three type sizes per screen (art §5).
        /// </summary>
        public static class Ui
        {
            public const float Unit = 12f;

            public const float FontTitle = 96f;       // screen title
            public const float FontValue = 88f;       // the big number on a tile
            public const float FontLabel = 34f;       // secondary line on a tile

            public const float EdgePad = 5 * Unit;    // 60 — screen edge → content
            public const float Gap = 3 * Unit;        // 36 — between tiles
            public const float TitleHeight = 16 * Unit; // 192 — title band at the top of a list screen

            /// <summary>Level tiles: 4 columns fill the 1080 safe width: 2 × 60 + 4 × 213 + 3 × 36 = 1080.</summary>
            public const int TileColumns = 4;
            public const float TileSize = 213f;
            public const float TileLip = Unit;        // the darker lip under a pill (art §5 "gờ dưới")
            public const float TilePress = 8f;        // how far the face sinks while pressed
            public const float TileNumberDrop = 10f;  // the number sits a little above centre, the label below it

            /// <summary>Round HUD buttons (art §5): ≈ 9 % of the 1080 width, a ≥ 120 px touch target, in the top corners.</summary>
            public const float HudButtonHit = 120f;     // the touch target (invisible)
            public const float HudButtonSize = 100f;    // the visible disc (≈ 9 % of 1080)
            public const float HudButtonRimWidth = 6f;
            public const float HudButtonShadowDrop = 8f;
            public const float HudButtonIcon = 52f;
            public const float HudInset = 4 * Unit;     // 48 — from the safe-area corner to the button's touch box

            /// <summary>Result popup (art §5, refs 09/10): a 3-band panel over one big primary button, on the Popup host.</summary>
            public const float ResultPanelWidth = 780f;
            public const float ResultHeader = 72f, ResultBody = 330f, ResultBand = 120f;
            public const float ResultPanelLip = 12f;
            public const float ResultPanelY = 150f;             // panel centre above the screen centre
            public const float ResultButtonWidth = 560f, ResultButtonHeight = 190f;
            public const float ResultButtonLip = 16f;           // ≈ 12 % of the face, art §5 "gờ dưới dày 12 %"
            public const float ResultGap = 5 * Unit;            // 60 — panel → button
            public const float FontResultTitle = 124f, FontResultButton = 104f, FontResultSubtitle = 46f;

            /// <summary>Rounded-rect sprite corner radius in texture px (== canvas px at PPU 1).</summary>
            public const float CornerRadius = 40f;
        }

        public static readonly ResultTheme Win  = new("57935B", "4B9B8B", "5DB987", "89D851", "86D64F", "75C34C", "428C66");
        public static readonly ResultTheme Lose = new("5733A2", "984BF5", "B34DFF", "D972FE", "B34FE6", "8434AB", "5E2A86");
    }
}
