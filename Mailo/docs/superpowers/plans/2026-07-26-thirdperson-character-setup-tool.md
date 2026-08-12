# ThirdPerson Character Setup Tool — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Create a Unity Editor tool (`Tools → Mahmoud SandBox → Setup ThirdPerson Character`) that takes any Humanoid-rigged model in the scene and automatically builds the full ThirdPersonPuppet (1) character hierarchy — ragdoll, PuppetMaster, gameplay scripts, camera, Animator Controller — wired and ready to play.

**Architecture:** A single `EditorWindow` class with one `Run()` method that calls focused private methods per step. Physics skeleton is cloned from the model's bone hierarchy using HumanBodyBones, ragdoll is built with `BipedRagdollCreator.Create()`, and PuppetMaster muscles are wired manually by matching bone names. No call to `pm.SetUpTo()` — we build our own hierarchy.

**Tech Stack:** Unity Editor (`EditorWindow`, `SerializedObject`), RootMotion.Dynamics (`PuppetMaster`, `BipedRagdollCreator`, `BipedRagdollReferences`, `Muscle`, `BehaviourPuppet`, `BehaviourFall`), RootMotion.Demos (`CharacterPuppet`, `CharacterAnimationThirdPerson`, `UserControlThirdPerson`, `LayerSetup`), project scripts (`GrappleController`, `ObjectGrabController`, `PlayerHitReaction`, `CameraController`).

## Global Constraints

- File: `Assets/Mahmoud SandBox/PhysicsCharacter/Editor/ThirdPersonCharacterSetupTool.cs`
- Namespace: none (plain EditorWindow)
- Menu item: `Tools/Mahmoud SandBox/Setup ThirdPerson Character`
- Source Animator Controller: `Assets/Plugins/RootMotion/PuppetMaster/_DEMOS/Assets/Humanoid Controllers/Humanoid Third Person Puppet.controller`
- Output Animator Controller: `Assets/Mahmoud SandBox/Characters/[ModelName]_AnimatorController.controller`
- Layer 8 = Character Controller, Layer 9 = Ragdoll
- All `[SerializeField]` private fields set via `SerializedObject` / `SerializedProperty`
- `BipedRagdollReferences.IsValid()` uses `ref string`, not `out string`
- `muscleSpring = 100f` is set on the PuppetMaster component, not on individual `Muscle.Props`
- The tool does NOT call `pm.SetUpTo()` — muscle array is built manually

---

### Task 1: EditorWindow Shell + Validate + Build Hierarchy + Move Model

**Files:**
- Create: `Assets/Mahmoud SandBox/PhysicsCharacter/Editor/ThirdPersonCharacterSetupTool.cs`

**Interfaces:**
- Produces: `ThirdPersonCharacterSetupTool` EditorWindow accessible via `Tools → Mahmoud SandBox → Setup ThirdPerson Character`
- Produces: hierarchy with named GameObjects and correct tags/layers, model moved under Animation Controller

- [ ] **Step 1: Create the Editor folder if it does not exist**

In Unity's Project window, navigate to `Assets/Mahmoud SandBox/PhysicsCharacter`. Right-click → Create → Folder → name it `Editor`.

- [ ] **Step 2: Create ThirdPersonCharacterSetupTool.cs with the skeleton, validation, and hierarchy**

Create `Assets/Mahmoud SandBox/PhysicsCharacter/Editor/ThirdPersonCharacterSetupTool.cs`:

