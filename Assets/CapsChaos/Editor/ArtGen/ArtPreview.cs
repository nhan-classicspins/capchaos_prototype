using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace Game.Editor
{
    /// <summary>
    /// Renders the generated props in a gameplay-like layout to a PNG, inside an isolated PREVIEW
    /// scene (the user's open scenes are never touched or dirtied). This is an edit-mode asset check,
    /// not the rule-17 play-mode visual check of a finished screen.
    /// </summary>
    public static class ArtPreview
    {
        private const string Prefabs = "Assets/CapsChaos/Content/Art/Prefabs/";

        /// <summary>Editor-only art review scene — NOT a game screen (those go through the scene manifest
        /// + Scaffold.Sync, rule #13), never in build settings, rebuilt from the prefabs on every run.</summary>
        public const string ScenePath = "Assets/CapsChaos/Content/Art/ArtPreview.unity";

        // camera framing that matches the reference video's layout (stack top · slots · lanes bottom)
        private const float Fov = 30f, Pitch = 60f, Distance = 19.5f, FocusZ = 1.2f;

        [MenuItem("CapsChaos/Art/Render Preview")]
        public static void RenderFromMenu() => Debug.Log(Render("Temp/ArtPreview.png"));

        [MenuItem("CapsChaos/Art/Build Preview Scene")]
        public static void BuildSceneFromMenu()
        {
            Debug.Log(BuildScene());
            if (EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        }

        /// <summary>Writes <see cref="ScenePath"/> (additively, so the open scenes are untouched) and closes it again.</summary>
        public static string BuildScene()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            try
            {
                Build(scene);
                var cam = MakeCamera(scene, Fov, Pitch, Distance, FocusZ);
                cam.gameObject.tag = "MainCamera";
                EditorSceneManager.SaveScene(scene, ScenePath);
                return "[ArtPreview] saved " + ScenePath + " — open it, then Play or orbit in the Scene view";
            }
            finally { EditorSceneManager.CloseScene(scene, true); }
        }

        private static Camera MakeCamera(Scene scene, float fov, float pitch, float distance, float focusZ)
        {
            var camGo = new GameObject("PreviewCamera");
            SceneManager.MoveGameObjectToScene(camGo, scene);
            var cam = camGo.AddComponent<Camera>();
            cam.fieldOfView = fov;
            cam.nearClipPlane = 0.1f; cam.farClipPlane = 100f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.30f, 0.34f, 0.50f);
            var rot = Quaternion.Euler(pitch, 0f, 0f);
            var focus = new Vector3(0f, 0f, focusZ);
            camGo.transform.SetPositionAndRotation(focus - rot * Vector3.forward * distance, rot);
            return cam;
        }

        public static string Render(string outPath, int width = 1080, int height = 1920,
            float fov = Fov, float pitch = Pitch, float distance = Distance, float focusZ = FocusZ)
        {
            var scene = EditorSceneManager.NewPreviewScene();
            try
            {
                Build(scene);
                var cam = MakeCamera(scene, fov, pitch, distance, focusZ);
                cam.scene = scene;

                var rt = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
                cam.targetTexture = rt;
                cam.Render();
                var prev = RenderTexture.active;
                RenderTexture.active = rt;
                var tex = new Texture2D(width, height, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                tex.Apply();
                RenderTexture.active = prev;
                cam.targetTexture = null;
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outPath))!);
                File.WriteAllBytes(outPath, tex.EncodeToPNG());
                Object.DestroyImmediate(tex); Object.DestroyImmediate(rt);
                return "[ArtPreview] wrote " + Path.GetFullPath(outPath);
            }
            finally { EditorSceneManager.ClosePreviewScene(scene); }
        }

        private static void Build(Scene scene)
        {
            var lightGo = new GameObject("Sun");
            SceneManager.MoveGameObjectToScene(lightGo, scene);
            var sun = lightGo.AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.color = new Color(1f, 0.97f, 0.92f);
            sun.intensity = 1.25f;
            sun.shadows = LightShadows.Soft;
            sun.shadowStrength = 0.55f;
            lightGo.transform.rotation = Quaternion.Euler(55f, -35f, 0f);

            Place(scene, "Floor", Vector3.zero, Quaternion.identity, Vector3.one);

            // ── slot band + 3 slots ──
            const float slotZ = 1.55f, laneX = 1.12f;
            var band = Place(scene, "Slot", new Vector3(0, 0.002f, slotZ), Quaternion.identity, new Vector3(4.2f, 0.5f, 1.35f));
            SetToken(band, "SlotBand");
            for (int i = -1; i <= 1; i++) Place(scene, "Slot", new Vector3(i * laneX, 0.02f, slotZ), Quaternion.identity, Vector3.one);

            // slot 0: pink tray filling (2 bottles capped), slot 1: orange box sealed, slot 2: blue box open
            var tray = Place(scene, "CapTray", new Vector3(-laneX, 0.05f, slotZ), Quaternion.identity, Vector3.one, "Red");
            var cells = ArtShapes.CellCentres();
            for (int c = 0; c < 2; c++)
            {
                tray.transform.Find("Cap_" + c).gameObject.SetActive(false);
                var pos = tray.transform.position + new Vector3(cells[c].x, ArtShapes.TrayHeight - 0.018f, cells[c].y);
                Place(scene, "Bottle", pos, Quaternion.identity, Vector3.one, "Red");
                Place(scene, "Cap", pos + Vector3.up * 0.925f, Quaternion.identity, Vector3.one, "Red");
            }
            var sealedBox = Place(scene, "Box", new Vector3(0, 0.05f, slotZ), Quaternion.identity, Vector3.one, "Orange");
            foreach (var f in new[] { "Flap_Left", "Flap_Right", "Flap_Front", "Flap_Back" })
            {
                var p = sealedBox.transform.Find(f);
                p.localRotation = p.localRotation * Quaternion.Euler(90f - ArtGenerator.FlapOpenLean, 0f, 0f);
            }
            sealedBox.transform.Find("Tape").gameObject.SetActive(true);
            Place(scene, "Box", new Vector3(laneX, 0.05f, slotZ), Quaternion.identity, Vector3.one, "Blue");

            // ── 3 conveyor lanes ──
            string[] lanes = { "BOGRY", "RGBOC", "GRPOB" };
            for (int l = 0; l < 3; l++)
            {
                float x = (l - 1) * laneX, z0 = 0.62f;
                Place(scene, "Lane", new Vector3(x, 0f, z0), Quaternion.identity, Vector3.one);
                for (int t = 0; t < lanes[l].Length; t++)
                    Place(scene, "CapTray", new Vector3(x, 0.01f, z0 - 0.5f - t * ArtShapes.LanePitch),
                        Quaternion.identity, Vector3.one, FlavorOf(lanes[l][t]));
            }

            // ── bottle stack: ground visible, upper layers hidden (rainbow) ──
            string[] ground = { "GOOBOOG", "GOBBBOG", "GRROYRG", "BBRORBB" };   // last row = FRONT
            const int cols = 7;
            float pitch = ArtShapes.CellPitch, stackFrontZ = 3.45f;
            for (int r = 0; r < ground.Length; r++)
                for (int c = 0; c < cols; c++)
                {
                    var basePos = new Vector3((c - (cols - 1) * 0.5f) * pitch, 0f,
                        stackFrontZ + (ground.Length - 1 - r) * pitch);
                    Place(scene, "Bottle", basePos, Quaternion.identity, Vector3.one, FlavorOf(ground[r][c]));
                    int layers = r == ground.Length - 1 ? (c % 3 == 0 ? 1 : 0) : (r == 0 ? 2 : 1);
                    for (int k = 1; k <= layers; k++)
                        Place(scene, "BottleHidden", basePos + Vector3.up * k * ArtShapes.BottleHeight * 0.92f,
                            Quaternion.Euler(0f, (r * 7 + c) * 37f, 0f), Vector3.one);
                }
        }

        private static GameObject Place(Scene scene, string prefab, Vector3 pos, Quaternion rot, Vector3 scale, string flavor = null)
        {
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(Prefabs + prefab + ".prefab");
            if (asset == null) throw new InvalidOperationException("missing prefab " + prefab);
            var go = (GameObject)PrefabUtility.InstantiatePrefab(asset, scene);
            go.transform.SetPositionAndRotation(pos, rot);
            go.transform.localScale = scale;
            if (flavor != null) SetFlavor(go, flavor);
            return go;
        }

        // TokenTint lives in Game.Views, which Game.Editor may not reference (pinned graph) — drive it
        // through its serialized fields; OnValidate repaints.
        // The preview layouts are written in level-file codes; Game.Editor sees neither Game.Domain's CapColor
        // nor Game.Views' TintFlavor (pinned graph), so the code → member-name table is mirrored here.
        private const string FlavorCodes = "ROBGPYCN";
        private static readonly string[] FlavorNames = { "Red", "Orange", "Blue", "Green", "Purple", "Yellow", "Cyan", "Brown" };

        private static string FlavorOf(char code)
        {
            int i = FlavorCodes.IndexOf(code);
            if (i < 0) throw new ArgumentException($"'{code}' is not a colour code ({FlavorCodes})");
            return FlavorNames[i];
        }

        /// <param name="flavor">A <c>Game.Views.TintFlavor</c> member name ("Red", "Orange", …).</param>
        private static void SetFlavor(GameObject go, string flavor)
        {
            foreach (var mb in go.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (mb == null || mb.GetType().FullName != "Game.Views.TokenTint") continue;
                var so = new SerializedObject(mb);
                var p = so.FindProperty("_color");
                p.enumValueIndex = Array.IndexOf(p.enumNames, flavor);
                so.ApplyModifiedPropertiesWithoutUndo();
                mb.SendMessage("Apply", SendMessageOptions.DontRequireReceiver);
            }
        }

        private static void SetToken(GameObject go, string token)
        {
            foreach (var mb in go.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (mb == null || mb.GetType().FullName != "Game.Views.TokenTint") continue;
                var so = new SerializedObject(mb);
                var p = so.FindProperty("_token");
                p.enumValueIndex = Array.IndexOf(p.enumNames, token);
                so.ApplyModifiedPropertiesWithoutUndo();
                mb.SendMessage("Apply", SendMessageOptions.DontRequireReceiver);
            }
        }
    }
}
