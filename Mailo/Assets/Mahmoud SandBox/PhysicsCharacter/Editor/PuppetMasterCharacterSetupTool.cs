// ─────────────────────────────────────────────────────────────────────────────
// PuppetMaster Character Setup Tool
// Run via:  Tools → PuppetMaster Character → Setup Testing_Player
//
// Flow (two-object approach — avoids the "setUpTo==transform" special-case path):
//  1. Finds Testing_Player (becomes the ANIMATED TARGET — stays clean, no ragdoll).
//  2. Unpacks the prefab instance so bones can be reparented.
//  3. Instantiates a DUPLICATE → this becomes the PHYSICS PUPPET.
//  4. Calls BipedRagdollCreator.Create() on the DUPLICATE's bones.
//  5. Adds PuppetMaster to the DUPLICATE and calls SetUpTo(original.transform, ...).
//     SetUpMuscles maps duplicate joint names → original bone names (same names ✓).
//  6. Adds CharacterController + PuppetMoverSimple to the original (animated target).
//
// No BehaviourPuppet/BehaviourFall — PuppetMaster runs in Active mode (pinWeight=1)
// exactly like the Dummy Root demo. Character stands immediately.
// SetUpTo() is NOT undoable — back up the scene first.
// ─────────────────────────────────────────────────────────────────────────────
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using RootMotion.Dynamics;

public static class PuppetMasterCharacterSetupTool
{
    const string CharName     = "Testing_Player";
    const int    CharLayer    = 8;
    const int    RagdollLayer = 9;

    static readonly string[] OldBones =
    {
        "mixamorig9:Hips",
        "mixamorig9:Spine1",   "mixamorig9:Spine2",
        "mixamorig9:Head",
        "mixamorig9:LeftArm",  "mixamorig9:RightArm",
        "mixamorig9:LeftForeArm", "mixamorig9:RightForeArm",
        "mixamorig9:LeftUpLeg",   "mixamorig9:RightUpLeg",
        "mixamorig9:LeftLeg",     "mixamorig9:RightLeg",
    };

