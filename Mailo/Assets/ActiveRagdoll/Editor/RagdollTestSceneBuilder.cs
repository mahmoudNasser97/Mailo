using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace ActiveRagdoll
{
    /// <summary>
    /// Builds the Phase 0 test scene (spec §Phase 0): flat ground, camera, light, and the
    /// <see cref="RagdollTestHarness"/> for spawning crates / firing projectiles / slow-mo.
    /// It deliberately does NOT add a character — drop your own model in and run the
    /// setup wizard on it.
    /// </summary>
    public static class RagdollTestSceneBuilder
    {
        [MenuItem("Tools/Active Ragdoll/Create Test Scene")]
        static void CreateTestScene()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // Ground.
            var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.name = "Ground";
            ground.transform.localScale = new Vector3(6f, 1f, 6f); // 60 m
            Colorize(ground, new Color(0.32f, 0.34f, 0.36f));

            // Light.
            var lightGO = new GameObject("Directional Light");
            var light = lightGO.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.1f;
            light.shadows = LightShadows.Soft;
            lightGO.transform.rotation = Quaternion.Euler(50f, -30f, 0f);

            // Camera.
            var camGO = new GameObject("Main Camera");
            camGO.tag = "MainCamera";
            var cam = camGO.AddComponent<Camera>();
            camGO.AddComponent<AudioListener>();
            cam.backgroundColor = new Color(0.16f, 0.17f, 0.19f);
            cam.clearFlags = CameraClearFlags.SolidColor;
            camGO.transform.position = new Vector3(0f, 1.6f, -4f);
            camGO.transform.rotation = Quaternion.Euler(12f, 0f, 0f);

            // Test harness.
            var harnessGO = new GameObject("ActiveRagdoll Test Harness");
            harnessGO.AddComponent<RagdollTestHarness>();

            const string dir = "Assets/ActiveRagdoll/Scenes";
            if (!AssetDatabase.IsValidFolder(dir)) AssetDatabase.CreateFolder("Assets/ActiveRagdoll", "Scenes");
            EditorSceneManager.SaveScene(scene, dir + "/RagdollTestScene.unity");

            EditorUtility.DisplayDialog("Active Ragdoll",
                "Test scene created (Assets/ActiveRagdoll/Scenes/RagdollTestScene.unity).\n\n" +
                "Next:\n" +
                "1. Drag your rigged humanoid into the scene.\n" +
                "2. Tools ▸ Active Ragdoll ▸ Setup Wizard, assign it, Build.\n" +
                "3. Press Play — it should collapse into a clean ragdoll.\n\n" +
                "Keys:  C drop crate   F fire projectile   T slow-mo   F1 tuning panel.",
                "OK");
        }

        static void Colorize(GameObject go, Color color)
        {
            var r = go.GetComponent<Renderer>();
            if (r == null) return;
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) return;
            var mat = new Material(shader);
            mat.SetColor("_BaseColor", color);
            r.sharedMaterial = mat;
        }
    }
}
