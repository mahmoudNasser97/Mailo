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

        PhysicsCharacterController physCtrl = player.GetComponent<PhysicsCharacterController>();
        if (physCtrl == null)
        {
            Debug.LogWarning("[CameraSetup] PhysicsCharacterController not found on Player — wiring will be skipped.");
        }

        ObjectGrabController grabCtrl = player.GetComponent<ObjectGrabController>();

        // ── 2. CinemachineBrain on Main Camera ────────────────────────────────
        Camera mainCam = Camera.main;
        if (mainCam == null)
        {
            Debug.LogError("[CameraSetup] No Main Camera found in scene.");
            return;
        }

        CinemachineBrain brain = GetOrAdd<CinemachineBrain>(mainCam.gameObject);
        brain.DefaultBlend = new CinemachineBlendDefinition(
            CinemachineBlendDefinition.Styles.EaseInOut, 0.2f);
        EditorUtility.SetDirty(brain);

        // ── 3. CameraPivot + CameraTarget ──────────────────────────────────────
        GameObject pivot = new GameObject("CameraPivot");
        Undo.RegisterCreatedObjectUndo(pivot, "Create CameraPivot");
        pivot.transform.SetPositionAndRotation(player.transform.position, Quaternion.identity);

        GameObject target = new GameObject("CameraTarget");
        Undo.RegisterCreatedObjectUndo(target, "Create CameraTarget");
        target.transform.SetParent(pivot.transform);
        target.transform.localPosition = new Vector3(0f, 1.4f, 0f);
        target.transform.localRotation = Quaternion.identity;

        // ── 4. NormalCamera ────────────────────────────────────────────────────
        GameObject normalGO = new GameObject("NormalCamera");
        Undo.RegisterCreatedObjectUndo(normalGO, "Create NormalCamera");
        CinemachineCamera normalCam = normalGO.AddComponent<CinemachineCamera>();
        normalCam.Priority = 10;
        normalCam.Follow   = target.transform;

        CinemachineThirdPersonFollow normalFollow = normalGO.AddComponent<CinemachineThirdPersonFollow>();
        normalFollow.ShoulderOffset    = new Vector3(0.5f, 0f, 0f);
        normalFollow.VerticalArmLength = 0.3f;
        normalFollow.CameraDistance    = 4f;
        var normalAvoid = normalFollow.AvoidObstacles;
        normalAvoid.Enabled         = true;
        normalAvoid.CameraRadius    = 0.2f;
        normalAvoid.CollisionFilter = BuildCollisionMask();
        normalFollow.AvoidObstacles = normalAvoid;
        normalFollow.CameraSide     = 1f;
        normalFollow.Damping        = Vector3.zero;
        EditorUtility.SetDirty(normalGO);

        LensSettings normalLens = normalCam.Lens;
        normalLens.FieldOfView = 65f;
        normalCam.Lens = normalLens;
        EditorUtility.SetDirty(normalCam);

        // ── 5. AimCamera ───────────────────────────────────────────────────────
        GameObject aimGO = new GameObject("AimCamera");
        Undo.RegisterCreatedObjectUndo(aimGO, "Create AimCamera");
        CinemachineCamera aimCam = aimGO.AddComponent<CinemachineCamera>();
        aimCam.Priority = 0;
        aimCam.Follow   = target.transform;

        CinemachineThirdPersonFollow aimFollow = aimGO.AddComponent<CinemachineThirdPersonFollow>();
        aimFollow.ShoulderOffset    = new Vector3(0.6f, 0f, 0f);
        aimFollow.VerticalArmLength = 0.2f;
        aimFollow.CameraDistance    = 1.8f;
        var aimAvoid = aimFollow.AvoidObstacles;
        aimAvoid.Enabled         = true;
        aimAvoid.CameraRadius    = 0.2f;
        aimAvoid.CollisionFilter = BuildCollisionMask();
        aimFollow.AvoidObstacles = aimAvoid;
        aimFollow.CameraSide     = 1f;
        aimFollow.Damping        = Vector3.zero;
        EditorUtility.SetDirty(aimGO);

        LensSettings aimLens = aimCam.Lens;
        aimLens.FieldOfView = 50f;
        aimCam.Lens = aimLens;
        EditorUtility.SetDirty(aimCam);

        // ── 6. Crosshair Canvas ────────────────────────────────────────────────
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

        // ── 7. CameraController on Player ─────────────────────────────────────
        CameraController camCtrl = GetOrAdd<CameraController>(player);

        var so = new SerializedObject(camCtrl);
        so.FindProperty("_cameraPivot").objectReferenceValue = pivot.transform;
        so.FindProperty("_aimCamera").objectReferenceValue   = aimCam;
        so.FindProperty("_crosshairUI").objectReferenceValue = canvasGO;
        so.ApplyModifiedProperties();

        EditorSceneManager.MarkSceneDirty(player.scene);

        Debug.Log(
            "[CameraSetup] ✓ Third-Person Camera Rig created\n" +
            $"  Player            : {player.name}\n" +
            $"  CameraPivot       : {pivot.name} (scene root)\n" +
            $"  CameraTarget      : {target.name} at local (0, 1.4, 0)\n" +
            $"  NormalCamera      : priority 10, FOV 65, distance 4\n" +
            $"  AimCamera         : priority 0, FOV 50, distance 1.8\n" +
            $"  CrosshairCanvas   : {canvasGO.name} (starts inactive)\n" +
             "  CameraController  : added to Player, all refs wired\n" +
             "  → Enter Play Mode and move mouse to verify orbit.\n" +
             "  → Pick up an object (F) then hold RMB to test aim zoom.\n" +
             "  → ESC toggles cursor lock.");
    }

    [MenuItem(MENU, true)]
    static bool Validate() => true;

    // Everything except the Player layer
    static LayerMask BuildCollisionMask()
    {
        int playerLayer = LayerMask.NameToLayer("Player");
        return playerLayer >= 0 ? ~(1 << playerLayer) : ~0;
    }

    static T GetOrAdd<T>(GameObject go) where T : Component
    {
        T c = go.GetComponent<T>();
        return c != null ? c : Undo.AddComponent<T>(go);
    }
}
