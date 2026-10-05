using System.Threading;
using Cysharp.Threading.Tasks;
using LitMotion;
using UnityEngine;
using Bd = Game.Views.DesignTokens.Board;
using M = Game.Views.DesignTokens.Motion;

namespace Game.Views
{
    /// <summary>
    /// The rope between two linked trays (GDD R19). Root of the <c>TrayLink</c> prefab: it follows both trays every
    /// frame — belt steps, shakes, a lane moving ahead of the other — arcing over their caps. Every
    /// <see cref="LineRenderer"/> under it (rope, outline) gets the same points. Humble: two transforms in, a curve out.
    /// </summary>
    /// <remarks>
    /// The rope's shape is tuned on the prefab (Inspector, live in play mode too): <see cref="_ropeY"/> is how high above
    /// each tray the ends attach (tray units, so it scales with the tray like the lock does), <see cref="_ropeArc"/> how far the middle of the rope rises above them (board units),
    /// <see cref="_segments"/> how smooth the curve is. The defaults come from <see cref="DesignTokens.Board"/>. Points
    /// set on the LineRenderers themselves are overwritten every frame.
    /// </remarks>
    public sealed class TrayLinkView : MonoBehaviour
    {
        [Tooltip("Height of each rope end above its tray's pivot, in the tray's own units (like the lock's LockY).")]
        [SerializeField] private float _ropeY = Bd.RopeY;
        [Tooltip("How far the middle of the rope rises above the straight line between its ends, in board units.")]
        [SerializeField] private float _ropeArc = Bd.RopeArc;
        [Tooltip("Straight pieces the rope curve is drawn with.")]
        [SerializeField, Min(1)] private int _segments = Bd.RopeSegments;

        private Transform _a, _b;
        private LineRenderer[] _lines;
        private float[] _widths;
        private Vector3[] _points;

        public Transform A => _a;
        public Transform B => _b;

        public void Bind(Transform a, Transform b)
        {
            _a = a; _b = b;
            _lines = GetComponentsInChildren<LineRenderer>(true);
            _widths = new float[_lines.Length];
            for (int i = 0; i < _lines.Length; i++)
            {
                _widths[i] = _lines[i].widthMultiplier;
                _lines[i].useWorldSpace = true;
            }
            EnsurePoints();
            LateUpdate();
        }

        public bool Joins(Transform tray) => tray != null && (tray == _a || tray == _b);

        private void LateUpdate()
        {
            if (_a == null || _b == null || _lines == null) return;
            EnsurePoints();                                            // the segment count may have been changed in the Inspector
            // the board is scaled to the screen (1 world unit = 1 px), so offsets go through the transforms, never raw
            var from = _a.TransformPoint(Vector3.up * _ropeY);
            var to = _b.TransformPoint(Vector3.up * _ropeY);
            var arc = transform.TransformVector(Vector3.up * _ropeArc);
            for (int i = 0; i < _points.Length; i++)
            {
                float k = i / (float)(_points.Length - 1);
                _points[i] = Vector3.Lerp(from, to, k) + arc * Mathf.Sin(k * Mathf.PI);
            }
            foreach (var line in _lines) line.SetPositions(_points);
        }

        private void EnsurePoints()
        {
            int count = Mathf.Max(2, _segments + 1);
            if (_points != null && _points.Length == count) return;
            _points = new Vector3[count];
            if (_lines != null) foreach (var line in _lines) if (line != null) line.positionCount = count;
        }

        /// <summary>The trays left the belt: the rope thins away, then destroys itself.</summary>
        public async UniTask ReleaseAsync(CancellationToken ct)
        {
            if (_lines == null) { Destroy(gameObject); return; }
            await LMotion.Create(1f, 0f, M.LinkRelease).WithEase(Ease.InQuad).Bind(k =>
            {
                for (int i = 0; i < _lines.Length; i++) if (_lines[i] != null) _lines[i].widthMultiplier = _widths[i] * k;
            }).AddTo(gameObject).ToUniTask(ct);
            if (this != null) Destroy(gameObject);
        }
    }
}