```csharp
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using RootMotion.Dynamics;
using RootMotion.Demos;
using MailoGame;

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

        // Steps 2-3
        GameObject root  = BuildHierarchy(modelName);
        GameObject pmGo  = root.transform.Find("PuppetMaster").gameObject;
        GameObject behGo = root.transform.Find("Behaviours").gameObject;
        GameObject camGo = root.transform.Find("Character Camera").gameObject;
        GameObject ccGo  = root.transform.Find("Character Controller").gameObject;
        GameObject acGo  = ccGo.transform.Find("Animation Controller").gameObject;
        Log("Hierarchy built.");

        MoveModel(acGo);
        Log("Model moved under Animation Controller.");

        // Steps 4-5 (Task 2 fills these in)
        SetupPhysicsSkeleton(anim, pmGo);
        Log("Physics skeleton + ragdoll created.");

        // Step 6 (Task 3)
        SetupPuppetMaster(pmGo, ccGo, behGo);
        Log("PuppetMaster configured.");

        // Steps 7-8 (Task 4)
        SetupCharacterControllerGo(ccGo);
        Log("Character Controller scripts added.");
        SetupAnimationControllerGo(acGo, anim.avatar, modelName);
        Log("Animation Controller scripts added.");
        SetupCameraGo(camGo);
        Log("Camera configured.");

        // Steps 10-11 (Task 5)
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

        // PuppetMaster child
        var pmGo = new GameObject("PuppetMaster");
        pmGo.transform.SetParent(root.transform, false);
        pmGo.tag   = "Player";
        pmGo.layer = RagdollLayer;

        // Behaviours child
        var behGo = new GameObject("Behaviours");
        behGo.transform.SetParent(root.transform, false);

        new GameObject("Puppet (with Fall)").transform.SetParent(behGo.transform, false);
        new GameObject("Fall").transform.SetParent(behGo.transform, false);

        // Character Camera child (disabled)
        var camGo = new GameObject("Character Camera");
        camGo.transform.SetParent(root.transform, false);
        camGo.tag   = "MainCamera";
        camGo.layer = RagdollLayer;
        camGo.SetActive(false);

        // Character Controller child
        var ccGo = new GameObject("Character Controller");
        ccGo.transform.SetParent(root.transform, false);
        ccGo.tag   = "Player";
        ccGo.layer = CharLayer;

        // Animation Controller grandchild
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
    // Steps 4-5: Physics skeleton + ragdoll  [filled in Task 2]
    // ─────────────────────────────────────────────────────────────────────────
    void SetupPhysicsSkeleton(Animator anim, GameObject pmGo)
    {
        // Placeholder — Task 2 replaces this body
        Log("(physics skeleton step — implement in Task 2)");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Step 6: PuppetMaster  [filled in Task 3]
    // ─────────────────────────────────────────────────────────────────────────
    void SetupPuppetMaster(GameObject pmGo, GameObject ccGo, GameObject behGo)
    {
        Log("(PuppetMaster step — implement in Task 3)");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Steps 7-9: Scripts  [filled in Task 4]
    // ─────────────────────────────────────────────────────────────────────────
    void SetupCharacterControllerGo(GameObject ccGo) { }
    void SetupAnimationControllerGo(GameObject acGo, Avatar avatar, string modelName) { }
    void SetupCameraGo(GameObject camGo) { }

    // ─────────────────────────────────────────────────────────────────────────
    // Steps 10-11: Wire + layers  [filled in Task 5]
    // ─────────────────────────────────────────────────────────────────────────
    void WireCrossReferences(GameObject root, GameObject pmGo, GameObject ccGo,
        GameObject acGo, GameObject camGo) { }

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
        Log("1. Adjust collider sizes if model proportions differ significantly.");
        Log("2. Assign missing animation clips in the duplicated Animator Controller.");
        Log("3. Verify knockdownThreshold in PuppetRagdollController (default: 200).");
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
```

- [ ] **Step 3: Verify it compiles**

Switch to Unity. The Console should show zero errors from this file. If there are missing namespace errors, ensure all referenced scripts are present in the project.

- [ ] **Step 4: Test hierarchy creation**

