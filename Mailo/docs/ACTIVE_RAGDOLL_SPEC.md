# Active Ragdoll System — Build Specification

> **How to use this file:** Drop it in your Unity project root as `ACTIVE_RAGDOLL_SPEC.md`, then start Claude Code in that directory and say:
> *"Read ACTIVE_RAGDOLL_SPEC.md. Build Phase 0 and Phase 1 only, then stop and tell me what to test in the editor."*
>
> Do **not** ask it to build everything at once. Each phase has an acceptance test you must run in the Unity editor before moving on. Claude Code cannot run Unity — you are the test harness.

---

## 0. Instructions to the implementing agent

You are building a self-balancing active ragdoll for Unity. Read this entire document before writing any code.

**Working rules:**

1. **Build one phase at a time.** After each phase, stop, summarise what you wrote, and state the exact acceptance test the user must run in the editor. Wait for their result before continuing.
2. **Every phase must be independently testable.** No phase may depend on an unbuilt later phase.
3. **All physics work happens in `FixedUpdate`.** Never write to a physical bone's `transform` directly at runtime — only ever set joint targets or apply forces. This rule is the whole architecture; violating it anywhere silently destroys the physical response.
4. **Ship debug visualisation with every phase.** Gizmos for centre of mass, support polygon, capture point, step targets, and impact points. The user cannot debug this system by reading numbers.
5. **Expose every tuning value** as a serialised field on a ScriptableObject profile, not as a constant. Include a runtime IMGUI panel (toggle with F1) with sliders for the live values listed in §8.
6. **Ask before assuming** the rig's bone naming, Unity version, or whether an existing character/animator is present. Do not scaffold a humanoid rig from scratch if the project already has one.

**Confirm with the user before starting:** Unity version, render pipeline, whether they have a rigged humanoid + locomotion clips already, and whether the target is desktop or mobile/VR (this changes the physics budget significantly).

---

## 1. Architecture

Two skeletons, one character.

```
Character
├── AnimatedRig          (kinematic; Animator + clips; no colliders; invisible)
│   └── Hips → Spine → ... → bones
├── PhysicalRig          (Rigidbody + ConfigurableJoint per bone + colliders)
│   └── Hips → Spine → ... → bones          ← SkinnedMeshRenderer binds here
└── Systems
    ├── RagdollProfile         (ScriptableObject: all tuning)
    ├── PoseMatcher            Phase 1
    ├── BalanceController      Phase 2
    ├── LegStepper (×2)        Phase 3
    ├── ImpactReceiver (×N)    Phase 4
    ├── PoiseController        Phase 4
    └── RecoveryController     Phase 5
```

The animated rig is the **target**. The physical rig **chases** it via joint drives. Everything in this system is either (a) generating a better target, or (b) modulating how hard the physical rig chases it.

The single scalar that ties it together is **poise** (0–1). Poise multiplies all drive strengths. Poise 1 = crisp and controlled. Poise 0 = limp ragdoll. Stagger, knockdown, fatigue, and death are all just poise values, not separate systems. Build it this way and you will not need a state machine for reactions.

### Joint technology decision

Use **`ConfigurableJoint`**, not `ArticulationBody`.

`ArticulationBody` is the better solver in isolation — reduced coordinates, no joint separation, far more forgiving of mass ratios. But it does **not** accept standard Unity joints attached to it, which kills runtime `FixedJoint` creation. Since grabbing, carrying, and object interaction are in scope (§Phase 5), `ConfigurableJoint` is the correct trade. Note this decision in a comment at the top of `PoseMatcher.cs` so it isn't silently reversed later.

---

## 2. File manifest

Create under `Assets/ActiveRagdoll/`:

```
Runtime/
  RagdollProfile.cs            ScriptableObject — all tuning values
  RagdollBone.cs               Per-bone data: part enum, joint, rigidbody, target transform
  RagdollRig.cs                Bone registry, total mass, CoM, kinetic energy queries
  PoseMatcher.cs               Phase 1 — drives physical rig toward animated rig
  ConfigurableJointExtensions.cs   Phase 1 — joint-space rotation conversion
  BalanceController.cs         Phase 2 — CoM, support polygon, capture point, hip/ankle strategy
  SupportPolygon.cs            Phase 2 — foot contact → convex hull → containment test
  LegStepper.cs                Phase 3 — per-leg swing/stance state machine + two-bone IK
  ImpactReceiver.cs            Phase 4 — per-bone collision listener
  PoiseController.cs           Phase 4 — poise pool, tiers, drive ramping
  RecoveryController.cs        Phase 5 — settle detection, get-up
  GrabController.cs            Phase 5 — runtime FixedJoint grab/carry
Editor/
  RagdollSetupWizard.cs        Duplicates animated rig → builds physical rig, joints, colliders
Debug/
  RagdollDebugDraw.cs          Gizmos
  RagdollTuningPanel.cs        Runtime IMGUI sliders (F1)
```

