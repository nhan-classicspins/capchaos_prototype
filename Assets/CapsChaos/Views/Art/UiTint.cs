using UnityEngine;
using UnityEngine.UI;

namespace Game.Views
{
    /// <summary>
    /// Which UI colour a <see cref="UiTint"/> paints with. SERIALIZED AS ITS NUMBER in every prefab and scene:
    /// the values are explicit, append new tokens at the end, never reorder or reuse a number.
    /// </summary>
    public enum UiToken
    {
        Ground = 0,
        HudPill = 1,
        HudPillLip = 2,
        HudButton = 3,
        HudButtonRim = 4,
        HudButtonShadow = 5,
        TextOnFill = 6,
        Invisible = 7,
        // 8 and 9 were LevelNumber / LevelLabel — text moved to TextTint (keyed by TintFlavor). Never reuse them.
        WinHeader = 10,
        WinBody = 11,
        WinBand = 12,
        WinButtonTop = 13,
        WinButtonBottom = 14,
        WinButtonLip = 15,
        LoseHeader = 16,
        LoseBody = 17,
        LoseBand = 18,
        LoseButtonTop = 19,
        LoseButtonBottom = 20,
        LoseButtonLip = 21,
    }

    /// <summary>
    /// The uGUI twin of <see cref="TokenTint"/>: paints the sibling <see cref="Graphic"/> (an Image — text has its own
    /// <see cref="TextTint"/>) with a
    /// token's colour from the session's <see cref="UiPalette"/>, so a prefab stores a TOKEN, never a colour. Change
    /// the colour on the palette (or the token here) — the Graphic's own Color field is overwritten by design.
    /// </summary>
    /// <remarks>
    /// The palette is never serialized here (that would copy the asset into every bundle holding a tinted prefab):
    /// at runtime the owning controller pushes the Root-cached palette in with <see cref="ApplyPalette"/>; in edit
    /// mode the tint previews the project's palette asset. With neither, it paints the <see cref="DesignTokens"/>
    /// defaults. Gallery-safe: any call order, no services.
    /// </remarks>
    [ExecuteAlways]
    [RequireComponent(typeof(Graphic))]
    public sealed class UiTint : MonoBehaviour
    {
        [SerializeField] private UiToken _token = UiToken.HudPill;

        private UiPalette _palette;   // pushed by the owning controller; never serialized

        public void SetToken(UiToken token)
        {
            _token = token;
            Apply();
        }

        public void SetPalette(UiPalette palette)
        {
            _palette = palette;
            Apply();
        }

        /// <summary>Hand <paramref name="palette"/> to every UiTint under <paramref name="root"/> (inactive ones too).</summary>
        public static void ApplyPalette(GameObject root, UiPalette palette)
        {
            if (root == null) return;
            foreach (var tint in root.GetComponentsInChildren<UiTint>(true)) tint.SetPalette(palette);
        }

        public void Apply()
        {
            var g = GetComponent<Graphic>();
            if (g == null) return;
            var palette = _palette != null ? _palette : UiPalette.EditorPreview;
            g.color = palette != null ? palette.Resolve(_token) : Resolve(_token);
        }

        /// <summary>The code defaults (what a fresh palette starts from).</summary>
        public static Color Resolve(UiToken token) => token switch
        {
            UiToken.Ground          => DesignTokens.Ground,
            UiToken.HudPill         => DesignTokens.HudPill,
            UiToken.HudPillLip      => DesignTokens.HudPillLip,
            UiToken.HudButton       => DesignTokens.HudButton,
            UiToken.HudButtonRim    => DesignTokens.HudButtonRim,
            UiToken.HudButtonShadow => DesignTokens.HudButtonShadow,
            UiToken.TextOnFill      => DesignTokens.TextOnFill,
            UiToken.Invisible       => DesignTokens.Invisible,
            UiToken.WinHeader        => DesignTokens.Win.Header,
            UiToken.WinBody          => DesignTokens.Win.Body,
            UiToken.WinBand          => DesignTokens.Win.Band,
            UiToken.WinButtonTop     => DesignTokens.Win.ButtonTop,
            UiToken.WinButtonBottom  => DesignTokens.Win.ButtonBottom,
            UiToken.WinButtonLip     => DesignTokens.Win.ButtonLip,
            UiToken.LoseHeader       => DesignTokens.Lose.Header,
            UiToken.LoseBody         => DesignTokens.Lose.Body,
            UiToken.LoseBand         => DesignTokens.Lose.Band,
            UiToken.LoseButtonTop    => DesignTokens.Lose.ButtonTop,
            UiToken.LoseButtonBottom => DesignTokens.Lose.ButtonBottom,
            UiToken.LoseButtonLip    => DesignTokens.Lose.ButtonLip,
            _                       => Color.magenta,
        };

        private void OnEnable()
        {
            UiPalette.Changed += Apply;
            Apply();
        }

        private void OnDisable() => UiPalette.Changed -= Apply;

        private void OnValidate() => Apply();
    }
}
