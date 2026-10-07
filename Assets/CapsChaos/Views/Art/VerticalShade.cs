using UnityEngine;
using UnityEngine.UI;

namespace Game.Views
{
    /// <summary>
    /// A shade that holds its <see cref="Graphic.color"/> over the top <see cref="_hold"/> of its rect, then fades to fully
    /// clear at the bottom along a smoothstep — drawn as vertex colours, so it needs no texture. (The project blends in
    /// linear space: a black that fades early reads far lighter than its alpha suggests, hence the hold.) Draws only;
    /// takes no taps.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class VerticalShade : MaskableGraphic
    {
        private const int Bands = 16;

        [Tooltip("The share of the height, from the top, drawn at full colour before the fade starts.")]
        [Range(0f, 1f)]
        [SerializeField] private float _hold = 0.35f;

        protected override void Awake()
        {
            base.Awake();
            raycastTarget = false;
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            var r = GetPixelAdjustedRect();
            Color32 top = color;
            for (int i = 0; i <= Bands; i++)
            {
                float t = i / (float)Bands;                 // 0 = top edge
                float k = Mathf.Clamp01((t - _hold) / Mathf.Max(0.0001f, 1f - _hold));
                float a = 1f - k * k * (3f - 2f * k);
                var c = top;
                c.a = (byte)(top.a * a);
                float y = Mathf.Lerp(r.yMax, r.yMin, t);
                vh.AddVert(new Vector3(r.xMin, y), c, Vector2.zero);
                vh.AddVert(new Vector3(r.xMax, y), c, Vector2.zero);
                if (i == 0) continue;
                int b = i * 2;
                vh.AddTriangle(b - 2, b - 1, b + 1);
                vh.AddTriangle(b + 1, b, b - 2);
            }
        }
    }
}
