using UnityEngine;

namespace Game.Views
{
    /// <summary>
    /// The table the board stands on, authored once in Gameplay.unity (under <c>WorldRoot</c>, root of the Floor prefab)
    /// instead of being spawned with every round. The board is rebuilt each round, so the floor is NOT its child:
    /// <see cref="BoardView.Frame"/> puts it on the board's pose and scale every time it frames itself, so on screen it
    /// sits exactly where the spawned floor did (board-local origin).
    /// </summary>
    /// <remarks>Humble: one pose in, nothing out. Where it sits in the scene at edit time does not matter — the first
    /// frame of a round moves it.</remarks>
    public sealed class BoardFloorView : MonoBehaviour
    {
        [Tooltip("Raises (+) or lowers (−) the floor along the board's up, in BOARD units (scaled with the board). " +
                 "Applied every time the board frames itself, so it can be tuned in play mode.")]
        [SerializeField] private float _offsetY;

        /// <summary>Stand on <paramref name="board"/>'s origin (raised by the Y offset), with its rotation and scale.</summary>
        public void FollowBoard(Transform board)
        {
            if (board == null) return;
            var parentScale = transform.parent != null ? transform.parent.lossyScale.x : 1f;
            transform.localScale = board.lossyScale / Mathf.Max(parentScale, 1e-6f);
            transform.SetPositionAndRotation(board.TransformPoint(Vector3.up * _offsetY), board.rotation);
        }

        public void SetVisible(bool visible) => gameObject.SetActive(visible);
    }
}
