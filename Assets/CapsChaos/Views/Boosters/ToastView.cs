using ClassicSpins.PrototypeFramework.Views;
using LitMotion;
using UnityEngine;
using UnityEngine.UI;

namespace Game.Views
{
    /// <summary>
    /// A one-line notice (e.g. "No available slots"): a full-width band in the lower third of the screen that rises
    /// and fades out over <see cref="GameFeel.ToastDuration"/>. A new message replaces the one on screen
    /// and starts the cycle again. Takes no taps. Humble: it shows the text it is given. Gallery-safe.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(CanvasGroup))]
    public sealed class ToastView : MonoBehaviour
    {
        [SerializeField] private Image _band;
        [SerializeField] private TextProxy _label;

        private CanvasGroup _group;
        private RectTransform _rt;
        private Vector2 _rest;
        private MotionHandle _motion;
        private GameFeel _feel;

        private GameFeel Feel => _feel != null ? _feel : GameFeel.Defaults;

        /// <summary>The session's feel (timings, distances); without it the defaults apply.</summary>
        public void UseFeel(GameFeel feel) => _feel = feel;

        private void Awake()
        {
            _group = GetComponent<CanvasGroup>();
            _group.blocksRaycasts = false;
            _group.interactable = false;
            _rt = (RectTransform)transform;
            _rest = _rt.anchoredPosition;
            if (_band != null) { _band.color = DesignTokens.Booster.ToastBand; _band.raycastTarget = false; }
        }

        public void Show(string text)
        {
            if (_group == null) Awake();
            if (_motion.IsActive()) _motion.Cancel();
            if (_label != null) _label.SetText(text);
            gameObject.SetActive(true);
            var feel = Feel;
            float hold = Mathf.Clamp(feel.ToastHold, 0f, 0.999f);
            _motion = LMotion.Create(0f, 1f, feel.ToastDuration).WithScheduler(MotionScheduler.UpdateIgnoreTimeScale)
                .WithOnComplete(() => { if (this != null) gameObject.SetActive(false); })
                .Bind(k =>
                {
                    _rt.anchoredPosition = _rest + Vector2.up * (feel.ToastRise * EaseOutCubic(k));
                    _group.alpha = k <= hold ? 1f : 1f - (k - hold) / (1f - hold);
                }).AddTo(gameObject);
        }

        /// <summary>Gone at once.</summary>
        public void Hide()
        {
            if (_motion.IsActive()) _motion.Cancel();
            if (_rt != null) _rt.anchoredPosition = _rest;
            gameObject.SetActive(false);
        }

        private static float EaseOutCubic(float k) => 1f - (1f - k) * (1f - k) * (1f - k);
    }
}