    // ─────────────────────────────────────────────────────────────────────────
    [MenuItem("Tools/PuppetMaster Character/Setup Testing_Player")]
    static void Setup()
    {
        Debug.Log("[PuppetMasterSetup] Starting...");

        // ── 1. Find original character (will stay as the animated target) ─────
        GameObject original = GameObject.Find(CharName);
        if (original == null)
        {
            Debug.LogError($"[PuppetMasterSetup] '{CharName}' not found in scene.");
            return;
        }

        if (original.GetComponent<PuppetMaster>() != null ||
            (original.transform.parent != null &&
             original.transform.parent.name == CharName + " Root"))
        {
            Debug.LogWarning("[PuppetMasterSetup] Already set up. " +
                             "Delete 'Testing_Player Root' and re-add a fresh character.");
            return;
        }

        // ── 2. Strip any leftover custom physics from a previous attempt ──────
        StripOldSetup(original);

        // ── 3. Ensure Animator exists and has a controller ────────────────────
        Animator anim = original.GetComponent<Animator>();
        if (anim == null) anim = original.AddComponent<Animator>();
        anim.applyRootMotion = false;

        if (anim.runtimeAnimatorController == null)
        {
            string[] guids = AssetDatabase.FindAssets("CharacterAnimaiton t:AnimatorController");
            if (guids.Length > 0)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[0]);
                anim.runtimeAnimatorController =
                    AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(path);
                Debug.Log($"[PuppetMasterSetup] Assigned controller: {path}");
            }
            else
            {
                Debug.LogWarning("[PuppetMasterSetup] CharacterAnimaiton controller not found — " +
                                 "assign one manually to the animated target's Animator.");
            }
        }

        // ── 4. Unpack prefab so PuppetMaster can reparent / destroy bones ─────
        if (PrefabUtility.IsPartOfPrefabInstance(original))
        {
            PrefabUtility.UnpackPrefabInstance(original, PrefabUnpackMode.Completely,
                                               InteractionMode.AutomatedAction);
            Debug.Log("[PuppetMasterSetup] Prefab instance unpacked.");
        }

        // ── 5. Auto-create physics layers ─────────────────────────────────────
        EnsureLayer(CharLayer,    "Character");
        EnsureLayer(RagdollLayer, "Ragdoll");

        // ── 6. Duplicate original → this copy becomes the PHYSICS PUPPET ──────
        // The original stays clean (no ragdoll) and becomes the animated target.
        GameObject ragdollGo = Object.Instantiate(
            original, original.transform.position, original.transform.rotation);
        ragdollGo.name = original.name; // identical name → bone name matching works

        Debug.Log($"[PuppetMasterSetup] Physics puppet duplicate created: '{ragdollGo.name}'");

        // ── 7. Build ragdoll references from the DUPLICATE's bones ────────────
        Animator ragdollAnim = ragdollGo.GetComponent<Animator>();
        BipedRagdollReferences refs;
        if (ragdollAnim != null && ragdollAnim.isHuman)
        {
            refs = BipedRagdollReferences.FromAvatar(ragdollAnim);
            Debug.Log("[PuppetMasterSetup] Bone refs from Humanoid avatar.");
        }
        else
        {
            refs = BuildRefsFromNames(ragdollGo.transform);
            Debug.LogWarning("[PuppetMasterSetup] No Humanoid avatar — using name-based refs. " +
                             "Import FBX as Humanoid for best results.");
        }

        string validMsg = string.Empty;
        if (!refs.IsValid(ref validMsg))
        {
            Object.DestroyImmediate(ragdollGo);
            Debug.LogError($"[PuppetMasterSetup] Invalid bone refs: {validMsg}\n" +
                           "Set the FBX Rig → Animation Type to Humanoid and re-run.");
            return;
        }

        // ── 8. Create ragdoll on the DUPLICATE ────────────────────────────────
        BipedRagdollCreator.Options opts = BipedRagdollCreator.AutodetectOptions(refs);
        BipedRagdollCreator.Create(refs, opts);
        Debug.Log("[PuppetMasterSetup] Ragdoll (ConfigurableJoints + Rigidbodies + Colliders) created.");

        // ── 9. Add PuppetMaster to the DUPLICATE; wire the original as target ─
        // Because ragdollGo != original, SetUpTo takes the direct path:
        //   - RemoveUnnecessaryBones on ragdollGo (strips non-physics bones)
        //   - SetUpMuscles maps ragdollGo joints → original bones by name ✓
        //   - Creates "[CharName] Root" parent; places both under it
        //   - pm.targetRoot = original
        PuppetMaster pm = ragdollGo.AddComponent<PuppetMaster>();
        pm.SetUpTo(original.transform, CharLayer, RagdollLayer);

        // Mark dirty so muscles array is captured by Unity's in-memory serialization.
        EditorUtility.SetDirty(pm);
        EditorUtility.SetDirty(pm.gameObject);

        Debug.Log($"[PuppetMasterSetup] PuppetMaster initiated. Muscles: {pm.muscles.Length}");

        // ── 10. Configure the animated target (the original Testing_Player) ───
        Transform animTarget = pm.targetRoot;
        if (animTarget != null)
        {
            // Remove any lingering Rigidbody — CharacterController can't coexist with one.
            Rigidbody leftoverRb = animTarget.GetComponent<Rigidbody>();
            if (leftoverRb != null) Object.DestroyImmediate(leftoverRb);

            CharacterController cc = animTarget.GetComponent<CharacterController>();
            if (cc == null) cc = animTarget.gameObject.AddComponent<CharacterController>();
            if (cc != null)
            {
                cc.height = 1.8f;
                cc.radius = 0.3f;
                cc.center = new Vector3(0f, 0.9f, 0f);
            }

            if (animTarget.GetComponent<PuppetMoverSimple>() == null)
                animTarget.gameObject.AddComponent<PuppetMoverSimple>();

            Debug.Log($"[PuppetMasterSetup] Animated target configured: '{animTarget.name}'");
        }
        else
        {
            Debug.LogWarning("[PuppetMasterSetup] pm.targetRoot is null after SetUpTo — " +
                             "add CharacterController + PuppetMoverSimple manually.");
        }

        // ── 11. Save + diagnostics ────────────────────────────────────────────
        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());

        var sb = new System.Text.StringBuilder();
        sb.AppendLine("[PuppetMasterSetup] ✓ COMPLETE");
        sb.AppendLine($"  Root:       '{pm.transform.parent?.name}'");
        sb.AppendLine($"  Puppet:     '{pm.name}'");
        sb.AppendLine($"  AnimTarget: '{pm.targetRoot?.name}'");
        sb.AppendLine($"  Muscles:    {pm.muscles.Length}");
        int nullCount = 0;
        foreach (var m in pm.muscles)
            if (m.target == null) { sb.AppendLine($"  !! NULL TARGET: {m.joint?.name}"); nullCount++; }
        sb.AppendLine(nullCount == 0 ? "  All muscle targets found ✓" : $"  {nullCount} NULL TARGETS");
        Debug.Log(sb.ToString());
    }

    // ── Remove ────────────────────────────────────────────────────────────────
    [MenuItem("Tools/PuppetMaster Character/Remove PuppetMaster Root (manual step)")]
    static void Remove()
    {
        GameObject root = GameObject.Find(CharName + " Root");
        if (root != null)
        {
            Debug.Log("[PuppetMasterSetup] Found 'Testing_Player Root'. " +
                      "Delete this object in the Hierarchy, " +
                      "then drag a fresh Testing_Player into the scene and run Setup again.");
            Selection.activeGameObject = root;
        }
        else
        {
            GameObject go = GameObject.Find(CharName);
            if (go != null)
            {
                StripOldSetup(go);
                TryDestroy<PuppetMaster>(go);
                EditorSceneManager.MarkSceneDirty(go.scene);
                Debug.Log("[PuppetMasterSetup] Stripped physics components from Testing_Player.");
            }
            else
            {
                Debug.LogWarning("[PuppetMasterSetup] Nothing found to remove.");
            }
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────────────────

    static void StripOldSetup(GameObject go)
    {
        TryDestroy<PhysicsCharacterController>(go);
        TryDestroy<CapsuleCollider>(go);
        TryDestroy<Rigidbody>(go);

        foreach (string boneName in OldBones)
        {
            Transform bone = FindInHierarchy(go.transform, boneName);
            if (bone == null) continue;
            TryDestroy<PhysicsImpactDetector>(bone.gameObject);
            TryDestroy<CharacterJoint>(bone.gameObject);
            TryDestroy<ConfigurableJoint>(bone.gameObject);
            TryDestroy<CapsuleCollider>(bone.gameObject);
            TryDestroy<Rigidbody>(bone.gameObject);
        }
    }

    static BipedRagdollReferences BuildRefsFromNames(Transform root)
    {
        var r = new BipedRagdollReferences();
        r.root          = root;
        r.hips          = FindInHierarchy(root, "mixamorig9:Hips");
        r.spine         = FindInHierarchy(root, "mixamorig9:Spine");
        r.chest         = FindInHierarchy(root, "mixamorig9:Spine2");
        r.head          = FindInHierarchy(root, "mixamorig9:Head");
        r.leftUpperArm  = FindInHierarchy(root, "mixamorig9:LeftArm");
        r.leftLowerArm  = FindInHierarchy(root, "mixamorig9:LeftForeArm");
        r.leftHand      = FindInHierarchy(root, "mixamorig9:LeftHand");
        r.rightUpperArm = FindInHierarchy(root, "mixamorig9:RightArm");
        r.rightLowerArm = FindInHierarchy(root, "mixamorig9:RightForeArm");
        r.rightHand     = FindInHierarchy(root, "mixamorig9:RightHand");
        r.leftUpperLeg  = FindInHierarchy(root, "mixamorig9:LeftUpLeg");
        r.leftLowerLeg  = FindInHierarchy(root, "mixamorig9:LeftLeg");
        r.leftFoot      = FindInHierarchy(root, "mixamorig9:LeftFoot");
        r.rightUpperLeg = FindInHierarchy(root, "mixamorig9:RightUpLeg");
        r.rightLowerLeg = FindInHierarchy(root, "mixamorig9:RightLeg");
        r.rightFoot     = FindInHierarchy(root, "mixamorig9:RightFoot");
        return r;
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
            Debug.Log($"[PuppetMasterSetup] Layer {index} created: '{layerName}'");
        }
    }

    static Transform FindInHierarchy(Transform t, string name)
    {
        if (t.name == name) return t;
        foreach (Transform child in t)
        {
            Transform found = FindInHierarchy(child, name);
            if (found != null) return found;
        }
        return null;
    }

    static void TryDestroy<T>(GameObject go) where T : Component
    {
        var c = go.GetComponent<T>();
        if (c != null) Object.DestroyImmediate(c);
    }
}
