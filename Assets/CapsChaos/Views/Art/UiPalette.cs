using System;
using UnityEngine;

namespace Game.Views
{
    /// <summary>
    /// The live UI colours, editable in the Inspector without a recompile: every <see cref="UiTint"/> that points at
    /// this asset repaints the moment a colour changes (edit mode and play mode).
    /// </summary>
    /// <remarks>
    /// <para><b>A deliberate deviation from pf-visual-design</b> ("the token file is code, not an asset"), chosen by
    /// the SKU owner on 2026-10-01 so colours can be tuned live. The cost the framework warns about is real: a
    /// colour change is now an asset edit (Inspector / Unity-MCP, never raw YAML — rule 12) rather than a one-line
    /// code diff. Scope is kept to the uGUI tokens: spacing, type sizes, motion, the 3D gameplay palette and the
    /// environment colours stay in <see cref="DesignTokens"/>.</para>
    /// <para><see cref="DesignTokens"/> still holds the DEFAULTS a new palette starts from (and what an unassigned
    /// UiTint falls back to); "Reset To DesignTokens" in the context menu copies them back.</para>
    /// <para><b>One instance per session.</b> The asset is addressable (<see cref="Address"/>); the Root-scoped
    /// <c>UiPaletteProvider</c> loads it once at boot and widget controllers push it into their views. No prefab
    /// references it, so it is never duplicated into a bundle.</para>
    /// </remarks>
    [CreateAssetMenu(fileName = "UiPalette", menuName = "CapsChaos/UI Palette")]
    public sealed class UiPalette : ScriptableObject
    {
        [Header("Surfaces")]
        [SerializeField] private Color _ground = DesignTokens.Ground;
        [SerializeField] private Color _hudPill = DesignTokens.HudPill;
        [SerializeField] private Color _hudPillLip = DesignTokens.HudPillLip;

        [Header("Round HUD buttons")]
        [SerializeField] private Color _hudButton = DesignTokens.HudButton;
        [SerializeField] private Color _hudButtonRim = DesignTokens.HudButtonRim;
        [SerializeField] private Color _hudButtonShadow = DesignTokens.HudButtonShadow;

        [Header("Result popup — Win (art §3.3)")]
        [SerializeField] private Color _winHeader = DesignTokens.Win.Header;
        [SerializeField] private Color _winBody = DesignTokens.Win.Body;
        [SerializeField] private Color _winBand = DesignTokens.Win.Band;
        [SerializeField] private Color _winButtonTop = DesignTokens.Win.ButtonTop;
        [SerializeField] private Color _winButtonBottom = DesignTokens.Win.ButtonBottom;
        [SerializeField] private Color _winButtonLip = DesignTokens.Win.ButtonLip;

        [Header("Result popup — Lose (art §3.3)")]
        [SerializeField] private Color _loseHeader = DesignTokens.Lose.Header;
        [SerializeField] private Color _loseBody = DesignTokens.Lose.Body;
        [SerializeField] private Color _loseBand = DesignTokens.Lose.Band;
        [SerializeField] private Color _loseButtonTop = DesignTokens.Lose.ButtonTop;
        [SerializeField] private Color _loseButtonBottom = DesignTokens.Lose.ButtonBottom;
        [SerializeField] private Color _loseButtonLip = DesignTokens.Lose.ButtonLip;

        [Header("Slot offers (R20) and the coin pill")]
        [SerializeField] private Color _offerPanel = DesignTokens.Offer.Panel;
        [SerializeField] private Color _offerPanelLip = DesignTokens.Offer.PanelLip;
        [SerializeField] private Color _offerBanner = DesignTokens.Offer.Banner;
        [SerializeField] private Color _offerBannerRim = DesignTokens.Offer.BannerRim;
        [SerializeField] private Color _offerCard = DesignTokens.Offer.Card;
        [SerializeField] private Color _offerFreeTop = DesignTokens.Offer.FreeTop;
        [SerializeField] private Color _offerFreeLip = DesignTokens.Offer.FreeLip;
        [SerializeField] private Color _offerCoinsTop = DesignTokens.Offer.CoinsTop;
        [SerializeField] private Color _offerCoinsLip = DesignTokens.Offer.CoinsLip;
        [SerializeField] private Color _offerClose = DesignTokens.Offer.Close;
        [SerializeField] private Color _offerCloseRim = DesignTokens.Offer.CloseRim;
        [SerializeField] private Color _coin = DesignTokens.Offer.Coin;
        [SerializeField] private Color _coinShine = DesignTokens.Offer.CoinShine;
        [SerializeField] private Color _offerArtGround = DesignTokens.Offer.ArtGround;
        [SerializeField] private Color _offerArtSlot = DesignTokens.Offer.ArtSlot;
        [SerializeField] private Color _offerArtPlus = DesignTokens.Offer.ArtPlus;
        [SerializeField] private Color _offerArtTrayWarm = DesignTokens.Offer.ArtTrayWarm;
        [SerializeField] private Color _offerArtTrayHot = DesignTokens.Offer.ArtTrayHot;
        [SerializeField] private Color _iconDark = DesignTokens.Offer.IconDark;

