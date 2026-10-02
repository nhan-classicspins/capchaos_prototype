using System;
using UnityEngine;
using UnityEngine.UI;
using ClassicSpins.PrototypeFramework.Views;

namespace Game.Views
{
    /// <summary>
    /// The Gameplay HUD (GDD §3, art §5): a round Restart button in the top-left corner and a round Home button in
    /// the top-right, inside the safe area, and the coin pill beside Home (R20 — coins buy an extra slot). It relays
    /// the two taps and shows the balance text it is given; what the taps do is the screen's call.
    /// Gallery-safe: works in any order without the game running.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class GameplayHudView : WidgetViewBase
    {
        [SerializeField] private Button _retry;
        [SerializeField] private Button _home;
        [SerializeField] private TextProxy _coins;

        public event Action RetryClicked;
        public event Action HomeClicked;

        private void Awake()
        {
            if (_retry != null) _retry.onClick.AddListener(OnRetry);
            if (_home != null) _home.onClick.AddListener(OnHome);
        }

        private void OnDestroy()
        {
            if (_retry != null) _retry.onClick.RemoveListener(OnRetry);
            if (_home != null) _home.onClick.RemoveListener(OnHome);
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
