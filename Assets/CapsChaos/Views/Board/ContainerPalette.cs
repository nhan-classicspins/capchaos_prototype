using UnityEngine;

namespace Game.Views
{
    /// <summary>
    /// The containers' materials, ONE asset for the whole session (addressable <see cref="Address"/>, loaded once by the
    /// Root <c>ContainerPaletteProvider</c>) — no container carries its own copy of the list. A colour per CapColor
    /// (R O B G P Y C N — the same numbers as the Items_NN prefabs: M_Container_01, 06, 02, 03, 07, 04, 08, 05), plus
    /// the locked (R18) and hidden (R17) looks. Filled by menu CapsChaos/Art/Wire Containers.
    /// </summary>
    [CreateAssetMenu(menuName = "CapsChaos/Container Palette", fileName = "ContainerPalette")]
    public sealed class ContainerPalette : ScriptableObject
    {
        public const string Address = "ContainerPalette";

        /// <summary>One per colour, <c>_colors[flavour − 1]</c>.</summary>
        [SerializeField] private Material[] _colors = new Material[8];
        [SerializeField] private Material _locked;
        [SerializeField] private Material _hidden;

        /// <summary>The material for a container of <paramref name="color"/> — locked wins over hidden; null if unset. The
        /// board no longer asks for the locked one (a locked tray keeps its colour, 2026-10-02); it stays for the art.</summary>
        public Material For(TintFlavor color, bool hidden, bool locked)
        {
            if (locked) return _locked;
            if (hidden) return _hidden;
            int i = (int)color - 1;
            return _colors != null && i >= 0 && i < _colors.Length ? _colors[i] : null;
        }
    }
}
