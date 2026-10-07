using System;
using UnityEngine;
using UnityEngine.UI;
using ClassicSpins.PrototypeFramework.Views;

namespace Game.Views
{
    /// <summary>
    /// The Gameplay HUD (GDD §3, art §5): a round Restart button in the top-left corner and a round Home button in
    /// the top-right, inside the safe area, and the coin pill beside Home (R20 — coins buy an extra slot). It relays
    /// the two taps and shows the balance text it is given; what the taps do is the screen's call. At the bottom, centred,
    /// the booster buttons (<see cref="BoosterButtonView"/>), one per slot in <c>_boosters</c>, left to right.
    /// Gallery-safe: works in any order without the game running.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class GameplayHudView : WidgetViewBase
    {
        [SerializeField] private Button _retry;
        [SerializeField] private Button _home;
        [SerializeField] private TextProxy _coins;
        [Tooltip("The level number at the top (\"LEVEL 41\").")]
        [SerializeField] private TextProxy _level;
        [SerializeField] private BoosterButtonView[] _boosters = Array.Empty<BoosterButtonView>();
        [Tooltip("Full-screen node the boosters' prompt and banner prefabs (BoosterCatalog) are put under at runtime — " +
                 "above the HUD's own buttons, below the toast.")]
        [SerializeField] private RectTransform _boosterOverlays;
        [Tooltip("The notice band in the lower third (\"No available slots\").")]
        [SerializeField] private ToastView _toast;

        public event Action RetryClicked;
        public event Action HomeClicked;
        /// <summary>A booster button was tapped: its index, left to right.</summary>
        public event Action<int> BoosterClicked;

        /// <summary>How many booster buttons the HUD has.</summary>
        public int BoosterSlots => _boosters.Length;

        /// <summary>Booster button <paramref name="index"/>, or null.</summary>
        public BoosterButtonView Booster(int index) => index >= 0 && index < _boosters.Length ? _boosters[index] : null;

        /// <summary>Where the boosters' prompt and banner instances go (falls back to the HUD root).</summary>
        public RectTransform BoosterOverlays => _boosterOverlays != null ? _boosterOverlays : (RectTransform)transform;
        public ToastView Toast => _toast;

        private Action[] _boosterHandlers = Array.Empty<Action>();

        private void Awake()
        {
            if (_retry != null) _retry.onClick.AddListener(OnRetry);
            if (_home != null) _home.onClick.AddListener(OnHome);
            if (_toast != null) _toast.gameObject.SetActive(false);
            _boosterHandlers = new Action[_boosters.Length];
            for (int i = 0; i < _boosters.Length; i++)
            {
                int index = i;
                _boosterHandlers[i] = () => BoosterClicked?.Invoke(index);
                if (_boosters[i] != null) _boosters[i].Clicked += _boosterHandlers[i];
            }
        }

        private void OnDestroy()
        {
            if (_retry != null) _retry.onClick.RemoveListener(OnRetry);
            if (_home != null) _home.onClick.RemoveListener(OnHome);
            for (int i = 0; i < _boosters.Length && i < _boosterHandlers.Length; i++)
                if (_boosters[i] != null) _boosters[i].Clicked -= _boosterHandlers[i];
        }

        /// <summary>The session's feel: how the toast moves (the booster overlays get it from the widget).</summary>
        public void UseFeel(GameFeel feel)
        {
            if (_toast != null) _toast.UseFeel(feel);
        }

        /// <summary>The level title, already formatted.</summary>
        public void SetLevel(string text)
        {
            if (_level != null) _level.SetText(text);
        }

        /// <summary>The coin balance, already formatted.</summary>
        public void SetCoins(string text)
        {
            if (_coins != null) _coins.SetText(text);
        }

        private void OnRetry() => RetryClicked?.Invoke();
        private void OnHome() => HomeClicked?.Invoke();
    }
}
