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
    public sealed class TrayLinkView : MonoBehaviour
    {
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
            _points = new Vector3[Mathf.Max(2, Bd.RopeSegments + 1)];
            for (int i = 0; i < _lines.Length; i++)
            {
                _widths[i] = _lines[i].widthMultiplier;
                _lines[i].useWorldSpace = true;
                _lines[i].positionCount = _points.Length;
            }
            LateUpdate();
        }

        public bool Joins(Transform tray) => tray != null && (tray == _a || tray == _b);

        private void LateUpdate()
        {
            if (_a == null || _b == null || _lines == null) return;
            var up = transform.parent != null ? transform.parent.up : Vector3.up;
            var from = _a.position + _a.up * Bd.RopeY;
            var to = _b.position + _b.up * Bd.RopeY;
            for (int i = 0; i < _points.Length; i++)
            {
                float k = i / (float)(_points.Length - 1);
                _points[i] = Vector3.Lerp(from, to, k) + up * (Mathf.Sin(k * Mathf.PI) * Bd.RopeArc);
            }
            foreach (var line in _lines) line.SetPositions(_points);
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
