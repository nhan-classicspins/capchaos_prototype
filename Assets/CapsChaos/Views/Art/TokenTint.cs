using UnityEngine;

namespace Game.Views
{
    /// <summary>Which design token a <see cref="TokenTint"/> paints with.</summary>
    public enum TintToken
    {
        FlavorBody,
        FlavorShade,
        FlavorCap,
        FlavorBand,   // Body lifted by DesignTokens.BottleBandLift
        Floor,
        SlotBand,
        SlotEmpty,
        Divider,
        LaneRail,
        LaneBelt,
        Tape,
        MysteryMark,
        LockBody,
        LockShackle,
        Rope,
        RopeOutline,
    }

    /// <summary>
    /// Paints one material slot of a renderer with a design token, through a MaterialPropertyBlock,
    /// so the generated art keeps ONE white material per surface and every colour stays in
    /// <see cref="DesignTokens"/> (art-direction §9, §11). Gallery-safe: any call order, no services,
    /// runs in edit mode so a preview scene shows the real palette.
    /// </summary>
    [ExecuteAlways]
    [RequireComponent(typeof(Renderer))]
    public sealed class TokenTint : MonoBehaviour
    {
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        [SerializeField] private TintToken _token = TintToken.FlavorBody;
        [Tooltip("Flavour palette; used by the Flavor* tokens.")]
        [SerializeField] private TintFlavor _color = TintFlavor.Red;
        [Tooltip("Material slot to paint; -1 paints the whole renderer.")]
        [SerializeField] private int _materialIndex = -1;

        private MaterialPropertyBlock _block;

        /// <summary>Repaint with another flavour (what a bottle / tray / box View calls).</summary>
        public void SetFlavor(TintFlavor flavor)
        {
            _color = flavor;
            Apply();
        }

        /// <summary>Repaint with another token (e.g. a Slot prefab reused as the slot band).</summary>
        public void SetToken(TintToken token)
        {
            _token = token;
            Apply();
        }

        public void Apply()
        {
            var renderer = GetComponent<Renderer>();
            if (renderer == null) return;
            _block ??= new MaterialPropertyBlock();
            _block.SetColor(BaseColorId, Resolve(_token, _color));
            if (_materialIndex < 0) renderer.SetPropertyBlock(_block);
            else renderer.SetPropertyBlock(_block, _materialIndex);
        }

        public static Color Resolve(TintToken token, TintFlavor flavor)
        {
            var f = DesignTokens.Flavor(flavor);
            return token switch
            {
                TintToken.FlavorBody  => f.Body,
                TintToken.FlavorShade => f.Shade,
                TintToken.FlavorCap   => f.Cap,
                TintToken.FlavorBand  => Color.Lerp(f.Body, Color.white, DesignTokens.BottleBandLift),
                TintToken.Floor       => DesignTokens.Floor,
                TintToken.SlotBand    => DesignTokens.SlotBand,
                TintToken.SlotEmpty   => DesignTokens.SlotEmpty,
                TintToken.Divider     => DesignTokens.Divider,
                TintToken.LaneRail    => DesignTokens.LaneRail,
                TintToken.LaneBelt    => DesignTokens.LaneBeltA,
                TintToken.Tape        => DesignTokens.Tape,
                TintToken.MysteryMark => DesignTokens.MysteryMark,
                TintToken.LockBody    => DesignTokens.LockBody,
                TintToken.LockShackle => DesignTokens.LockShackle,
                TintToken.Rope        => DesignTokens.Rope,
                TintToken.RopeOutline => DesignTokens.RopeOutline,
                _                     => Color.magenta,
            };
        }

        private void OnEnable() => Apply();
        private void OnValidate() => Apply();
    }
}
