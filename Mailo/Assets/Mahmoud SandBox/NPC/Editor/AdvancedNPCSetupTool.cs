using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using RootMotion.Dynamics;

public static class AdvancedNPCSetupTool
{
    const string SourceRootName = "Ch06_nonPBR Root";
    const string AnimTargetName = "Ch06_nonPBR";
    const string PlayerName     = "ThirdPersonPuppet (1)";

    [MenuItem("Tools/Advanced NPC/Setup Advanced NPC")]
    static void SetupAdvancedNPC()
    {
        GameObject source = GameObject.Find(SourceRootName);
        if (source == null)
        {
            Debug.LogError($"[AdvancedNPC] '{SourceRootName}' not found. Open the scene that contains it first.");
            return;
        }

        // 1. Duplicate hierarchy
        GameObject npc = Object.Instantiate(source, source.transform.parent);
        Undo.RegisterCreatedObjectUndo(npc, "Create Advanced NPC");
        npc.name               = "AdvancedNPC_Ch06";
        npc.transform.position = source.transform.position + new Vector3(3f, 0f, 0f);

        // 2. Find animated target
        Transform animTf = npc.transform.Find(AnimTargetName);
        if (animTf == null)
        {
            Debug.LogError($"[AdvancedNPC] '{AnimTargetName}' not found under NPC root.");
            Undo.DestroyObjectImmediate(npc);
            return;
        }
        GameObject animTarget = animTf.gameObject;

        // 3. Strip old movement components
        RemoveIfPresent<PuppetMoverSimple>(animTarget);
        RemoveIfPresent<NPCChaseController>(animTarget);

        // 4. Add all new NPC components
        NPCBrain       brain   = GetOrAdd<NPCBrain>(animTarget);
        NPCPatroller   patrol  = GetOrAdd<NPCPatroller>(animTarget);
        NPCChaser      chaser  = GetOrAdd<NPCChaser>(animTarget);
        NPCThrower     thrower = GetOrAdd<NPCThrower>(animTarget);
        NPCHitReaction hitReac = GetOrAdd<NPCHitReaction>(animTarget);
        NPCRVOAgent    rvo     = GetOrAdd<NPCRVOAgent>(animTarget);
        NPCHitVFX      vfx     = GetOrAdd<NPCHitVFX>(animTarget);

        // 5. Wire Brain's SerializeField references
        var brainSO = new SerializedObject(brain);
        brainSO.FindProperty("_patroller").objectReferenceValue   = patrol;
        brainSO.FindProperty("_chaser").objectReferenceValue      = chaser;
        brainSO.FindProperty("_thrower").objectReferenceValue     = thrower;
        brainSO.FindProperty("_hitReaction").objectReferenceValue = hitReac;
        brainSO.FindProperty("_rvoAgent").objectReferenceValue    = rvo;
        brainSO.FindProperty("_hitVFX").objectReferenceValue      = vfx;
        brainSO.ApplyModifiedProperties();

        // 6. HitReactor on NPC root
        GetOrAdd<HitReactor>(npc);

        // 7. Configure ragdoll for NPC (lower threshold than player's 200)
        PuppetRagdollController ragdoll = npc.GetComponentInChildren<PuppetRagdollController>();
        if (ragdoll != null)
        {
            Undo.RecordObject(ragdoll, "Configure NPC ragdoll");
            ragdoll.mover              = null;
            ragdoll.knockdownThreshold = 80f;
            EditorUtility.SetDirty(ragdoll);
        }
        else Debug.LogWarning("[AdvancedNPC] PuppetRagdollController not found — run 'Setup Ch06 Movement' on source first.");

        // 8. Assign AnimatorController
        string[] guids = AssetDatabase.FindAssets("CharacterAnimaiton t:AnimatorController");
        if (guids.Length > 0)
        {
            var ctrl = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(
                AssetDatabase.GUIDToAssetPath(guids[0]));
            Animator anim = animTarget.GetComponent<Animator>();
            if (anim != null && ctrl != null)
            {
                Undo.RecordObject(anim, "Assign NPC AnimatorController");
                anim.runtimeAnimatorController = ctrl;
                anim.applyRootMotion           = false;
                EditorUtility.SetDirty(anim);
            }
        }
        else Debug.LogWarning("[AdvancedNPC] 'CharacterAnimaiton' animator controller not found — assign manually.");

        // 9. Create NPCSpawner in scene
        GameObject spawnerGO = new GameObject("NPCSpawner");
        Undo.RegisterCreatedObjectUndo(spawnerGO, "Create NPCSpawner");
        spawnerGO.transform.position = source.transform.position + new Vector3(0f, 0f, 8f);
        spawnerGO.AddComponent<NPCSpawner>();

        EditorSceneManager.MarkSceneDirty(npc.scene);

        Debug.Log(
            $"[AdvancedNPC] ✓ Created '{npc.name}' at {npc.transform.position}\n" +
             "  Components: NPCBrain, NPCPatroller, NPCChaser, NPCThrower, NPCHitReaction, NPCRVOAgent, NPCHitVFX\n" +
             "  HitReactor: on NPC root\n" +
             "  Ragdoll knockdownThreshold: 80\n" +
            $"  NPCSpawner: '{spawnerGO.name}' at {spawnerGO.transform.position}\n" +
             "  → In Inspector: assign Patrol Points, Throwable Prefabs, Hand Bone,\n" +
             "    Hit Material, Hit Particle Prefab on NPCHitVFX,\n" +
             "    and NPC Prefab + Wave Configs on NPCSpawner.\n" +
             "  → In Animator: add 'Throw' Trigger and 'HitReact' Trigger parameters.\n" +
             "    Optionally add animation event on Throw clip calling NPCThrower.ReleaseThrowable().");
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
