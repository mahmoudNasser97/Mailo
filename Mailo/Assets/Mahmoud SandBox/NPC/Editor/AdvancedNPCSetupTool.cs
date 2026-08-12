using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using RootMotion.Dynamics;

public static class AdvancedNPCSetupTool
{
    [MenuItem("Tools/Advanced NPC/Setup Selected As Advanced NPC")]
    static void SetupAdvancedNPC()
    {
        GameObject selected = Selection.activeGameObject;
        if (selected == null)
        {
            Debug.LogError("[AdvancedNPC] Nothing selected. Select a character root or its animated child in the Hierarchy.");
            return;
        }

        // Detect which object carries the CharacterController — that is the animated target.
        // Check the selected object first, then fall back to searching its children.
        GameObject animTarget = FindAnimatedTarget(selected);
        if (animTarget == null)
        {
            Debug.LogError($"[AdvancedNPC] No CharacterController found on '{selected.name}' or its children. " +
                           "Run the character movement setup tool on this model first.");
            return;
        }

        // NPC root = parent of the animated target (or the object itself if it has no parent)
        GameObject npcRoot = animTarget.transform.parent != null
            ? animTarget.transform.parent.gameObject
            : animTarget;

        // Strip legacy movement components if present
        RemoveIfPresent<PuppetMoverSimple>(animTarget);
        RemoveIfPresent<NPCChaseController>(animTarget);

        // Add all NPC components to the animated target
        NPCBrain       brain   = GetOrAdd<NPCBrain>(animTarget);
        NPCPatroller   patrol  = GetOrAdd<NPCPatroller>(animTarget);
        NPCChaser      chaser  = GetOrAdd<NPCChaser>(animTarget);
        NPCThrower     thrower = GetOrAdd<NPCThrower>(animTarget);
        NPCHitReaction hitReac = GetOrAdd<NPCHitReaction>(animTarget);
        NPCRVOAgent    rvo     = GetOrAdd<NPCRVOAgent>(animTarget);
        NPCHitVFX      vfx     = GetOrAdd<NPCHitVFX>(animTarget);

        // Wire Brain's sub-component references
        var brainSO = new SerializedObject(brain);
        brainSO.FindProperty("_patroller").objectReferenceValue   = patrol;
        brainSO.FindProperty("_chaser").objectReferenceValue      = chaser;
        brainSO.FindProperty("_thrower").objectReferenceValue     = thrower;
        brainSO.FindProperty("_hitReaction").objectReferenceValue = hitReac;
        brainSO.FindProperty("_rvoAgent").objectReferenceValue    = rvo;
        brainSO.FindProperty("_hitVFX").objectReferenceValue      = vfx;
        brainSO.ApplyModifiedProperties();

        // HitReactor lives on the NPC root so thrown-object collisions propagate up correctly
        GetOrAdd<HitReactor>(npcRoot);

        // Lower ragdoll knockdown threshold for NPCs (player threshold stays at 200)
        PuppetRagdollController ragdoll = npcRoot.GetComponentInChildren<PuppetRagdollController>();
        if (ragdoll != null)
        {
            Undo.RecordObject(ragdoll, "Configure NPC ragdoll");
            ragdoll.mover              = null;
            ragdoll.knockdownThreshold = 80f;
            EditorUtility.SetDirty(ragdoll);
        }
        else Debug.LogWarning("[AdvancedNPC] PuppetRagdollController not found — run the character movement setup tool first.");

        // Assign the dedicated NPC AnimatorController (build it if it doesn't exist yet)
        var ctrl = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(
            NPCAnimatorBuilder.ControllerPath);
        if (ctrl == null)
            ctrl = NPCAnimatorBuilder.Build();

        Animator anim = animTarget.GetComponent<Animator>();
        if (anim != null && ctrl != null)
        {
            Undo.RecordObject(anim, "Assign NPC AnimatorController");
            anim.runtimeAnimatorController = ctrl;
            anim.applyRootMotion           = false;
            EditorUtility.SetDirty(anim);
        }

        // Create an NPCSpawner nearby for convenience
        GameObject spawnerGO = new GameObject("NPCSpawner");
        Undo.RegisterCreatedObjectUndo(spawnerGO, "Create NPCSpawner");
        spawnerGO.transform.position = npcRoot.transform.position + new Vector3(0f, 0f, 8f);
        spawnerGO.AddComponent<NPCSpawner>();

        EditorSceneManager.MarkSceneDirty(selected.scene);

        Debug.Log(
            $"[AdvancedNPC] ✓ '{selected.name}' configured as Advanced NPC\n" +
            $"  Animated target : '{animTarget.name}'\n" +
            $"  NPC root        : '{npcRoot.name}'\n" +
             "  Components added: NPCBrain, NPCPatroller, NPCChaser, NPCThrower, NPCHitReaction, NPCRVOAgent, NPCHitVFX\n" +
             "  HitReactor: on NPC root\n" +
             "  Ragdoll knockdownThreshold: 80\n" +
            $"  NPCSpawner: '{spawnerGO.name}' at {spawnerGO.transform.position}\n" +
             "  → In Inspector: assign Patrol Points, Throwable Prefabs, Hand Bone,\n" +
             "    Hit Material, Hit Particle Prefab on NPCHitVFX,\n" +
             "    and NPC Prefab + Wave Configs on NPCSpawner.\n" +
             "  → In Animator: add 'Throw' Trigger and 'HitReact' Trigger parameters.\n" +
             "    Optionally add animation event on Throw clip calling NPCThrower.ReleaseThrowable().");
    }

    // Greys out the menu item when nothing is selected in the Hierarchy
    [MenuItem("Tools/Advanced NPC/Setup Selected As Advanced NPC", true)]
    static bool ValidateSetupAdvancedNPC() => Selection.activeGameObject != null;

    // Returns the first GameObject in the hierarchy (root first, then children)
    // that carries a CharacterController — that object is the animated target.
    static GameObject FindAnimatedTarget(GameObject root)
    {
        if (root.GetComponent<CharacterController>() != null) return root;
        foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
            if (child != root.transform && child.GetComponent<CharacterController>() != null)
                return child.gameObject;
        return null;
    }

    static T GetOrAdd<T>(GameObject go) where T : Component
    {
        var c = go.GetComponent<T>();
        return c != null ? c : Undo.AddComponent<T>(go);
    }

    static void RemoveIfPresent<T>(GameObject go) where T : Component
    {
        var c = go.GetComponent<T>();
        if (c != null) Undo.DestroyObjectImmediate(c);
    }
}
