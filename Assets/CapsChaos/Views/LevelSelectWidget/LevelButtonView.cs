using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using ClassicSpins.PrototypeFramework.Views;

namespace Game.Views
{
    /// <summary>
    /// One level tile — and the panel's Sync Config pill, the same look: a rounded <c>HudPill</c> face on a darker
    /// lip, a big number (or word) and a small label under it.
    /// Pressing sinks the face onto its lip (art §5 "khi nhấn thì lún xuống"); a click raises
    /// <see cref="Clicked"/>. It never knows which level it shows — the controller decides that. Pooled: the
    /// controller subscribes on rent and unsubscribes on return.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class LevelButtonView : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IPointerExitHandler
    {
        [SerializeField] private Button _button;
        [SerializeField] private RectTransform _face;
        [SerializeField] private TextProxy _number;
        [SerializeField] private TextProxy _label;

        public event Action Clicked;

        private Vector2 _faceRest;
        private bool _restKnown;

        public void SetLabels(string number, string label)
        {
            if (_number != null) _number.SetText(number);
            if (_label != null) _label.SetText(label);
        }

        /// <summary>Takes taps or not; a button that does not is drawn dimmed (the Button's own disabled tint).</summary>
        public void SetInteractable(bool interactable)
        {
            if (_button != null) _button.interactable = interactable;
        }

        private void Awake()
        {
            if (_button != null) _button.onClick.AddListener(OnClick);
            CacheRest();
        }

        private void OnDestroy()
        {
            if (_button != null) _button.onClick.RemoveListener(OnClick);
        }

        private void OnDisable() => Sink(false);   // a pooled tile always comes back at rest

        private void OnClick() => Clicked?.Invoke();

        public void OnPointerDown(PointerEventData e) => Sink(true);
        public void OnPointerUp(PointerEventData e) => Sink(false);
        public void OnPointerExit(PointerEventData e) => Sink(false);

        private void CacheRest()
        {
            if (_face == null || _restKnown) return;
            _faceRest = _face.anchoredPosition;
            _restKnown = true;
        }

        private void Sink(bool down)
        {
            if (_face == null) return;
            CacheRest();
            _face.anchoredPosition = _faceRest + (down ? Vector2.down * DesignTokens.Ui.TilePress : Vector2.zero);
        }
    }
}
