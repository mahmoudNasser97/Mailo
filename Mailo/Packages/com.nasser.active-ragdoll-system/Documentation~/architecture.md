# Nasser Active Ragdoll System — architecture notes

No PuppetMaster, no FinalIK, no paid assets. Built-in Unity physics only.
Tested shape: Unity 2022 LTS and Unity 6 (the `RbCompat` helper covers the
`velocity` → `linearVelocity` rename).

---

## 1. The one idea

You never simulate "a ragdoll that walks". You run **two skeletons**:

```
Player (empty root)
├── Controller           Rigidbody capsule. Owns locomotion + world position.
│                        Never falls over. Hovers on a raycast spring.
│   └── HandTarget_L / HandTarget_R   (parented under the camera in practice)
├── Character            PHYSICAL rig. SkinnedMeshRenderer + colliders +
│                        Rigidbodies + ConfigurableJoints. This is what you see.
├── Character_Puppet     ANIMATED rig. Clone of Character with everything
│                        stripped except the Animator. Invisible. No physics.
└── Camera               Follows the head bone of the PHYSICAL rig.
```

Every `FixedUpdate`, each physical joint reads its twin's local rotation off the
puppet and pushes it in as `targetRotation`. Muscle strength scales the drive
springs. Strength 1 = animated. Strength 0 = corpse. Everything else in the
system is a reason to move that one number.

---

## 2. Build order

Do these in order. Each step should be playable before you move on.

| # | Step | You should see |
|---|------|----------------|
| 1 | Import humanoid FBX, Rig → Humanoid. | Nothing yet. |
| 2 | `GameObject → 3D Object → Ragdoll…`, fill in bones. | Falls in a heap on Play. |
| 3 | `Tools → Nasser Active Ragdoll System → Character Setup` does steps 2–10 for you. | A built character. |
| 4 | *(the rows below describe what the wizard builds, for when you need to debug it)* | — |
| 5 | Add `ActiveRagdollMuscles`, assign both roots. | Ragdoll stands up in its animated pose but still tips over. |
| 6 | Add the Controller capsule + `FloatingCapsuleController`. | Capsule hovers and walks. Ragdoll left behind. |
| 7 | Add `PelvisAnchor`. | Character walks, wobbles, recovers. **This is the milestone.** |
| 8 | Add `CharacterBody`. | Hit it with a crate → it goes down and gets up. |
| 9 | Add `PhysicsHand` + hand targets. | Physics arms, breakable grabs. |
| 10 | Add `FirstPersonRagdollCamera` and `TurbulenceField`. | Cabin chaos. |

Step 7 is where it either feels good or doesn't. Budget most of your tuning
time there, not on steps 8–10.

---

## 3. Mass and joint tables

Physics stability depends far more on **mass ratios** than on absolute values.
Never let a parent-child mass ratio exceed ~10:1 or the solver will fight you.

| Bone | Mass (kg) | Collider |
|------|-----------|----------|
| Pelvis / Hips | 12 | Capsule |
| Spine / Chest | 15 | Box |
| Head | 5 | Sphere |
| Upper arm | 2.5 | Capsule |
| Lower arm | 1.5 | Capsule |
| Hand | 0.6 | Box |
| Thigh | 7 | Capsule |
| Shin | 4 | Capsule |
| Foot | 1.2 | Box |

Total ≈ 66 kg for a 1.8 m character. **Controller capsule: 40 kg** — heavy
enough that the flailing ragdoll doesn't drag it around.

Joint angular limits (`lowAngularXLimit` / `high` / `angularYLimit` / `angularZLimit`):

| Joint | X twist | Y swing | Z swing |
|-------|---------|---------|---------|
| Spine | −20 / 20 | 20 | 20 |
| Neck | −30 / 30 | 30 | 25 |
| Shoulder | −60 / 60 | 90 | 70 |
| Elbow | −5 / 130 | 5 | 0 |
| Hip | −60 / 40 | 45 | 30 |
| Knee | −130 / 2 | 2 | 0 |
| Ankle | −30 / 30 | 20 | 15 |

Set `angularYMotion`/`angularZMotion` to `Locked` on elbows and knees. A hinge
that can only hinge is a hinge that can't explode.

---

## 4. Starting tuning values

```
ActiveRagdollMuscles
  baseSpring        2500
  baseDamper         120
  arms multiplier     0.35   ← weak arms are where the comedy lives
  legs multiplier     1.2

FloatingCapsuleController
  rideHeight          0.9
  rayLength           1.4
  rideSpring         4000
  rideDamper          250
  maxSpeed            4.5
  acceleration        120
  maxAccelForce      1800

PelvisAnchor
  positionSpring      900
  positionDamper       60
  uprightSpring       900
  uprightDamper        90
  leanIntoMotion      0.12

CharacterBody
  impactImpulseThreshold  12
  limpStrength            0.06
  minimumDownTime         1.0
  getUpDuration           0.8

PhysicsHand
  reachSpring         400
  breakForce         2200
```

