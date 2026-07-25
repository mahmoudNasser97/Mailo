# ThirdPerson Character Setup Tool — Design Spec
**Date:** 2026-07-26
**Status:** Approved

## Overview

A Unity Editor tool that takes any humanoid model (Animation Type: Humanoid) already placed in the scene and automatically builds the full `ThirdPersonPuppet (1)` character hierarchy around it — ragdoll physics, PuppetMaster, all gameplay scripts, camera, and Animator Controller — wired and ready to play in one click.

---

## Tool Entry Point

**Menu:** `Tools → Mahmoud SandBox → Setup ThirdPerson Character`

**Window fields:**
- Model field (auto-populated from scene selection)
- "Setup Character" button
- Progress log showing each step as it completes

**Input requirements:**
- A humanoid model GameObject in the scene
- Animation Type must be set to **Humanoid** in the model's import settings
- No bone naming requirements — all bone mapping done via Unity's `HumanBodyBones` enum

---

## Output Hierarchy

```
[ModelName]_Character          Root — Tag: Player, Layer: 0
│  CameraController
│
├── PuppetMaster               Tag: Player, Layer: 9
│   ├── LayerSetup
│   ├── PuppetMaster
│   └── [Physics Skeleton]     stripped bone copy — Rigidbody + CapsuleCollider + ConfigurableJoint per bone
│
├── Behaviours                 Tag: Untagged, Layer: 0
│   ├── Puppet (with Fall)     BehaviourPuppet
│   └── Fall                   BehaviourFall
│
├── Character Camera           Tag: MainCamera, Layer: 9 — GameObject DISABLED
│   Camera + AudioListener
│
└── Character Controller       Tag: Player, Layer: 8
    │  CharacterController (Unity built-in)
    │  PhysicsCharacterController
    │  GrappleController
    │  ObjectGrabController
    │  PlayerHitReaction
    │  PuppetMover
    │  PuppetRagdollController
    │
    └── Animation Controller   Tag: Player, Layer: 8
        │  Animator
        │  TopDownEngineAnimationController
        │
        └── [Input model]      moved here by the tool
```

---

## Setup Steps (executed in order)

### Step 1: Validate
- Selected GameObject has an `Animator` component
- Animator has a humanoid `Avatar` (`avatar.isHuman == true`)
- All required bones resolve via `Animator.GetBoneTransform(HumanBodyBones.*)`
- Log errors and abort if any validation fails

### Step 2: Build GameObject Hierarchy
Create all GameObjects with correct names, tags, and layers:
- `[ModelName]_Character` — Tag: Player, Layer: 0
- `PuppetMaster` child — Tag: Player, Layer: 9
- `Behaviours` child — Tag: Untagged, Layer: 0
  - `Puppet (with Fall)` child
  - `Fall` child
- `Character Camera` child — Tag: MainCamera, Layer: 9, **SetActive(false)**
- `Character Controller` child — Tag: Player, Layer: 8
- `Animation Controller` grandchild — Tag: Player, Layer: 8

### Step 3: Move Model
Re-parent the input model GameObject under `Animation Controller`.

### Step 4: Create Physics Skeleton
Duplicate the model's skeleton (bone transforms only — no SkinnedMeshRenderer, no MeshRenderer, no MeshFilter):
- Walk `Animator.GetBoneTransform(HumanBodyBones.Hips)` hierarchy recursively
- Clone transforms only into `PuppetMaster` child
- Preserve local position/rotation/scale of every bone

### Step 5: Configure Ragdoll on Physics Skeleton
For each of the 17 humanoid bones (using `HumanBodyBones`):

**CapsuleCollider** — auto-sized:
- Direction: axis pointing toward child bone
- Radius: 0.07 × character height estimate
- Height: distance from bone to its first child bone

**Rigidbody** — mass by segment:
| Bone | Mass (kg) |
|---|---|
| Hips | 15 |
| Spine / Chest | 10 |
| Head | 6 |
| UpperArm | 2 |
| LowerArm | 1 |
| Hand | 0.5 |
| UpperLeg | 5 |
| LowerLeg | 3 |
| Foot | 1 |

**ConfigurableJoint** — rotation limits by body part:
| Body Part | Angular X | Angular YZ | Joint Type |
|---|---|---|---|
| Spine | ±20° | ±10° | Soft |
| Head / Neck | ±40° | ±20° | Soft |
| Shoulder | ±80° | ±60° | Ball |
| Elbow | ±90° | 0° | Hinge |
| Hip | ±60° | ±40° | Ball |
| Knee | ±90° | 0° | Hinge |
| Ankle | ±30° | ±15° | Soft |

All joints use `ConfigurableJointMotion.Limited` on angular axes and `Locked` on linear axes.