---

## Phase 0 — Scaffolding and test scene

**Build:**
- `RagdollProfile`, `RagdollBone`, `RagdollRig` with a `BodyPart` enum (`Head, Chest, Spine, Hips, UpperArmL/R, LowerArmL/R, HandL/R, ThighL/R, ShinL/R, FootL/R`).
- `RagdollSetupWizard`: an editor window that takes an animated humanoid root, duplicates it, strips the Animator, adds `Rigidbody` + capsule/box colliders + `ConfigurableJoint` per bone with sane defaults, and wires up `RagdollRig`.
- A test scene: flat ground, character, a few spawnable crates and a projectile launcher bound to a key.

**Mass distribution** — total 70 kg, do not leave bones at 1 kg:

| Part | kg | | Part | kg |
|---|---|---|---|---|
| Head | 5 | | Thigh (each) | 8 |
| Chest | 18 | | Shin (each) | 4 |
| Spine | 8 | | Foot (each) | 1.5 |
| Hips | 10 | | Upper arm (each) | 2.5 |
| | | | Lower arm (each) | 1.5 |
| | | | Hand (each) | 0.5 |

**Project settings to apply:**
```
Time.fixedDeltaTime            = 0.01   (0.02 acceptable on mobile)
Physics.defaultSolverIterations = 12
Physics.defaultSolverVelocityIterations = 4
per-bone Rigidbody.solverIterations = 20
per-bone Rigidbody.maxAngularVelocity = 30      // default 7 is far too low
interpolation: Hips only
collision: disable adjacent-bone pairs, plus hand↔thigh and forearm↔chest
```

**Acceptance test:** Press play with all joint drives at zero. The character collapses into a plain, non-exploding ragdoll and comes to rest. No jitter, no limbs interpenetrating and vibrating, no launching. **If this isn't clean, nothing downstream will work — fix it here.**

---

## Phase 1 — Pose matching

**Build:** `PoseMatcher` + `ConfigurableJointExtensions`.

Each `FixedUpdate`, for every bone: read the corresponding animated bone's `localRotation`, convert to joint space, write to `joint.targetRotation`.

**The critical detail:** `ConfigurableJoint.targetRotation` is expressed in **joint space**, not local space, and joint space depends on the joint's `axis` and `secondaryAxis`. Cache each joint's `transform.localRotation` at setup time as `startLocalRotation`. Implement the conversion as:

```csharp
public static void SetTargetRotationLocal(this ConfigurableJoint joint,
                                          Quaternion targetLocalRotation,
                                          Quaternion startLocalRotation)
{
    if (joint.configuredInWorldSpace)
        Debug.LogError("SetTargetRotationLocal requires joint to be configured in local space.");

    Vector3 right   = joint.axis;
    Vector3 forward = Vector3.Cross(joint.axis, joint.secondaryAxis).normalized;
    Vector3 up      = Vector3.Cross(forward, right).normalized;
    Quaternion worldToJointSpace = Quaternion.LookRotation(forward, up);

    Quaternion resultRotation = Quaternion.Inverse(worldToJointSpace)
                              * Quaternion.Inverse(targetLocalRotation)
                              * startLocalRotation
                              * worldToJointSpace;

    joint.targetRotation = resultRotation;
}
```

Do not attempt a simpler version. Getting this subtly wrong produces a character that *almost* poses correctly, and you will waste days blaming the balance controller.

**Drive configuration:** `rotationDriveMode = Slerp`, then set `slerpDrive` with `positionSpring`, `positionDamper`, `maximumForce`. Stiffness is **non-uniform** — a single global gain is the most common cause of a rig that looks like a mannequin on strings:

