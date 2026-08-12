using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.UI;
using Unity.Cinemachine;

public static class CameraSetupTool
{
    const string MENU = "Tools/Camera/Setup Third-Person Camera Rig";

    [MenuItem(MENU)]
    static void SetupCameraRig()
    {
        // ── 1. Resolve Player ──────────────────────────────────────────────────
        GameObject player = Selection.activeGameObject;
        if (player == null)
            player = GameObject.FindWithTag("Player");

        if (player == null)
        {
            Debug.LogError("[CameraSetup] Select the Player in the Hierarchy first (or tag it 'Player').");
            return;
        }

        // ── 2. Main Camera ─────────────────────────────────────────────────────
        Camera mainCam = Camera.main;
        if (mainCam == null)
        {
            Debug.LogError("[CameraSetup] No Main Camera found in scene.");
            return;
        }

        // Disable CinemachineBrain so CameraController can drive the camera directly
        CinemachineBrain brain = mainCam.GetComponent<CinemachineBrain>();
        if (brain != null)
        {
            Undo.RecordObject(brain, "Disable CinemachineBrain");
            brain.enabled = false;
            EditorUtility.SetDirty(brain);
        }

        // ── 3. Crosshair Canvas ────────────────────────────────────────────────
        GameObject canvasGO = new GameObject("CrosshairCanvas");
        Undo.RegisterCreatedObjectUndo(canvasGO, "Create CrosshairCanvas");
        Canvas canvas = canvasGO.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvasGO.AddComponent<CanvasScaler>();
        canvasGO.AddComponent<GraphicRaycaster>();

        GameObject dotGO = new GameObject("CrosshairDot");
        Undo.RegisterCreatedObjectUndo(dotGO, "Create CrosshairDot");
        dotGO.transform.SetParent(canvasGO.transform, false);
        Image dot = dotGO.AddComponent<Image>();
        dot.color = Color.white;
        RectTransform dotRect = dotGO.GetComponent<RectTransform>();
        dotRect.anchorMin        = new Vector2(0.5f, 0.5f);
        dotRect.anchorMax        = new Vector2(0.5f, 0.5f);
        dotRect.pivot            = new Vector2(0.5f, 0.5f);
        dotRect.anchoredPosition = Vector2.zero;
        dotRect.sizeDelta        = new Vector2(8f, 8f);

        canvasGO.SetActive(false);

        // ── 4. CameraController on Player ─────────────────────────────────────
        CameraController camCtrl = GetOrAdd<CameraController>(player);

        var so = new SerializedObject(camCtrl);
        so.FindProperty("_camera").objectReferenceValue      = mainCam;
        so.FindProperty("_crosshairUI").objectReferenceValue = canvasGO;

        // Exclude Player layer from camera collision
        int playerLayer = LayerMask.NameToLayer("Player");
        int mask = playerLayer >= 0 ? ~(1 << playerLayer) : ~0;
        so.FindProperty("_collisionMask").intValue = mask;

        so.ApplyModifiedProperties();

        EditorSceneManager.MarkSceneDirty(player.scene);

        Debug.Log(
            "[CameraSetup] ✓ Third-Person Camera Rig created\n" +
            $"  Player          : {player.name}\n" +
            $"  Camera          : {mainCam.name} (direct control, CinemachineBrain disabled)\n" +
            $"  CrosshairCanvas : {canvasGO.name} (starts inactive)\n" +
             "  CameraController: added to Player, camera + crosshair wired\n" +
             "  → Enter Play Mode and move mouse to verify orbit.\n" +
             "  → Pick up an object (F) then hold RMB to test aim zoom.\n" +
             "  → ESC toggles cursor lock.\n" +
             "  → Tune Normal Distance / Aim Distance / Shoulder Offset in Inspector.");
    }

    [MenuItem(MENU, true)]
    static bool Validate() => true;

    static T GetOrAdd<T>(GameObject go) where T : Component
    {
        T c = go.GetComponent<T>();
        return c != null ? c : Undo.AddComponent<T>(go);
    }
}
