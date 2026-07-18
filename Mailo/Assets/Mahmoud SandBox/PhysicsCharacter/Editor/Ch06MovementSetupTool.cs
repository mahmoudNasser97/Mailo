using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using RootMotion.Dynamics;

public static class Ch06MovementSetupTool
{
    const string RootName   = "Ch06_nonPBR Root";
    const string TargetName = "Ch06_nonPBR";

    [MenuItem("Tools/PuppetMaster Character/Setup Ch06 Movement")]
    static void Setup()
    {
        // 1. Find scene objects
        GameObject root = GameObject.Find(RootName);
        if (root == null) { Debug.LogError($"[Ch06Setup] '{RootName}' not found in scene."); return; }

        Transform targetTf = root.transform.Find(TargetName);
        if (targetTf == null) { Debug.LogError($"[Ch06Setup] '{TargetName}' not found under '{RootName}'."); return; }
        GameObject target = targetTf.gameObject;

        PuppetMaster pm = root.GetComponentInChildren<PuppetMaster>();
        if (pm == null) { Debug.LogError("[Ch06Setup] No PuppetMaster found. Run PuppetMaster setup first."); return; }

        // 2. Fix Animator on animated target
        Animator anim = target.GetComponent<Animator>();
        if (anim == null) anim = Undo.AddComponent<Animator>(target);

        // Record BEFORE any property mutation, including controller assignment
        Undo.RecordObject(anim, "Fix Ch06 Animator");

        if (anim.runtimeAnimatorController == null)
        {
            string[] guids = AssetDatabase.FindAssets("CharacterAnimaiton t:AnimatorController");
            if (guids.Length > 0)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[0]);
                anim.runtimeAnimatorController = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(path);
                Debug.Log($"[Ch06Setup] Assigned controller: {path}");
            }
            else Debug.LogWarning("[Ch06Setup] CharacterAnimaiton controller not found — assign it manually.");
        }

        anim.applyRootMotion = false;
        anim.updateMode      = AnimatorUpdateMode.Fixed;
        anim.cullingMode     = AnimatorCullingMode.AlwaysAnimate;
        EditorUtility.SetDirty(anim);

        // 3. Add CharacterController
        CharacterController cc = target.GetComponent<CharacterController>();
        if (cc == null) cc = Undo.AddComponent<CharacterController>(target);
        Undo.RecordObject(cc, "Configure Ch06 CharacterController");
        cc.height = 1.8f;
        cc.radius = 0.3f;
        cc.center = new Vector3(0f, 0.9f, 0f);
        EditorUtility.SetDirty(cc);

        // 4. Add PuppetMoverSimple to animated target
        PuppetMoverSimple mover = target.GetComponent<PuppetMoverSimple>();
        if (mover == null) mover = Undo.AddComponent<PuppetMoverSimple>(target);

        // 5. Add PuppetRagdollController to the PuppetMaster's GameObject
        PuppetRagdollController ctrl = pm.GetComponent<PuppetRagdollController>();
        if (ctrl == null) ctrl = Undo.AddComponent<PuppetRagdollController>(pm.gameObject);

        // 6. Add PuppetImpactReporter to every Rigidbody in the puppet hierarchy
        var bodies = new List<Rigidbody>();
        foreach (Rigidbody rb in pm.GetComponentsInChildren<Rigidbody>())
        {
            PuppetImpactReporter rep = rb.GetComponent<PuppetImpactReporter>();
            if (rep == null) rep = Undo.AddComponent<PuppetImpactReporter>(rb.gameObject);
            rep.Init(ctrl);
            EditorUtility.SetDirty(rep);
            bodies.Add(rb);
        }

        // 7. Wire all references on PuppetRagdollController
        Undo.RecordObject(ctrl, "Wire Ch06 ragdoll references");
        ctrl.pm           = pm;
        ctrl.mover        = mover;
        ctrl.muscleBodies = bodies.ToArray();
        EditorUtility.SetDirty(ctrl);

        EditorSceneManager.MarkSceneDirty(root.scene);

        Debug.Log($"[Ch06Setup] ✓ COMPLETE\n" +
                  $"  Animated target : '{target.name}' (layer {target.layer})\n" +
                  $"  PuppetMaster    : '{pm.name}'\n" +
                  $"  Muscle bodies   : {bodies.Count}\n" +
                  "  Press Play → WASD to move. Push a heavy cube to trigger ragdoll.");
    }

    [MenuItem("Tools/PuppetMaster Character/Remove Ch06 Movement Setup")]
    static void Remove()
    {
        GameObject root = GameObject.Find(RootName);
        if (root == null) { Debug.LogError($"[Ch06Setup] '{RootName}' not found."); return; }

        Transform targetTf = root.transform.Find(TargetName);
        if (targetTf != null)
        {
            TryDestroy<PuppetMoverSimple>(targetTf.gameObject);
            TryDestroy<CharacterController>(targetTf.gameObject);
        }

        PuppetMaster pm = root.GetComponentInChildren<PuppetMaster>();
        if (pm != null)
        {
            foreach (var rep in pm.GetComponentsInChildren<PuppetImpactReporter>())
                Object.DestroyImmediate(rep);
            TryDestroy<PuppetRagdollController>(pm.gameObject);
        }

        EditorSceneManager.MarkSceneDirty(root.scene);
        Debug.Log("[Ch06Setup] Movement setup removed.");
    }

    static void TryDestroy<T>(GameObject go) where T : Component
    {
        var c = go.GetComponent<T>();
        if (c != null) Object.DestroyImmediate(c);
    }
}