        [Header("Icons on a fill")]
        [SerializeField] private Color _textOnFill = DesignTokens.TextOnFill;

        [Header("Text — TextTint, keyed by TintFlavor")]
        [SerializeField] private Color _textNone = DesignTokens.TextColor(TintFlavor.None);
        [SerializeField] private Color _textRed = DesignTokens.TextColor(TintFlavor.Red);
        [SerializeField] private Color _textOrange = DesignTokens.TextColor(TintFlavor.Orange);
        [SerializeField] private Color _textBlue = DesignTokens.TextColor(TintFlavor.Blue);
        [SerializeField] private Color _textGreen = DesignTokens.TextColor(TintFlavor.Green);
        [SerializeField] private Color _textPurple = DesignTokens.TextColor(TintFlavor.Purple);
        [SerializeField] private Color _textYellow = DesignTokens.TextColor(TintFlavor.Yellow);
        [SerializeField] private Color _textCyan = DesignTokens.TextColor(TintFlavor.Cyan);
        [SerializeField] private Color _textBrown = DesignTokens.TextColor(TintFlavor.Brown);
        [SerializeField] private Color _textWhite = DesignTokens.TextColor(TintFlavor.White);

        /// <summary>The Addressables address of the palette asset.</summary>
        public const string Address = "UiPalette";

        /// <summary>Where the palette asset lives (edit-mode preview only).</summary>
        public const string AssetPath = "Assets/CapsChaos/Content/UI/UiPalette.asset";

        /// <summary>Raised when any colour changes in the Inspector; UiTints repaint on it.</summary>
        public static event Action Changed;

        /// <summary>The project palette for EDIT-MODE preview (so a prefab or scene shows the real colours without the
        /// game running); null in play mode and in a player — there the Root-cached instance is pushed in.</summary>
        public static UiPalette EditorPreview
        {
            get
            {
#if UNITY_EDITOR
                if (!UnityEngine.Application.isPlaying)
                    return _editorPreview != null ? _editorPreview
                        : _editorPreview = UnityEditor.AssetDatabase.LoadAssetAtPath<UiPalette>(AssetPath);
#endif
                return null;
            }
        }
#if UNITY_EDITOR
        private static UiPalette _editorPreview;
#endif

        public Color Resolve(UiToken token) => token switch
        {
            UiToken.Ground          => _ground,
            UiToken.HudPill         => _hudPill,
            UiToken.HudPillLip      => _hudPillLip,
            UiToken.HudButton       => _hudButton,
            UiToken.HudButtonRim    => _hudButtonRim,
            UiToken.HudButtonShadow => _hudButtonShadow,
            UiToken.TextOnFill      => _textOnFill,
            UiToken.WinHeader        => _winHeader,
            UiToken.WinBody          => _winBody,
            UiToken.WinBand          => _winBand,
            UiToken.WinButtonTop     => _winButtonTop,
            UiToken.WinButtonBottom  => _winButtonBottom,
            UiToken.WinButtonLip     => _winButtonLip,
            UiToken.LoseHeader       => _loseHeader,
            UiToken.LoseBody         => _loseBody,
            UiToken.LoseBand         => _loseBand,
            UiToken.LoseButtonTop    => _loseButtonTop,
            UiToken.LoseButtonBottom => _loseButtonBottom,
            UiToken.LoseButtonLip    => _loseButtonLip,
            UiToken.OfferPanel => _offerPanel,
            UiToken.OfferPanelLip => _offerPanelLip,
            UiToken.OfferBanner => _offerBanner,
            UiToken.OfferBannerRim => _offerBannerRim,
            UiToken.OfferCard => _offerCard,
            UiToken.OfferFreeTop => _offerFreeTop,
            UiToken.OfferFreeLip => _offerFreeLip,
            UiToken.OfferCoinsTop => _offerCoinsTop,
            UiToken.OfferCoinsLip => _offerCoinsLip,
            UiToken.OfferClose => _offerClose,
            UiToken.OfferCloseRim => _offerCloseRim,
            UiToken.Coin => _coin,
            UiToken.CoinShine => _coinShine,
            UiToken.OfferArtGround => _offerArtGround,
            UiToken.OfferArtSlot => _offerArtSlot,
            UiToken.OfferArtPlus => _offerArtPlus,
            UiToken.OfferArtTrayWarm => _offerArtTrayWarm,
            UiToken.OfferArtTrayHot => _offerArtTrayHot,
            UiToken.IconDark => _iconDark,
            UiToken.Invisible       => DesignTokens.Invisible,   // a raycast-only surface is never a colour choice
            _                       => Color.magenta,
        };

