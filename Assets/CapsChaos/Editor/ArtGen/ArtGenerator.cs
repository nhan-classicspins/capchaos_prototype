using System;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace Game.Editor
{
    /// <summary>
    /// Generates the MVP 3D art (art-direction §9): meshes, greyscale textures, white materials and
    /// prefabs under <c>Assets/CapsChaos/Content/Art/</c>. Idempotent and GUID-stable — an existing
    /// asset is overwritten IN PLACE, so every prefab/scene reference survives a regeneration.
    /// Run from the menu or headlessly through Unity-MCP (<c>ArtGenerator.GenerateAll()</c>).
    /// </summary>
    public static class ArtGenerator
    {
        private const string Root = "Assets/CapsChaos/Content/Art";
        private const string Meshes = Root + "/Meshes", Textures = Root + "/Textures",
                             Materials = Root + "/Materials", Prefabs = Root + "/Prefabs";

        // Game.Editor's reference list is pinned EMPTY by SkuHeadlessTests/Gate/AssemblyReferenceTests —
        // adding Game.Views would be a spine change. The colour tint component is therefore attached
        // by type NAME; the colours themselves stay in Game.Views.DesignTokens.
        private const string TokenTintType = "Game.Views.TokenTint, Game.Views";

        /// <summary>uGUI sprites (art §5): white 9-slice shapes the UiTint tokens colour.</summary>
        public const string UiSprites = "Assets/CapsChaos/Content/UI/Common/Sprites";
        private const float UiCornerRadius = 40f;   // == DesignTokens.Ui.CornerRadius

        [MenuItem("CapsChaos/Art/Generate UI Sprites")]
        public static void GenerateUiSpritesFromMenu() => Debug.Log(GenerateUiSprites());

        public static string GenerateUiSprites()
        {
            EnsureFolder(UiSprites);
            var paths = new System.Collections.Generic.List<string>
            {
                SaveUiSprite(ProceduralTextures.RoundedRect(128, UiCornerRadius), Mathf.CeilToInt(UiCornerRadius) + 4),
                SaveUiSprite(ProceduralTextures.Disc(128), 0),
                SaveUiSprite(ProceduralTextures.IconRetry(128), 0),
                SaveUiSprite(ProceduralTextures.IconHome(128), 0),
            };
            return "[ArtGen] UI sprites → " + string.Join(", ", paths);
        }

        /// <summary>White sprite at PPU 1 (the rig ruler); <paramref name="border"/> &gt; 0 makes it 9-slice.</summary>
        private static string SaveUiSprite(Texture2D tex, int border)
        {
            var path = $"{UiSprites}/{tex.name}.png";
            File.WriteAllBytes(path, tex.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            var imp = (TextureImporter)AssetImporter.GetAtPath(path);
            imp.textureType = TextureImporterType.Sprite;
            imp.spriteImportMode = SpriteImportMode.Single;
            imp.spritePixelsPerUnit = 1f;                         // the rig ruler: 1 texture px == 1 canvas px
            imp.spriteBorder = new Vector4(border, border, border, border);   // 9-slice: corners never stretch
            imp.mipmapEnabled = false;
            imp.alphaIsTransparency = true;
            imp.wrapMode = TextureWrapMode.Clamp;
            imp.filterMode = FilterMode.Bilinear;
            imp.SaveAndReimport();
            return path;
        }

        [MenuItem("CapsChaos/Art/Generate 3D Assets")]
        public static void GenerateFromMenu() => Debug.Log(GenerateAll());

        public static string GenerateAll()
        {
            var log = new StringBuilder("[ArtGen] ");
            foreach (var d in new[] { Meshes, Textures, Materials, Prefabs }) EnsureFolder(d);

            // meshes
            var bottle = SaveMesh(ArtShapes.Bottle());
            var cap = SaveMesh(ArtShapes.Cap());
            var tray = SaveMesh(ArtShapes.CapTray());
            var boxBody = SaveMesh(ArtShapes.BoxBody());
            var flapLong = SaveMesh(ArtShapes.BoxFlap("BoxFlapLong", ArtShapes.BoxWidth - 0.01f, ArtShapes.BoxDepth * 0.5f));
            var flapShort = SaveMesh(ArtShapes.BoxFlap("BoxFlapShort", ArtShapes.BoxDepth - 0.05f, ArtShapes.BoxWidth * 0.5f - 0.01f));
            var tape = SaveMesh(ArtShapes.Tape());
            var slot = SaveMesh(ArtShapes.Slot());
            var rail = SaveMesh(ArtShapes.LaneRail());
            var belt = SaveMesh(ArtShapes.LaneBelt());
            var floor = SaveMesh(ArtShapes.Floor());
            log.Append("meshes 11 · ");

            // textures
            var tRainbow = SaveTexture(ProceduralTextures.Rainbow(), TextureWrapMode.Repeat);
            var tBelt = SaveTexture(ProceduralTextures.BeltStripes(), TextureWrapMode.Repeat);
            var tCard = SaveTexture(ProceduralTextures.Cardboard(), TextureWrapMode.Repeat);
            log.Append("textures 3 · ");

            // materials — white, tinted per instance by TokenTint (URP Lit, instancing on)
            var mPlastic = SaveMaterial("M_Plastic", null, 0.86f);
            var mFrosted = SaveMaterial("M_PlasticFrosted", null, 0.35f);
            var mRainbow = SaveMaterial("M_Rainbow", tRainbow, 0.8f);
            var mCard = SaveMaterial("M_Cardboard", tCard, 0.15f);
            var mTape = SaveMaterial("M_Tape", null, 0.55f);
            var mMatte = SaveMaterial("M_Matte", null, 0.25f);
            var mBelt = SaveMaterial("M_Belt", tBelt, 0.3f);
            log.Append("materials 7 · ");

            // prefabs
            var tintType = Type.GetType(TokenTintType);
            if (tintType == null) log.Append("WARNING TokenTint not found — prefabs saved untinted · ");

            var pCap = SavePrefab("Cap", go =>
            {
                Renderer(go, cap, mPlastic);
                Tint(go, tintType, "FlavorCap");
            });
            SavePrefab("Bottle", go =>
            {
                Renderer(go, bottle, mPlastic, mFrosted);
                Tint(go, tintType, "FlavorBody", materialIndex: 0);
                Tint(go, tintType, "FlavorBand", materialIndex: 1);
            });
            SavePrefab("BottleHidden", go => Renderer(go, bottle, mRainbow, mRainbow));
            SavePrefab("CapTray", go =>
            {
                Renderer(go, tray, mPlastic);
                Tint(go, tintType, "FlavorBody");
                var cells = ArtShapes.CellCentres();
                for (int i = 0; i < cells.Length; i++)
                {
                    var c = (GameObject)PrefabUtility.InstantiatePrefab(pCap);
                    c.name = "Cap_" + i;
                    c.transform.SetParent(go.transform, false);
                    c.transform.localPosition = new Vector3(cells[i].x, ArtShapes.TrayHeight - 0.018f, cells[i].y);
                    c.transform.localScale = Vector3.one * ArtShapes.CapOnTrayScale;
                }
            });
            SavePrefab("Box", go =>
            {
                var body = Child(go, "Body", Vector3.zero, Quaternion.identity);
                Renderer(body, boxBody, mCard); Tint(body, tintType, "FlavorBody");
                float W = ArtShapes.BoxWidth, D = ArtShapes.BoxDepth, H = ArtShapes.BoxHeight, t = ArtShapes.BoxWall;
                // every pivot's local +Z points INTO the box (see Flap for the open / closed poses)
                Flap(go, "Flap_Left", new Vector3(-W * 0.5f + t, H, 0), 90f, flapShort, mCard, tintType);
                Flap(go, "Flap_Right", new Vector3(W * 0.5f - t, H, 0), -90f, flapShort, mCard, tintType);
                Flap(go, "Flap_Front", new Vector3(0, H + t, D * 0.5f - t), 180f, flapLong, mCard, tintType);
                Flap(go, "Flap_Back", new Vector3(0, H + t, -D * 0.5f + t), 0f, flapLong, mCard, tintType);
                var tp = Child(go, "Tape", new Vector3(0, H + 2 * t, 0), Quaternion.identity);
                Renderer(tp, tape, mTape); Tint(tp, tintType, "Tape");
                tp.SetActive(false);                                  // shown after the flaps close
            });
            SavePrefab("Slot", go => { Renderer(go, slot, mMatte); Tint(go, tintType, "SlotEmpty"); });
            SavePrefab("Lane", go =>
            {
                var r = Child(go, "Rail", Vector3.zero, Quaternion.identity);
                Renderer(r, rail, mMatte); Tint(r, tintType, "LaneRail");
                var b = Child(go, "Belt", Vector3.zero, Quaternion.identity);
                Renderer(b, belt, mBelt); Tint(b, tintType, "LaneBelt");
            });
            SavePrefab("Floor", go => { Renderer(go, floor, mMatte); Tint(go, tintType, "Floor"); });
            log.Append("prefabs 9 (Cap, Bottle, BottleHidden, CapTray, Box, Slot, Lane, Floor + nested caps)");

            AssetDatabase.SaveAssets();
            return log.ToString();
        }

        // ── assets ───────────────────────────────────────────────────────────────────────────
        private static Mesh SaveMesh(Mesh mesh)
        {
            var path = $"{Meshes}/{mesh.name}.asset";
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (existing == null) { AssetDatabase.CreateAsset(mesh, path); return mesh; }
            existing.Clear();
            EditorUtility.CopySerialized(mesh, existing);
            UnityEngine.Object.DestroyImmediate(mesh);
            EditorUtility.SetDirty(existing);
            return existing;
        }

        private static Texture2D SaveTexture(Texture2D tex, TextureWrapMode wrap)
        {
            var path = $"{Textures}/{tex.name}.png";
            File.WriteAllBytes(path, tex.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            var imp = (TextureImporter)AssetImporter.GetAtPath(path);
            imp.textureType = TextureImporterType.Default;        // never Sprite (SpriteImportDefaults scope)
            imp.wrapMode = wrap;
            imp.mipmapEnabled = true;
            imp.sRGBTexture = true;
            imp.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        private static Material SaveMaterial(string name, Texture2D baseMap, float smoothness)
        {
            var path = $"{Materials}/{name}.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (mat == null) { mat = new Material(shader) { name = name }; AssetDatabase.CreateAsset(mat, path); }
            else mat.shader = shader;
            mat.SetColor("_BaseColor", Color.white);
            mat.SetTexture("_BaseMap", baseMap);
            mat.SetTexture("_MainTex", baseMap);
            mat.SetFloat("_Smoothness", smoothness);
            mat.SetFloat("_Metallic", 0f);
            mat.enableInstancing = true;
            EditorUtility.SetDirty(mat);
            return mat;
        }

        private static GameObject SavePrefab(string name, Action<GameObject> build)
        {
            var go = new GameObject(name);
            try
            {
                build(go);
                return PrefabUtility.SaveAsPrefabAsset(go, $"{Prefabs}/{name}.prefab");
            }
            finally { UnityEngine.Object.DestroyImmediate(go); }
        }

        // ── prefab helpers ───────────────────────────────────────────────────────────────────
        private static void Renderer(GameObject go, Mesh mesh, params Material[] materials)
        {
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterials = materials;
        }

        private static GameObject Child(GameObject parent, string name, Vector3 pos, Quaternion rot)
        {
            var c = new GameObject(name);
            c.transform.SetParent(parent.transform, false);
            c.transform.localPosition = pos; c.transform.localRotation = rot;
            return c;
        }

        /// <summary>Open pose: the flap leans OUTWARD this far past vertical [QS video 1, 18.0 s].</summary>
        public const float FlapOpenLean = -35f;

        private static void Flap(GameObject box, string name, Vector3 hinge, float yaw, Mesh mesh, Material mat, Type tintType)
        {
            // pivot yaw aims local +Z into the box; closed = Euler(0, yaw, 0) * Euler(90, 0, 0)
            var pivot = Child(box, name, hinge, Quaternion.Euler(0f, yaw, 0f) * Quaternion.Euler(FlapOpenLean, 0f, 0f));
            var leaf = Child(pivot, "Leaf", new Vector3(0, 0, ArtShapes.BoxWall * 0.5f), Quaternion.identity);
            Renderer(leaf, mesh, mat);
            Tint(leaf, tintType, "FlavorBody");
        }

        private static void Tint(GameObject go, Type tintType, string token, int materialIndex = -1)
        {
            if (tintType == null) return;
            var so = new SerializedObject(go.AddComponent(tintType));
            var tok = so.FindProperty("_token");
            tok.enumValueIndex = Array.IndexOf(tok.enumNames, token);
            so.FindProperty("_materialIndex").intValue = materialIndex;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            var parent = Path.GetDirectoryName(path)!.Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }
    }
}