1. In the scene, place any Humanoid-rigged model (FBX imported as Humanoid).
2. Open `Tools → Mahmoud SandBox → Setup ThirdPerson Character`.
3. Drag the model into the Model field, click **Setup Character**.
4. In the Hierarchy window verify:
   - `[ModelName]_Character` exists at root, Tag=Player
   - Children: `PuppetMaster` (Layer=9), `Behaviours`, `Character Camera` (disabled), `Character Controller` (Layer=8)
   - `Behaviours` has children `Puppet (with Fall)` and `Fall`
   - `Character Controller` has child `Animation Controller`
   - `Animation Controller` has the model as child
5. Log shows `"✓ Setup complete!"`.

- [ ] **Step 5: Commit**

```bash
git add "Assets/Mahmoud SandBox/PhysicsCharacter/Editor/ThirdPersonCharacterSetupTool.cs"
git commit -m "feat: ThirdPerson setup tool — EditorWindow, validate, hierarchy, move model"
```

---

### Task 2: Physics Skeleton + BipedRagdollCreator

**Files:**
- Modify: `Assets/Mahmoud SandBox/PhysicsCharacter/Editor/ThirdPersonCharacterSetupTool.cs`

**Interfaces:**
- Consumes: `BipedRagdollReferences.FromAvatar` pattern adapted for a cloned skeleton; `BipedRagdollCreator.AutodetectOptions`, `BipedRagdollCreator.Create`
- Produces: Physics skeleton bones under PuppetMaster child, each bone has `Rigidbody` + `CapsuleCollider` + `ConfigurableJoint`

- [ ] **Step 1: Replace the SetupPhysicsSkeleton placeholder**

Replace the entire `SetupPhysicsSkeleton` method body and add `CloneBoneRecursive` and `BuildBipedRefs` methods:

```csharp
void SetupPhysicsSkeleton(Animator anim, GameObject pmGo)
{
    // Clone the animation skeleton's bone hierarchy (transforms only) under pmGo
    Transform hips = anim.GetBoneTransform(HumanBodyBones.Hips);
    CloneBoneRecursive(hips, pmGo.transform);

    // Build biped ragdoll references pointing to the cloned bones
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
    // Helper: get bone name from the original animator, find clone by that name
    Transform Clone(HumanBodyBones b)
    {
        Transform orig = anim.GetBoneTransform(b);
        if (orig == null) return null;
        return FindByName(pmParent, orig.name);
    }

    return new BipedRagdollReferences
    {
        root         = pmParent,
        hips         = Clone(HumanBodyBones.Hips),
        spine        = Clone(HumanBodyBones.Spine),
        chest        = Clone(HumanBodyBones.Chest),
        head         = Clone(HumanBodyBones.Head),
        leftUpperArm = Clone(HumanBodyBones.LeftUpperArm),
        leftLowerArm = Clone(HumanBodyBones.LeftLowerArm),
        leftHand     = Clone(HumanBodyBones.LeftHand),
        rightUpperArm= Clone(HumanBodyBones.RightUpperArm),
        rightLowerArm= Clone(HumanBodyBones.RightLowerArm),
        rightHand    = Clone(HumanBodyBones.RightHand),
        leftUpperLeg = Clone(HumanBodyBones.LeftUpperLeg),
        leftLowerLeg = Clone(HumanBodyBones.LeftLowerLeg),
        leftFoot     = Clone(HumanBodyBones.LeftFoot),
        rightUpperLeg= Clone(HumanBodyBones.RightUpperLeg),
        rightLowerLeg= Clone(HumanBodyBones.RightLowerLeg),
        rightFoot    = Clone(HumanBodyBones.RightFoot),
    };
}
```

- [ ] **Step 2: Verify it compiles**

Unity Console shows zero errors.

- [ ] **Step 3: Test physics skeleton**

Run the tool on a Humanoid model. In the Hierarchy, select the `PuppetMaster` child. Expand it — you should see bone clones (e.g., `mixamorig:Hips`, `mixamorig:Spine1`, etc.). Click on a physics bone like `mixamorig:Hips` — the Inspector should show `Rigidbody`, `CapsuleCollider`, `ConfigurableJoint` components on it.

- [ ] **Step 4: Commit**

