using System;
using UnityEditor;
using UnityEngine;

namespace Game.Editor
{
    /// <summary>
    /// The SKU rule "every text gets a TextTint": whenever a TextMeshPro text appears in the Editor — added in the
    /// Inspector, or created from a GameObject menu (UI ▸ Text - TextMeshPro, 3D Object ▸ Text - TextMeshPro) — a
    /// <c>Game.Views.TextTint</c> is added next to it (undoable), so text colour is uniform by default. Remove it on
    /// a text that must keep its own colour; the hook never re-adds to an existing text.
    /// </summary>
    /// <remarks>
    /// Game.Editor references nothing (pinned graph), so both TMP and TextTint are found by name. Edit mode only:
    /// nothing is added to objects created while playing. Script-built hierarchies (PrefabUtility.LoadPrefabContents,
    /// runtime AddComponent) raise no Editor event — tools that build text add TextTint themselves.
    /// </remarks>
    [InitializeOnLoad]
    internal static class TextTintAutoAdd
    {
        private const string TmpTextType = "TMPro.TMP_Text, Unity.TextMeshPro";
        private const string TextTintType = "Game.Views.TextTint, Game.Views";

        static TextTintAutoAdd()
        {
            ObjectFactory.componentWasAdded += OnComponentAdded;
            ObjectChangeEvents.changesPublished += OnChangesPublished;
        }

        private static void OnComponentAdded(Component component)
        {
            if (component != null) EnsureOn(component.gameObject, includeChildren: false);
        }

        // menu-created GameObjects are built with new GameObject + AddComponent, which ObjectFactory does not see
        private static void OnChangesPublished(ref ObjectChangeEventStream stream)
        {
            for (int i = 0; i < stream.length; i++)
            {
                if (stream.GetEventType(i) != ObjectChangeKind.CreateGameObjectHierarchy) continue;
                stream.GetCreateGameObjectHierarchyEvent(i, out var created);
                if (EditorUtility.InstanceIDToObject(created.instanceId) is GameObject go)
                    EnsureOn(go, includeChildren: true);
            }
        }

        private static void EnsureOn(GameObject root, bool includeChildren)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || root == null) return;
            var tmp = Type.GetType(TmpTextType);
            var tint = Type.GetType(TextTintType);
            if (tmp == null || tint == null) return;
            var texts = includeChildren ? root.GetComponentsInChildren(tmp, true) : root.GetComponents(tmp);
            foreach (var text in texts)
                if (text != null && text.GetComponent(tint) == null)
                    Undo.AddComponent(text.gameObject, tint);
        }
    }
}
