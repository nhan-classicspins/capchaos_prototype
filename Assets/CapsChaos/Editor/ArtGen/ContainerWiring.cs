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

        public static string Wire() => WirePalette() + " · " + WirePrefab();

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

        private static string WirePrefab()
        {
            var type = Type.GetType(ViewType);
            if (type == null) return "ContainerView not found — compile Game.Views first";
            var root = PrefabUtility.LoadPrefabContents(PrefabPath);
            try
            {
                var view = root.GetComponent(type) ?? root.AddComponent(type);
                var so = new SerializedObject(view);
                so.FindProperty("_box").objectReferenceValue = Find(root.transform, "Box")?.GetComponent<Renderer>();
                so.FindProperty("_lid").objectReferenceValue = Find(root.transform, "BoxLid")?.GetComponent<Renderer>();
                so.FindProperty("_mystery").objectReferenceValue = Find(root.transform, "Mystery")?.gameObject;
                var anchorsRoot = Find(root.transform, "ItemAnchors");
                var anchors = so.FindProperty("_anchors");
                anchors.arraySize = 0;
                if (anchorsRoot != null)
                {
                    var list = new Transform[anchorsRoot.childCount];
                    for (int i = 0; i < list.Length; i++) list[i] = anchorsRoot.GetChild(i);
                    Array.Sort(list, (a, b) => string.CompareOrdinal(a.name, b.name));          // 01, 02, 03, 04 = cell 0..3
                    anchors.arraySize = list.Length;
                    for (int i = 0; i < list.Length; i++) anchors.GetArrayElementAtIndex(i).objectReferenceValue = list[i];
                }
                so.ApplyModifiedPropertiesWithoutUndo();
                PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
                return $"prefab {PrefabPath}: anchors {anchors.arraySize}" + (anchorsRoot == null ? " (no ItemAnchors!)" : "");
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
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
