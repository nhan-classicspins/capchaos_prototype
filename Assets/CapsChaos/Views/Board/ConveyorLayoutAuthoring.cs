using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;
using UnityEngine.Splines;

namespace Game.Views
{
    /// <summary>
    /// Edit a conveyor layout file in a scene — the ConveyorKit authoring loop (its <c>ConveyorEditorSpawner</c> +
    /// <c>ConveyorJsonExporter</c>) for Cap Chaos's format: point it at a <c>Conveyors/&lt;id&gt;.json</c>, <b>Spawn</b>
    /// builds the loop and the three feeders (right, left, middle) from the same ConveyorBelt prefab the game uses, edit
    /// the knots with the Splines tool (and the rule numbers below), then <b>Export</b> writes the file back. Both are on
    /// the component's context menu (⋮).
    /// </summary>
    /// <remarks>
    /// <para>Put it on an object at the world origin, unrotated and unscaled: knots are BOARD units, the same space the
    /// game draws them in. The gizmos show what the rules will see — every row on the loop (the pick zone highlighted),
    /// and each feeder's entrance (<c>mergeAt</c>) on the loop.</para>
    /// <para>Exported knots keep their position, their heading (the knot's rotation round Y, as in the Splines tool) and
    /// whether they are sharp (Linear) or smooth. The writer matches <c>ConveyorJson.Write</c> line for line, so
    /// <c>LevelTool migrate --check</c> stays clean; run <c>LevelTool validate</c> after an export.</para>
    /// Edit-time only: nothing in the game reads it.
    /// </remarks>
    public sealed class ConveyorLayoutAuthoring : MonoBehaviour
    {
        [Serializable] public sealed class NodeData { public float x, z, yRotation; public int tangentMode; }
        [Serializable] public sealed class LoopData { public NodeData[] nodes = Array.Empty<NodeData>(); }
        [Serializable] public sealed class FeederData { public string side; public int mergeAt; public NodeData[] nodes = Array.Empty<NodeData>(); }
        [Serializable] public sealed class MetaData { public string name, notes; }
        [Serializable] public sealed class LayoutData
        {
            public int formatVersion = 2;
            public string id;
            public int rows = 16, width = 4, pickRows = 4;
            public float scale = 1f;
            public LoopData loop = new LoopData();
            public FeederData[] feeders = Array.Empty<FeederData>();
            public MetaData meta = new MetaData();
        }

        private static readonly string[] Sides = { "right", "left", "middle" };

        [Tooltip("The conveyor file to edit (Content/LevelConfig/Conveyors/<id>.json).")]
        [SerializeField] private TextAsset _conveyorJson;
        [Tooltip("The ConveyorBelt prefab (Content/Art/Prefabs/ConveyorBelt).")]
        [SerializeField] private GameObject _beltPrefab;
        [Tooltip("What Spawn read; the rule numbers can be edited here. The knots come from the spawned belts on Export.")]
        [SerializeField] private LayoutData _layout = new LayoutData();
        [SerializeField, HideInInspector] private ConveyorBeltView[] _belts = Array.Empty<ConveyorBeltView>();

        public LayoutData Layout => _layout;

        [ContextMenu("Spawn")]
        public void Spawn()
        {
            if (_conveyorJson == null || _beltPrefab == null) { Debug.LogWarning("[ConveyorLayout] assign the conveyor JSON and the ConveyorBelt prefab first.", this); return; }
            Clear();
            _layout = JsonUtility.FromJson<LayoutData>(_conveyorJson.text);
            if (_layout.scale <= 0f) _layout.scale = 1f;
            var belts = new List<ConveyorBeltView> { NewBelt("Loop", _layout.loop.nodes, closed: true) };
            for (int f = 0; f < _layout.feeders.Length; f++) belts.Add(NewBelt("Feeder_" + _layout.feeders[f].side, _layout.feeders[f].nodes, closed: false));
            _belts = belts.ToArray();
            Dirty();
        }

        [ContextMenu("Clear")]
        public void Clear()
        {
            foreach (var b in _belts) if (b != null) Kill(b.gameObject);
            _belts = Array.Empty<ConveyorBeltView>();
            Dirty();
        }

