using System;
using UnityEngine;
using UnityEngine.UI;
using ClassicSpins.PrototypeFramework.Views;

namespace Game.Views
{
    /// <summary>
    /// One booster button of the Gameplay HUD (art ref 2026-10-07): the <c>btn-booster</c> plate with the booster's icon,
    /// and at its bottom-right corner either the red count badge (<c>ic-red-dot</c>) or the green "+" (<c>ic_add-more</c>).
    /// Humble: it shows the icon and the badge it is told to show and relays a tap — which badge, and what a tap does,
    /// are the controller's call. Gallery-safe: every member works in any order without the game running.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class BoosterButtonView : MonoBehaviour
    {
        [SerializeField] private Button _button;
        [SerializeField] private Image _icon;
        [SerializeField] private GameObject _countBadge;
        [SerializeField] private TextProxy _count;
        [SerializeField] private GameObject _addMore;

        public event Action Clicked;

        /// <summary>The booster's icon; null hides it.</summary>
        public void SetIcon(Sprite icon)
        {
            if (_icon == null) return;
            _icon.sprite = icon;
            _icon.enabled = icon != null;
        }

        /// <summary>The red badge reads <paramref name="count"/> (already formatted); the "+" is hidden.</summary>
        public void ShowCount(string count)
        {
            if (_count != null) _count.SetText(count);
            if (_countBadge != null) _countBadge.SetActive(true);
            if (_addMore != null) _addMore.SetActive(false);
        }

        /// <summary>The green "+" instead of a count.</summary>
        public void ShowAddMore()
        {
            if (_countBadge != null) _countBadge.SetActive(false);
            if (_addMore != null) _addMore.SetActive(true);
        }

        public void SetVisible(bool visible) => gameObject.SetActive(visible);

        /// <summary>false: it takes no tap and reads as off (another booster is in play).</summary>
        public void SetInteractable(bool interactable)
        {
            if (_button != null) _button.interactable = interactable;
            if (_group == null && !TryGetComponent(out _group)) _group = gameObject.AddComponent<CanvasGroup>();
            _group.alpha = interactable ? 1f : DesignTokens.Booster.ButtonDisabledAlpha;
        }

        private CanvasGroup _group;

        private void Awake()
        {
            if (_button != null) _button.onClick.AddListener(OnClick);
        }

        private void OnDestroy()
        {
            if (_button != null) _button.onClick.RemoveListener(OnClick);
        }

        private void OnClick() => Clicked?.Invoke();
    }
}
