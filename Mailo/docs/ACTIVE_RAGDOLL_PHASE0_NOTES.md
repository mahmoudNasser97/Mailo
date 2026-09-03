# Active Ragdoll — Phase 0 build notes

Companion to `ACTIVE_RAGDOLL_SPEC.md`. Records the setup decisions and the project
settings that were **deliberately deferred** (not applied), so nothing is lost.

## Setup decisions (confirmed with user, 2026-08-12)

| §0 question | Answer |
|---|---|
| Unity version | 6000.2.7f2 (Unity 6.2) — verified |
| Render pipeline | URP — verified |
| Rigged humanoid present | Yes — wizard is generic, no model hardcoded |
| Platform | **Desktop** (target fixedTimestep 0.01) |
| Code placement | New `Assets/ActiveRagdoll/` in `ActiveRagdoll` namespace + asmdef |
| Global project settings | **Deferred** — documented here, not applied |
| Test rig | User supplies a model; wizard stays generic |

The existing `Assets/Mahmoud SandBox/ActiveRagdoll/` prototype (global namespace,
Assembly-CSharp) is untouched. The new system lives in its own assembly, so identical
class names (BalanceController, ConfigurableJointExtensions, …) do **not** collide.

## DEFERRED global project settings — apply deliberately, then A/B test

Phase 0 of the spec asks for these **project-wide** changes. They affect the ENTIRE
game's physics (PuppetMaster, Ragdoll Animator 2, TopDownEngine, vehicles), so they were
not applied automatically. Current values → spec target:

| Setting | Location | Current | Desktop target | Mobile/VR |
|---|---|---|---|---|
| Fixed Timestep | Project Settings ▸ Time | `0.02` | **`0.01`** | `0.02` |
| Default Solver Iterations | Project Settings ▸ Physics | `6` | **`12`** | 8 |
| Default Solver Velocity Iterations | Project Settings ▸ Physics | `1` | **`4`** | 2 |

Dropping the timestep to 0.01 **doubles physics cost project-wide**. Apply, then play the
existing gameplay scenes to confirm nothing regressed before committing.

**Already applied per-bone by the setup wizard** (local, safe, no global impact):
`Rigidbody.solverIterations = 20`, `Rigidbody.maxAngularVelocity = 30` (default 7 is far
too low), interpolation on Hips only, adjacent/grandparent/hand↔thigh/forearm↔chest
self-collisions disabled.

## Heads-up for Phase 2 (not a Phase 0 issue)

Project **gravity is y = -30**, but the spec's capture-point math hardcodes `9.81`:

```csharp
float omega = Mathf.Sqrt(9.81f / comHeight);
```

When we build `BalanceController`, use `Mathf.Abs(Physics.gravity.y)` instead of `9.81`,
or the inverted-pendulum response will be mistuned by ~1.7×.

## Phase 0 acceptance test

1. `Tools ▸ Active Ragdoll ▸ Create Test Scene`.
2. Drag your rigged humanoid into the scene.
3. `Tools ▸ Active Ragdoll ▸ Setup Wizard` → assign the model → **Build Active Ragdoll**.
   Check the Console: it reports mapped bones (n/16), total mass, and any unmapped parts.
4. Press **Play**. All joint drives are zero.

**Pass:** the character collapses into a plain, non-exploding ragdoll and comes to rest.
No jitter, no interpenetrating/vibrating limbs, no launching.

If it isn't clean, fix it here — nothing downstream works otherwise (spec §Phase 0):
- Vibrating limbs → a self-collision pair was missed (check the wizard's ignore list).
- One joint whips → a bad mass ratio (check the Console mass report).
- Explodes → deep penetration; verify colliders aren't badly oversized/overlapping.

Test keys: `C` drop crate · `F` fire projectile · `T` slow-mo · `F1` tuning panel.

## Files created (all under `Assets/ActiveRagdoll/`)

```
ActiveRagdoll.asmdef                 runtime assembly (namespace ActiveRagdoll)
Runtime/RagdollProfile.cs            ScriptableObject — all §8 tuning
Runtime/RagdollBone.cs               BodyPart enum + per-bone data
Runtime/RagdollRig.cs                registry, mass/CoM/KE queries, self-collision setup
Debug/RagdollDebugDraw.cs            CoM + bone gizmos
Debug/RagdollTuningPanel.cs          F1 IMGUI panel (sliders + live readouts)
Debug/RagdollTestHarness.cs          crate / projectile / slow-mo keys
Editor/ActiveRagdoll.Editor.asmdef   editor assembly
Editor/RagdollSetupWizard.cs         builds the two-skeleton rig from any humanoid
Editor/RagdollTestSceneBuilder.cs    Tools ▸ Active Ragdoll ▸ Create Test Scene
```

Not built yet (Phase 1+): PoseMatcher, ConfigurableJointExtensions, BalanceController,
SupportPolygon, LegStepper, ImpactReceiver, PoiseController, RecoveryController,
GrabController.