        [ContextMenu("Export")]
        public void Export()
        {
#if UNITY_EDITOR
            if (_conveyorJson == null || _belts.Length == 0 || _belts[0] == null) { Debug.LogWarning("[ConveyorLayout] Spawn first.", this); return; }
            _layout.loop.nodes = Read(_belts[0]);
            for (int f = 0; f < _layout.feeders.Length && f + 1 < _belts.Length; f++) _layout.feeders[f].nodes = Read(_belts[f + 1]);
            string path = UnityEditor.AssetDatabase.GetAssetPath(_conveyorJson);
            System.IO.File.WriteAllText(path, Write(_layout), new UTF8Encoding(false));
            UnityEditor.AssetDatabase.ImportAsset(path);
            Debug.Log($"[ConveyorLayout] exported {_layout.id} → {path}. Run `dotnet run --project Tools/LevelTool -- validate`.", this);
#endif
        }

        private ConveyorBeltView NewBelt(string name, NodeData[] nodes, bool closed)
        {
#if UNITY_EDITOR
            var go = (GameObject)UnityEditor.PrefabUtility.InstantiatePrefab(_beltPrefab, Scaled());
#else
            var go = Instantiate(_beltPrefab, Scaled(), false);
#endif
            go.name = name;
            var belt = go.GetComponent<ConveyorBeltView>();
            var knots = new BeltNode[nodes.Length];
            float s = _layout.scale;                                   // board units → the scaled holder's units, as the game draws it
            for (int i = 0; i < nodes.Length; i++) knots[i] = new BeltNode(nodes[i].x / s, nodes[i].z / s, nodes[i].yRotation, nodes[i].tangentMode == 1);
            if (knots.Length >= 2) belt.SetRoute(knots, closed);
            return belt;
        }

        /// <summary>The holder the belts are built under: scaled by the layout's scale, as LoopBeltView is in the game.</summary>
        private Transform Scaled()
        {
            var t = transform.Find("Scaled");
            if (t == null) { t = new GameObject("Scaled").transform; t.SetParent(transform, false); }
            t.localScale = Vector3.one * _layout.scale;
            return t;
        }

        /// <summary>The belt's knots as the file stores them (board units: the scaled holder's units × scale).</summary>
        private NodeData[] Read(ConveyorBeltView belt)
        {
            float s = _layout.scale;
            var spline = belt.Spline.Spline;
            var nodes = new NodeData[spline.Count];
            for (int i = 0; i < spline.Count; i++)
            {
                var k = spline[i];
                var q = new Quaternion(k.Rotation.value.x, k.Rotation.value.y, k.Rotation.value.z, k.Rotation.value.w);
                nodes[i] = new NodeData
                {
                    x = Round(k.Position.x * s), z = Round(k.Position.z * s),
                    yRotation = Mathf.Round(Mathf.Repeat(q.eulerAngles.y, 360f) * 10f) / 10f,
                    tangentMode = spline.GetTangentMode(i) == TangentMode.Linear ? 1 : 0,
                };
            }
            return nodes;
        }

        private static float Round(float v) => Mathf.Round(v * 1000f) / 1000f;