| Group | Spring | Damper |
|---|---|---|
| Hips / Chest | 2000 | 150 |
| Spine / Neck | 1200 | 100 |
| Thighs / Shins | 3000 | 200 |
| Feet | 1500 | 100 |
| Arms | 500 | 50 |

All springs are multiplied by `poise` (fixed at 1.0 for now).

**Acceptance test:** Suspend the character in the air (temporarily set Hips to kinematic, or hang it from a joint). Play an idle or walk clip on the animated rig. The physical rig should follow the animation recognisably, with slight lag and overshoot. Poke a limb in play mode with a crate — it should deflect and spring back. Then unfreeze the hips and let it stand on the ground: **it will fall over.** That is correct and expected. Pose matching alone never balances.

---

## Phase 2 — Balance

**Build:** `BalanceController`, `SupportPolygon`.

Each `FixedUpdate`:

1. **Centre of mass** — mass-weighted average over all bone rigidbodies. Also track its velocity (finite difference, smoothed).
2. **Support polygon** — collect current foot contact points from `OnCollisionStay` on both feet, build the 2D convex hull on the XZ plane. Empty polygon = airborne.
3. **Capture point** — where a foot must land to bring the body to rest:

```csharp
float omega = Mathf.Sqrt(9.81f / comHeight);
Vector2 capturePoint = comXZ + comVelocityXZ / omega;
```

This single equation is the core of the whole system. It falls out of the linear inverted pendulum model, so it responds correctly to shoves without hand-tuning.

4. **Response, layered:**
   - **Ankle strategy** (capture point well inside polygon): torque at the ankles to shift centre of pressure. Small corrections only.
   - **Hip strategy** (capture point near the polygon edge): PD controller on pelvis upright error, applied to the Hips rigidbody with `ForceMode.Acceleration` so gains are mass-independent. Most of your balancing happens here.
   - **Stepping** (capture point outside polygon): raise a flag for Phase 3. In Phase 2, just log it.

5. **Gravity compensation** — apply a small feed-forward upward force on the torso equal to a fraction (0.3–0.6) of the upper-body weight. This lets you run much lower PD gains, which is what makes the character look loose rather than rigid. It is invisible to players and every working implementation does it.

**Gizmos required:** CoM (sphere), CoM velocity (ray), support polygon (outline on ground), capture point (coloured marker — green inside polygon, red outside).

**Acceptance test:** Character stands with feet fixed in a stance, no stepping. Shove it with a crate. It should lean, resist, and return upright for small pushes; for large pushes the capture point marker should visibly exit the support polygon and the step flag should log. It will still fall over on big shoves — correct for now.

---

## Phase 3 — Stepping

**Build:** `LegStepper` (one per leg), coordinated by `BalanceController`.

Per-leg state machine: `Stance` / `Swing`.

- **Trigger:** capture point outside support polygon, **or** foot has drifted beyond `maxStanceOffset` from its desired stance position.
- **Target:** spherecast down at the capture point, clamped to the leg's reachable range and to acceptable slope. Add a small outward bias so the legs don't cross.
- **Trajectory:** parabolic or Bézier arc over `stepDuration` (0.25–0.4s), eased in/out.
- **Solve:** two-bone analytic IK (hip + knee) toward the current point on the swing trajectory. Feed the resulting rotations **into the animated rig's leg bones**, so they flow through `PoseMatcher` as joint targets. Never write physical leg transforms — this is what preserves reactivity mid-step.
- **Constraints:** minimum stance time (~0.15s), never both legs in swing simultaneously, alternate preference, force a step if stance time exceeds a ceiling while unbalanced.

Arms: leave on weak drives with light target matching. Reactive flailing during disturbance comes for free and sells the whole effect.

**Gizmos required:** step target, swing arc, per-leg state label.

**Acceptance test:** Shove the character from every direction with crates of varying mass. It should take recovery steps toward the shove direction and stay upright for moderate impacts. Push it hard enough and it should fail — but it should fail by running out of steps, not by snapping or exploding.

**You now have a self-balancing active ragdoll.** Everything after this is reactions.

---

## Phase 4 — Impact and knockdown

**Build:** `ImpactReceiver` (on every bone with a collider), `PoiseController`.

### 4a. Read the impulse

