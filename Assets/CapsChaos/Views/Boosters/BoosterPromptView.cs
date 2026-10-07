using ClassicSpins.PrototypeFramework.Views;
using LitMotion;
using UnityEngine;
using UnityEngine.UI;

namespace Game.Views
{
    /// <summary>
    /// "Pick your target" for a booster in play (ref: the REMOVE prompt, 2026-10-07): a black shade falls from the top of
    /// the screen under the booster's icon, a title and a one-line hint. Taps pass straight through it to the board.
    /// Humble: it shows what it is told and fades; when to show it is the controller's call. Gallery-safe.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(CanvasGroup))]
    public sealed class BoosterPromptView : MonoBehaviour
    {
        [SerializeField] private Graphic _shade;
        [Tooltip("Icon, title and hint together: this drops into place as the prompt fades in.")]
        [SerializeField] private RectTransform _content;
        [SerializeField] private Image _icon;
        [SerializeField] private TextProxy _title;
        [SerializeField] private TextProxy _hint;

        private CanvasGroup _group;
        private Vector2 _rest;
        private MotionHandle _motion;
        private GameFeel _feel;

        private GameFeel Feel => _feel != null ? _feel : GameFeel.Defaults;

        /// <summary>The session's feel (timings, eases); without it the defaults apply.</summary>
        public void UseFeel(GameFeel feel) => _feel = feel;

        private void Awake()
        {
            _group = GetComponent<CanvasGroup>();
            _group.blocksRaycasts = false;                     // the board under it takes the pick
            _group.interactable = false;
            if (_shade != null) { _shade.color = DesignTokens.Booster.PromptShade; _shade.raycastTarget = false; }
            if (_content != null) _rest = _content.anchoredPosition;
        }

        public void Show(Sprite icon, string title, string hint)
        {
            if (_group == null) Awake();
            if (_icon != null) { _icon.sprite = icon; _icon.enabled = icon != null; }
            if (_title != null) _title.SetText(title);
            if (_hint != null) _hint.SetText(hint);
            gameObject.SetActive(true);
            Stop();
            var feel = Feel;
            _motion = LMotion.Create(0f, 1f, feel.PromptIn).WithScheduler(MotionScheduler.UpdateIgnoreTimeScale).WithEase(feel.PromptInEase)
                .Bind(k =>
                {
                    _group.alpha = Mathf.Clamp01(k);
                    if (_content != null) _content.anchoredPosition = _rest + Vector2.up * (feel.PromptDrop * (1f - k));
                }).AddTo(gameObject);
        }

        public void Hide()
        {
            if (!gameObject.activeSelf) return;
            if (_group == null) Awake();
            Stop();
            _motion = LMotion.Create(_group.alpha, 0f, Feel.PromptOut).WithScheduler(MotionScheduler.UpdateIgnoreTimeScale)
                .WithOnComplete(() => { if (this != null) gameObject.SetActive(false); })
                .Bind(a => _group.alpha = a).AddTo(gameObject);
        }

        private void Stop()
        {
            if (_motion.IsActive()) _motion.Cancel();
            if (_content != null) _content.anchoredPosition = _rest;
        }
    }
}
