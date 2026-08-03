using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Nasser.SceneChunks.EditorTools
{
    /// <summary>
    /// One-off helper for maps whose props live under several category roots (Grass, Rocks, ...)
    /// rather than one parent. The Slice tool groups the *direct children of a single root*, so to
    /// slice a multi-root map you first move every prop under one flat root and point the slicer at
    /// that. This reparents the direct children of the selected roots into a single "Streamable
    /// Root", preserving world transforms. It does not change the slicer's grouping rule.
    ///
    /// Select ONLY the static, visual category roots (exclude terrain, lights, audio zones, navmesh,
    /// and anything gameplay references or that moves), then run this, then slice the Streamable Root.
    /// </summary>
    public static class SceneChunksFlattenTool
    {
        private const string StreamableRootName = "Streamable Root";

        [MenuItem("Tools/Scene Chunks/Flatten Selected Roots For Slicing")]
        private static void FlattenSelectedRoots()
        {
            List<Transform> roots = new List<Transform>();
            foreach (Object obj in Selection.objects)
            {
                GameObject go = obj as GameObject;
                if (go != null && go.scene.IsValid()) roots.Add(go.transform);
            }

            if (roots.Count == 0)
            {
                EditorUtility.DisplayDialog(
                    "Flatten For Slicing",
                    "Select one or more scene GameObjects (the static category roots whose children you " +
                    "want to stream), then run this again.",
                    "OK");
                return;
            }

            string names = string.Empty;
            for (int i = 0; i < roots.Count; i++) names += "\n  - " + roots[i].name + " (" + roots[i].childCount + " children)";

            bool go2 = EditorUtility.DisplayDialog(
                "Flatten For Slicing",
                "Move the direct children of these roots into a single \"" + StreamableRootName + "\":" + names +
                "\n\nWorld positions are preserved. The original roots are left in place (empty). Continue?",
                "Flatten", "Cancel");
            if (!go2) return;

            GameObject streamable = GameObject.Find(StreamableRootName);
            if (streamable == null)
            {
                streamable = new GameObject(StreamableRootName);
                Undo.RegisterCreatedObjectUndo(streamable, "Create Streamable Root");
                streamable.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            }

            int moved = 0;
            for (int i = 0; i < roots.Count; i++)
            {
                Transform root = roots[i];
                if (root == streamable.transform) continue;

                // Snapshot children first; reparenting mutates the child list mid-iteration.
                List<Transform> children = new List<Transform>(root.childCount);
                foreach (Transform child in root) children.Add(child);

                for (int c = 0; c < children.Count; c++)
                {
                    Undo.SetTransformParent(children[c], streamable.transform, "Flatten For Slicing");
                    moved++;
                }
            }

            Selection.activeGameObject = streamable;
            EditorUtility.DisplayDialog(
                "Flatten For Slicing",
                "Moved " + moved + " objects under \"" + StreamableRootName + "\".\n\n" +
                "Now open Tools > Scene Chunks > Streaming Setup > Slice Scene, set Source Root to \"" +
                StreamableRootName + "\", Preview, and check the distribution before baking.",
                "OK");
        }
    }
}
