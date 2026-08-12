using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using RootMotion.Dynamics;
using RootMotion.Demos;
using PMMuscle = RootMotion.Dynamics.Muscle;

public class ThirdPersonCharacterSetupTool : EditorWindow
{
    private GameObject _model;
    private Vector2    _scroll;
    private readonly List<string> _log = new List<string>();

    const int CharLayer    = 8;
    const int RagdollLayer = 9;
    const string SourceControllerPath =
        "Assets/Plugins/RootMotion/PuppetMaster/_DEMOS/Assets/Humanoid Controllers/Humanoid Third Person Puppet.controller";
    const string OutputControllerDir = "Assets/Mahmoud SandBox/Characters";

    [MenuItem("Tools/Mahmoud SandBox/Setup ThirdPerson Character")]
    static void ShowWindow() => GetWindow<ThirdPersonCharacterSetupTool>("ThirdPerson Setup");

    void OnGUI()
    {
        EditorGUILayout.LabelField("ThirdPerson Character Setup", EditorStyles.boldLabel);
        EditorGUILayout.Space();

        _model = (GameObject)EditorGUILayout.ObjectField("Model", _model, typeof(GameObject), true);
        if (_model == null && Selection.activeGameObject != null)
            _model = Selection.activeGameObject;

        EditorGUI.BeginDisabledGroup(_model == null);
        if (GUILayout.Button("Setup Character", GUILayout.Height(30)))
            Run();
        EditorGUI.EndDisabledGroup();

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Progress", EditorStyles.boldLabel);
        _scroll = EditorGUILayout.BeginScrollView(_scroll, GUILayout.ExpandHeight(true));
        foreach (string line in _log)
            EditorGUILayout.LabelField(line, EditorStyles.wordWrappedLabel);
        EditorGUILayout.EndScrollView();
    }