```csharp
void OnCollisionEnter(Collision c)
{
    Vector3 J = c.impulse;
    if (Vector3.Dot(J, c.GetContact(0).normal) < 0f) J = -J;   // normalise sign

    float severity = J.magnitude;
    if (severity < profile.ignoreThreshold) return;             // brushing past a crate
    if (Time.time - lastImpactTime < 0.1f) return;              // per-bone cooldown

    severity = Mathf.Min(severity, profile.maxImpulse);         // anti-tunnelling clamp
    lastImpactTime = Time.time;

    poise.RegisterImpact(new Impact {
        point    = c.GetContact(0).point,
        impulse  = J,
        body     = rb,
        bodyPart = part,
        severity = severity
    });
}
```

Use `c.impulse` — **do not** compute force from `relativeVelocity` yourself. `c.impulse` already accounts for both masses and the solver's actual resolution, so a heavy slow crate and a light fast ball scale correctly against each other with no extra work.

### 4b. Convert impulse into rotational disruption

This is what makes a takedown read as a takedown rather than a shove:

```csharp
Vector3 r = impact.point - rig.CenterOfMass;
Vector3 angularImpulse = Vector3.Cross(r, impact.impulse);
float   spinFactor     = angularImpulse.magnitude / rig.TotalMass;
```

A low sweep at the shins produces a huge `spinFactor` about a horizontal axis → the body rotates backward over the contact point → it lands on its back. A chest-height hit through the CoM produces mostly linear disruption → stagger backward, maybe recover. **The difference between "shoved" and "taken down" falls out of geometry, not a lookup table.** Feed both linear and angular components into `BalanceController`'s error term.

### 4c. Poise pool

```csharp
poise -= severity * partMultiplier[part] * (1f + spinFactor * profile.spinWeight)
       / profile.poiseCapacity;
poise = Mathf.Clamp01(poise);

if (isBalanced && !isDown)
    poise = Mathf.MoveTowards(poise, 1f, profile.regenRate * Time.fixedDeltaTime);
```

`partMultiplier`: Head 2.0, Chest 1.0, Spine 1.0, Hips 1.2, Thigh/Shin 1.4 (legs matter disproportionately for takedowns), Foot 1.0, Arms 0.4.

### 4d. Tiers

| Poise | Response |
|---|---|
| 0.90–1.00 | **Absorb** — brief drive dip on the hit limb only; balance unaffected |
| 0.50–0.90 | **Flinch** — hit limb soft for ~0.2s, torso drive to 0.8, arms react |
| 0.15–0.50 | **Stagger** — aggressive stepping toward the capture point, global gains ~0.6 |
| 0.00–0.15 | **Knockdown** — global drives → ~0.05, character goes limp |

These are **not separate code paths.** They are the same PD controller at different gains plus different step-trigger thresholds. Implement them as a curve/lookup over poise, not as a switch statement. This is what keeps the system maintainable.

### 4e. The knockdown

1. **Ramp drives down asymmetrically over ~0.15s**, not instantly: legs first, then torso, arms last. Instant zeroing looks like a puppet with cut strings; the ramp looks like a body failing, and arms staying live longest gives you the involuntary arm-out-to-catch-yourself motion for free.
2. **Apply residual impulse** at the struck bone with `ForceMode.Impulse`, plus a smaller distributed share (~30%) to its neighbours, so the whole body carries momentum instead of one shin rocketing away.
3. **Do not disable `BalanceController`.** Just let poise sit near zero. It recovers on its own.

Ground contact needs no special handling — that's the payoff of a fully physical character. It tumbles, lands on whatever geometry is there, slides down ramps, drapes over barrels. Nothing scripted.

**Acceptance test:** Launch crates of varying mass at head, chest, and shin height. Shin hits should produce backward rotation and a back landing. Chest hits should produce stagger or a backward fall. Light taps should be absorbed with a visible flinch. Nothing should launch into orbit — if it does, lower `maxImpulse` and enable continuous collision detection on the projectiles (not on the character bones).

---

## Phase 5 — Recovery and object interaction

### 5a. Get up

**Build:** `RecoveryController`.

Poll for rest, not for elapsed time:

```csharp
bool IsSettled =>
    rig.TotalKineticEnergy < profile.restEnergyThreshold &&
    restTimer > 0.4f &&
    torsoGroundContacts > 0;
```

Then determine facing via `Vector3.Dot(chest.transform.up, Vector3.up)` → prone vs supine, play the matching get-up clip on the **animated rig**, and ramp poise from 0 → 1 over 0.6–1.2s.