```bash
git add "Assets/Mahmoud SandBox/PhysicsCharacter/Editor/ThirdPersonCharacterSetupTool.cs"
git commit -m "feat: physics skeleton clone + BipedRagdollCreator ragdoll setup"
```

---

### Task 3: PuppetMaster + Behaviours

**Files:**
- Modify: `Assets/Mahmoud SandBox/PhysicsCharacter/Editor/ThirdPersonCharacterSetupTool.cs`

**Interfaces:**
- Consumes: `ConfigurableJoint[]` from physics skeleton; animation model root (`_model.transform`) for muscle targets
- Produces: `PuppetMaster` component on `pmGo` with `muscles[]` populated, `LayerSetup` wired, `BehaviourPuppet` and `BehaviourFall` added

- [ ] **Step 1: Replace the SetupPuppetMaster placeholder**

Replace the entire `SetupPuppetMaster` method body:

```csharp
void SetupPuppetMaster(GameObject pmGo, GameObject ccGo, GameObject behGo)
{
    var pm = pmGo.AddComponent<PuppetMaster>();
    pm.targetRoot   = _model.transform;
    pm.muscleSpring = 100f;

    // Build muscles: match each ConfigurableJoint (physics bone) to its
    // animation-skeleton counterpart by bone name.
    var joints  = pmGo.GetComponentsInChildren<ConfigurableJoint>(true);
    var muscles = new Muscle[joints.Length];

    for (int i = 0; i < joints.Length; i++)
    {
        ConfigurableJoint joint    = joints[i];
        Transform         animBone = FindByName(_model.transform, joint.gameObject.name);

        if (animBone == null)
            Log($"WARNING: No animation bone found for physics bone '{joint.gameObject.name}'");

        muscles[i] = new Muscle
        {
            joint  = joint,
            target = animBone,
            props  = new Muscle.Props(), // defaults: mappingWeight=1, pinWeight=1, muscleWeight=1, muscleDamper=1
        };
    }

    pm.muscles = muscles;
    EditorUtility.SetDirty(pm);

    // LayerSetup — handles layer assignment + IgnoreLayerCollision at runtime
    var layerSetup = pmGo.AddComponent<LayerSetup>();
    layerSetup.characterController      = ccGo.transform;
    layerSetup.characterControllerLayer = CharLayer;
    layerSetup.ragdollLayer             = RagdollLayer;

    // BehaviourPuppet + BehaviourFall
    behGo.transform.Find("Puppet (with Fall)").gameObject.AddComponent<BehaviourPuppet>();
    behGo.transform.Find("Fall").gameObject.AddComponent<BehaviourFall>();
}
```

- [ ] **Step 2: Verify it compiles**

Unity Console shows zero errors.

- [ ] **Step 3: Test PuppetMaster**

Run the tool. Select `PuppetMaster` child in Hierarchy → Inspector should show:
- `PuppetMaster` component with `Target Root` set to the model, and a non-empty `Muscles` list.
- `Layer Setup` component with `Character Controller` = Character Controller GO, layers 8 and 9.
- `Puppet (with Fall)` child has `BehaviourPuppet` component.
- `Fall` child has `BehaviourFall` component.

- [ ] **Step 4: Commit**

```bash
git add "Assets/Mahmoud SandBox/PhysicsCharacter/Editor/ThirdPersonCharacterSetupTool.cs"
git commit -m "feat: PuppetMaster muscles, LayerSetup, BehaviourPuppet, BehaviourFall"
```

---

### Task 4: Character Controller + Animation Controller + Camera Scripts

**Files:**
- Modify: `Assets/Mahmoud SandBox/PhysicsCharacter/Editor/ThirdPersonCharacterSetupTool.cs`

**Interfaces:**
- Consumes: `CharacterPuppet`, `UserControlThirdPerson` (RootMotion.Demos); `GrappleController`, `ObjectGrabController`, `PlayerHitReaction` (project); `CharacterAnimationThirdPerson` (RootMotion.Demos)
- Produces: all gameplay components on `Character Controller` and `Animation Controller` GameObjects; HoldPoint child; duplicated AnimatorController asset

