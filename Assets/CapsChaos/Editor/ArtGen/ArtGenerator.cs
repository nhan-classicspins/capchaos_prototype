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
        // same reason: the tray-modifier views and the text tint, plus TextMeshPro (not referenced either)
        private const string TrayLockViewType = "Game.Views.TrayLockView, Game.Views";
        private const string TrayLinkViewType = "Game.Views.TrayLinkView, Game.Views";
        private const string TextTintType = "Game.Views.TextTint, Game.Views";
        private const string TextMeshProType = "TMPro.TextMeshPro, Unity.TextMeshPro";
        private const string CountFont = "Assets/CapsChaos/Fonts/LilitaOne-Regular Bitmap.asset";


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
                SaveUiSprite(ProceduralTextures.IconStar(128), 0),
                SaveUiSprite(ProceduralTextures.IconPlus(128), 0),
                SaveUiSprite(ProceduralTextures.IconPlay(128), 0),
                SaveUiSprite(ProceduralTextures.IconClose(128), 0),
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
            var slot = SaveMesh(ArtShapes.Slot());
            var rail = SaveMesh(ArtShapes.LaneRail());
            var belt = SaveMesh(ArtShapes.LaneBelt());
            var floor = SaveMesh(ArtShapes.Floor());
            log.Append("meshes 4 · ");
            // gone (2026-10-02): the screw cap, the bottle (the belt carries the Items_NN prefabs), the cap tray (the
            // authored Container_S replaces it) and the carton (a full container closes its own lid) — delete leftovers
            // through the AssetDatabase so their Addressables entries go with them
            foreach (var stale in new[]
                     {
                         Prefabs + "/Cap.prefab", Meshes + "/Cap.asset", Prefabs + "/Bottle.prefab", Meshes + "/Bottle.asset",
                         Materials + "/M_PlasticFrosted.mat", Prefabs + "/CapTray.prefab", Meshes + "/CapTray.asset",
                         Prefabs + "/Box.prefab", Meshes + "/BoxBody.asset", Meshes + "/BoxFlapLong.asset",
                         Meshes + "/BoxFlapShort.asset", Meshes + "/BoxTape.asset", Materials + "/M_Plastic.mat",
                         Materials + "/M_Cardboard.mat", Materials + "/M_Tape.mat", Materials + "/M_MysteryMark.mat",
                         Textures + "/T_Cardboard.png", Textures + "/T_MysteryMark.png",
                     })
                if (AssetDatabase.LoadMainAssetAtPath(stale) != null && AssetDatabase.DeleteAsset(stale)) log.Append("deleted ").Append(stale).Append(" · ");

            // textures
            var tBelt = SaveTexture(ProceduralTextures.BeltStripes(), TextureWrapMode.Repeat);
            var tShackle = SaveTexture(ProceduralTextures.LockShackle(), TextureWrapMode.Clamp);
            var tLockBody = SaveTexture(ProceduralTextures.LockBody(), TextureWrapMode.Clamp);
            var tRope = SaveTexture(ProceduralTextures.Rope(), TextureWrapMode.Repeat);
            log.Append("textures 4 · ");

            // materials — white, tinted per instance by TokenTint (URP Lit, instancing on)
            var mMatte = SaveMaterial("M_Matte", null, 0.25f);
            var mBelt = SaveMaterial("M_Belt", tBelt, 0.3f);
            // tray modifiers: unlit, alpha-blended decals / lines, still white and token-tinted
            var mShackle = SaveUnlitMaterial("M_LockShackle", tShackle);
            var mLockBody = SaveUnlitMaterial("M_LockBody", tLockBody);
            var mRope = SaveUnlitMaterial("M_Rope", tRope);
            var mRopeOutline = SaveUnlitMaterial("M_RopeOutline", null);
            log.Append("materials 6 · ");

            // prefabs
            var tintType = Type.GetType(TokenTintType);
            if (tintType == null) log.Append("WARNING TokenTint not found — prefabs saved untinted · ");

            SavePrefab("Slot", go => { Renderer(go, slot, mMatte); Tint(go, tintType, "SlotEmpty"); });
            SavePrefab("Lane", go =>
            {
                var r = Child(go, "Rail", Vector3.zero, Quaternion.identity);
                Renderer(r, rail, mMatte); Tint(r, tintType, "LaneRail");
                var b = Child(go, "Belt", Vector3.zero, Quaternion.identity);
                Renderer(b, belt, mBelt); Tint(b, tintType, "LaneBelt");
            });
            SavePrefab("Floor", go => { Renderer(go, floor, mMatte); Tint(go, tintType, "Floor"); });
            SavePrefab("TrayLock", go => BuildTrayLock(go, mShackle, mLockBody, tintType));
            SavePrefab("TrayLink", go => BuildTrayLink(go, mRope, mRopeOutline, tintType));
            log.Append("prefabs 5 (Slot, Lane, Floor, TrayLock, TrayLink)");

            AssetDatabase.SaveAssets();
            return log.ToString();
        }

        // ── tray modifiers (GDD R17–R19) ─────────────────────────────────────────────────────────
        private const float LockSize = 0.78f;                      // the padlock quad; the board places it (DesignTokens.Board.LockY)
        /// <summary>Tilt that turns a quad from the board's up toward the level GamePlay camera (board tilt −60°, ADR-001 §5).</summary>
        private const float FaceCamera = 60f;

        private static Mesh Quad => Resources.GetBuiltinResource<Mesh>("Quad.fbx");

        /// <summary>
        /// The padlock on a locked tray (R18) — placeholder art the SKU owner will replace (2026-10-01): a steel shackle
        /// and a charcoal body (two quads, one frame) with the remaining count printed on the body. The root carries
        /// TrayLockView; keep it when replacing the content.
        /// </summary>
        private static void BuildTrayLock(GameObject go, Material shackle, Material body, Type tintType)
        {
            go.transform.localRotation = Quaternion.Euler(FaceCamera, 0f, 0f);
            var sh = Child(go, "Shackle", Vector3.zero, Quaternion.identity);
            sh.transform.localScale = Vector3.one * LockSize;
            Renderer(sh, Quad, shackle).sortingOrder = 2;
            Tint(sh, tintType, "LockShackle");
            var bd = Child(go, "Body", new Vector3(0f, 0f, -0.002f), Quaternion.identity);   // quads face −Z: −Z is toward the camera
            bd.transform.localScale = Vector3.one * LockSize;
            Renderer(bd, Quad, body).sortingOrder = 3;
            Tint(bd, tintType, "LockBody");

            // the count, centred on the body (body centre is y −0.38 of the [−1, 1] frame → −0.19 × LockSize)
            var count = Child(go, "Count", new Vector3(0f, -0.19f * LockSize, -0.004f), Quaternion.identity);
            var text = AddText(count, "3", 3.4f, new Vector2(0.5f * LockSize, 0.36f * LockSize));
            var r = count.GetComponent<MeshRenderer>();
            if (r != null) r.sortingOrder = 4;

            var viewType = Type.GetType(TrayLockViewType);
            if (viewType == null) return;
            var so = new SerializedObject(go.AddComponent(viewType));
            so.FindProperty("_count").objectReferenceValue = text;
            so.FindProperty("_shackle").objectReferenceValue = sh.transform;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>The rope between linked trays (R19): a hemp line over a darker, wider outline; TrayLinkView sets the points.</summary>
        private static void BuildTrayLink(GameObject go, Material rope, Material outline, Type tintType)
        {
            Line(Child(go, "Outline", Vector3.zero, Quaternion.identity), outline, 0.15f, 5, tiled: false, tintType, "RopeOutline");
            Line(Child(go, "Rope", Vector3.zero, Quaternion.identity), rope, 0.1f, 6, tiled: true, tintType, "Rope");
            var viewType = Type.GetType(TrayLinkViewType);
            if (viewType != null) go.AddComponent(viewType);
        }

        private static void Line(GameObject go, Material mat, float width, int order, bool tiled, Type tintType, string token)
        {
            var lr = go.AddComponent<LineRenderer>();
            lr.sharedMaterial = mat;
            lr.widthMultiplier = width;
            lr.positionCount = 2;
            lr.SetPositions(new[] { Vector3.zero, Vector3.right });
            lr.useWorldSpace = true;
            lr.alignment = LineAlignment.View;
            lr.numCapVertices = 4;
            lr.numCornerVertices = 2;
            lr.textureMode = tiled ? LineTextureMode.Tile : LineTextureMode.Stretch;
            if (tiled) lr.textureScale = new Vector2(10f, 1f);
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            lr.receiveShadows = false;
            lr.sortingOrder = order;
            Tint(go, tintType, token);
        }

        /// <summary>A world-space TextMeshPro + TextTint keyed White (every text gets one), by type name — Game.Editor references neither.</summary>
        private static UnityEngine.Object AddText(GameObject go, string preview, float fontSize, Vector2 size)
        {
            var tmpType = Type.GetType(TextMeshProType);
            if (tmpType == null) return null;
            go.AddComponent<RectTransform>().sizeDelta = size;
            var text = go.AddComponent(tmpType);
            void Set(string prop, object value) => tmpType.GetProperty(prop)?.SetValue(text, value);
            Set("font", AssetDatabase.LoadAssetAtPath(CountFont, tmpType.GetProperty("font")!.PropertyType));
            // the font's own material: at this size the outline-black one swallows the white face
            Set("fontSize", fontSize);
            var align = tmpType.GetProperty("alignment")!.PropertyType;
            Set("alignment", Enum.Parse(align, "Center"));
            Set("text", preview);
            var tintText = Type.GetType(TextTintType);
            if (tintText != null)
            {
                // key White: the palette's None text is black (SKU owner), unreadable on the charcoal lock body
                var so = new SerializedObject(go.AddComponent(tintText));
                var flavor = so.FindProperty("_flavor");
                flavor.enumValueIndex = Array.IndexOf(flavor.enumNames, "White");
                so.ApplyModifiedPropertiesWithoutUndo();
            }
            return text;
        }

        private static Material SaveUnlitMaterial(string name, Texture2D baseMap)
        {
            var path = $"{Materials}/{name}.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            var shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (mat == null) { mat = new Material(shader) { name = name }; AssetDatabase.CreateAsset(mat, path); }
            else mat.shader = shader;
            mat.SetColor("_BaseColor", Color.white);
            mat.SetTexture("_BaseMap", baseMap);
            mat.SetTexture("_MainTex", baseMap);
            // alpha-blended, both faces, no depth write — sorted by the renderers' sortingOrder
            mat.SetFloat("_Surface", 1f);
            mat.SetFloat("_Blend", 0f);
            mat.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            mat.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            mat.SetFloat("_ZWrite", 0f);
            mat.SetFloat("_Cull", 0f);
            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            mat.enableInstancing = true;
            EditorUtility.SetDirty(mat);
            return mat;
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
        private static MeshRenderer Renderer(GameObject go, Mesh mesh, params Material[] materials)
        {
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterials = materials;
            return r;
        }

        private static GameObject Child(GameObject parent, string name, Vector3 pos, Quaternion rot)
        {
            var c = new GameObject(name);
            c.transform.SetParent(parent.transform, false);
            c.transform.localPosition = pos; c.transform.localRotation = rot;
            return c;
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
