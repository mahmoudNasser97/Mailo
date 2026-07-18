# Ch06_nonPBR Movement & Ragdoll — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Wire WASD movement, animation, and ragdoll-on-impact for the Ch06_nonPBR character using its already-configured PuppetMaster setup.

**Architecture:** Three new C# files — a lightweight per-bone collision reporter (`PuppetImpactReporter.cs`), a central ragdoll state machine (`PuppetRagdollController.cs`), and a one-click editor setup tool (`Ch06MovementSetupTool.cs`). The animated target (`Ch06_nonPBR`) gets CharacterController + PuppetMoverSimple for WASD. Each physics puppet bone gets `PuppetImpactReporter`. On a strong hit, `PuppetRagdollController` sets `pinWeight=0` (full ragdoll), waits for the body to settle, then lerps `pinWeight` back to 1 (standing).

**Tech Stack:** Unity 6, C#, RootMotion.Dynamics (PuppetMaster already installed)

## Global Constraints

- Unity 6 API: use `rb.linearVelocity` not `rb.velocity`
- PuppetMaster namespace: `using RootMotion.Dynamics;`
- All editor tools go in `Editor/` subfolders (Unity strips them from builds)
- Scene: `Assets/TopDownEngine/Demos/Colonel/MainGamePlayScene.unity`
- Character root name in scene: `Ch06_nonPBR Root`
- Animated target child name: `Ch06_nonPBR`
- Animator controller asset name: `CharacterAnimaiton` (typo is intentional — matches existing asset)
- Layers: Character = 8, Ragdoll = 9

---

### Task 1: PuppetImpactReporter.cs

**Files:**
- Create: `Assets/Mahmoud SandBox/PhysicsCharacter/PuppetImpactReporter.cs`

**Interfaces:**
- Produces: `public void Init(PuppetRagdollController controller)` — called by the setup tool to wire the reference; `[SerializeField]` ensures the reference survives scene save/load
- Produces: `OnCollisionEnter` → `controller.ReportImpact(float impulse)`

- [ ] **Step 1: Create the file**

Create `Assets/Mahmoud SandBox/PhysicsCharacter/PuppetImpactReporter.cs`:

```csharp
using UnityEngine;

[RequireComponent(typeof(Rigidbody))]
public class PuppetImpactReporter : MonoBehaviour
{
    [SerializeField] PuppetRagdollController _controller;

    public void Init(PuppetRagdollController controller) => _controller = controller;

    void OnCollisionEnter(Collision col) => _controller?.ReportImpact(col.impulse.magnitude);
}
```

- [ ] **Step 2: Verify compilation**

Switch to Unity. Wait for domain reload (progress bar in bottom-right). Check the Console panel — no errors should reference `PuppetImpactReporter`. A warning about `PuppetRagdollController` being undefined is expected until Task 2.

- [ ] **Step 3: Commit**

```bash
git add "Assets/Mahmoud SandBox/PhysicsCharacter/PuppetImpactReporter.cs"
git add "Assets/Mahmoud SandBox/PhysicsCharacter/PuppetImpactReporter.cs.meta"
git commit -m "Add PuppetImpactReporter: per-bone collision impulse reporter"
```

---

### Task 2: PuppetRagdollController.cs

**Files:**
- Create: `Assets/Mahmoud SandBox/PhysicsCharacter/PuppetRagdollController.cs`

**Interfaces:**
- Consumes: `PuppetMaster.pinWeight` (public float, 0 = full ragdoll, 1 = fully pinned) from `RootMotion.Dynamics`
- Consumes: `PuppetMoverSimple.enabled` (bool) to freeze/resume movement during ragdoll
- Produces: `public void ReportImpact(float impulse)` — called by every `PuppetImpactReporter`
- Produces: `public PuppetPhysicsState State` — readable enum (Balanced / Ragdoll / GettingUp)

- [ ] **Step 1: Create the file**

Create `Assets/Mahmoud SandBox/PhysicsCharacter/PuppetRagdollController.cs`:

```csharp
using System.Collections;
using UnityEngine;
using RootMotion.Dynamics;

public enum PuppetPhysicsState { Balanced, Ragdoll, GettingUp }

public class PuppetRagdollController : MonoBehaviour
{
    [Header("Ragdoll")]
    public float knockdownThreshold   = 20f;
    public float settleDelay          = 1.5f;
    public float settleSpeedThreshold = 0.4f;
    public float maxSettleWait        = 6f;
    public float pinRestoreTime       = 1.2f;

    [Header("References")]
    public PuppetMaster      pm;
    public PuppetMoverSimple mover;
    public Rigidbody[]       muscleBodies;

    public PuppetPhysicsState State { get; private set; } = PuppetPhysicsState.Balanced;

    bool _knockdownPending;

    public void ReportImpact(float impulse)
    {
        if (State != PuppetPhysicsState.Balanced || _knockdownPending) return;
        if (impulse < knockdownThreshold) return;
        _knockdownPending = true;
        StartCoroutine(RagdollSequence());
    }

    IEnumerator RagdollSequence()
    {
        State = PuppetPhysicsState.Ragdoll;
        if (mover != null) mover.enabled = false;
        pm.pinWeight = 0f;

        float elapsed   = 0f;
        float stillTime = 0f;

        while (elapsed < maxSettleWait)
        {
            yield return new WaitForFixedUpdate();
            elapsed += Time.fixedDeltaTime;

            float maxSpd = 0f;
            foreach (var rb in muscleBodies)
                if (rb != null) maxSpd = Mathf.Max(maxSpd, rb.linearVelocity.magnitude);

            stillTime = maxSpd < settleSpeedThreshold
                ? stillTime + Time.fixedDeltaTime
                : 0f;

            if (stillTime >= settleDelay) break;
        }

        _knockdownPending = false;
        StartCoroutine(GetUpSequence());
    }

    IEnumerator GetUpSequence()
    {
        State = PuppetPhysicsState.GettingUp;

        float elapsed = 0f;
        while (elapsed < pinRestoreTime)
        {
            elapsed += Time.deltaTime;
            pm.pinWeight = Mathf.Lerp(0f, 1f, elapsed / pinRestoreTime);
            yield return null;
        }
        pm.pinWeight = 1f;

        if (mover != null) mover.enabled = true;
        State = PuppetPhysicsState.Balanced;
    }
}
```

- [ ] **Step 2: Verify compilation**

Switch to Unity. Wait for domain reload. Console must show zero errors for `PuppetRagdollController` or `PuppetImpactReporter`.

- [ ] **Step 3: Commit**

```bash
git add "Assets/Mahmoud SandBox/PhysicsCharacter/PuppetRagdollController.cs"
git add "Assets/Mahmoud SandBox/PhysicsCharacter/PuppetRagdollController.cs.meta"
git commit -m "Add PuppetRagdollController: pinWeight-based ragdoll state machine"
```

---

### Task 3: Ch06MovementSetupTool.cs — Write, Run, Verify

**Files:**
- Create: `Assets/Mahmoud SandBox/PhysicsCharacter/Editor/Ch06MovementSetupTool.cs`

**Interfaces:**
- Consumes: `PuppetImpactReporter.Init(PuppetRagdollController)` from Task 1
- Consumes: `PuppetRagdollController.pm`, `.mover`, `.muscleBodies` from Task 2
- Consumes: `PuppetMoverSimple` (existing at `Assets/Mahmoud SandBox/PhysicsCharacter/PuppetMoverSimple.cs`)
- Consumes: `PuppetMaster` from `RootMotion.Dynamics`

- [ ] **Step 1: Create the editor tool**

Create `Assets/Mahmoud SandBox/PhysicsCharacter/Editor/Ch06MovementSetupTool.cs`:

```csharp
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

        Undo.RecordObject(anim, "Fix Ch06 Animator");
        anim.applyRootMotion = false;
        anim.updateMode      = AnimatorUpdateMode.AnimatePhysics;
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
```

- [ ] **Step 2: Verify compilation**

Switch to Unity, wait for domain reload. Console must show zero errors. Check `Tools → PuppetMaster Character` — the menu entry `Setup Ch06 Movement` must appear.

- [ ] **Step 3: Run the setup tool**

`Tools → PuppetMaster Character → Setup Ch06 Movement`