- [ ] **Step 1: Replace the three script-setup placeholder methods**

Replace `SetupCharacterControllerGo`, `SetupAnimationControllerGo`, and `SetupCameraGo` with full implementations:

```csharp
void SetupCharacterControllerGo(GameObject ccGo)
{
    // HoldPoint child — used by GrappleController and ObjectGrabController
    var holdPoint = new GameObject("HoldPoint");
    holdPoint.transform.SetParent(ccGo.transform, false);
    holdPoint.transform.localPosition = new Vector3(0f, 1.4f, 0.5f);

    var cc = ccGo.AddComponent<CharacterController>();
    cc.height      = 2f;
    cc.radius      = 0.5f;
    cc.center      = new Vector3(0f, 1f, 0f);
    cc.slopeLimit  = 45f;
    cc.stepOffset  = 0.3f;

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
    // Ensure output folder exists
    if (!AssetDatabase.IsValidFolder(OutputControllerDir))
        AssetDatabase.CreateFolder("Assets/Mahmoud SandBox", "Characters");

    // Duplicate the source Animator Controller
    string destPath = $"{OutputControllerDir}/{modelName}_AnimatorController.controller";
    AssetDatabase.CopyAsset(SourceControllerPath, destPath);
    AssetDatabase.SaveAssets();
    var controller = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(destPath);

    var anim = acGo.AddComponent<Animator>();
    anim.runtimeAnimatorController = controller;
    anim.avatar          = avatar;
    anim.applyRootMotion = true;
    anim.updateMode      = AnimatorUpdateMode.AnimatePhysics;

    acGo.AddComponent<CharacterAnimationThirdPerson>();
}

void SetupCameraGo(GameObject camGo)
{
    camGo.AddComponent<Camera>();
    camGo.AddComponent<AudioListener>();
    // camGo already disabled in BuildHierarchy
}
```

- [ ] **Step 2: Verify it compiles**

Unity Console shows zero errors.

- [ ] **Step 3: Test component placement**

Run the tool and check:
- `Character Controller` Inspector shows: `CharacterController`, `Rigidbody` (isKinematic=true), `CapsuleCollider`, `CharacterPuppet`, `UserControlThirdPerson`, `GrappleController`, `ObjectGrabController`, `PlayerHitReaction`.
- `Character Controller` has child `HoldPoint` at local (0, 1.4, 0.5).
- `Animation Controller` Inspector shows: `Animator` (applyRootMotion=true, AnimatePhysics mode), `CharacterAnimationThirdPerson`.
- `Assets/Mahmoud SandBox/Characters/[ModelName]_AnimatorController.controller` exists in Project.
- `Character Camera` Inspector shows: `Camera`, `AudioListener`. GameObject is disabled.

- [ ] **Step 4: Commit**

```bash
git add "Assets/Mahmoud SandBox/PhysicsCharacter/Editor/ThirdPersonCharacterSetupTool.cs"
git commit -m "feat: character controller + animation controller + camera scripts added"
```

---

### Task 5: Wire Cross-References + Layer Setup + Post-Setup Log

**Files:**
- Modify: `Assets/Mahmoud SandBox/PhysicsCharacter/Editor/ThirdPersonCharacterSetupTool.cs`

**Interfaces:**
- Consumes: all components added in Tasks 1-4
- Produces: all cross-references wired (private fields via `SerializedObject`), `CameraController` on root with all settings, `Physics.IgnoreLayerCollision(8, 9)` applied

- [ ] **Step 1: Replace WireCrossReferences with full implementation**

Replace the empty `WireCrossReferences` method:

