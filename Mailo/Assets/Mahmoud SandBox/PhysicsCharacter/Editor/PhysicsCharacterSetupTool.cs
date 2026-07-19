// ─────────────────────────────────────────────────────────────────────────────
// Physics Character Setup Tool
// Run via:  Tools → Physics Character → Setup Testing_Player
//
// What it does (fully automatic):
//  1. Finds Testing_Player in the active scene by name.
//  2. Adds Rigidbody + CapsuleCollider to the root (movement body).
//  3. Adds Animator and assigns CharacterAnimaiton.controller if found.
//  4. Adds Rigidbody + CapsuleCollider + CharacterJoint + PhysicsImpactDetector
//     to 12 key Mixamo bones (mixamorig9: prefix).
//     Hips is the ragdoll root — Rigidbody only, no joint.
//  5. Adds PhysicsCharacterController to root and wires ragdollBodies array.
//  6. Ignores self-collision between adjacent ragdoll bones.
// ─────────────────────────────────────────────────────────────────────────────
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class PhysicsCharacterSetupTool
{
    const string CharacterName = "Testing_Player";
    const string HipsName      = "mixamorig9:Hips";

    // Key bones that get Rigidbody + Collider + Joint (Hips = root, no joint).
    static readonly string[] RagdollBones =
    {
        "mixamorig9:Hips",
        "mixamorig9:Spine1",
        "mixamorig9:Spine2",
        "mixamorig9:Head",
        "mixamorig9:LeftArm",        "mixamorig9:RightArm",
        "mixamorig9:LeftForeArm",    "mixamorig9:RightForeArm",
        "mixamorig9:LeftUpLeg",      "mixamorig9:RightUpLeg",
        "mixamorig9:LeftLeg",        "mixamorig9:RightLeg",
    };

    static readonly Dictionary<string, float> BoneMass = new()
    {
        { "mixamorig9:Hips",      15f },
        { "mixamorig9:Spine1",     6f },
        { "mixamorig9:Spine2",     6f },
        { "mixamorig9:Head",       5f },
        { "mixamorig9:LeftArm",    3f }, { "mixamorig9:RightArm",    3f },
        { "mixamorig9:LeftForeArm",2f }, { "mixamorig9:RightForeArm",2f },
        { "mixamorig9:LeftUpLeg",  8f }, { "mixamorig9:RightUpLeg",  8f },
        { "mixamorig9:LeftLeg",    5f }, { "mixamorig9:RightLeg",    5f },
    };

    // Capsule axis per bone (0=X  1=Y  2=Z).
    static readonly Dictionary<string, int> CapsuleAxis = new()
    {
        { "mixamorig9:Hips",      1 },
        { "mixamorig9:Spine1",    1 }, { "mixamorig9:Spine2",     1 },
        { "mixamorig9:Head",      1 },
        { "mixamorig9:LeftArm",   2 }, { "mixamorig9:RightArm",   2 },
        { "mixamorig9:LeftForeArm",2 },{ "mixamorig9:RightForeArm",2 },
        { "mixamorig9:LeftUpLeg", 1 }, { "mixamorig9:RightUpLeg", 1 },
        { "mixamorig9:LeftLeg",   1 }, { "mixamorig9:RightLeg",   1 },
    };

    static HashSet<string> _boneSet;

    // ─────────────────────────────────────────────────────────────────────────
    [MenuItem("Tools/Physics Character/Setup Testing_Player")]
    static void Setup()
    {
        _boneSet = new HashSet<string>(RagdollBones);

        // ── 1. Find character ─────────────────────────────────────────────────
        GameObject root = GameObject.Find(CharacterName);
        if (root == null)
        {
            Debug.LogError($"[PhysicsCharacterSetup] '{CharacterName}' not found in scene.");
            return;
        }

        if (root.GetComponent<PhysicsCharacterController>() != null)
        {
            Debug.LogWarning("[PhysicsCharacterSetup] Already set up. " +
                             "Run 'Remove Setup' first to re-run.");
            return;
        }

        // ── 2. Find Hips bone ─────────────────────────────────────────────────
        Transform hips = FindInHierarchy(root.transform, HipsName);
        if (hips == null)
        {
            Debug.LogError($"[PhysicsCharacterSetup] '{HipsName}' not found under {CharacterName}.");
            return;
        }

        Undo.SetCurrentGroupName("Setup Physics Character");
        int undoGroup = Undo.GetCurrentGroup();

        // ── 3. Root: CapsuleCollider ──────────────────────────────────────────
        CapsuleCollider cap = Undo.AddComponent<CapsuleCollider>(root);
        cap.height    = 1.8f;
        cap.radius    = 0.3f;
        cap.center    = new Vector3(0f, 0.9f, 0f);
        cap.direction = 1; // Y

        // ── 4. Root: Rigidbody ────────────────────────────────────────────────
        Rigidbody rootRb = Undo.AddComponent<Rigidbody>(root);
        rootRb.mass                  = 70f;
        rootRb.linearDamping         = 0f;
        rootRb.angularDamping        = 0.05f;
        rootRb.interpolation         = RigidbodyInterpolation.Interpolate;
        rootRb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        rootRb.constraints = RigidbodyConstraints.FreezeRotationX
                           | RigidbodyConstraints.FreezeRotationZ;

        // ── 5. Root: Animator ─────────────────────────────────────────────────
        Animator anim = root.GetComponent<Animator>();
        if (anim == null) anim = Undo.AddComponent<Animator>(root);
        anim.applyRootMotion = false;
        anim.updateMode      = AnimatorUpdateMode.Fixed;
        anim.cullingMode     = AnimatorCullingMode.AlwaysAnimate;

        // Auto-assign CharacterAnimaiton.controller if found in project.
        if (anim.runtimeAnimatorController == null)
        {
            string[] guids = AssetDatabase.FindAssets("CharacterAnimaiton t:AnimatorController");
            if (guids.Length > 0)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[0]);
                anim.runtimeAnimatorController =
                    AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(path);
                Debug.Log($"[PhysicsCharacterSetup] Assigned controller: {path}");
            }
            else
            {
                Debug.LogWarning("[PhysicsCharacterSetup] CharacterAnimaiton controller not found. " +
                                 "Assign it manually to the Animator on Testing_Player.");
            }
        }

        // ── 6. Bone map ───────────────────────────────────────────────────────
        var boneMap = new Dictionary<string, Transform>();
        CollectByName(hips, boneMap);

        var ragdollList = new List<Rigidbody>();

        foreach (string boneName in RagdollBones)
        {
            if (!boneMap.TryGetValue(boneName, out Transform bone))
            {
                Debug.LogWarning($"[PhysicsCharacterSetup] Bone not found: {boneName}");
                continue;
            }

            bool isHips = boneName == HipsName;

            // ── Rigidbody ─────────────────────────────────────────────────────
            Rigidbody rb = Undo.AddComponent<Rigidbody>(bone.gameObject);
            rb.mass                   = BoneMass.TryGetValue(boneName, out float m) ? m : 2f;
            rb.linearDamping          = 0.05f;
            rb.angularDamping         = 0.05f;
            rb.interpolation          = RigidbodyInterpolation.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            // Must serialize as kinematic so bones never fall before Awake() runs.
            rb.isKinematic            = true;

            // Hips must be index 0 in ragdollBodies (PhysicsCharacterController uses it
            // to reposition the root when getting up from ragdoll).
            if (isHips) ragdollList.Insert(0, rb);
            else        ragdollList.Add(rb);

            // ── CapsuleCollider ───────────────────────────────────────────────
            int axis = CapsuleAxis.TryGetValue(boneName, out int a) ? a : 1;
            BuildBoneCapsule(bone, boneName, axis);

            // ── PhysicsImpactDetector ─────────────────────────────────────────
            Undo.AddComponent<PhysicsImpactDetector>(bone.gameObject);

            if (isHips) continue; // Hips is the ragdoll root — no joint

            // ── CharacterJoint ────────────────────────────────────────────────
            Rigidbody parentRb = FindParentRigidbody(bone);
            CharacterJoint joint = Undo.AddComponent<CharacterJoint>(bone.gameObject);
            SetupCharacterJoint(joint, parentRb);
        }

        // ── 7. PhysicsCharacterController ─────────────────────────────────────
        PhysicsCharacterController ctrl =
            Undo.AddComponent<PhysicsCharacterController>(root);
        // RecordObject is required so Unity serializes the array assignment.
        Undo.RecordObject(ctrl, "Wire ragdollBodies");
        ctrl.ragdollBodies = ragdollList.ToArray();
        EditorUtility.SetDirty(ctrl);

        // ── 8. Self-collision ignore ───────────────────────────────────────────
        SetupSelfCollisionIgnore(boneMap);

        // ── 9. Save ────────────────────────────────────────────────────────────
        EditorUtility.SetDirty(root);
        EditorSceneManager.MarkSceneDirty(root.scene);
        Undo.CollapseUndoOperations(undoGroup);

        Debug.Log($"[PhysicsCharacterSetup] Done — {ragdollList.Count} ragdoll bodies.\n" +
                  "WASD to move. F3 = medium wind. F4 = strong wind.\n" +
                  "If the Animator shows no avatar, set the rig to Humanoid " +
                  "in the model's Import Settings and re-assign the Avatar.");
    }

    // ─────────────────────────────────────────────────────────────────────────
    [MenuItem("Tools/Physics Character/Remove Setup from Testing_Player")]
    static void Remove()
    {
        GameObject root = GameObject.Find(CharacterName);
        if (root == null) { Debug.LogError($"'{CharacterName}' not found."); return; }

        Undo.SetCurrentGroupName("Remove Physics Character Setup");
        int group = Undo.GetCurrentGroup();

        TryDestroy<PhysicsCharacterController>(root);
        TryDestroy<CapsuleCollider>(root);
        TryDestroy<Rigidbody>(root);

        Transform hips = FindInHierarchy(root.transform, HipsName);
        if (hips != null)
        {
            var boneMap = new Dictionary<string, Transform>();
            CollectByName(hips, boneMap);

            foreach (string boneName in RagdollBones)
            {
                if (!boneMap.TryGetValue(boneName, out Transform bone)) continue;
                TryDestroy<PhysicsImpactDetector>(bone.gameObject);
                TryDestroy<CharacterJoint>(bone.gameObject);
                TryDestroy<CapsuleCollider>(bone.gameObject);
                TryDestroy<Rigidbody>(bone.gameObject);
            }
        }

        EditorSceneManager.MarkSceneDirty(root.scene);
        Undo.CollapseUndoOperations(group);
        Debug.Log("[PhysicsCharacterSetup] All physics components removed from Testing_Player.");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────────────────

    static void BuildBoneCapsule(Transform bone, string boneName, int axis)
    {
        float   length = 0.2f, radius = 0.06f;
        Vector3 center = Vector3.zero;

        foreach (Transform child in bone)
        {
            if (_boneSet.Contains(child.name))
            {
                Vector3 localChild = bone.InverseTransformPoint(child.position);
                float   dist       = localChild.magnitude;
                if (dist > 0.01f)
                {
                    length = dist;
                    radius = Mathf.Clamp(dist * 0.22f, 0.03f, 0.18f);
                    center = localChild * 0.5f;
                }
                break;
            }
        }

        // Override for specific bones
        if (boneName == HipsName)
        {
            length = 0.30f; radius = 0.14f; center = new Vector3(0f, 0.05f, 0f);
        }
        else if (boneName == "mixamorig9:Head")
        {
            length = 0.24f; radius = 0.12f; center = new Vector3(0f, 0.12f, 0f);
        }

        CapsuleCollider col = Undo.AddComponent<CapsuleCollider>(bone.gameObject);
        col.direction = axis;
        col.height    = Mathf.Max(length, radius * 2.05f);
        col.radius    = radius;
        col.center    = center;
    }

    static void SetupCharacterJoint(CharacterJoint joint, Rigidbody connectedBody)
    {
        joint.connectedBody               = connectedBody;
        joint.autoConfigureConnectedAnchor = true;

        joint.lowTwistLimit  = new SoftJointLimit { limit = -40f };
        joint.highTwistLimit = new SoftJointLimit { limit =  40f };
        joint.swing1Limit    = new SoftJointLimit { limit =  60f };
        joint.swing2Limit    = new SoftJointLimit { limit =  60f };

        joint.enableProjection   = true;
        joint.projectionDistance = 0.1f;
        joint.projectionAngle    = 40f;
    }

    static void SetupSelfCollisionIgnore(Dictionary<string, Transform> boneMap)
    {
        foreach (string boneName in RagdollBones)
        {
            if (!boneMap.TryGetValue(boneName, out Transform bone)) continue;
            Collider[] boneCols = bone.GetComponents<Collider>();
            if (boneCols.Length == 0) continue;

            Transform cur = bone.parent;
            while (cur != null)
            {
                Collider[] parentCols = cur.GetComponents<Collider>();
                if (parentCols.Length > 0)
                {
                    foreach (var bc in boneCols)
                    foreach (var pc in parentCols)
                        Physics.IgnoreCollision(bc, pc, true);
                    break;
                }
                cur = cur.parent;
            }
        }
    }

    static Rigidbody FindParentRigidbody(Transform bone)
    {
        Transform cur = bone.parent;
        while (cur != null)
        {
            Rigidbody rb = cur.GetComponent<Rigidbody>();
            if (rb != null) return rb;
            cur = cur.parent;
        }
        return null;
    }

    static void CollectByName(Transform t, Dictionary<string, Transform> map)
    {
        map[t.name] = t;
        foreach (Transform child in t) CollectByName(child, map);
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
        T c = go.GetComponent<T>();
        if (c != null) Undo.DestroyObjectImmediate(c);
    }
}
