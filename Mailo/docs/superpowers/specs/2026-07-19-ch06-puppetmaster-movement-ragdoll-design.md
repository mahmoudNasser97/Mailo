# Ch06_nonPBR Root — PuppetMaster Movement & Ragdoll Design

**Date:** 2026-07-19
**Scene:** `Assets/TopDownEngine/Demos/Colonel/MainGamePlayScene.unity`
**Character:** `Ch06_nonPBR Root` (active, fileID 123005356)

---

## Goal

Make the Ch06_nonPBR character:
1. Move and rotate with WASD keyboard input
2. Play walk/idle animations from the existing `CharacterAnimaiton.controller`
3. Go full ragdoll when struck hard enough by an object (e.g. a cube), then automatically stand back up

---

## Current State

PuppetMaster is **already set up** on `Ch06_nonPBR Root`:

```
Ch06_nonPBR Root  (empty parent, layer 0)
├── Ch06_nonPBR          (animated target, layer 8 = "Character")
│     ├── Animator        — has Avatar, NO controller assigned, applyRootMotion=true (wrong)
│     └── [bone hierarchy with mixamorig9: prefix]
└── PuppetMaster          (physics puppet root, layer 9 = "Ragdoll")
      ├── PuppetMaster.cs — mode=Active, pinWeight=1, muscleWeight=1, muscleSpring=100
      └── [~14 physics bones with ConfigurableJoints + Rigidbodies]
```

**Missing:**
- No Animator controller assigned on `Ch06_nonPBR`
- `applyRootMotion = true` (must be false for PuppetMover)
- `updateMode = Normal` (must be AnimatePhysics for PuppetMaster sync)
- No `CharacterController` on `Ch06_nonPBR`
- No `PuppetMoverSimple` on `Ch06_nonPBR`
- No impact detection or ragdoll trigger logic

---

## Architecture

### Component Layout (after setup)

```
Ch06_nonPBR Root
├── Ch06_nonPBR          (animated target, layer 8)
│     ├── Animator        — controller=CharacterAnimaiton, applyRootMotion=false, updateMode=AnimatePhysics
│     ├── CharacterController  — height=1.8, radius=0.3, center=(0,0.9,0)
│     └── PuppetMoverSimple    — WASD input → CharacterController + Animator params
└── PuppetMaster          (physics puppet, layer 9)
      ├── PuppetMaster.cs       — unchanged
      ├── PuppetRagdollController.cs  — receives impact reports, manages pinWeight
      └── [each muscle bone also gets PuppetImpactReporter.cs]
```

---

## New Files

### 1. `Assets/Mahmoud SandBox/PhysicsCharacter/PuppetImpactReporter.cs`

**Purpose:** Lightweight collision listener on each physics muscle bone.

- `[RequireComponent(typeof(Rigidbody))]`
- Field: `PuppetRagdollController controller` (set by setup tool)
- `OnCollisionEnter(Collision col)` → calls `controller.ReportImpact(col.impulse.magnitude)`

### 2. `Assets/Mahmoud SandBox/PhysicsCharacter/PuppetRagdollController.cs`

**Purpose:** Central ragdoll state machine on the PuppetMaster root.

**States:** `Balanced` → `Ragdoll` → `GettingUp` → `Balanced`

**Fields (Inspector-tunable):**
```
[Header("Ragdoll")]
float knockdownThreshold = 20f      // impulse (N·s) needed to trigger ragdoll
float settleDelay        = 1.5f     // seconds body must be still before get-up
float settleSpeedThreshold = 0.4f   // max muscle speed (m/s) to count as "still"
float maxSettleWait      = 6f       // give up waiting after this many seconds
float pinRestoreTime     = 1.2f     // seconds to lerp pinWeight back to 1

[Header("References")]
PuppetMaster      pm            // auto-found on same GameObject
PuppetMoverSimple mover         // reference to animated target's mover — disabled during ragdoll
Rigidbody[]       muscleBodies  // auto-collected via GetComponentsInChildren<Rigidbody>()
```

