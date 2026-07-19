using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using RootMotion.Dynamics;

public static class NPCSetupTool
{
    const string SourceRootName = "Ch06_nonPBR Root";
    const string AnimTargetName = "Ch06_nonPBR";
    const string PlayerName     = "ThirdPersonPuppet (1)";

    [MenuItem("Tools/PuppetMaster Character/Create Ch06 NPC")]
    static void CreateNPC()
    {
        GameObject source = GameObject.Find(SourceRootName);
        if (source == null)
        {
            Debug.LogError($"[NPCSetup] '{SourceRootName}' not found in scene. Open the scene that contains it first.");
            return;
        }

        // 1. Duplicate entire PuppetMaster hierarchy — Unity remaps all internal cross-references automatically
        GameObject npc = Object.Instantiate(source, source.transform.parent);
        Undo.RegisterCreatedObjectUndo(npc, "Create Ch06 NPC");
        npc.name             = "NPC_Ch06";
        npc.transform.position = source.transform.position + new Vector3(5f, 0f, 0f);

        // 2. Find animated target child
        Transform animTf = npc.transform.Find(AnimTargetName);
        if (animTf == null)
        {
            Debug.LogError($"[NPCSetup] '{AnimTargetName}' not found under NPC root. Aborting.");
            Undo.DestroyObjectImmediate(npc);
            return;
        }
        GameObject animTarget = animTf.gameObject;

        // 3. Swap PuppetMoverSimple → NPCChaseController
        PuppetMoverSimple oldMover = animTarget.GetComponent<PuppetMoverSimple>();
        if (oldMover != null)
            Undo.DestroyObjectImmediate(oldMover);

        NPCChaseController chase = animTarget.GetComponent<NPCChaseController>();
        if (chase == null)
            chase = Undo.AddComponent<NPCChaseController>(animTarget);

        // 4. Wire player as chase target
        GameObject player = GameObject.Find(PlayerName);
        if (player != null)
        {
            SerializedObject so = new SerializedObject(chase);
            so.FindProperty("_target").objectReferenceValue = player.transform;
            so.ApplyModifiedProperties();
            Debug.Log($"[NPCSetup] Chase target wired to '{PlayerName}'.");
        }
        else
            Debug.LogWarning($"[NPCSetup] '{PlayerName}' not found — assign _target on NPCChaseController manually.");

        // 5. Add HitReactor on the NPC root so thrown-object hits propagate from any muscle bone up to it
        HitReactor hitReactor = npc.GetComponent<HitReactor>();
        if (hitReactor == null)
            Undo.AddComponent<HitReactor>(npc);

        // 6. Clear PuppetRagdollController.mover — NPC reads State directly, no need for the reference
        PuppetRagdollController ragdoll = npc.GetComponentInChildren<PuppetRagdollController>();
        if (ragdoll != null)
        {
            Undo.RecordObject(ragdoll, "Clear NPC mover ref");
            ragdoll.mover = null;
            EditorUtility.SetDirty(ragdoll);
        }
        else
            Debug.LogWarning("[NPCSetup] PuppetRagdollController not found — run 'Setup Ch06 Movement' on source first.");

        EditorSceneManager.MarkSceneDirty(npc.scene);

        Debug.Log($"[NPCSetup] ✓ NPC created: '{npc.name}' at {npc.transform.position}\n" +
                  $"  Animated target : '{animTarget.name}'\n" +
                  $"  HitReactor      : on NPC root\n" +
                  $"  Chase target    : {(player != null ? PlayerName : "NOT SET — assign manually")}\n" +
                  "  Press Play → NPC chases player. Throw objects to knock it down.");
    }
}