Expected Console output:
```
[Ch06Setup] Assigned controller: Assets/CharacterAnimaiton.controller
[Ch06Setup] ✓ COMPLETE
  Animated target : 'Ch06_nonPBR' (layer 8)
  PuppetMaster    : 'PuppetMaster'
  Muscle bodies   : 14    ← should be > 0
```

If `Muscle bodies: 0` — the PuppetMaster has no Rigidbody children. Verify `Ch06_nonPBR Root` is the active version (not the inactive duplicate at the bottom of the Hierarchy).

- [ ] **Step 4: Verify Inspector**

**Select `Ch06_nonPBR`** in Hierarchy → Inspector must show:
- Animator → Controller: `CharacterAnimaiton`
- Animator → Apply Root Motion: **unchecked**
- `CharacterController` component (height 1.8, radius 0.3)
- `PuppetMoverSimple` component

**Select `PuppetMaster` child** (inside Ch06_nonPBR Root) → Inspector must show:
- `PuppetRagdollController` component
- `Pm` field: references the PuppetMaster component on the same GameObject
- `Mover` field: references the PuppetMoverSimple on Ch06_nonPBR
- `Muscle Bodies`: array with ~14 entries (not empty)

- [ ] **Step 5: Commit**

```bash
git add "Assets/Mahmoud SandBox/PhysicsCharacter/Editor/Ch06MovementSetupTool.cs"
git add "Assets/Mahmoud SandBox/PhysicsCharacter/Editor/Ch06MovementSetupTool.cs.meta"
git add "Assets/TopDownEngine/Demos/Colonel/MainGamePlayScene.unity"
git commit -m "Add Ch06 movement setup tool and apply to scene"
```

---

### Task 4: Play Mode Testing & Scene Save

No new files. Validate the full feature end-to-end in Play mode.

- [ ] **Step 1: Test idle / animation**

Press Play. Look at `Ch06_nonPBR Root` in the scene view.

Expected: Character stands upright, idle animation plays.

If character is in T-pose: Animator controller not assigned — stop Play, re-run the setup tool, check for the `[Ch06Setup] Assigned controller` log line.

- [ ] **Step 2: Test WASD movement**

Hold **W** — character moves forward, walk animation blends in.
Hold **A** or **D** — character rotates to face that direction.
Release all keys — character stops, animation returns to idle.

If character doesn't move: Check `PuppetMoverSimple` is on `Ch06_nonPBR` and `CharacterController` is present. Check there are no error logs.

- [ ] **Step 3: Test ragdoll trigger**

Stop Play. Add a cube to the scene: `GameObject → 3D Object → Cube`. Add a `Rigidbody` component to it, set `Mass = 30`. Position it directly beside the character. Press Play, then in the Inspector push the cube's Rigidbody `velocity` to `(10, 0, 0)` to slam it into the character.

Expected: Character ragdolls (goes limp, falls) when the cube hits hard enough.

If ragdoll never triggers: Stop Play, select the `PuppetMaster` GameObject, find `PuppetRagdollController`, and lower `Knockdown Threshold` from `20` to `5`. Re-test.

- [ ] **Step 4: Test recovery**

After the character ragdolls, stop pressing any keys. Wait for the body to come to rest.

Expected: After ~1.5 seconds of stillness, `pinWeight` smoothly increases over ~1.2 seconds and the character returns to a standing pose. WASD works again afterward.

If character never gets up: Check Console for coroutine errors in `PuppetRagdollController`. Verify `Muscle Bodies` array on `PuppetRagdollController` is non-empty in the Inspector.

Note on recovery position: During ragdoll the animated target stays frozen in place, and the physics puppet gets "pulled back" toward it when pinWeight is restored. This is expected v1 behavior — the puppet acts like it's being pulled back to its feet. A future improvement would teleport the animated target to the ragdoll hip position before restoring, but that is out of scope here.

- [ ] **Step 5: Save scene and commit**

Stop Play mode. `File → Save` (Ctrl+S).

```bash
git add "Assets/TopDownEngine/Demos/Colonel/MainGamePlayScene.unity"
git commit -m "Save scene after Ch06 movement and ragdoll validation"
```