        /// <summary>The text colour for a TextTint key.</summary>
        public Color ResolveText(TintFlavor flavor) => flavor switch
        {
            TintFlavor.None   => _textNone,
            TintFlavor.Red    => _textRed,
            TintFlavor.Orange => _textOrange,
            TintFlavor.Blue   => _textBlue,
            TintFlavor.Green  => _textGreen,
            TintFlavor.Purple => _textPurple,
            TintFlavor.Yellow => _textYellow,
            TintFlavor.Cyan   => _textCyan,
            TintFlavor.Brown  => _textBrown,
            TintFlavor.White => _textWhite,
            _                 => Color.magenta,
        };

        private void OnValidate() => Changed?.Invoke();

        [ContextMenu("Reset To DesignTokens")]
        private void ResetToDesignTokens()
        {
            _ground = DesignTokens.Ground; _hudPill = DesignTokens.HudPill; _hudPillLip = DesignTokens.HudPillLip;
            _offerPanel = DesignTokens.Offer.Panel; _offerPanelLip = DesignTokens.Offer.PanelLip; _offerBanner = DesignTokens.Offer.Banner; _offerBannerRim = DesignTokens.Offer.BannerRim; _offerCard = DesignTokens.Offer.Card; _offerFreeTop = DesignTokens.Offer.FreeTop; _offerFreeLip = DesignTokens.Offer.FreeLip; _offerCoinsTop = DesignTokens.Offer.CoinsTop; _offerCoinsLip = DesignTokens.Offer.CoinsLip; _offerClose = DesignTokens.Offer.Close; _offerCloseRim = DesignTokens.Offer.CloseRim; _coin = DesignTokens.Offer.Coin; _coinShine = DesignTokens.Offer.CoinShine; _offerArtGround = DesignTokens.Offer.ArtGround; _offerArtSlot = DesignTokens.Offer.ArtSlot; _offerArtPlus = DesignTokens.Offer.ArtPlus; _offerArtTrayWarm = DesignTokens.Offer.ArtTrayWarm; _offerArtTrayHot = DesignTokens.Offer.ArtTrayHot; _iconDark = DesignTokens.Offer.IconDark;
            _hudButton = DesignTokens.HudButton; _hudButtonRim = DesignTokens.HudButtonRim; _hudButtonShadow = DesignTokens.HudButtonShadow;
            _textOnFill = DesignTokens.TextOnFill;
            _winHeader = DesignTokens.Win.Header; _winBody = DesignTokens.Win.Body; _winBand = DesignTokens.Win.Band;
            _winButtonTop = DesignTokens.Win.ButtonTop; _winButtonBottom = DesignTokens.Win.ButtonBottom; _winButtonLip = DesignTokens.Win.ButtonLip;
            _loseHeader = DesignTokens.Lose.Header; _loseBody = DesignTokens.Lose.Body; _loseBand = DesignTokens.Lose.Band;
            _loseButtonTop = DesignTokens.Lose.ButtonTop; _loseButtonBottom = DesignTokens.Lose.ButtonBottom; _loseButtonLip = DesignTokens.Lose.ButtonLip;
            _textNone = DesignTokens.TextColor(TintFlavor.None); 
            _textRed = DesignTokens.TextColor(TintFlavor.Red);
            _textOrange = DesignTokens.TextColor(TintFlavor.Orange); 
            _textBlue = DesignTokens.TextColor(TintFlavor.Blue);
            _textGreen = DesignTokens.TextColor(TintFlavor.Green); 
            _textPurple = DesignTokens.TextColor(TintFlavor.Purple);
            _textYellow = DesignTokens.TextColor(TintFlavor.Yellow); 
            _textCyan = DesignTokens.TextColor(TintFlavor.Cyan);
            _textBrown = DesignTokens.TextColor(TintFlavor.Brown);
            _textWhite = DesignTokens.TextColor(TintFlavor.White);
            Changed?.Invoke();
        }
    }
}
