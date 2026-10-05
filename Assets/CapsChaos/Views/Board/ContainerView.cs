using System.Threading;
using Cysharp.Threading.Tasks;
using LitMotion;
using TMPro;
using UnityEngine;

namespace Game.Views
{
    /// <summary>
    /// The tray an item queue fills (GDD R6–R13, R17, R18, R21): a <c>Container_S / M / L / XL</c> model. Caches ITS OWN parts only — the
    /// <c>Box</c> and <c>BoxLid</c> renderers, the "?" <c>Mystery</c> mark and the four <c>ItemAnchors</c> (01–04, cell
    /// 0–3) — and puts on whatever material the board hands it (the shared <see cref="ContainerPalette"/> holds them; no
    /// container keeps a copy). The anchors are where a collected item flies to and the parent it stays under. It
    /// also closes its lid (R13: a full container closes, then leaves). Humble: it is told the look and when to close.
    /// <para>The references are serialized on the prefab (menu CapsChaos/Art/Wire Containers); anything unwired is found
    /// by name on Awake, so an unwired instance still works. The bigger sizes are Container_S with more anchor layers
    /// stacked 2×2, a taller box and the lid raised by <see cref="Rise"/> (ContainerWiring builds them).</para>
    /// <para>On the belt the lid is closed; it opens when the tray lands in a slot, where the <c>Count</c> text shows how
    /// many items are still missing (text handed in by the controller).</para>
    /// </summary>
    public sealed class ContainerView : MonoBehaviour
    {
        [SerializeField] private Renderer _box;
        [SerializeField] private Renderer _lid;
        [SerializeField] private GameObject _mystery;
        /// <summary>Cell n → the <c>ItemAnchors</c> child numbered n + 1 (01, 02, …).</summary>
        [SerializeField] private Transform[] _anchors = new Transform[0];
        /// <summary>"How many are still missing", shown while the tray fills in a slot.</summary>
        [SerializeField] private TMP_Text _count;
        [Tooltip("How much taller than Container_S this model is (its own units): extra anchor layers, raised lid.")]
        [SerializeField] private float _rise;
        [Tooltip("The padlock shown while the tray is locked (R18) — a nested TrayLock, off by default; move it per size.")]
        [SerializeField] private TrayLockView _lock;

        /// <summary>The lid's renderer (<c>BoxLid</c>) — what a tray link's rope hangs off. Null if the model has none.</summary>
        public Renderer Lid { get { if (_lid == null) Cache(); return _lid; } }

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
            if (_count == null) { var c = FindDeep(transform, "Count"); if (c != null) _count = c.GetComponent<TMP_Text>(); }
            if (_lock == null) _lock = GetComponentInChildren<TrayLockView>(true);
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

        /// <summary>How much taller than an S container this one stands, in the units of the model's PARENT (the tray).</summary>
        public float Rise => _rise * transform.localScale.y;

        /// <summary>This container's own padlock (R18), or null when the prefab has none.</summary>
        public TrayLockView Lock
        {
            get
            {
                if (_lock == null) Cache();
                return _lock;
            }
        }

        /// <summary>How many items it takes (its anchors).</summary>
        public int AnchorCount
        {
            get
            {
                if (_anchors == null || _anchors.Length == 0) Cache();
                return _anchors?.Length ?? 0;
            }
        }

        /// <summary>Show <paramref name="text"/> on the container (the missing-item count); null hides it.</summary>
        public void SetCount(string text)
        {
            if (_count == null) Cache();
            if (_count == null) return;
            _count.gameObject.SetActive(text != null);
            if (text != null) _count.SetText(text);
        }

        /// <summary>The count changed: show the new one with a small pop (null hides it).</summary>
        public async UniTask PlayCountAsync(string text, float seconds, CancellationToken ct)
        {
            SetCount(text);
            if (_count == null || text == null) return;
            var t = _count.transform;
            if (!_countPose) { _countScale = t.localScale; _countPose = true; }
            var rest = _countScale;
            await LMotion.Create(1.3f, 1f, seconds).WithEase(Ease.OutBack)
                .Bind(k => { if (t != null) t.localScale = rest * k; }).AddTo(gameObject).ToUniTask(ct);
        }

        private Vector3 _countScale;
        private bool _countPose;

