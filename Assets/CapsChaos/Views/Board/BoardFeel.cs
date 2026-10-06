using LitMotion;
using UnityEngine;

namespace Game.Views
{
    /// <summary>
    /// The board's tunable feel (SKU owner, 2026-10-06): how items stack into a container, where the lid waits while
    /// the container fills and how it flies back, and how fast the game runs while the player holds an empty spot. ONE
    /// asset for the session (addressable <see cref="Address"/>, <c>Content/Configs/BoardFeel.asset</c>), loaded by the
    /// Gameplay screen and handed to the board; without it the defaults below apply (<see cref="CreateDefault"/>).
    /// </summary>
    [CreateAssetMenu(menuName = "CapsChaos/Board Feel", fileName = "BoardFeel")]
    public sealed class BoardFeel : ScriptableObject
    {
        public const string Address = "BoardFeel";

        [Header("Items into a container — every container has 4 anchors")]
        [Tooltip("Items stack this far above their anchors (board units) and wait there until the group of 4 is complete.")]
        [SerializeField] private float _itemStackY = 0.45f;
        [Tooltip("Seconds an item flies from the belt to its place in the stack.")]
        [SerializeField] private float _itemToStack = 0.2f;
        [Tooltip("Seconds a complete group takes to drop from the stack onto the anchors.")]
        [SerializeField] private float _itemDrop = 0.15f;
        [SerializeField] private Ease _itemDropEase = Ease.InQuad;
        [Tooltip("A container taking more than 4: seconds a full group of 4 squashes flat (scale y → 0) before it vanishes.")]
        [SerializeField] private float _itemGroupClear = 0.1f;

        [Header("Lid — parked beside the container while it fills")]
        [Tooltip("Where the lid waits, in the container model's own units (x right, y up, z away from the camera).")]
        [SerializeField] private Vector3 _lidParkPosition = new Vector3(0f, -0.05f, -0.95f);
        [Tooltip("Its rotation there (degrees). The default leans it back against the box, facing the camera (pitch 60°).")]
        [SerializeField] private Vector3 _lidParkEuler = new Vector3(-30f, 0f, 0f);
        [SerializeField] private float _lidParkScale = 0.7f;
        [Tooltip("Seconds the lid flies off to its parked place as the container lands in a slot.")]
        [SerializeField] private float _lidOpen = 0.25f;
        [Tooltip("How high above the straight line the lid's Bézier arc rises (container units).")]
        [SerializeField] private float _lidOpenArc = 0.5f;
        [SerializeField] private Ease _lidOpenEase = Ease.OutQuad;
        [Tooltip("Seconds the lid flies back onto a full container.")]
        [SerializeField] private float _lidClose = 0.3f;
        [SerializeField] private float _lidCloseArc = 0.8f;
        [SerializeField] private Ease _lidCloseEase = Ease.InOutQuad;

        [Header("Speed")]
        [Tooltip("The game runs this many times faster while the player holds an empty spot of the board.")]
        [SerializeField] private float _holdSpeed = 2f;

        public float ItemStackY => _itemStackY;
        public float ItemToStack => _itemToStack;
        public float ItemDrop => _itemDrop;
        public Ease ItemDropEase => _itemDropEase;
        public float ItemGroupClear => _itemGroupClear;
        public Vector3 LidParkPosition => _lidParkPosition;
        public Quaternion LidParkRotation => Quaternion.Euler(_lidParkEuler);
        public float LidParkScale => _lidParkScale;
        public float LidOpen => _lidOpen;
        public float LidOpenArc => _lidOpenArc;
        public Ease LidOpenEase => _lidOpenEase;
        public float LidClose => _lidClose;
        public float LidCloseArc => _lidCloseArc;
        public Ease LidCloseEase => _lidCloseEase;
        public float HoldSpeed => _holdSpeed;

        /// <summary>A runtime instance holding the defaults — used when the asset is missing.</summary>
        public static BoardFeel CreateDefault()
        {
            var feel = CreateInstance<BoardFeel>();
            feel.name = "BoardFeel (defaults)";
            feel.hideFlags = HideFlags.DontSave;
            return feel;
        }
    }
}
