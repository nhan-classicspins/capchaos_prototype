using System;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace Game.Editor
{
    /// <summary>
    /// Wires the containers for the board. (1) The shared <c>ContainerPalette</c> asset — the eight colour materials in
    /// CapColor order (R O B G P Y C N) plus the locked and hidden ones — made addressable as <c>ContainerPalette</c>
    /// in the Shared group (like the UI palette). (2) The authored <c>Container_S</c> prefab's <c>ContainerView</c> —
    /// its own parts only: the <c>Box</c> / <c>BoxLid</c> renderers, the "?" <c>Mystery</c> mark and the four
    /// <c>ItemAnchors</c> by name (01–04). Game.Editor may not reference Game.Views or Addressables (pinned graph), so
    /// both are reached by type name — the components through SerializedObject, Addressables through reflection.
    /// (3) The bigger sizes (GDD R21, SKU owner 2026-10-02: "keep the S model, stack the items"): Container_M / L / XL
    /// are COPIES of Container_S with 2 / 3 / 4 layers of the 2×2 anchors, the box stretched up and the lid, the "?"
    /// mark and the ice raised by the same amount. A copy is made only when it is missing — once it exists it is the
    /// art's to replace; it is only re-wired. (4) Every size gets a world-space <c>Count</c> text (TextMeshPro +
    /// TextTint) on the front of the box: the items still missing while the tray fills.
    /// </summary>
    internal static class ContainerWiring
    {
        private const string PrefabPath = "Assets/CapsChaos/Content/Art/Prefabs/Containers/Container_S.prefab";
        private const string Materials = "Assets/CapsChaos/Content/Art/Materials/Containers/";
        private const string PalettePath = Materials + "ContainerPalette.asset";
        private const string PaletteAddress = "ContainerPalette", PaletteGroup = "Shared";
        private const string ViewType = "Game.Views.ContainerView, Game.Views";
        private const string PaletteType = "Game.Views.ContainerPalette, Game.Views";
        /// <summary>M_Container_NN per CapColor R O B G P Y C N — the same numbers as the Items_NN prefabs
        /// GameplayScreen draws for each colour (01 red, 06 orange, 02 blue, 03 green, 07 purple, 04 yellow, 08 cyan,
        /// 05 pink for N).</summary>
        private static readonly string[] ColourMaterials = { "01", "06", "02", "03", "07", "04", "08", "05" };

        [MenuItem("CapsChaos/Art/Wire Containers")]
        public static void WireFromMenu() => Debug.Log(Wire());

        public static string Wire() => WirePalette() + " · " + CreateSizes() + " · " + WirePrefabs();

        // ── sizes (R21) ──────────────────────────────────────────────────────────────────────
        private const string Folder = "Assets/CapsChaos/Content/Art/Prefabs/Containers/";
        private static readonly (string name, int layers)[] Sizes = { ("Container_S", 1), ("Container_M", 2), ("Container_L", 3), ("Container_XL", 4) };

        // Mirrors of Game.Views.DesignTokens.Board (Game.Editor may not reference Game.Views): the board fits a container
        // ContainerSize across and shows a collected item at ItemInTray of its own size. Together with the model's width
        // they give an item's height in the container's own units — one anchor layer.
        private const float ContainerSize = 0.9f, ItemInTray = 0.85f;
        private const string ItemsFolder = "Assets/CapsChaos/Content/Art/Prefabs/Items";

        private const string TextMeshProType = "TMPro.TextMeshPro, Unity.TextMeshPro";
        private const string TextTintType = "Game.Views.TextTint, Game.Views";
        private const string CountFont = "Assets/CapsChaos/Fonts/LilitaOne-Regular Bitmap.asset";
        private const string CountFontMaterial = "Assets/CapsChaos/Fonts/LilitaOne-Regular-outline-black.mat";
        /// <summary>World-space TMP size of the count: readable on a container scaled into a slot (~0.83 of the belt size).</summary>
        private const float CountFontSize = 7f;

        /// <summary>Copy Container_S to every missing bigger size and build its layers (an existing copy is left alone).</summary>
        private static string CreateSizes()
        {
            var made = new System.Collections.Generic.List<string>();
            float step = LayerStep();
            foreach (var (name, layers) in Sizes)
            {
                if (layers == 1) continue;
                string path = Folder + name + ".prefab";
                if (AssetDatabase.LoadAssetAtPath<GameObject>(path) != null) continue;
                if (!AssetDatabase.CopyAsset(PrefabPath, path)) { made.Add(name + " COPY FAILED"); continue; }
                var root = PrefabUtility.LoadPrefabContents(path);
                try
                {
                    root.name = name;
                    float rise = (layers - 1) * step;
                    var anchorsRoot = Find(root.transform, "ItemAnchors");
                    if (anchorsRoot != null)
                    {
                        var baseLayer = new Transform[anchorsRoot.childCount];
                        for (int i = 0; i < baseLayer.Length; i++) baseLayer[i] = anchorsRoot.GetChild(i);
                        Array.Sort(baseLayer, (a, b) => string.CompareOrdinal(a.name, b.name));
                        int n = baseLayer.Length;
                        for (int layer = 1; layer < layers; layer++)
                            for (int i = 0; i < n; i++)
                            {
                                var a = UnityEngine.Object.Instantiate(baseLayer[i].gameObject, anchorsRoot).transform;
                                a.name = (layer * n + i + 1).ToString("00");
                                a.localPosition = baseLayer[i].localPosition + Vector3.up * (layer * step);
                                a.localRotation = baseLayer[i].localRotation;
                            }
                    }
                    StretchUp(Find(root.transform, "Box"), rise, root.transform);
                    StretchUp(Find(root.transform, "IceCube_4x4"), rise, root.transform);
                    foreach (var raised in new[] { "BoxLid", "Mystery", "Text (TMP)" })
                    {
                        var t = Find(root.transform, raised);
                        if (t != null) t.position += root.transform.up * rise;
                    }
                    PrefabUtility.SaveAsPrefabAsset(root, path);
                    made.Add($"{name} ({layers * 4} anchors, +{rise:0.00})");
                }
                finally { PrefabUtility.UnloadPrefabContents(root); }
            }
            return made.Count == 0 ? "sizes: all present" : "sizes made: " + string.Join(", ", made);
        }

        /// <summary>One anchor layer: the tallest item, at its in-tray size, in the S model's own units.</summary>
        private static float LayerStep()
        {
            var s = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            var sb = RootBounds(s);
            float fit = ContainerSize / Mathf.Max(Mathf.Max(sb.size.x, sb.size.z), 1e-5f);   // model units → tray units
            float tallest = 0f;
            foreach (var guid in AssetDatabase.FindAssets("t:Prefab", new[] { ItemsFolder }))
            {
                var item = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(guid));
                if (item != null) tallest = Mathf.Max(tallest, RootBounds(item).size.y);
            }
            if (tallest <= 0f) tallest = 0.55f;
            return tallest * ItemInTray / fit;
        }

        /// <summary>Bounds of every mesh under <paramref name="go"/>, in its root's space — the same rule as the board's
        /// PrefabFit.BoundsOf, so the layer step matches the size the board draws.</summary>
        private static Bounds RootBounds(GameObject go)
        {
            var b = new Bounds();
            bool any = false;
            foreach (var mf in go.GetComponentsInChildren<MeshFilter>(true))
            {
                if (mf.sharedMesh == null) continue;                    // like PrefabFit.BoundsOf: inactive parts count too
                var m = go.transform.worldToLocalMatrix * mf.transform.localToWorldMatrix;
                var mb = mf.sharedMesh.bounds;
                for (int i = 0; i < 8; i++)
                {
                    var c = m.MultiplyPoint3x4(mb.center + Vector3.Scale(mb.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1)));
                    if (!any) { b = new Bounds(c, Vector3.zero); any = true; } else b.Encapsulate(c);
                }
            }
            return b;
        }

        /// <summary>Make <paramref name="t"/>'s mesh <paramref name="rise"/> taller (in <paramref name="space"/>'s up), its
        /// bottom where it was.</summary>
        private static void StretchUp(Transform t, float rise, Transform space)
        {
            var mf = t != null ? t.GetComponent<MeshFilter>() : null;
            if (mf == null || mf.sharedMesh == null || rise <= 0f) return;
            float Bottom(out float height)
            {
                var m = space.worldToLocalMatrix * t.localToWorldMatrix;
                var mb = mf.sharedMesh.bounds;
                float lo = float.MaxValue, hi = float.MinValue;
                for (int i = 0; i < 8; i++)
                {
                    var c = m.MultiplyPoint3x4(mb.center + Vector3.Scale(mb.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1)));
                    lo = Mathf.Min(lo, c.y); hi = Mathf.Max(hi, c.y);
                }
                height = hi - lo;
                return lo;
            }
            float bottom = Bottom(out float h);
            // the local axis that points up in the container
            var up = space.up;
            int axis = 0; float best = -1f;
            for (int i = 0; i < 3; i++)
            {
                var dir = t.TransformDirection(i == 0 ? Vector3.right : i == 1 ? Vector3.up : Vector3.forward);
                float d = Mathf.Abs(Vector3.Dot(dir, up));
                if (d > best) { best = d; axis = i; }
            }
            var scale = t.localScale;
            scale[axis] *= (h + rise) / Mathf.Max(h, 1e-5f);
            t.localScale = scale;
            float shift = bottom - Bottom(out _);
            t.position += up * shift;
        }

        private static string WirePalette()
        {
            var type = Type.GetType(PaletteType);
            if (type == null) return "[ContainerWiring] ContainerPalette not found — compile Game.Views first";
            var palette = AssetDatabase.LoadAssetAtPath(PalettePath, type);
            if (palette == null)
            {
                palette = ScriptableObject.CreateInstance(type);
                AssetDatabase.CreateAsset(palette, PalettePath);
            }
            var so = new SerializedObject(palette);
            var colors = so.FindProperty("_colors");
            colors.arraySize = ColourMaterials.Length;
            for (int i = 0; i < ColourMaterials.Length; i++)
                colors.GetArrayElementAtIndex(i).objectReferenceValue = Material("M_Container_" + ColourMaterials[i]);
            so.FindProperty("_locked").objectReferenceValue = Material("M_Container_Locked");
            so.FindProperty("_hidden").objectReferenceValue = Material("M_Container_Hidden");
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(palette);
            AssetDatabase.SaveAssets();
            return $"[ContainerWiring] palette {PalettePath} ({colors.arraySize} colours) — {MakeAddressable(PalettePath)}";
        }

        private static string WirePrefabs()
        {
            var parts = new System.Collections.Generic.List<string>();
            float step = LayerStep();
            foreach (var (name, layers) in Sizes)
            {
                string path = Folder + name + ".prefab";
                if (AssetDatabase.LoadAssetAtPath<GameObject>(path) == null) { parts.Add(name + " missing"); continue; }
                parts.Add(WirePrefab(path, (layers - 1) * step));
            }
            return string.Join(" · ", parts);
        }

        private static string WirePrefab(string prefabPath, float rise)
        {
            var type = Type.GetType(ViewType);
            if (type == null) return "ContainerView not found — compile Game.Views first";
            var root = PrefabUtility.LoadPrefabContents(prefabPath);
            try
            {
                var view = root.GetComponent(type) ?? root.AddComponent(type);
                var so = new SerializedObject(view);
                so.FindProperty("_box").objectReferenceValue = Find(root.transform, "Box")?.GetComponent<Renderer>();
                so.FindProperty("_lid").objectReferenceValue = Find(root.transform, "BoxLid")?.GetComponent<Renderer>();
                so.FindProperty("_mystery").objectReferenceValue = Find(root.transform, "Mystery")?.gameObject;
                so.FindProperty("_rise").floatValue = rise;
                so.FindProperty("_count").objectReferenceValue = EnsureCount(root, rise);
                so.FindProperty("_lock").objectReferenceValue = EnsureLock(root, rise);
                var anchorsRoot = Find(root.transform, "ItemAnchors");
                var anchors = so.FindProperty("_anchors");
                anchors.arraySize = 0;
                if (anchorsRoot != null)
                {
                    var list = new Transform[anchorsRoot.childCount];
                    for (int i = 0; i < list.Length; i++) list[i] = anchorsRoot.GetChild(i);
                    Array.Sort(list, (a, b) => string.CompareOrdinal(a.name, b.name));          // 01, 02, … = cell 0, 1, …
                    anchors.arraySize = list.Length;
                    for (int i = 0; i < list.Length; i++) anchors.GetArrayElementAtIndex(i).objectReferenceValue = list[i];
                }
                so.ApplyModifiedPropertiesWithoutUndo();
                PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
                return $"{System.IO.Path.GetFileNameWithoutExtension(prefabPath)}: anchors {anchors.arraySize}" + (anchorsRoot == null ? " (no ItemAnchors!)" : "");
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }

        // Mirror of DesignTokens.Board.LockY: where the board used to spawn the padlock, tray-local (over an S container).
        private const float LockY = 0.42f;
        private const string TrayLockPrefab = "Assets/CapsChaos/Content/Art/Prefabs/TrayLock.prefab";
        private const string TrayLockViewType = "Game.Views.TrayLockView, Game.Views";

        /// <summary>
        /// The padlock (R18), wired into the container (SKU owner, 2026-10-02) so the art can place it per size: a nested
        /// TrayLock instance, off until the board shows the tray locked. Placed once where the board used to spawn it —
        /// LockY over the tray, raised by <paramref name="rise"/>, at its tray size (undoing the board's fit scale);
        /// an existing one is kept wherever the art moved it.
        /// </summary>
        private static UnityEngine.Object EnsureLock(GameObject root, float rise)
        {
            var viewType = Type.GetType(TrayLockViewType);
            var existing = Find(root.transform, "TrayLock");
            if (existing != null) return viewType != null ? existing.GetComponent(viewType) : null;
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(TrayLockPrefab);
            if (asset == null) return null;
            var sb = RootBounds(AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath));
            float fit = ContainerSize / Mathf.Max(Mathf.Max(sb.size.x, sb.size.z), 1e-5f);    // model units → tray units
            var go = (GameObject)PrefabUtility.InstantiatePrefab(asset, root.transform);
            go.name = "TrayLock";
            // tray y = fit × (model y − bottom)  ⇒  model y = LockY / fit + bottom
            go.transform.localPosition = new Vector3(0f, LockY / fit + sb.min.y + rise, 0f);
            go.transform.localScale = asset.transform.localScale / fit;
            go.SetActive(false);
            return viewType != null ? go.GetComponent(viewType) : null;
        }

        /// <summary>
        /// The missing-item count: a world-space TextMeshPro (+ TextTint keyed White — the palette's None text is
        /// black) standing in front of the box, tilted 60° toward the level GamePlay camera (board tilt −60°, ADR-001).
        /// Created once; an existing <c>Count</c> is kept as the art left it.
        /// </summary>
        private static UnityEngine.Object EnsureCount(GameObject root, float rise)
        {
            var tmpType = Type.GetType(TextMeshProType);
            if (tmpType == null) return null;
            var existing = Find(root.transform, "Count");
            if (existing != null) return existing.GetComponent(tmpType);
            var go = new GameObject("Count");
            go.transform.SetParent(root.transform, false);
            go.transform.localPosition = new Vector3(0f, 0.03f + rise * 0.5f, -0.8f);
            go.transform.localRotation = Quaternion.Euler(60f, 0f, 0f);
            go.AddComponent<RectTransform>().sizeDelta = new Vector2(1f, 0.5f);
            var text = go.AddComponent(tmpType);
            void Set(string prop, object value) => tmpType.GetProperty(prop)?.SetValue(text, value);
            Set("font", AssetDatabase.LoadAssetAtPath(CountFont, tmpType.GetProperty("font")!.PropertyType));
            var mat = AssetDatabase.LoadAssetAtPath<Material>(CountFontMaterial);
            if (mat != null) Set("fontSharedMaterial", mat);
            Set("fontSize", CountFontSize);
            Set("alignment", Enum.Parse(tmpType.GetProperty("alignment")!.PropertyType, "Center"));
            Set("text", "4");
            var tintType = Type.GetType(TextTintType);
            if (tintType != null)
            {
                var tso = new SerializedObject(go.AddComponent(tintType));
                var flavor = tso.FindProperty("_flavor");
                flavor.enumValueIndex = Array.IndexOf(flavor.enumNames, "White");
                tso.ApplyModifiedPropertiesWithoutUndo();
            }
            go.SetActive(false);                                     // shown by the board once the tray is in a slot
            return text;
        }

        /// <summary>Address the asset at <paramref name="path"/> as <see cref="PaletteAddress"/> in <see cref="PaletteGroup"/>,
        /// through Addressables' editor API found by reflection.</summary>
        private static string MakeAddressable(string path)
        {
            try
            {
                var settingsType = Type.GetType("UnityEditor.AddressableAssets.AddressableAssetSettingsDefaultObject, Unity.Addressables.Editor");
                var settings = settingsType?.GetProperty("Settings", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
                if (settings == null) return "Addressables settings not found: mark it addressable by hand";
                var group = settings.GetType().GetMethod("FindGroup", new[] { typeof(string) })?.Invoke(settings, new object[] { PaletteGroup });
                if (group == null) return $"no '{PaletteGroup}' group: mark it addressable by hand";
                var guid = AssetDatabase.AssetPathToGUID(path);
                MethodInfo create = null;
                foreach (var m in settings.GetType().GetMethods())
                    if (m.Name == "CreateOrMoveEntry" && m.GetParameters().Length >= 2 && m.GetParameters()[0].ParameterType == typeof(string)) { create = m; break; }
                if (create == null) return "CreateOrMoveEntry not found: mark it addressable by hand";
                var args = new object[create.GetParameters().Length];
                args[0] = guid; args[1] = group;
                for (int i = 2; i < args.Length; i++) args[i] = create.GetParameters()[i].HasDefaultValue ? create.GetParameters()[i].DefaultValue : false;
                var entry = create.Invoke(settings, args);
                entry?.GetType().GetProperty("address")?.SetValue(entry, PaletteAddress);
                EditorUtility.SetDirty((UnityEngine.Object)settings);
                return $"addressable '{PaletteAddress}' in {PaletteGroup}";
            }
            catch (Exception e) { return "making it addressable failed (" + e.Message + "): mark it addressable by hand"; }
        }

        private static Material Material(string name)
        {
            var m = AssetDatabase.LoadAssetAtPath<Material>(Materials + name + ".mat");
            if (m == null) Debug.LogWarning("[ContainerWiring] missing material " + name);
            return m;
        }

        private static Transform Find(Transform t, string name)
        {
            if (t.name == name) return t;
            for (int i = 0; i < t.childCount; i++)
            {
                var r = Find(t.GetChild(i), name);
                if (r != null) return r;
            }
            return null;
        }
    }
}
