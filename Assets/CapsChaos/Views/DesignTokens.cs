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
        /// <summary>A locked extra slot (R20): a dimmer tile with a green plus — decided here (rule 17), after the reference.</summary>
        public static readonly Color SlotLocked = Hex("353C62");
        public static readonly Color SlotPlus   = Hex("5BD45B");

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

        // ── 3D board layout (ADR-001 §5, art §2) — board-local units, 1 = one bottle's height ───
        // Mirrors Editor/ArtGen/ArtShapes + ArtPreview (Game.Editor may not reference Game.Views, so the
        // generator keeps its own copy of the prop dimensions; the LAYOUT lives only here).
        public static class Board
        {
            /// <summary>Camera → board pose (ADR-001 rev. 2026-10-02: orthographic GamePlay camera). The camera stays level;
            /// the board tilts, sits ViewDistance WORLD units in front of it (the canvas plane), with its focus point
            /// (0, 0, FocusZ) on the view axis, and scales so ViewHeight BOARD units fill the SAFE RECT's height (a taller
            /// screen shows more board above and below; the safe width, ViewHeight × SafeAspect, is always on screen).</summary>
            public const float ViewDistance = 1000f, TiltDegrees = -60f, FocusZ = 1.2f, ViewHeight = 11.6f;
            /// <summary>The safe rect's width / height (1080 × 1920).</summary>
            public const float SafeAspect = 1080f / 1920f;
            /// <summary>The belt loop (GDD R1–R4), loop-local units before the fit: rows stand evenly round it, at least
            /// LoopRowPitch apart; LoopTrackSpacing between the bottles of a row (a bottle is 0.38 across). At the tightest
            /// corner the INNER track keeps rows at least LoopInnerPitch apart (under 0.38: a little overlap there is
            /// accepted — product owner, 2026-10-02) and leaves a hole of at least LoopMinHole.</summary>
            public const float LoopRowPitch = 0.44f, LoopTrackSpacing = 0.42f, LoopInnerPitch = 0.30f, LoopMinHole = 0.45f;
            public const float LoopBeltMargin = 0.12f, LoopBeltTop = 0.03f;
            public const float LoopRailWidth = 0.1f, LoopRailHeight = 0.16f;
            /// <summary>Mesh segments per row of path — enough that the bends read round.</summary>
            public const int LoopRowSegments = 6;
            /// <summary>Path length per mesh segment along a feeder's curve.</summary>
            public const float LoopMeshStep = 0.12f;
            /// <summary>The front edge of the belt (board z) and the box the whole oval shrinks to fit (board units).</summary>
            public const float LoopFrontZ = 2.6f;
            /// The oval is always centred across the board.</summary>
            public const float LoopMaxWidth = 4.0f, LoopMaxDepth = 3.7f;
            /// <summary>A feeder queue (an on-ramp merging into the oval) — how many rows of it are drawn. Where it comes in
            /// (board units): from the top edge it starts FeederSwing beyond a belt's width out from the merge point and
            /// FeederTopLead upstream of it (a left-bend feeder is the mirror image of a right-side one).</summary>
            public const int FeederVisibleRows = 12;
            public const float FeederSwing = 0.3f, FeederTopLead = 0.6f;
            /// <summary>How long (board units) a queue runs along the oval's tangent as it lands on it.</summary>
            public const float FeederMergeRun = 0.5f;
            /// <summary>Queues sharing the top edge stand as evenly spread columns; the outer ones keep this clear of the
            /// safe rect's side (board units).</summary>
            public const float FeederEdgeMargin = 0.25f;
            /// <summary>Rows past the merge row a joining bottle may still be sliding over (the belt keeps moving); a rail
            /// never stands there.</summary>
            public const float FeederLandRows = 1f;
            /// <summary>A bottle's radius (0.38 across) — the clearance a rail keeps from a sliding bottle.</summary>
            public const float BottleRadius = 0.19f;
            /// <summary>Rows past the merge row where the queue has fully merged into the oval (tangent to it).</summary>
            public const float FeederMergeRows = 0.5f;
            /// <summary>Rows between a queue's entrance (its merge position, where its head row stands) and where its ramp
            /// lands on the loop — how far a ramp converges before it has fully merged.</summary>
            public const float FeederEntryRows = 1f;
            /// <summary>The queue's belt sits this much under the oval's, so the oval's wins where they overlap.</summary>
            public const float FeederBeltSink = 0.006f;
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
            /// <summary>The "+" on a locked slot (R20), slot-tile-local: arm length, arm width, thickness, height; and the
            /// tile's tap box.</summary>
            public const float SlotPlusLength = 0.46f, SlotPlusWidth = 0.13f, SlotPlusThickness = 0.03f, SlotPlusY = 0.04f;
            public static readonly Vector3 SlotHitSize = new Vector3(1.02f, 0.3f, 0.98f);
            /// <summary>Tray-local heights over the caps' tops (≈ 0.2): the lock icon, the rope's ends; and how far
            /// the rope arcs up between them.</summary>
            public const float LockY = 0.42f, RopeY = 0.24f, RopeArc = 0.16f;
            public const int RopeSegments = 14;
        }

        // ── Motion (GDD §2 timings measured off the reference video) ────────────────────────────
        /// <summary>Scene-view gizmos (editor only, never in the game view): the feeders' merge points (art §4.2b).</summary>
        public static class Gizmo
        {
            public static readonly Color Entrance = Hex("FFD400"), Reach = Hex("00D1FF"), Head = Hex("3DFF6E"), Land = Hex("FF3DD8");
            /// <summary>Marker sizes and how far above the belt they float (loop-local units).</summary>
            public const float Marker = 0.22f, Lift = 0.35f;
        }

        public static class Motion
        {
            public const float TrayToSlot = 0.20f, LaneAdvance = 0.20f;
            public const float BottleFlight = 0.30f, BottleArcHeight = 1.2f;
            /// <summary>Oval belt speed (≈ the reference video's crowd), a feeder bottle sliding onto the belt, a feeder queue
            /// moving up one row; how far ahead of the last fixed tick the belt may be drawn.</summary>
            public const float BeltRowsPerSecond = 3.5f, FeederStep = 0.2f, BeltExtrapolateMax = 0.06f;
            /// <summary>A bottle stepping from a feeder onto the belt walks to its own spot on its own (art §4.2b): it
            /// waits up to BottleJoinDelayMax, takes BottleJoinRamp to reach its pace — BottleJoinSpeed board units/s,
            /// ± BottleJoinSpeedSpread of it, well above the belt's own ~0.75 — and chases the spot as it moves.
            /// BottleJoinMax is the safety cap after which it simply snaps.</summary>
            public const float BottleJoinSpeed = 4.4f, BottleJoinSpeedSpread = 0.25f, BottleJoinDelayMax = 0.12f;
            public const float BottleJoinRamp = 0.08f, BottleJoinMax = 1.5f;
            /// <summary>Bottles picked in one belt step leave one after another, this far apart.</summary>
            public const float PickStagger = 0.04f;
            public const float TrayShake = 0.35f, TrayShakeCycles = 3f;
            public const float BoxHold = 0.35f, BoxDrop = 0.25f, BoxFlaps = 0.25f, BoxExit = 0.40f;
            public const float RoundEndPause = 1.0f;
            /// <summary>A locked slot opening: its "+" shrinks away while the tile pops.</summary>
            public const float SlotUnlock = 0.3f;
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

            /// <summary>The coin offer's button while the price is out of reach.</summary>
            public const float OfferUnaffordableAlpha = 0.45f;

            /// <summary>Rounded-rect sprite corner radius in texture px (== canvas px at PPU 1).</summary>
            public const float CornerRadius = 40f;
        }

        /// <summary>
        /// The "one more slot" offers (GDD R20 — refs: Parking Slot / Out of Slot popups) and the HUD coin pill. Not in the
        /// art spec — decided here (rule 17), read off the reference: a royal-blue panel under a banner with a gold rim, a
        /// cream card, a yellow FREE button and a green coin button, each over a darker lip; a red round ✕. Defaults only —
        /// the live values are in the UiPalette asset.
        /// </summary>
        public static class Offer
        {
            public static readonly Color Panel = Hex("1F6FE0"), PanelLip = Hex("0E47A8");
            public static readonly Color Banner = Hex("2A63D8"), BannerRim = Hex("FFC21A");
            public static readonly Color Card = Hex("FDF0D9");
            public static readonly Color FreeTop = Hex("FFC928"), FreeLip = Hex("D98A00");
            public static readonly Color CoinsTop = Hex("5ED42A"), CoinsLip = Hex("2E9A12");
            public static readonly Color Close = Hex("E8323C"), CloseRim = Hex("FFFFFF");
            public static readonly Color Coin = Hex("FFC21A"), CoinShine = Hex("FFE680");
            /// <summary>Out of Slot's illustration: the slot bar's asphalt, an empty slot, the "+" of a locked one, two trays.</summary>
            public static readonly Color ArtGround = Hex("7E86A6"), ArtSlot = Hex("5D6587"), ArtPlus = Hex("5BD45B");
            public static readonly Color ArtTrayWarm = Hex("F7C520"), ArtTrayHot = Hex("EF2B86");
            /// <summary>The dark glyph on a light fill (the ad clapper).</summary>
            public static readonly Color IconDark = Hex("1E2433");
            /// <summary>Text outlines (baked into the offer font materials by the prefab build): banner title and Restart,
            /// the FREE label, the price label.</summary>
            public static readonly Color TitleOutline = Hex("0E3A8C"), FreeOutline = Hex("A65100"), CoinsOutline = Hex("1E7A0E");
        }

        public static readonly ResultTheme Win  = new("57935B", "4B9B8B", "5DB987", "89D851", "86D64F", "75C34C", "428C66");
        public static readonly ResultTheme Lose = new("5733A2", "984BF5", "B34DFF", "D972FE", "B34FE6", "8434AB", "5E2A86");
    }
}
