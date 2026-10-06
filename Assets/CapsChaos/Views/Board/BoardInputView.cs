using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Game.Views
{
    /// <summary>
    /// ADR-001 §5.3 — the GamePlay layer only has a Physics2DRaycaster, so 3D colliders get no pointer
    /// events. This full-screen, invisible uGUI surface on the GamePlay host receives the click through
    /// the EventSystem (rule #9: no polling), raycasts the 3D board itself, and reports WHICH BELT TRAY was hit —
    /// lane + queue position, never a screen position (rule #10) — or which LOCKED SLOT (R20). A press that hits neither is a
    /// hold on EMPTY space: it reports when the hold starts and ends (one pointer at a time). What a tap or a hold MEANS
    /// is the controller's call (rule #8).
    /// </summary>
    [RequireComponent(typeof(RectTransform))]
    public sealed class BoardInputView : MonoBehaviour, IPointerClickHandler, IPointerDownHandler, IPointerUpHandler
    {
        private Camera _camera;
        private BoardView _board;
        private int _mask;

        /// <summary>(lane, index in that lane's queue; 0 = the front tray).</summary>
        public event Action<int, int> TrayTapped;
        /// <summary>A locked slot (R20) was tapped: its index in the slot bar.</summary>
        public event Action<int> LockedSlotTapped;
        /// <summary>true: a pointer went down on empty space (no tray, no locked slot) and is held; false: it let go.</summary>
        public event Action<bool> EmptyHoldChanged;

        private const int NoPointer = int.MinValue;
        private int _holdPointer = NoPointer;

        public void Bind(Camera boardCamera, BoardView board)
        {
            _camera = boardCamera;
            _board = board;
            _mask = boardCamera != null ? boardCamera.cullingMask : ~0;

            var rt = (RectTransform)transform;
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
            var img = GetComponent<Image>() ?? gameObject.AddComponent<Image>();
            img.color = DesignTokens.Invisible;
            img.raycastTarget = true;
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            var hit = HitAt(eventData.position);
            if (hit == null) return;
            if (_board.TryGetBeltTray(hit, out int lane, out int index)) TrayTapped?.Invoke(lane, index);
            else if (_board.TryGetLockedSlot(hit, out int slot)) LockedSlotTapped?.Invoke(slot);
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            if (_holdPointer != NoPointer || _camera == null || _board == null) return;
            var hit = HitAt(eventData.position);
            if (hit != null && (_board.TryGetBeltTray(hit, out _, out _) || _board.TryGetLockedSlot(hit, out _))) return;
            _holdPointer = eventData.pointerId;
            EmptyHoldChanged?.Invoke(true);
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            if (eventData.pointerId != _holdPointer) return;
            EndHold();
        }

        private void OnDisable() => EndHold();

        private void EndHold()
        {
            if (_holdPointer == NoPointer) return;
            _holdPointer = NoPointer;
            EmptyHoldChanged?.Invoke(false);
        }

        /// <summary>The board collider under a screen point, or null.</summary>
        private Collider HitAt(Vector2 screen)
        {
            if (_camera == null || _board == null) return null;
            var ray = _camera.ScreenPointToRay(screen);
            // the board stands ViewDistance in front of the camera (ADR-001 §7) — reach as far as the camera sees
            return Physics.Raycast(ray, out var hit, _camera.farClipPlane, _mask, QueryTriggerInteraction.Collide) ? hit.collider : null;
        }
    }
}
