using System;
using UnityEngine;
using UnityEngine.UI;
using ClassicSpins.PrototypeFramework.Views;

namespace Game.Views
{
    /// <summary>
    /// The shared body of the two "one more slot" popups (GDD R20, refs: Parking Slot / Out of Slot): a blue panel
    /// under a gold-rimmed banner, a cream card (icon or illustration + a line of text), and two offers side by side —
    /// FREE (watch an ad, yellow) and a coin price (green). The way out is the subclass's: the red ✕ on Parking Slot,
    /// the Restart link on Out of Slot; both relay <see cref="DialogViewBase.RaiseCloseRequested"/>.
    /// It is told the texts and whether the price is affordable; it never knows a balance or a rule.
    /// Gallery-safe: any order, no services.
    /// </summary>
    public abstract class SlotOfferView : DialogViewBase
    {
        [Header("Text")]
        [SerializeField] private TextProxy _title;
        [SerializeField] private TextProxy _body;
        [SerializeField] private TextProxy _freeLabel;
        [SerializeField] private TextProxy _priceLabel;

        [Header("Input")]
        [SerializeField] private Button _free;
        [SerializeField] private Button _coins;
        [SerializeField] private Button _close;
        /// <summary>Faded over the coin button while the price is out of reach.</summary>
        [SerializeField] private CanvasGroup _coinsGroup;

        public event Action FreeClicked;
        public event Action CoinsClicked;

        public void SetTexts(string title, string body, string free, string price)
        {
            if (_title != null) _title.SetText(title);
            if (_body != null) _body.SetText(body);
            if (_freeLabel != null) _freeLabel.SetText(free);
            if (_priceLabel != null) _priceLabel.SetText(price);
        }

        /// <summary>The coin offer can be taken (true) or is greyed out (false).</summary>
        public void SetPriceAffordable(bool affordable)
        {
            if (_coins != null) _coins.interactable = affordable;
            if (_coinsGroup != null) _coinsGroup.alpha = affordable ? 1f : DesignTokens.Ui.OfferUnaffordableAlpha;
        }

        /// <summary>Art §5: in = scale 0.8 → 1 (OutBack) + fade; out is the same, reversed.</summary>
        public override ViewTransition CreateTransition()
            => ViewTransition.Combine(new FadeTransition(0.15f), new ScaleTransition(0.8f, 0.25f));

        protected virtual void Awake()
        {
            if (_free != null) _free.onClick.AddListener(OnFree);
            if (_coins != null) _coins.onClick.AddListener(OnCoins);
            if (_close != null) _close.onClick.AddListener(RaiseCloseRequested);
        }

        protected virtual void OnDestroy()
        {
            if (_free != null) _free.onClick.RemoveListener(OnFree);
            if (_coins != null) _coins.onClick.RemoveListener(OnCoins);
            if (_close != null) _close.onClick.RemoveListener(RaiseCloseRequested);
        }

        private void OnFree() => FreeClicked?.Invoke();
        private void OnCoins() => CoinsClicked?.Invoke();
    }
}
