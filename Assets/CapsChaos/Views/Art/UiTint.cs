using UnityEngine;
using UnityEngine.UI;

namespace Game.Views
{
    /// <summary>Which design token a <see cref="UiTint"/> paints a uGUI graphic with.</summary>
    public enum UiToken
    {
        Ground,
        HudPill,
        HudPillLip,
        HudButton,
        HudButtonRim,
        HudButtonShadow,
        TextOnFill,
        Invisible,
    }

    /// <summary>
    /// The uGUI twin of <see cref="TokenTint"/>: paints the sibling <see cref="Graphic"/> (Image, TMP text…)
    /// with a design token, so a prefab stores a TOKEN NAME, never a colour, and every hex stays in
    /// <see cref="DesignTokens"/> (rule 17). Gallery-safe: runs in edit mode, any call order, no services.
    /// </summary>
    [ExecuteAlways]
    [RequireComponent(typeof(Graphic))]
    public sealed class UiTint : MonoBehaviour
    {
        [SerializeField] private UiToken _token = UiToken.HudPill;

        public void SetToken(UiToken token)
        {
            _token = token;
            Apply();
        }

        public void Apply()
        {
            var g = GetComponent<Graphic>();
            if (g != null) g.color = Resolve(_token);
        }

        public static Color Resolve(UiToken token) => token switch
        {
            UiToken.Ground     => DesignTokens.Ground,
            UiToken.HudPill    => DesignTokens.HudPill,
            UiToken.HudPillLip => DesignTokens.HudPillLip,
            UiToken.HudButton  => DesignTokens.HudButton,
            UiToken.HudButtonRim    => DesignTokens.HudButtonRim,
            UiToken.HudButtonShadow => DesignTokens.HudButtonShadow,
            UiToken.TextOnFill => DesignTokens.TextOnFill,
            UiToken.Invisible  => DesignTokens.Invisible,
            _                  => Color.magenta,
        };

        private void OnEnable() => Apply();
        private void OnValidate() => Apply();
    }
}
