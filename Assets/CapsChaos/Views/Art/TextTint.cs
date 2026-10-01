using TMPro;
using UnityEngine;

namespace Game.Views
{
    /// <summary>
    /// Paints the sibling TextMeshPro text (uGUI or 3D) with the text colour the session's <see cref="UiPalette"/>
    /// gives its <see cref="TintFlavor"/> key — <see cref="TintFlavor.None"/> for the plain display white, a flavour
    /// to match a bottle / tray colour. The TMP Color field is overwritten by design: change the key here or the
    /// colour on the palette.
    /// </summary>
    /// <remarks>
    /// Text only (<c>RequireComponent(TMP_Text)</c>); images use <see cref="UiTint"/>. The palette is pushed in at
    /// runtime by the owning controller (<see cref="ApplyPalette"/>), previewed from the project asset in edit mode,
    /// and falls back to <see cref="DesignTokens.TextColor"/> otherwise. An Editor hook adds one to every new TMP text
    /// (Game.Editor TextTintAutoAdd) — remove it where a text must keep its own colour.
    /// </remarks>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(TMP_Text))]
    public sealed class TextTint : MonoBehaviour
    {
        [SerializeField] private TintFlavor _flavor = TintFlavor.None;

        private UiPalette _palette;   // pushed by the owning controller; never serialized

        public void SetFlavor(TintFlavor flavor)
        {
            _flavor = flavor;
            Apply();
        }

        public void SetPalette(UiPalette palette)
        {
            _palette = palette;
            Apply();
        }

        /// <summary>Hand <paramref name="palette"/> to every TextTint under <paramref name="root"/> (inactive ones too).</summary>
        public static void ApplyPalette(GameObject root, UiPalette palette)
        {
            if (root == null) return;
            foreach (var tint in root.GetComponentsInChildren<TextTint>(true)) tint.SetPalette(palette);
        }

        public void Apply()
        {
            var text = GetComponent<TMP_Text>();
            if (text == null) return;
            var palette = _palette != null ? _palette : UiPalette.EditorPreview;
            text.color = palette != null ? palette.ResolveText(_flavor) : DesignTokens.TextColor(_flavor);
        }

        private void OnEnable()
        {
            UiPalette.Changed += Apply;
            Apply();
        }

        private void OnDisable() => UiPalette.Changed -= Apply;

        private void OnValidate() => Apply();
    }
}