```csharp
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

    // ── CameraController on root ────────────────────────────────────────────
    var camCtrl = root.AddComponent<CameraController>();
    var camSo = new SerializedObject(camCtrl);
    camSo.FindProperty("_camera").objectReferenceValue          = cameraComp;
    camSo.FindProperty("_followTarget").objectReferenceValue    = ccGo.transform;
    camSo.FindProperty("_sensitivity").floatValue               = 3f;
    camSo.FindProperty("_pitchMin").floatValue                  = -30f;
    camSo.FindProperty("_pitchMax").floatValue                  = 60f;
    camSo.FindProperty("_normalOffset").vector3Value            = new Vector3(0.81f, 1.38f, 0.21f);
    camSo.FindProperty("_normalDistance").floatValue            = 4.3f;
    camSo.FindProperty("_normalFOV").floatValue                 = 37.8f;
    camSo.FindProperty("_aimOffset").vector3Value               = new Vector3(5f, 2f, 0f);
    camSo.FindProperty("_aimDistance").floatValue               = 1.67f;
    camSo.FindProperty("_aimFOV").floatValue                    = 50f;
    camSo.ApplyModifiedProperties();

    // ── CharacterPuppet (public fields on CharacterThirdPerson base) ────────
    charPuppet.characterAnimation = charAnim;
    charPuppet.userControl        = userControl;
    EditorUtility.SetDirty(charPuppet);

    // ── CharacterAnimationThirdPerson (public field) ────────────────────────
    charAnim.characterController = charPuppet;
    EditorUtility.SetDirty(charAnim);

    // ── GrappleController ───────────────────────────────────────────────────
    var grappleSo = new SerializedObject(grapple);
    grappleSo.FindProperty("_animator").objectReferenceValue    = animComp;
    grappleSo.FindProperty("_ropeOrigin").objectReferenceValue  = holdPoint;
    grappleSo.ApplyModifiedProperties();

    // ── ObjectGrabController ────────────────────────────────────────────────
    var grabSo = new SerializedObject(grabCtrl);
    grabSo.FindProperty("_animator").objectReferenceValue       = animComp;
    grabSo.FindProperty("_holdPoint").objectReferenceValue      = holdPoint;
    grabSo.ApplyModifiedProperties();
}
```

- [ ] **Step 2: Verify it compiles**

Unity Console shows zero errors.

- [ ] **Step 3: Test full end-to-end**

Run the tool on a Humanoid model. After setup:

1. **CameraController on root** → Inspector shows Camera = Character Camera's Camera component, FollowTarget = Character Controller transform, Sensitivity = 3.
2. **CharacterPuppet** → Inspector shows `Character Animation` = the `CharacterAnimationThirdPerson` on Animation Controller, `User Control` = the `UserControlThirdPerson` on Character Controller.
3. **CharacterAnimationThirdPerson** → Inspector shows `Character Controller` = the `CharacterPuppet` on Character Controller.
4. **GrappleController** → Inspector shows `Animator` = Animator on Animation Controller, `Rope Origin` = HoldPoint transform.
5. **ObjectGrabController** → Inspector shows `Animator` = Animator on Animation Controller, `Hold Point` = HoldPoint transform.
6. **Physics Settings** (Edit → Project Settings → Physics → Layer Collision Matrix): verify Layer 8 ↔ Layer 9 is unchecked (collision ignored).

Then press **Play**:
- Character stands upright (PuppetMaster balances it).
- WASD moves the character.
- Camera follows the Character Controller.
- No console errors on startup.

- [ ] **Step 4: Commit**

```bash
git add "Assets/Mahmoud SandBox/PhysicsCharacter/Editor/ThirdPersonCharacterSetupTool.cs"
git commit -m "feat: wire all cross-references, layer collision setup — tool complete"
```

---

## Post-Setup Manual Steps

After the tool completes, the log reminds the user:
1. Adjust collider sizes if model proportions differ significantly from defaults (height=2, radius=0.5).
2. Assign missing animation clips in the duplicated Animator Controller (`[ModelName]_AnimatorController.controller`).
3. Verify `knockdownThreshold` in `PuppetRagdollController` if added manually (default: 200).