Tune in this order: ride spring first (no bounce, no sag), then pelvis spring
(no orbiting, no rubber-banding), then muscle spring, then damping everywhere.
**Damping is what kills the jitter.** If something buzzes, you have too much
spring, not too little.

Project settings:

```
Fixed Timestep                    0.01667  (60 Hz)
Default Solver Iterations         15
Default Solver Velocity Iterations 4
Enable Adaptive Force             off
```

`Tools → Nasser Active Ragdoll System → Apply Recommended Physics Settings` does this.

---

## 5. Get-up

Knockdown is never animated. Recovery partly is:

1. Wait `minimumDownTime`, then wait until the body's speed drops below
   `settleSpeed` (hard cap at `maxDownTime` so you can't be pinned forever).
2. Raycast down from the pelvis, teleport the **kinematic** controller capsule
   to that point.
3. Set the capsule's yaw to match where the body is lying, so the get-up
   doesn't spin the camera.
4. Pick a get-up clip from `dot(chest.forward, Vector3.up)` — face-up vs face-down.
5. Ramp `muscles.strength` and `anchor.weight` 0 → 1 over `getUpDuration`,
   easing the capsule toward the hips as the blend goes in.

You need exactly two animation clips for this: `GetUpProne` and `GetUpSupine`.
Mixamo has both, free.

---

## 6. The two things specific to a plane cabin

**Don't move the aircraft.** Keep the fuselage at world origin and fake flight
by moving the skybox, clouds and terrain. A rigidbody character inside a
fast-moving parent transform will jitter forever — the solver works in world
space and does not care about your hierarchy. Every game that does this well
keeps the vehicle interior stationary.

Turbulence then becomes trivial: forces applied to loose rigidbodies inside a
stationary tube, plus a rotation of the cabin *interior mesh* around the player.
That's `TurbulenceField`.

**Netcode.** Ragdolls do not survive naive client-side prediction — divergence
compounds every frame. Distributed authority instead:

- Each client owns and simulates **only their own** character's physics.
- Broadcast pelvis position/rotation + compressed bone rotations
  (smallest-three quaternion, ~2 bytes/component) at ~20 Hz. Interpolate on
  remotes; remote ragdolls run with `isKinematic = true` bones.
- Loose props: ownership transfers to whoever grabs them; that client
  simulates and broadcasts.
- Send the *state* (Standing/Ragdolled/GettingUp) as an event, not derived
  per-client, or clients will disagree about who is down.

Netcode for GameObjects 2.x supports distributed authority directly. Fish-Net
is more comfortable for this pattern if you're starting fresh.

---

## 7. Pitfalls, ranked by how much time they'll cost you

1. **`targetRotation` inverse math.** It's in the joint's own basis and it's
   inverted. Wrong sign = instant flailing. Handled in `JointMath.cs`; don't
   rewrite it inline.
2. **Animator culling.** Set `cullingMode = AlwaysAnimate` on the puppet. If
   the puppet's renderers are gone (they are) Unity will happily cull the
   Animator and your character goes limp off-screen.
3. **`enablePreprocessing = false`** on every joint. With it on, hard impacts
   make joints silently violate their limits.
4. **Self-collision.** `joint.enableCollision = false` handles adjacent bones.
   Don't disable the whole layer against itself — non-adjacent limbs *should*
   collide, and other players definitely should.
5. **Reading `joint.transform.localRotation` after animation has started.**
   Capture `startLocalRotation` in `Awake`, before anything moves.
6. **`Rigidbody.solverIterations`** is per-body and defaults to the project
   value. Ragdoll bones need ~20. Set it on the bones, not globally, or your
   whole scene pays for it.
7. **Mass ratio.** A 0.2 kg hand joined to a 15 kg chest will vibrate. Keep
   ratios under 10:1.
8. **Interpolation.** Bones `Interpolate`, controller `Interpolate`, everything
   else `None`. Mixing `Extrapolate` in causes visible swimming.

---

## 8. Where to go once it works

- Foot IK: raycast from the puppet's foot bones and offset them before the
  muscles read the pose. Cheap, and it kills the "skating on stairs" look.
- Per-limb damage: reduce a single group's `springMultiplier` when that limb
  takes a hit. A dead arm with everything else working sells injury better
  than any animation.
- Push-back on grab: apply the reaction force of `PhysicsHand` to the chest,
  so dragging a heavy crate visibly pulls your torso forward.

See `INTERACTIONS.md` for the grabbing, weapon and throwing layer built on top
of this. `CharacterBody` there replaces `RagdollStateMachine` in step 8.