**Logic:**
- `ReportImpact(float impulse)`: if `Balanced` and impulse > threshold → `StartCoroutine(RagdollSequence())`
- `RagdollSequence()`:
  1. State = Ragdoll, disable `mover` (stops CharacterController so puppet isn't dragged), set `pm.pinWeight = 0` instantly
  2. Wait until all `muscleBodies` slow below `settleSpeedThreshold` for `settleDelay` seconds (or `maxSettleWait` elapses)
  3. `StartCoroutine(GetUpSequence())`
- `GetUpSequence()`:
  1. State = GettingUp
  2. Lerp `pm.pinWeight` from 0 → 1 over `pinRestoreTime` seconds
  3. Re-enable `mover`, State = Balanced

### 3. `Assets/Mahmoud SandBox/PhysicsCharacter/Editor/Ch06MovementSetupTool.cs`

**Purpose:** One-click editor tool that wires everything up.

**Menu:** `Tools → PuppetMaster Character → Setup Ch06 Movement`

**Steps (in order):**
1. Find `Ch06_nonPBR Root` in active scene — error if not found
2. Find `Ch06_nonPBR` child (the animated target by name) — error if not found
3. Find `PuppetMaster` component under root — error if not found
4. **Fix Animator** on animated target:
   - Find or add `Animator`
   - Auto-assign `CharacterAnimaiton.controller` from AssetDatabase
   - Set `applyRootMotion = false`
   - Set `updateMode = AnimatorUpdateMode.AnimatePhysics`
   - Set `cullingMode = AlwaysAnimate`
5. **Add CharacterController** to animated target (if not present):
   - height=1.8, radius=0.3, center=(0, 0.9, 0)
6. **Add PuppetMoverSimple** to animated target (if not present)
7. **Add PuppetRagdollController** to PuppetMaster root (if not present):
   - Auto-find `PuppetMaster` component and assign to `pm` field
   - Assign `PuppetMoverSimple` from animated target to `mover` field
8. **Add PuppetImpactReporter** to each `Rigidbody` found via `GetComponentsInChildren<Rigidbody>()` on PuppetMaster root:
   - Assign the `PuppetRagdollController` reference on each reporter
   - Auto-populate `PuppetRagdollController.muscleBodies` array
9. Mark scene dirty and log completion summary

**Remove menu:** `Tools → PuppetMaster Character → Remove Ch06 Movement Setup`
- Removes `CharacterController`, `PuppetMoverSimple`, `PuppetRagdollController`, all `PuppetImpactReporter` components
- Does NOT remove PuppetMaster (that was set up by a different tool)

---

## Reused Files (no changes)

| File | Role |
|------|------|
| `PuppetMoverSimple.cs` | WASD + gravity + Animator parameter drive |
| `CharacterAnimaiton.controller` | Idle/Walk state machine, Speed/MoveX/MoveY params |
| `PuppetMasterCharacterSetupTool.cs` | Not re-run; PuppetMaster already set up |

---

## Prerequisites (user must do before running tool)

1. **Ch06_nonPBR.fbx import settings → Rig → Animation Type = Humanoid** (already set, avatar confirmed)
2. `CharacterAnimaiton.controller` must exist at `Assets/CharacterAnimaiton.controller` (already present)
3. `Ch06_nonPBR Root` must be the **active** object in the scene (already confirmed)

---

## Testing Checklist

1. Press Play — character stands idle, animation plays
2. WASD — character moves and rotates, walk animation blends in
3. Release keys — character stops, idle animation returns
4. Push a heavy physics cube into the character — character ragdolls
5. Body settles → character smoothly stands back up, animation resumes
6. F5 (or re-run) — ragdoll can trigger again after recovery

---

## Tuning Guide

| Problem | Parameter | Adjustment |
|---------|-----------|------------|
| Too easy to knock down | `knockdownThreshold` | Increase (default 20) |
| Takes too long to get up | `pinRestoreTime` | Decrease (default 1.2s) |
| Stays down too long | `maxSettleWait` | Decrease (default 6s) |
| Character too stiff while ragdolled | `pm.muscleSpring` | Decrease (default 100) |
| Character slides instead of walks | `PuppetMoverSimple.moveSpeed` | Adjust (default 4) |
