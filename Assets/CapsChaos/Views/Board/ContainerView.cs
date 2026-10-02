using System.Threading;
using Cysharp.Threading.Tasks;
using LitMotion;
using UnityEngine;

namespace Game.Views
{
    /// <summary>
    /// The tray an item queue fills (GDD R6–R13, R17, R18): the <c>Container_S</c> model. Caches ITS OWN parts only — the
    /// <c>Box</c> and <c>BoxLid</c> renderers, the "?" <c>Mystery</c> mark and the four <c>ItemAnchors</c> (01–04, cell
    /// 0–3) — and puts on whatever material the board hands it (the shared <see cref="ContainerPalette"/> holds them; no
    /// container keeps a copy). The anchors are where a collected item flies to and the parent it stays under. It
    /// also closes its lid (R13: a full container closes, then leaves). Humble: it is told the look and when to close.
    /// <para>The references are serialized on the prefab (menu CapsChaos/Art/Wire Containers); anything unwired is found
    /// by name on Awake, so an unwired instance still works.</para>
    /// </summary>
    public sealed class ContainerView : MonoBehaviour
    {
        [SerializeField] private Renderer _box;
        [SerializeField] private Renderer _lid;
        [SerializeField] private GameObject _mystery;
        /// <summary>Cell 0–3 → the <c>ItemAnchors</c> child of the same number (01–04).</summary>
        [SerializeField] private Transform[] _anchors = new Transform[0];

        private Vector3 _lidPos;
        private Quaternion _lidRot;
        private bool _lidPose;

        private void Awake() => Cache();

        /// <summary>Find what the prefab did not wire, by name.</summary>
        private void Cache()
        {
            if (_box == null) _box = FindRenderer("Box");
            if (_lid == null) _lid = FindRenderer("BoxLid");
            if (_mystery == null) { var m = FindDeep(transform, "Mystery"); if (m != null) _mystery = m.gameObject; }
            if (_anchors == null || _anchors.Length == 0)
            {
                var root = FindDeep(transform, "ItemAnchors");
                if (root != null)
                {
                    _anchors = new Transform[root.childCount];
                    for (int i = 0; i < _anchors.Length; i++) _anchors[i] = root.GetChild(i);
                    System.Array.Sort(_anchors, (a, b) => string.CompareOrdinal(a.name, b.name));   // 01, 02, 03, 04
                }
            }
        }

        /// <summary>Put <paramref name="material"/> on the box and its lid (null leaves them as they are), and show the
        /// "?" mark when <paramref name="hidden"/> (R17).</summary>
        public void SetLook(Material material, bool hidden)
        {
            if (_box == null && _lid == null) Cache();
            if (material != null)
            {
                if (_box != null) _box.sharedMaterial = material;
                if (_lid != null) _lid.sharedMaterial = material;
            }
            if (_mystery != null) _mystery.SetActive(hidden);
        }

        /// <summary>Open (the lid is put away — a container on the belt or filling up) or closed (the lid in its authored
        /// place).</summary>
        public void SetLidOpen(bool open)
        {
            if (_lid == null) Cache();
            if (_lid == null) return;
            KeepLidPose();
            var t = _lid.transform;
            t.localPosition = _lidPos;
            t.localRotation = _lidRot;
            _lid.gameObject.SetActive(!open);
        }

        /// <summary>The lid drops onto the box from <paramref name="drop"/> above it (in the model's own units — the box
        /// is about 0.5 tall), tilted <paramref name="tilt"/>°, and settles in place over <paramref name="seconds"/>.</summary>
        public async UniTask CloseLidAsync(float seconds, float drop, float tilt, CancellationToken ct)
        {
            if (_lid == null) Cache();
            if (_lid == null) return;
            KeepLidPose();
            var t = _lid.transform;
            var parent = t.parent;
            var lift = transform.TransformVector(Vector3.up * drop);                   // model units → world
            var from = _lidPos + (parent != null ? parent.InverseTransformVector(lift) : lift);
            var axis = parent != null ? parent.InverseTransformDirection(transform.right) : Vector3.right;
            var fromRot = Quaternion.AngleAxis(tilt, axis) * _lidRot;
            t.localPosition = from;
            t.localRotation = fromRot;
            _lid.gameObject.SetActive(true);
            await LMotion.Create(0f, 1f, seconds).WithEase(Ease.OutBounce).Bind(k =>
            {
                if (t == null) return;
                t.localPosition = Vector3.LerpUnclamped(from, _lidPos, k);
                t.localRotation = Quaternion.Slerp(fromRot, _lidRot, k);
            }).AddTo(gameObject).ToUniTask(ct);
        }

        private void KeepLidPose()
        {
            if (_lidPose || _lid == null) return;
            _lidPos = _lid.transform.localPosition;
            _lidRot = _lid.transform.localRotation;
            _lidPose = true;
        }

        /// <summary>Item <paramref name="cell"/>'s anchor (0–3 → ItemAnchors 01–04): where a collected item flies to and the
        /// parent it then stays under; null if the model has no anchors.</summary>
        public Transform Anchor(int cell)
        {
            if (_anchors == null || _anchors.Length == 0) Cache();
            return _anchors != null && cell >= 0 && cell < _anchors.Length ? _anchors[cell] : null;
        }

        private Renderer FindRenderer(string name)
        {
            var t = FindDeep(transform, name);
            return t != null ? t.GetComponent<Renderer>() : null;
        }

        private static Transform FindDeep(Transform t, string name)
        {
            if (t.name == name) return t;
            for (int i = 0; i < t.childCount; i++)
            {
                var r = FindDeep(t.GetChild(i), name);
                if (r != null) return r;
            }
            return null;
        }
    }
}