### Step 6: Set Up PuppetMaster
Add `PuppetMaster` component to the PuppetMaster GameObject:
- `targetRoot` = Animation Controller child's Hips transform (animation skeleton)
- `state` = 0 (Alive)
- Build one `Muscle` entry per ragdoll bone:
  - `muscle.target` = matching animation skeleton bone transform
  - `muscle.rigidbody` = physics skeleton bone Rigidbody
  - `muscle.props.weight` = 1
  - `muscle.props.pinWeight` = 1
  - `muscle.props.muscleWeight` = 1
  - `muscle.props.muscleSpring` = 100
  - `muscle.props.muscleDamper` = 0

Add `LayerSetup` to PuppetMaster GameObject:
- `characterController` = Character Controller transform
- `characterControllerLayer` = 8
- `ragdollLayer` = 9

Add `BehaviourPuppet` to `Puppet (with Fall)` child.
Add `BehaviourFall` to `Fall` child.

### Step 7: Set Up Character Controller Scripts
Create a `HoldPoint` child GameObject under `Character Controller` at local position (0, 1.4, 0.5) — used by both `GrappleController._ropeOrigin` and `ObjectGrabController._holdPoint`.

Add to `Character Controller` GameObject:
- `CharacterController`: height=2, radius=0.5, center=(0,1,0), slopeLimit=45, stepOffset=0.3
- `PhysicsCharacterController`
- `GrappleController`
- `ObjectGrabController`
- `PlayerHitReaction`
- `PuppetMover`
- `PuppetRagdollController`

### Step 8: Set Up Animation Controller Scripts
- Duplicate `Assets/Mahmoud SandBox/Models/ThirdPersonPuppet (1).prefab`'s Animator Controller asset
- Save copy to `Assets/Mahmoud SandBox/Characters/[ModelName]_AnimatorController.controller`
- Add `Animator` to Animation Controller GameObject:
  - `runtimeAnimatorController` = duplicated controller
  - `avatar` = input model's humanoid avatar
  - `applyRootMotion` = true
  - `animatePhysics` = true
- Add `TopDownEngineAnimationController`

### Step 9: Set Up Character Camera
Add to `Character Camera` GameObject:
- `Camera` component (default settings)
- `AudioListener` component
- Confirm GameObject is disabled

### Step 10: Wire All Cross-References

| Script | Field | Target |
|---|---|---|
| `CameraController` | `_camera` | Character Camera's `Camera` |
| `CameraController` | `_followTarget` | Character Controller `Transform` |
| `CameraController` | `_sensitivity` | 3 |
| `CameraController` | `_pitchMin/_pitchMax` | -30 / 60 |
| `CameraController` | `_normalOffset` | (0.81, 1.38, 0.21) |
| `CameraController` | `_normalDistance` | 4.3 |
| `CameraController` | `_normalFOV` | 37.8 |
| `CameraController` | `_aimOffset` | (5, 2, 0) |
| `CameraController` | `_aimDistance` | 1.67 |
| `CameraController` | `_aimFOV` | 50 |
| `PhysicsCharacterController` | `characterAnimation` | `TopDownEngineAnimationController` |
| `GrappleController` | `_animator` | `Animator` on Animation Controller |
| `GrappleController` | `_ropeOrigin` | `HoldPoint` Transform |
| `ObjectGrabController` | `_animator` | `Animator` on Animation Controller |
| `ObjectGrabController` | `_holdPoint` | `HoldPoint` Transform |
| `PuppetMover` | PuppetMaster ref | `PuppetMaster` component |
| `PuppetRagdollController` | `pm` | `PuppetMaster` component |
| `PuppetRagdollController` | body Rigidbodies | all 15 ragdoll bone Rigidbodies |
| `TopDownEngineAnimationController` | `characterController` | `PhysicsCharacterController` |
| `PuppetMaster` | `targetRoot` | Animation Controller skeleton root |

### Step 11: Set Physics Layer Collision
Call at runtime from the tool (no manual Physics Settings editing required):

```csharp
// Player controller and ragdoll must not collide with each other
Physics.IgnoreLayerCollision(8, 9, true);
```

Layer 8 ↔ Layer 18 (Ground) collision is left **enabled** (default) — no call needed.

> Note: `Physics.IgnoreLayerCollision` modifies the project-level Physics Settings layer collision matrix and persists between play sessions.

---

## File Location

`Assets/Mahmoud SandBox/PhysicsCharacter/Editor/ThirdPersonCharacterSetupTool.cs`

---

## Post-Setup Manual Steps (documented in tool log)

After the tool completes, the log reminds the user:
1. Adjust collider sizes if model proportions differ significantly
2. Assign missing animation clips in the duplicated Animator Controller
3. Verify `knockdownThreshold` in `PuppetRagdollController` (default: 200)

---

## Out of Scope

- Automatic animation clip assignment (clips vary per model)
- Prefab saving (scene setup only)
- Non-humanoid rigs
- Multi-player setup (one character per run)
