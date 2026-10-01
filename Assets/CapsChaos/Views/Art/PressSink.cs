using UnityEngine;
using UnityEngine.EventSystems;

namespace Game.Views
{
    /// <summary>
    /// "When pressed it sinks" (art §5): while a pointer is down on this object, <see cref="_face"/> drops by
    /// <see cref="DesignTokens.Ui.TilePress"/> onto whatever sits under it (a lip, a shadow). EventSystem only
    /// (rule #9). Purely how it looks — the click itself is the sibling Button's.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PressSink : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IPointerExitHandler
    {
        [SerializeField] private RectTransform _face;

        private Vector2 _rest;
        private bool _restKnown;

        public void OnPointerDown(PointerEventData e) => Sink(true);
        public void OnPointerUp(PointerEventData e) => Sink(false);
        public void OnPointerExit(PointerEventData e) => Sink(false);

        private void OnDisable() => Sink(false);

        private void Sink(bool down)
        {
            if (_face == null) return;
            if (!_restKnown) { _rest = _face.anchoredPosition; _restKnown = true; }
            _face.anchoredPosition = _rest + (down ? Vector2.down * DesignTokens.Ui.TilePress : Vector2.zero);
        }
    }
}
