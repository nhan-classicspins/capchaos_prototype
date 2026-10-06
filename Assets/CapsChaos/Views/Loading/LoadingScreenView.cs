using System.Threading;
using Cysharp.Threading.Tasks;
using LitMotion;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using M = Game.Views.DesignTokens.Motion;

namespace Game.Views
{
    /// <summary>
    /// The loading cover (Loading.unity's canvas root): the board's ground colour edge to edge, the label, and a row of
    /// item-coloured dots bobbing in turn under it so the wait never reads as a freeze. Humble: it shows the label it is
    /// given and fades out when told. Real time throughout (the cover shows while the game clock is not running).
    /// Visible the moment the scene activates — boot has nothing behind it to fade from.
    /// </summary>
    [RequireComponent(typeof(Canvas))]
    public sealed class LoadingScreenView : MonoBehaviour
    {
        [Tooltip("Full-screen backdrop (found by name 'Background' when unwired).")]
        [SerializeField] private Image _background;
        [Tooltip("The label (found by name 'LoadingLabel' when unwired).")]
        [SerializeField] private TMP_Text _label;

        private static readonly TintFlavor[] DotColors = { TintFlavor.Red, TintFlavor.Orange, TintFlavor.Blue, TintFlavor.Green };
        private static Sprite _dotSprite;
        private RectTransform[] _dots;
        private CanvasGroup _group;

        private void Awake()
        {
            if (_background == null) _background = FindDeep<Image>("Background");
            if (_label == null) _label = FindDeep<TMP_Text>("LoadingLabel");
            if (_background != null) _background.color = DesignTokens.LoadingBackground;
            // not `GetComponent() ?? AddComponent()`: in the Editor a missing component comes back as a fake null
            if (!TryGetComponent(out _group)) _group = gameObject.AddComponent<CanvasGroup>();
            _group.alpha = 1f;
            _group.blocksRaycasts = true;                     // the board under a cover takes no taps
            BuildDots();
        }

        /// <summary>The label reads <paramref name="text"/> (already localized); null keeps the authored one.</summary>
        public void SetLabel(string text)
        {
            if (_label != null && text != null) _label.SetText(text);
        }

        /// <summary>Fully up again (a cover raised while it was fading out).</summary>
        public void ShowNow()
        {
            if (_group != null) _group.alpha = 1f;
        }

        /// <summary>Fade out over <see cref="DesignTokens.Motion.LoadingFadeOut"/> (real time).</summary>
        public async UniTask FadeOutAsync(CancellationToken ct)
        {
            if (_group == null) return;
            _group.blocksRaycasts = false;
            await LMotion.Create(_group.alpha, 0f, M.LoadingFadeOut).WithScheduler(MotionScheduler.UpdateIgnoreTimeScale)
                .WithEase(Ease.OutQuad).Bind(a => { if (_group != null) _group.alpha = a; }).AddTo(gameObject).ToUniTask(ct);
        }

        private void Update()
        {
            if (_dots == null) return;
            float t = Time.unscaledTime / M.LoadingDotPeriod;
            for (int i = 0; i < _dots.Length; i++)
            {
                // each dot hops up and lands, a beat behind the one before: |sin| keeps it on or above the line
                float hop = Mathf.Abs(Mathf.Sin((t - i * M.LoadingDotLag) * Mathf.PI));
                _dots[i].anchoredPosition = new Vector2(_dots[i].anchoredPosition.x, hop * M.LoadingDotBob);
            }
        }

        /// <summary>A row of round dots, one per item colour, centred under the label.</summary>
        private void BuildDots()
        {
            var row = new GameObject("Dots", typeof(RectTransform)).GetComponent<RectTransform>();
            row.SetParent(transform, false);
            row.anchorMin = row.anchorMax = new Vector2(0.5f, 0.5f);
            var labelPos = _label != null ? ((RectTransform)_label.transform).anchoredPosition : Vector2.zero;
            row.anchoredPosition = labelPos - new Vector2(0f, M.LoadingDotsBelowLabel);
            row.sizeDelta = Vector2.zero;
            _dots = new RectTransform[DotColors.Length];
            float step = M.LoadingDotSize + M.LoadingDotGap, first = -(DotColors.Length - 1) * 0.5f * step;
            for (int i = 0; i < DotColors.Length; i++)
            {
                var dot = new GameObject("Dot" + i, typeof(RectTransform), typeof(Image));
                var rt = dot.GetComponent<RectTransform>();
                rt.SetParent(row, false);
                rt.sizeDelta = Vector2.one * M.LoadingDotSize;
                rt.anchoredPosition = new Vector2(first + i * step, 0f);
                var img = dot.GetComponent<Image>();
                img.sprite = DotSprite();
                img.color = DesignTokens.Flavor(DotColors[i]).Body;
                img.raycastTarget = false;
                _dots[i] = rt;
            }
        }

        /// <summary>A soft-edged white disc, made once (no built-in UI sprite is guaranteed in a player build).</summary>
        private static Sprite DotSprite()
        {
            if (_dotSprite != null) return _dotSprite;
            const int size = 64;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { name = "LoadingDot", wrapMode = TextureWrapMode.Clamp };
            var pixels = new Color32[size * size];
            float r = size * 0.5f;
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float d = Mathf.Sqrt((x + 0.5f - r) * (x + 0.5f - r) + (y + 0.5f - r) * (y + 0.5f - r));
                    byte a = (byte)(Mathf.Clamp01(r - d) * 255f);
                    pixels[y * size + x] = new Color32(255, 255, 255, a);
                }
            tex.SetPixels32(pixels);
            tex.Apply(false, true);
            _dotSprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
            return _dotSprite;
        }

        private T FindDeep<T>(string name) where T : Component
        {
            foreach (var c in GetComponentsInChildren<T>(true)) if (c.name == name) return c;
            return null;
        }
    }
}