        // ── writer: the same layout as Game.Domain.ConveyorJson.Write ──
        public static string Write(LayoutData c)
        {
            var sb = new StringBuilder();
            sb.Append("{\n");
            sb.Append("  \"$schema\": \"../../../../../docs/design/conveyor.schema.json\",\n");
            sb.Append($"  \"formatVersion\": {c.formatVersion},\n");
            sb.Append($"  \"id\": {Q(c.id)},\n");
            sb.Append($"  \"rows\": {c.rows},\n");
            sb.Append($"  \"width\": {c.width},\n");
            sb.Append($"  \"pickRows\": {c.pickRows},\n");
            sb.Append($"  \"scale\": {Num(c.scale)},\n");
            sb.Append("  \"loop\": { \"nodes\": ");
            Nodes(sb, c.loop.nodes, "    ");
            sb.Append(" },\n");
            sb.Append("  \"feeders\": [\n");
            for (int f = 0; f < c.feeders.Length; f++)
            {
                var fd = c.feeders[f];
                sb.Append($"    {{ \"side\": {Q(fd.side)}, \"mergeAt\": {fd.mergeAt}, \"nodes\": ");
                Nodes(sb, fd.nodes, "      ");
                sb.Append(" }").Append(f < c.feeders.Length - 1 ? ",\n" : "\n");
            }
            sb.Append("  ]");
            var meta = new List<string>();
            if (!string.IsNullOrEmpty(c.meta?.name)) meta.Add($"\"name\": {Q(c.meta.name)}");
            if (!string.IsNullOrEmpty(c.meta?.notes)) meta.Add($"\"notes\": {Q(c.meta.notes)}");
            if (meta.Count > 0) sb.Append(",\n  \"meta\": { ").Append(string.Join(", ", meta)).Append(" }");
            sb.Append("\n}\n");
            return sb.ToString();
        }

        private static void Nodes(StringBuilder sb, NodeData[] nodes, string indent)
        {
            if (nodes.Length == 0) { sb.Append("[]"); return; }
            sb.Append("[\n");
            for (int i = 0; i < nodes.Length; i++)
            {
                var n = nodes[i];
                sb.Append(indent).Append($"{{ \"x\": {Num(n.x)}, \"z\": {Num(n.z)}, \"yRotation\": {Num(n.yRotation)}");
                if (n.tangentMode == 1) sb.Append(", \"tangentMode\": 1");
                sb.Append(" }").Append(i < nodes.Length - 1 ? ",\n" : "\n");
            }
            sb.Append(indent, 0, indent.Length - 2).Append("]");
        }

        private static string Num(float d) => ((double)d).ToString("0.###", CultureInfo.InvariantCulture);

        private static string Q(string s)
        {
            var sb = new StringBuilder("\"");
            foreach (char c in s ?? "")
            {
                if (c == '"' || c == '\\') sb.Append('\\').Append(c);
                else if (c == '\n') sb.Append("\\n");
                else if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                else sb.Append(c);
            }
            return sb.Append('"').ToString();
        }

        private void Dirty()
        {
#if UNITY_EDITOR
            UnityEditor.EditorUtility.SetDirty(this);
#endif
        }

        private static void Kill(GameObject go)
        {
            if (Application.isPlaying) Destroy(go); else DestroyImmediate(go);
        }

        /// <summary>What the rules will see: every row on the loop (the pick zone brighter) and each feeder's entrance.</summary>
        private void OnDrawGizmos()
        {
            if (_belts.Length == 0 || _belts[0] == null || _layout.rows <= 0) return;
            var spline = _belts[0].Spline.Spline;
            if (spline == null || spline.Count < 2) return;
            var m = _belts[0].Spline.transform.localToWorldMatrix;            // includes the scaled holder
            Vector3 At(float row) => m.MultiplyPoint3x4((Vector3)spline.EvaluatePosition(Mathf.Repeat(row / _layout.rows, 1f))) + Vector3.up * DesignTokens.Gizmo.Lift;
            float g = DesignTokens.Gizmo.Marker;
            for (int r = 0; r < _layout.rows; r++)
            {
                Gizmos.color = r < _layout.pickRows ? DesignTokens.Gizmo.Land : DesignTokens.Gizmo.Reach;
                Gizmos.DrawWireSphere(At(r + 0.5f), g * (r < _layout.pickRows ? 0.6f : 0.35f));
            }
            for (int f = 0; f < _layout.feeders.Length; f++)
            {
                var e = At(_layout.feeders[f].mergeAt + 0.5f);
                Gizmos.color = DesignTokens.Gizmo.Entrance;
                Gizmos.DrawSphere(e, g);
#if UNITY_EDITOR
                UnityEditor.Handles.Label(e, $"{(f < Sides.Length ? Sides[f] : "?")} merge {_layout.feeders[f].mergeAt}");
#endif
            }
#if UNITY_EDITOR
            UnityEditor.Handles.Label(At(0f), "row 0 (pick zone)");
#endif
        }
    }
}