Because the physical rig chases the animation as gains rise, the get-up is itself fully simulated: drop another crate on the character mid-recovery and poise drains again and it collapses back down, with no re-entrancy bug — there is no state to be in.

### 5b. Pushing objects back

Mostly free, given correct masses from Phase 0. Two additions:

- **Friction materials:** high friction on feet, low on torso and limbs, so a downed body slides rather than grips.
- **Foot planting:** during stance, raise foot drive strength and apply a downward bias force so the character has something to push against.

### 5c. Grab and carry

**Build:** `GrabController`. On grab input with a hand overlapping a grabbable rigidbody, create a runtime `FixedJoint` on the hand connected to that object, with a `breakForce` and `breakTorque`. Listen for `OnJointBreak` to release.

That one component gives you grab, carry, two-handed hold, and "it got yanked out of my hands" during a takedown — for free, because the joint breaks under the same impulses that drain poise.

**Acceptance test:** Character picks up a crate, carries it (balance should visibly shift — the crate's mass is now part of the system), gets hit, drops it, falls, settles, and stands back up unaided.

---

## 6. Debug requirements (build alongside every phase, not at the end)

- **Gizmos:** CoM + velocity, support polygon, capture point (green/red), step targets and swing arcs, impact points with impulse vectors (persisting ~1s), per-bone drive strength as colour tint.
- **Runtime panel (F1):** live poise readout, current tier, per-group drive multipliers, capture-point distance from polygon, and sliders for every value in §8.
- **Slow-mo key** bound to `Time.timeScale = 0.15f`. You cannot evaluate a knockdown at full speed.

---

## 7. Known failure modes

| Symptom | Cause | Fix |
|---|---|---|
| Character explodes on contact | Deep penetration → enormous `c.impulse` | Clamp `severity` to `maxImpulse`; CCD on projectiles |
| Limbs vibrate during collapse | Self-collision as drives reach zero | Ignore hand↔thigh, forearm↔chest, adjacent bones |
| Follows animation but "almost" wrong | `SetTargetRotationLocal` implemented incorrectly | Use the exact function in Phase 1 |
| Looks like a mannequin on strings | Uniform drive stiffness | Non-uniform per-group gains; lower globally + gravity compensation |
| Can't push anything | Bones left at 1 kg default | Apply the Phase 0 mass table |
| Syrupy, slow limb motion | `maxAngularVelocity` at the 7 rad/s default | Raise to 30 |
| Jitter everywhere | fixedDeltaTime too large or solver iterations too low | 0.01 and 12/20 |
| One joint dominates and whips | Mass ratio between connected bodies > 10:1 | Rebalance masses |

---

## 8. Tuning values to expose (RagdollProfile)

```
[Drives]        springHips, springSpine, springLegs, springFeet, springArms,
                damperScale, maxForce
[Balance]       hipKp, hipKd, ankleKp, gravityCompensation (0–1),
                comVelocitySmoothing
[Stepping]      stepDuration, stepHeight, maxStanceOffset, minStanceTime,
                maxStanceTime, stepOutwardBias, maxStepDistance
[Impact]        ignoreThreshold, maxImpulse, poiseCapacity, spinWeight,
                regenRate, impactCooldown, partMultipliers[]
[Tiers]         flinchThreshold, staggerThreshold, knockdownThreshold,
                driveRampDuration, legRampDelay, armRampDelay
[Recovery]      restEnergyThreshold, restDuration, getUpRampDuration
[Grab]          grabBreakForce, grabBreakTorque, grabRadius
```

---

## 9. Build order — do not deviate

```
Phase 0  →  clean passive ragdoll, no jitter          ← hardest to skip, most costly to skip
Phase 1  →  pose matching in the air
Phase 2  →  balance on fixed stance
Phase 3  →  dynamic stepping                          ← "it works" moment
Phase 4  →  impact → poise → knockdown
Phase 5  →  get-up + grab
```

For Phase 4 specifically: implement a **binary** knockdown first (drives snap to zero), confirm the collapse and tumble look right, and only then add the intermediate tiers and asymmetric ramping. The middle tiers are the enjoyable tuning work but they are meaningless until the collapse itself feels good.

Trying to debug balance, stepping, and impact simultaneously is where these projects die.