        /// <summary>
        /// Landing impact (SKU owner, 2026-10-02): the container squashes — y to <paramref name="flat"/>, x and z to
        /// <paramref name="wide"/> — over <paramref name="squash"/> seconds, then all three bounce back to 1 over
        /// <paramref name="recover"/>. Scales this model about its BOTTOM, so it stays standing on the slot.
        /// </summary>
        public async UniTask PlayImpactAsync(float squash, float recover, float wide, float flat, CancellationToken ct)
        {
            var t = transform;
            var restScale = t.localScale;
            var restPos = t.localPosition;
            float bottom = BottomY();                                                   // model units
            void Apply(Vector3 k)
            {
                if (t == null) return;
                t.localScale = Vector3.Scale(restScale, k);
                t.localPosition = restPos + Vector3.up * (bottom * (restScale.y - t.localScale.y));   // keep the bottom put
            }
            var squashed = new Vector3(wide, flat, wide);
            await LMotion.Create(Vector3.one, squashed, squash).WithEase(Ease.OutQuad).Bind(Apply).AddTo(gameObject).ToUniTask(ct);
            await LMotion.Create(squashed, Vector3.one, recover).WithEase(Ease.OutBack).Bind(Apply).AddTo(gameObject).ToUniTask(ct);
            if (t != null) { t.localScale = restScale; t.localPosition = restPos; }
        }

        /// <summary>The lowest point of the model's meshes, in its own units.</summary>
        private float BottomY()
        {
            if (_bottomKnown) return _bottom;
            float lo = float.MaxValue;
            var toLocal = transform.worldToLocalMatrix;
            foreach (var mf in GetComponentsInChildren<MeshFilter>())
            {
                if (mf.sharedMesh == null) continue;
                var m = toLocal * mf.transform.localToWorldMatrix;
                var b = mf.sharedMesh.bounds;
                for (int c = 0; c < 8; c++)
                    lo = Mathf.Min(lo, m.MultiplyPoint3x4(b.center + Vector3.Scale(b.extents,
                        new Vector3((c & 1) == 0 ? -1 : 1, (c & 2) == 0 ? -1 : 1, (c & 4) == 0 ? -1 : 1))).y);
            }
            _bottom = lo == float.MaxValue ? 0f : lo;
            _bottomKnown = true;
            return _bottom;
        }

        private float _bottom;
        private bool _bottomKnown;

        /// <summary>Every collected item (the children of the anchors) shrinks to nothing over <paramref name="seconds"/> —
        /// played with the lid closing on a full container.</summary>
        public async UniTask ShrinkItemsAsync(float seconds, CancellationToken ct)
        {
            if (_anchors == null || _anchors.Length == 0) Cache();
            var shrinks = new System.Collections.Generic.List<UniTask>();
            foreach (var a in _anchors)
            {
                if (a == null) continue;
                for (int i = 0; i < a.childCount; i++)
                {
                    var item = a.GetChild(i);
                    var from = item.localScale;
                    shrinks.Add(LMotion.Create(1f, 0f, seconds).WithEase(Ease.InBack)
                        .Bind(k => { if (item != null) item.localScale = from * k; }).AddTo(item.gameObject).ToUniTask(ct));
                }
            }
            await UniTask.WhenAll(shrinks);
        }

        /// <summary>The lid lifts off — up <paramref name="drop"/> (model units), tilting <paramref name="tilt"/>° — and
        /// is put away: the reverse of <see cref="CloseLidAsync"/>.</summary>
        public async UniTask OpenLidAsync(float seconds, float drop, float tilt, CancellationToken ct)
        {
            if (_lid == null) Cache();
            if (_lid == null || !_lid.gameObject.activeSelf) return;
            KeepLidPose();
            var t = _lid.transform;
            var parent = t.parent;
            var lift = transform.TransformVector(Vector3.up * drop);
            var to = _lidPos + (parent != null ? parent.InverseTransformVector(lift) : lift);
            var axis = parent != null ? parent.InverseTransformDirection(transform.right) : Vector3.right;
            var toRot = Quaternion.AngleAxis(-tilt, axis) * _lidRot;
            var rest = t.localScale;
            await LMotion.Create(0f, 1f, seconds).WithEase(Ease.InCubic).Bind(k =>
            {
                if (t == null) return;
                t.localPosition = Vector3.LerpUnclamped(_lidPos, to, k);
                t.localRotation = Quaternion.Slerp(_lidRot, toRot, k);
                t.localScale = rest * (1f - k);
            }).AddTo(gameObject).ToUniTask(ct);
            if (t == null) return;
            t.localScale = rest;
            t.localPosition = _lidPos;
            t.localRotation = _lidRot;
            _lid.gameObject.SetActive(false);
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

        /// <summary>Item <paramref name="cell"/>'s anchor (0, 1, … → ItemAnchors 01, 02, …): where a collected item flies to and the
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
