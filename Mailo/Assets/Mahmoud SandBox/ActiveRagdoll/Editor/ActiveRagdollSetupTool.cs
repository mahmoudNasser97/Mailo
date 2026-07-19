// ─────────────────────────────────────────────────────────────────────────────
// Active Ragdoll Setup Tool
// Run via:  Tools → Active Ragdoll → Setup Player in Scene
//
// What it does:
//  1. Finds the GameObject tagged "Player" in the active scene.
//  2. Finds the child "T-Pose" (which owns the Animator + Mixamo skeleton).
//  3. Creates a hidden "RefRig" child — a pure-Transform copy of the bone
//     hierarchy that the Animator will drive going forward.
//  4. Moves the Animator from T-Pose → RefRig (AnimatePhysics, AlwaysAnimate).
//     Adds GaitIKDriver to RefRig.
//  5. Adds Rigidbody + CapsuleCollider + ConfigurableJoint + Muscle + BoneCollision
//     to every significant Mixamo bone on the physical T-Pose skeleton.
//     (Hips = root: Rigidbody + Collider + BoneCollision only, no joint.)
//  6. Adds ActiveRagdollController, BalanceController, GaitController to Player
//     and wires all serialised references.
//
// After running:
//  • Assign your AnimatorController to RefRig's Animator if none is set.
//  • Tune spring / damper / thresholds in the Inspector.
//  • Enable IK Pass on the Animator's Base Layer in the controller asset.
// ─────────────────────────────────────────────────────────────────────────────
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class ActiveRagdollSetupTool
{
    // Bones that get full physics treatment (Rigidbody + Collider + Joint + Muscle).
    // Hips is treated separately (root — Rigidbody + Collider only, no joint).
    static readonly string[] PhysicsBones =
    {
        "mixamorig:Hips",
        "mixamorig:Spine",  "mixamorig:Spine1",  "mixamorig:Spine2",
        "mixamorig:Neck",   "mixamorig:Head",
        "mixamorig:LeftShoulder",  "mixamorig:RightShoulder",
        "mixamorig:LeftArm",       "mixamorig:RightArm",
        "mixamorig:LeftForeArm",   "mixamorig:RightForeArm",
        "mixamorig:LeftHand",      "mixamorig:RightHand",
        "mixamorig:LeftUpLeg",     "mixamorig:RightUpLeg",
        "mixamorig:LeftLeg",       "mixamorig:RightLeg",
        "mixamorig:LeftFoot",      "mixamorig:RightFoot",
    };

    // Mass per bone (should total ≈ 70 kg for a reference human).
    static readonly Dictionary<string, float> BoneMass = new Dictionary<string, float>
    {
        { "mixamorig:Hips",          12f },
        { "mixamorig:Spine",          4f },
        { "mixamorig:Spine1",         4f },
        { "mixamorig:Spine2",         4f },
        { "mixamorig:Neck",           1f },
        { "mixamorig:Head",           4f },
        { "mixamorig:LeftShoulder",   1f }, { "mixamorig:RightShoulder",  1f },
        { "mixamorig:LeftArm",        2f }, { "mixamorig:RightArm",       2f },
        { "mixamorig:LeftForeArm",  1.5f }, { "mixamorig:RightForeArm", 1.5f },
        { "mixamorig:LeftHand",      0.5f }, { "mixamorig:RightHand",    0.5f },
        { "mixamorig:LeftUpLeg",      6f }, { "mixamorig:RightUpLeg",    6f },
        { "mixamorig:LeftLeg",       3.5f }, { "mixamorig:RightLeg",    3.5f },
        { "mixamorig:LeftFoot",       1f }, { "mixamorig:RightFoot",     1f },
    };

    // Capsule direction per bone (0=X, 1=Y, 2=Z).
    static readonly Dictionary<string, int> CapsuleAxis = new Dictionary<string, int>
    {
        { "mixamorig:Hips",          1 },
        { "mixamorig:Spine",         1 }, { "mixamorig:Spine1",        1 },
        { "mixamorig:Spine2",        1 }, { "mixamorig:Neck",          1 },
        { "mixamorig:Head",          1 },
        { "mixamorig:LeftShoulder",  2 }, { "mixamorig:RightShoulder", 2 },
        { "mixamorig:LeftArm",       2 }, { "mixamorig:RightArm",      2 },
        { "mixamorig:LeftForeArm",   2 }, { "mixamorig:RightForeArm",  2 },
        { "mixamorig:LeftHand",      2 }, { "mixamorig:RightHand",     2 },
        { "mixamorig:LeftUpLeg",     1 }, { "mixamorig:RightUpLeg",    1 },
        { "mixamorig:LeftLeg",       1 }, { "mixamorig:RightLeg",      1 },
        { "mixamorig:LeftFoot",      2 }, { "mixamorig:RightFoot",     2 },
    };

    static HashSet<string> _physicsSet;

    // ─────────────────────────────────────────────────────────────────────────
    [MenuItem("Tools/Active Ragdoll/Setup Player in Scene")]
    static void Setup()
    {
        _physicsSet = new HashSet<string>(PhysicsBones);

        // ── 1. Find Player ─────────────────────────────────────────────────
        GameObject player = GameObject.FindGameObjectWithTag("Player");
        if (player == null)
        {
            Debug.LogError("[ActiveRagdollSetup] No GameObject with tag 'Player' found.");
            return;
        }

        // Guard against double-setup
        if (player.GetComponent<ActiveRagdollController>() != null)
        {
            Debug.LogWarning("[ActiveRagdollSetup] Player already has ActiveRagdollController. " +
                             "Remove all ragdoll components first before re-running.");
            return;
        }

        // ── 2. Find T-Pose child ───────────────────────────────────────────
        Transform tPose = player.transform.Find("T-Pose");
        if (tPose == null)
        {
            Debug.LogError("[ActiveRagdollSetup] No child named 'T-Pose' found under Player.");
            return;
        }

        Animator existingAnim = tPose.GetComponent<Animator>();
        if (existingAnim == null)
        {
            Debug.LogError("[ActiveRagdollSetup] T-Pose has no Animator component.");
            return;
        }

        // ── 3. Find the Mixamo root bone ───────────────────────────────────
        Transform hips = FindInHierarchy(tPose, "mixamorig:Hips");
        if (hips == null)
        {
            Debug.LogError("[ActiveRagdollSetup] Cannot find 'mixamorig:Hips' under T-Pose.");
            return;
        }
        Transform armature = hips.parent; // "Armature"

        Undo.SetCurrentGroupName("Setup Active Ragdoll");
        int undoGroup = Undo.GetCurrentGroup();

        // ── 4. Create RefRig ──────────────────────────────────────────────
        GameObject refRigGO = new GameObject("RefRig");
        Undo.RegisterCreatedObjectUndo(refRigGO, "Create RefRig");
        Undo.SetTransformParent(refRigGO.transform, player.transform, "Parent RefRig");
        refRigGO.transform.SetAsLastSibling();
        refRigGO.transform.localPosition = tPose.localPosition;
        refRigGO.transform.localRotation = tPose.localRotation;
        refRigGO.transform.localScale    = tPose.localScale;

        // Deep-copy the entire bone hierarchy (including non-physics bones so the
        // Animator avatar mapping resolves correctly).
        var physToRef = new Dictionary<Transform, Transform>();
        CopyHierarchy(armature, refRigGO.transform, physToRef);

        // ── 5. Add Animator to RefRig (keep T-Pose's Animator intact) ────────
        // T-Pose Animator is left in place so TopDownEngine components that cache
        // it (CharacterHandleWeapon, etc.) keep working. RefRig gets its own
        // separate Animator that drives the reference poses for the muscles.
        RuntimeAnimatorController ctrl   = existingAnim.runtimeAnimatorController;
        Avatar                    avatar = existingAnim.avatar;

        Animator refAnim = Undo.AddComponent<Animator>(refRigGO);
        refAnim.runtimeAnimatorController = ctrl;
        refAnim.avatar                    = avatar;
        refAnim.cullingMode               = AnimatorCullingMode.AlwaysAnimate;
        // AnimatePhysics → updates in FixedUpdate, stays in sync with muscle loop.
        refAnim.updateMode                = AnimatorUpdateMode.Fixed;
        refAnim.applyRootMotion           = false;

        GaitIKDriver ikDriver = Undo.AddComponent<GaitIKDriver>(refRigGO);

        // ── 6. Add physics to T-Pose bones ───────────────────────────────
        // Build a flat name → Transform map for the physical skeleton.
        var physMap = new Dictionary<string, Transform>();
        CollectByName(hips, physMap);

        // Build the matching ref bone map.
        Transform refHips = FindInHierarchy(refRigGO.transform, "mixamorig:Hips");
        var refMap = new Dictionary<string, Transform>();
        if (refHips != null) CollectByName(refHips, refMap);

        var allMuscles    = new List<Muscle>();
        var allBodies     = new List<Rigidbody>();
        var allColliders  = new List<BoneCollision>();

        foreach (string boneName in PhysicsBones)
        {
            if (!physMap.TryGetValue(boneName, out Transform bone))
            {
                Debug.LogWarning($"[ActiveRagdollSetup] Bone not found: {boneName}");
                continue;
            }

            bool isRoot = boneName == "mixamorig:Hips";

            // ── Rigidbody ─────────────────────────────────────────────────
            Rigidbody rb = Undo.AddComponent<Rigidbody>(bone.gameObject);
            rb.mass            = BoneMass.TryGetValue(boneName, out float m) ? m : 1f;
            rb.linearDamping   = 0.05f;
            rb.angularDamping  = 0.05f;
            rb.interpolation   = RigidbodyInterpolation.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            allBodies.Add(rb);

            // ── CapsuleCollider ───────────────────────────────────────────
            int axis = CapsuleAxis.TryGetValue(boneName, out int a) ? a : 1;
            BuildCapsule(bone, boneName, axis);

            // ── BoneCollision ──────────────────────────────────────────────
            BoneCollision bc = Undo.AddComponent<BoneCollision>(bone.gameObject);
            allColliders.Add(bc);

            if (isRoot) continue; // Hips has no joint or Muscle

            // ── ConfigurableJoint ─────────────────────────────────────────
            Rigidbody parentRB = FindParentRigidbody(bone, physMap);
            ConfigurableJoint joint = Undo.AddComponent<ConfigurableJoint>(bone.gameObject);
            SetupJoint(joint, parentRB);

            // ── Muscle ────────────────────────────────────────────────────
            Muscle muscle = Undo.AddComponent<Muscle>(bone.gameObject);
            if (refMap.TryGetValue(boneName, out Transform refBone))
                muscle.animatedTarget = refBone;
            allMuscles.Add(muscle);
        }

        // ── 7. Add controllers to Player ──────────────────────────────────
        ActiveRagdollController arc = Undo.AddComponent<ActiveRagdollController>(player);
        BalanceController       bc2 = Undo.AddComponent<BalanceController>(player);
        GaitController          gc  = Undo.AddComponent<GaitController>(player);

        // ── 8. Wire ActiveRagdollController ───────────────────────────────
        arc.animatedRoot      = refRigGO.transform;
        arc.physicalHips      = physMap.TryGetValue("mixamorig:Hips", out Transform h)
                                    ? h.GetComponent<Rigidbody>() : null;
        arc.muscles           = allMuscles.ToArray();
        arc.ragdollBodies     = allBodies.ToArray();
        arc.boneColliders     = allColliders.ToArray();
        arc.balanceController = bc2;

        // ── 9. Wire BalanceController ─────────────────────────────────────
        bc2.pelvis    = arc.physicalHips;
        bc2.allBodies = allBodies.ToArray();
        bc2.leftFoot  = physMap.TryGetValue("mixamorig:LeftFoot",  out Transform lf) ? lf.GetComponent<Rigidbody>()  : null;
        bc2.rightFoot = physMap.TryGetValue("mixamorig:RightFoot", out Transform rf) ? rf.GetComponent<Rigidbody>()  : null;

        // ── 10. Wire GaitController ───────────────────────────────────────
        gc.balance    = bc2;
        gc.controller = arc;
        gc.ikDriver   = ikDriver;

        if (physMap.TryGetValue("mixamorig:LeftFoot", out Transform lfg))
        {
            gc.leftAnkleMuscle = lfg.GetComponent<Muscle>();
            gc.leftFootBody    = lfg.GetComponent<Rigidbody>();
        }
        if (physMap.TryGetValue("mixamorig:RightFoot", out Transform rfg))
        {
            gc.rightAnkleMuscle = rfg.GetComponent<Muscle>();
            gc.rightFootBody    = rfg.GetComponent<Rigidbody>();
        }

        // ── 11. Ignore self-collision between ragdoll bones ────────────────
        SetupSelfCollisionIgnore(allBodies, physMap);

        // ── 12. Save ──────────────────────────────────────────────────────
        EditorUtility.SetDirty(player);
        EditorSceneManager.MarkSceneDirty(player.scene);
        Undo.CollapseUndoOperations(undoGroup);

        int bodyCount   = allBodies.Count;
        int muscleCount = allMuscles.Count;
        Debug.Log($"[ActiveRagdollSetup] ✓ Done.\n" +
                  $"  Rigidbodies: {bodyCount}   Muscles: {muscleCount}\n" +
                  "  NEXT STEPS:\n" +
                  "  1. Assign an AnimatorController to RefRig → Animator if none is set.\n" +
                  "  2. Open the AnimatorController, select Base Layer, enable IK Pass.\n" +
                  "  3. Tune spring/damper on ActiveRagdollController, balance on BalanceController.\n" +
                  "  4. (Optional) Create a 'Ragdoll' physics layer and set all bone GameObjects\n" +
                  "     to it, then ignore layer-self collision in Project Settings → Physics.");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Undo-aware removal so the tool can be re-run on a clean slate.
    // ─────────────────────────────────────────────────────────────────────────
    [MenuItem("Tools/Active Ragdoll/Remove Setup from Player")]
    static void Remove()
    {
        GameObject player = GameObject.FindGameObjectWithTag("Player");
        if (player == null) { Debug.LogError("No Player found."); return; }

        Undo.SetCurrentGroupName("Remove Active Ragdoll");
        int group = Undo.GetCurrentGroup();

        // Remove RefRig — but first recover its Animator data so we can restore
        // T-Pose's Animator if it was destroyed by an earlier (buggy) Setup run.
        RuntimeAnimatorController savedCtrl   = null;
        Avatar                    savedAvatar = null;
        Transform refRig = player.transform.Find("RefRig");
        if (refRig != null)
        {
            Animator refAnim = refRig.GetComponent<Animator>();
            if (refAnim != null)
            {
                savedCtrl   = refAnim.runtimeAnimatorController;
                savedAvatar = refAnim.avatar;
            }
            Undo.DestroyObjectImmediate(refRig.gameObject);
        }

        // Remove controller scripts from Player root
        TryDestroy<GaitController>(player);
        TryDestroy<BalanceController>(player);
        TryDestroy<ActiveRagdollController>(player);

        // Remove physics from T-Pose bones
        Transform tPose = player.transform.Find("T-Pose");
        if (tPose != null)
        {
            foreach (string boneName in PhysicsBones)
            {
                Transform bone = FindInHierarchy(tPose, boneName);
                if (bone == null) continue;
                TryDestroy<Muscle>(bone.gameObject);
                TryDestroy<BoneCollision>(bone.gameObject);
                TryDestroy<ConfigurableJoint>(bone.gameObject);
                TryDestroy<CapsuleCollider>(bone.gameObject);
                TryDestroy<Rigidbody>(bone.gameObject);
            }

            // If the previous Setup destroyed T-Pose's Animator, restore it with the
            // data recovered from RefRig so the next Setup run reads it correctly.
            Animator tpAnim = tPose.GetComponent<Animator>();
            if (tpAnim == null)
            {
                tpAnim = Undo.AddComponent<Animator>(tPose.gameObject);
                tpAnim.runtimeAnimatorController = savedCtrl;
                tpAnim.avatar                    = savedAvatar;
            }
        }

        EditorSceneManager.MarkSceneDirty(player.scene);
        Undo.CollapseUndoOperations(group);
        Debug.Log("[ActiveRagdollSetup] Removed all ragdoll components from Player.");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────────────────

    static void CopyHierarchy(Transform src, Transform parent,
                               Dictionary<Transform, Transform> physToRef)
    {
        var go = new GameObject(src.name);
        Undo.RegisterCreatedObjectUndo(go, "RefRig bone");
        Undo.SetTransformParent(go.transform, parent, "RefRig bone parent");
        go.transform.localPosition = src.localPosition;
        go.transform.localRotation = src.localRotation;
        go.transform.localScale    = src.localScale;
        physToRef[src] = go.transform;
        foreach (Transform child in src)
            CopyHierarchy(child, go.transform, physToRef);
    }

    static void CollectByName(Transform root, Dictionary<string, Transform> map)
    {
        map[root.name] = root;
        foreach (Transform child in root)
            CollectByName(child, map);
    }

    static Transform FindInHierarchy(Transform root, string name)
    {
        if (root.name == name) return root;
        foreach (Transform child in root)
        {
            Transform found = FindInHierarchy(child, name);
            if (found != null) return found;
        }
        return null;
    }

    static Rigidbody FindParentRigidbody(Transform bone,
                                          Dictionary<string, Transform> physMap)
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

    static void BuildCapsule(Transform bone, string boneName, int axis)
    {
        // Estimate length from this bone to its first physics-set child.
        float length = 0.2f, radius = 0.06f;
        Vector3 center = Vector3.zero;

        foreach (Transform child in bone)
        {
            if (_physicsSet.Contains(child.name))
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

        // Head uses a sphere-like capsule; clamp minimum size
        if (boneName == "mixamorig:Head") { length = 0.24f; radius = 0.12f; center = new Vector3(0, 0.12f, 0); }
        if (boneName == "mixamorig:Hips") { length = 0.30f; radius = 0.14f; center = new Vector3(0, 0.05f, 0); }

        CapsuleCollider col = Undo.AddComponent<CapsuleCollider>(bone.gameObject);
        col.direction = axis;
        col.height    = Mathf.Max(length, radius * 2.05f);
        col.radius    = radius;
        col.center    = center;
    }

    static void SetupJoint(ConfigurableJoint joint, Rigidbody connectedBody)
    {
        joint.connectedBody             = connectedBody;
        joint.configuredInWorldSpace    = false;
        joint.autoConfigureConnectedAnchor = true;

        // Linear: locked — bones can only rotate relative to each other, not
        // translate. Without this constraint the chain explodes on physics activation.
        joint.xMotion = ConfigurableJointMotion.Locked;
        joint.yMotion = ConfigurableJointMotion.Locked;
        joint.zMotion = ConfigurableJointMotion.Locked;

        // Angular: limited — prevents extreme / impossible poses.
        joint.angularXMotion = ConfigurableJointMotion.Limited;
        joint.angularYMotion = ConfigurableJointMotion.Limited;
        joint.angularZMotion = ConfigurableJointMotion.Limited;

        joint.lowAngularXLimit  = new SoftJointLimit { limit = -60f };
        joint.highAngularXLimit = new SoftJointLimit { limit =  60f };
        joint.angularYLimit     = new SoftJointLimit { limit =  60f };
        joint.angularZLimit     = new SoftJointLimit { limit =  60f };

        // Slerp drive: spring values are set at runtime by Muscle.Init().
        joint.rotationDriveMode = RotationDriveMode.Slerp;
        joint.slerpDrive        = new JointDrive
        {
            positionSpring = 0f,
            positionDamper = 0f,
            maximumForce   = float.MaxValue
        };

        // Projection snaps the joint back if physics drifts too far.
        joint.projectionMode     = JointProjectionMode.PositionAndRotation;
        joint.projectionDistance = 0.1f;
        joint.projectionAngle    = 40f;
    }

    static void SetupSelfCollisionIgnore(List<Rigidbody> bodies,
                                          Dictionary<string, Transform> physMap)
    {
        // Build a list of adjacent (parent-child) collider pairs and ignore them.
        // This prevents the most common jitter source without requiring a dedicated layer.
        foreach (string boneName in PhysicsBones)
        {
            if (!physMap.TryGetValue(boneName, out Transform bone)) continue;
            Collider[] boneCols = bone.GetComponents<Collider>();
            if (boneCols.Length == 0) continue;

            if (physMap.TryGetValue(bone.parent != null ? bone.parent.name : "", out Transform parentBone))
            {
                Collider[] parentCols = parentBone.GetComponents<Collider>();
                foreach (var bc  in boneCols)
                foreach (var pc  in parentCols)
                    Physics.IgnoreCollision(bc, pc, true);
            }
        }
    }

    static void TryDestroy<T>(GameObject go) where T : Component
    {
        T c = go.GetComponent<T>();
        if (c != null) Undo.DestroyObjectImmediate(c);
    }
}