    void Log(string msg)
    {
        _log.Add(msg);
        Debug.Log("[ThirdPersonSetup] " + msg);
        Repaint();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Entry point
    // ─────────────────────────────────────────────────────────────────────────
    void Run()
    {
        _log.Clear();
        Log("Starting setup…");

        if (!Validate(out Animator anim)) return;

        string modelName = _model.name;
        Log($"Model: {modelName}");

        GameObject root  = BuildHierarchy(modelName);
        GameObject pmGo  = root.transform.Find("PuppetMaster").gameObject;
        GameObject behGo = root.transform.Find("Behaviours").gameObject;
        GameObject camGo = root.transform.Find("Character Camera").gameObject;
        GameObject ccGo  = root.transform.Find("Character Controller").gameObject;
        GameObject acGo  = ccGo.transform.Find("Animation Controller").gameObject;
        Log("Hierarchy built.");

        MoveModel(acGo);
        Log("Model moved under Animation Controller.");

        SetupPhysicsSkeleton(anim, pmGo);
        Log("Physics skeleton + ragdoll created.");

        SetupPuppetMaster(pmGo, ccGo, behGo);
        Log("PuppetMaster configured.");

        SetupCharacterControllerGo(ccGo);
        Log("Character Controller scripts added.");
        SetupAnimationControllerGo(acGo, anim.avatar, modelName);
        Log("Animation Controller scripts added.");
        SetupCameraGo(camGo);
        Log("Camera configured.");

        WireCrossReferences(root, pmGo, ccGo, acGo, camGo);
        Log("Cross-references wired.");
        FinalizeLayersAndSave(root);
        Log("✓ Setup complete!");
        LogPostSetupNotes();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Step 1: Validate
    // ─────────────────────────────────────────────────────────────────────────
    bool Validate(out Animator anim)
    {
        anim = _model.GetComponent<Animator>();
        if (anim == null) { Log("ERROR: No Animator on selected GameObject."); return false; }
        if (anim.avatar == null || !anim.avatar.isHuman)
        {
            Log("ERROR: Animator has no humanoid Avatar. Set Animation Type = Humanoid in Import Settings.");
            return false;
        }

        HumanBodyBones[] required =
        {
            HumanBodyBones.Hips,
            HumanBodyBones.Spine, HumanBodyBones.Head,
            HumanBodyBones.LeftUpperArm,  HumanBodyBones.LeftLowerArm,  HumanBodyBones.LeftHand,
            HumanBodyBones.RightUpperArm, HumanBodyBones.RightLowerArm, HumanBodyBones.RightHand,
            HumanBodyBones.LeftUpperLeg,  HumanBodyBones.LeftLowerLeg,  HumanBodyBones.LeftFoot,
            HumanBodyBones.RightUpperLeg, HumanBodyBones.RightLowerLeg, HumanBodyBones.RightFoot,
        };

        var missing = new List<string>();
        foreach (var bone in required)
            if (anim.GetBoneTransform(bone) == null) missing.Add(bone.ToString());

        if (missing.Count > 0) { Log($"ERROR: Missing bones: {string.Join(", ", missing)}"); return false; }
        return true;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Step 2: Build GameObject hierarchy
    // ─────────────────────────────────────────────────────────────────────────
    GameObject BuildHierarchy(string modelName)
    {
        var root = new GameObject($"{modelName}_Character");
        root.tag   = "Player";
        root.layer = 0;

        var pmGo = new GameObject("PuppetMaster");
        pmGo.transform.SetParent(root.transform, false);
        pmGo.tag   = "Player";
        pmGo.layer = RagdollLayer;

        var behGo = new GameObject("Behaviours");
        behGo.transform.SetParent(root.transform, false);

        new GameObject("Puppet (with Fall)").transform.SetParent(behGo.transform, false);
        new GameObject("Fall").transform.SetParent(behGo.transform, false);

        var camGo = new GameObject("Character Camera");
        camGo.transform.SetParent(root.transform, false);
        camGo.tag   = "MainCamera";
        camGo.layer = RagdollLayer;
        camGo.SetActive(false);

        var ccGo = new GameObject("Character Controller");
        ccGo.transform.SetParent(root.transform, false);
        ccGo.tag   = "Player";
        ccGo.layer = CharLayer;

        var acGo = new GameObject("Animation Controller");
        acGo.transform.SetParent(ccGo.transform, false);
        acGo.tag   = "Player";
        acGo.layer = CharLayer;

        return root;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Step 3: Move model
    // ─────────────────────────────────────────────────────────────────────────
    void MoveModel(GameObject acGo)
    {
        if (PrefabUtility.IsPartOfPrefabInstance(_model))
            PrefabUtility.UnpackPrefabInstance(_model, PrefabUnpackMode.Completely,
                InteractionMode.AutomatedAction);

        _model.transform.SetParent(acGo.transform, false);
        _model.transform.localPosition = Vector3.zero;
        _model.transform.localRotation = Quaternion.identity;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Steps 4-5: Physics skeleton clone + BipedRagdollCreator ragdoll
    // ─────────────────────────────────────────────────────────────────────────
    void SetupPhysicsSkeleton(Animator anim, GameObject pmGo)
    {
        Transform hips = anim.GetBoneTransform(HumanBodyBones.Hips);
        CloneBoneRecursive(hips, pmGo.transform);

        var refs = BuildBipedRefs(anim, pmGo.transform);

        string validMsg = string.Empty;
        if (!refs.IsValid(ref validMsg))
        {
            Log($"ERROR: Invalid biped refs — {validMsg}");
            return;
        }

        var opts = BipedRagdollCreator.AutodetectOptions(refs);
        BipedRagdollCreator.Create(refs, opts);
    }

    void CloneBoneRecursive(Transform src, Transform parent)
    {
        var go = new GameObject(src.name);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = src.localPosition;
        go.transform.localRotation = src.localRotation;
        go.transform.localScale    = src.localScale;
        foreach (Transform child in src)
            CloneBoneRecursive(child, go.transform);
    }

    BipedRagdollReferences BuildBipedRefs(Animator anim, Transform pmParent)
    {
        Transform Clone(HumanBodyBones b)
        {
            Transform orig = anim.GetBoneTransform(b);
            if (orig == null) return null;
            return FindByName(pmParent, orig.name);
        }

        return new BipedRagdollReferences
        {
            root          = pmParent,
            hips          = Clone(HumanBodyBones.Hips),
            spine         = Clone(HumanBodyBones.Spine),
            chest         = Clone(HumanBodyBones.Chest),
            head          = Clone(HumanBodyBones.Head),
            leftUpperArm  = Clone(HumanBodyBones.LeftUpperArm),
            leftLowerArm  = Clone(HumanBodyBones.LeftLowerArm),
            leftHand      = Clone(HumanBodyBones.LeftHand),
            rightUpperArm = Clone(HumanBodyBones.RightUpperArm),
            rightLowerArm = Clone(HumanBodyBones.RightLowerArm),
            rightHand     = Clone(HumanBodyBones.RightHand),
            leftUpperLeg  = Clone(HumanBodyBones.LeftUpperLeg),
            leftLowerLeg  = Clone(HumanBodyBones.LeftLowerLeg),
            leftFoot      = Clone(HumanBodyBones.LeftFoot),
            rightUpperLeg = Clone(HumanBodyBones.RightUpperLeg),
            rightLowerLeg = Clone(HumanBodyBones.RightLowerLeg),
            rightFoot     = Clone(HumanBodyBones.RightFoot),
        };
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Step 6: PuppetMaster + LayerSetup + Behaviours
    // ─────────────────────────────────────────────────────────────────────────
    void SetupPuppetMaster(GameObject pmGo, GameObject ccGo, GameObject behGo)
    {
        var pm = pmGo.AddComponent<PuppetMaster>();
        pm.targetRoot   = _model.transform;
        pm.muscleSpring = 100f;

        var joints  = pmGo.GetComponentsInChildren<ConfigurableJoint>(true);
        var muscles = new PMMuscle[joints.Length];

        for (int i = 0; i < joints.Length; i++)
        {
            ConfigurableJoint joint    = joints[i];
            Transform         animBone = FindByName(_model.transform, joint.gameObject.name);

            if (animBone == null)
                Log($"WARNING: No animation bone found for physics bone '{joint.gameObject.name}'");

            muscles[i] = new PMMuscle
            {
                joint  = joint,
                target = animBone,
                props  = new PMMuscle.Props(),
            };
        }

        pm.muscles = muscles;
        EditorUtility.SetDirty(pm);

        var layerSetup = pmGo.AddComponent<LayerSetup>();
        layerSetup.characterController      = ccGo.transform;
        layerSetup.characterControllerLayer = CharLayer;
        layerSetup.ragdollLayer             = RagdollLayer;

        behGo.transform.Find("Puppet (with Fall)").gameObject.AddComponent<BehaviourPuppet>();
        behGo.transform.Find("Fall").gameObject.AddComponent<BehaviourFall>();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Steps 7-9: Gameplay scripts
    // ─────────────────────────────────────────────────────────────────────────
    void SetupCharacterControllerGo(GameObject ccGo)
    {
        var holdPoint = new GameObject("HoldPoint");
        holdPoint.transform.SetParent(ccGo.transform, false);
        holdPoint.transform.localPosition = new Vector3(0f, 1.4f, 0.5f);

        var cc = ccGo.AddComponent<CharacterController>();
        cc.height     = 2f;
        cc.radius     = 0.5f;
        cc.center     = new Vector3(0f, 1f, 0f);
        cc.slopeLimit = 45f;
        cc.stepOffset = 0.3f;

        var rb = ccGo.AddComponent<Rigidbody>();
        rb.isKinematic = true;

        var cap = ccGo.AddComponent<CapsuleCollider>();
        cap.height = 2f;
        cap.radius = 0.5f;
        cap.center = new Vector3(0f, 1f, 0f);

        ccGo.AddComponent<CharacterPuppet>();
        ccGo.AddComponent<UserControlThirdPerson>();
        ccGo.AddComponent<GrappleController>();
        ccGo.AddComponent<ObjectGrabController>();
        ccGo.AddComponent<PlayerHitReaction>();
    }

    void SetupAnimationControllerGo(GameObject acGo, Avatar avatar, string modelName)
    {
        if (!AssetDatabase.IsValidFolder(OutputControllerDir))
            AssetDatabase.CreateFolder("Assets/Mahmoud SandBox", "Characters");

        string destPath = $"{OutputControllerDir}/{modelName}_AnimatorController.controller";
        AssetDatabase.CopyAsset(SourceControllerPath, destPath);
        AssetDatabase.SaveAssets();
        var controller = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(destPath);

        var anim = acGo.AddComponent<Animator>();
        anim.runtimeAnimatorController = controller;
        anim.avatar          = avatar;
        anim.applyRootMotion = true;
        anim.updateMode      = AnimatorUpdateMode.Fixed;

        acGo.AddComponent<CharacterAnimationThirdPerson>();
    }

    void SetupCameraGo(GameObject camGo)
    {
        camGo.AddComponent<Camera>();
        camGo.AddComponent<AudioListener>();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Steps 10-11: Wire cross-references
    // ─────────────────────────────────────────────────────────────────────────
    void WireCrossReferences(GameObject root, GameObject pmGo, GameObject ccGo,
        GameObject acGo, GameObject camGo)
    {
        var cameraComp  = camGo.GetComponent<Camera>();
        var animComp    = acGo.GetComponent<Animator>();
        var charAnim    = acGo.GetComponent<CharacterAnimationThirdPerson>();
        var charPuppet  = ccGo.GetComponent<CharacterPuppet>();
        var userControl = ccGo.GetComponent<UserControlThirdPerson>();
        var holdPoint   = ccGo.transform.Find("HoldPoint");
        var grapple     = ccGo.GetComponent<GrappleController>();
        var grabCtrl    = ccGo.GetComponent<ObjectGrabController>();

        // CameraController on root (all fields are [SerializeField] private)
        var camCtrl = root.AddComponent<CameraController>();
        var camSo = new SerializedObject(camCtrl);
        camSo.FindProperty("_camera").objectReferenceValue       = cameraComp;
        camSo.FindProperty("_followTarget").objectReferenceValue = ccGo.transform;
        camSo.FindProperty("_sensitivity").floatValue            = 3f;
        camSo.FindProperty("_pitchMin").floatValue               = -30f;
        camSo.FindProperty("_pitchMax").floatValue               = 60f;
        camSo.FindProperty("_normalOffset").vector3Value         = new Vector3(0.81f, 1.38f, 0.21f);
        camSo.FindProperty("_normalDistance").floatValue         = 4.3f;
        camSo.FindProperty("_normalFOV").floatValue              = 37.8f;
        camSo.FindProperty("_aimOffset").vector3Value            = new Vector3(5f, 2f, 0f);
        camSo.FindProperty("_aimDistance").floatValue            = 1.67f;
        camSo.FindProperty("_aimFOV").floatValue                 = 50f;
        camSo.ApplyModifiedProperties();

        // CharacterPuppet (public fields on CharacterThirdPerson base)
        charPuppet.characterAnimation = charAnim;
        charPuppet.userControl        = userControl;
        EditorUtility.SetDirty(charPuppet);

        // CharacterAnimationThirdPerson (public field)
        charAnim.characterController = charPuppet;
        EditorUtility.SetDirty(charAnim);

        // GrappleController ([SerializeField] private fields)
        var grappleSo = new SerializedObject(grapple);
        grappleSo.FindProperty("_animator").objectReferenceValue   = animComp;
        grappleSo.FindProperty("_ropeOrigin").objectReferenceValue = holdPoint;
        grappleSo.ApplyModifiedProperties();

        // ObjectGrabController ([SerializeField] private fields)
        var grabSo = new SerializedObject(grabCtrl);
        grabSo.FindProperty("_animator").objectReferenceValue  = animComp;
        grabSo.FindProperty("_holdPoint").objectReferenceValue = holdPoint;
        grabSo.ApplyModifiedProperties();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Finalize: layer collision + mark scene dirty
    // ─────────────────────────────────────────────────────────────────────────
    void FinalizeLayersAndSave(GameObject root)
    {
        EnsureLayer(CharLayer,    "Character");
        EnsureLayer(RagdollLayer, "Ragdoll");
        Physics.IgnoreLayerCollision(CharLayer, RagdollLayer, true);
        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
    }

    void LogPostSetupNotes()
    {
        Log("Post-setup steps:");
        Log("1. Adjust collider sizes if model proportions differ from defaults.");
        Log("2. Assign missing animation clips in the duplicated Animator Controller.");
        Log("3. Verify knockdownThreshold in BehaviourPuppet (default: 200).");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Shared helpers
    // ─────────────────────────────────────────────────────────────────────────
    static Transform FindByName(Transform root, string name)
    {
        if (string.IsNullOrEmpty(name)) return null;
        foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
            if (t.name == name) return t;
        return null;
    }

    static void EnsureLayer(int index, string layerName)
    {
        var tagManager = new SerializedObject(
            AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);
        SerializedProperty layers = tagManager.FindProperty("layers");
        SerializedProperty entry  = layers.GetArrayElementAtIndex(index);
        if (string.IsNullOrEmpty(entry.stringValue))
        {
            entry.stringValue = layerName;
            tagManager.ApplyModifiedProperties();
        }
    }
}
