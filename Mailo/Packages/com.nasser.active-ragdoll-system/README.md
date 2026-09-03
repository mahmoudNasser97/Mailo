# Nasser Active Ragdoll System

Physics-driven humanoid characters for Unity. Self-balancing active ragdolls,
physics hands that grab anything (including other characters), a unified
impact system, and a one-window setup wizard.

**No third-party dependencies.** Built-in physics, built-in Animator, nothing else.

Namespace: `NasserActiveRagdoll` (editor code: `NasserActiveRagdoll.EditorTools`).

**Developed against Unity 6.2 (6000.2).** Also compiles on 2022.3 LTS and
2023.x — version differences are handled by conditional compilation, not by
you.

---

## Install

Package Manager → **+** → *Install package from disk…* → pick this folder's
`package.json`. Or drop the folder into `Packages/`.

### Unity 6.2 — do this first

**Project Settings → Player → Active Input Handling → `Both`.**

New Unity 6 projects default to *Input System Package (New)*, which makes the
legacy `UnityEngine.Input` class throw `InvalidOperationException` at runtime.
`PlayerDriver` uses the legacy API deliberately, so the package has no dependency
on the Input System package. If you skip this, the player character will not
respond to input and the Console will fill with exceptions.

If your project is already all-in on the Input System, replace the three
`Input.Get*` blocks in `PlayerDriver` — they're isolated in one class for exactly
this reason. NPCs are unaffected either way.

### Unity 6 API differences, already handled

| Changed in Unity 6 | Where it's shimmed |
|---|---|
| `Rigidbody.velocity` → `linearVelocity` | `RbCompat.Vel()` / `SetVel()` in `JointMath.cs` |
| `Rigidbody.angularDrag` → `angularDamping` | `PhysicsHand.Grab()` / `Release()` |
| `AnimatorUpdateMode.AnimatePhysics` → `Fixed` | `RagdollBuilder.BuildPuppet()` |

All guarded by `UNITY_6000_0_OR_NEWER` / `UNITY_2023_1_OR_NEWER`, so the same
source compiles on 2022.3 without edits.

---

## Quickstart

1. Drag a humanoid model into the scene. Make sure the FBX is imported with
   **Rig → Animation Type → Humanoid**.
2. **Tools → Nasser Active Ragdoll System → Character Setup** (or `Ctrl/Cmd + Shift + R`).
3. Assign the model, choose **Player** or **NPC**, press **Build character**.
4. Press Play.

That's it. The wizard produces:

```
Model_Character                 CharacterBody, ActiveRagdollMuscles, PelvisAnchor, driver
├── Physical                    colliders + rigidbodies + ConfigurableJoints + the mesh
├── Physical_Puppet             Animator only. No colliders, no renderers.
├── Controller                  floating capsule, FloatingCapsuleController
├── LeftHand / RightHand        PhysicsHand
└── FirstPersonRig              Player only: camera, audio listener, hand targets
    └── NpcHandTargets          NPC instead: hand targets parented to the chest
```

Everything is wired. Layers are created. Grab handles are added to hips, chest
and both ankles so other characters can pick this one up.

---

## Player vs NPC

The role flag changes exactly two things: which rig gets built, and which driver
gets attached.

|  | Player | NPC |
|---|---|---|
| Camera | `FirstPersonRig` on the head bone | none |
| Audio listener | yes | no |
| Hand targets | camera space, aim-linked | chest space |
| Driver | `PlayerDriver` (input) | `NpcDriver` (wander/follow) |

**Nothing below the driver layer knows the difference.** Balance, grabbing,
impacts, knockdown and throwing are one code path. That's why a thrown NPC and
a thrown player behave identically — they *are* identical.

Replace `NpcDriver.Think()` with your behaviour tree; nothing else changes.

---

## Animation

Assign clips to a **Locomotion Clip Set** asset and the wizard generates the
whole graph: nested blend trees (1D speed containing 2D directional), airborne
states, both get-ups, and a mask-limited upper-body layer for carrying.

Four clips are required (Idle, Walk Forward, and the two get-ups); the rest is
polish. Missing clips are omitted from the trees rather than erroring.

`CharacterBody` drives `Speed`, `MoveX`, `MoveY`, `Grounded` and `Carrying`,
all damped, with playback rate synced to real ground speed.

**See `Documentation~/animation.md`** for the exact Mixamo clip list, the import
settings that matter, and why each smoothing measure exists.

---

## Profiles

`Assets → Create → Nasser Active Ragdoll System → Profile`.

One asset holds every tuning number. Make one per archetype (Crew, Passenger,
Heavy) and share it — otherwise you hand-edit eight components per character and
your twelve NPCs drift apart until nobody remembers which is correct.

`CharacterBody.ApplyProfile()` pushes values into every component on Awake, and
the wizard applies it at build time.

---

## Tuning order

This matters. Do it in this sequence:

1. **Ride spring** (`FloatingCapsuleController`) — no bounce, no sag.
2. **Pelvis spring** (`PelvisAnchor`) — no orbiting, no rubber-banding.
   **This is the milestone.** If it feels right here, everything after is content.
3. **Muscle spring** (`RagdollProfile.baseSpring`).
4. **Damping everywhere.** If something buzzes, you have too much spring — not
   too little.

Then knockdown. Start `knockdownImpulse` too low: a game where everyone falls
over constantly is funny and you can tune up, but a game where nothing reacts
reads as broken.

---

## Troubleshooting

**Character lies on the ground and the root shows a missing script** — fixed in
0.5.1. If you built a character on an older build, delete it and rebuild; stale
components cannot be repaired in place.

**Character flails wildly on Play** — `targetRotation` basis math. Don't rewrite
it inline; it lives in `JointMath.cs` for a reason.

**Character goes limp when off-screen** — the puppet Animator got culled. The
wizard sets `AlwaysAnimate`; check it survived.

**Capsule colliders lie sideways through the torso** — the bone's local axis
doesn't point at its child the way you assumed. `AddCollider` detects the
dominant axis, but a badly exported rig can still fool it. Fix the collider
`direction` by hand.

**Limbs pass through the torso** — expected for *adjacent* bones
(`joint.enableCollision = false`). Non-adjacent limbs should collide; if they
don't, check the layer matrix. Never disable Character-vs-Character.

**Everything jitters** — mass ratios. Keep parent:child under 10:1. A 0.2 kg
hand joined to a 15 kg chest will vibrate forever.

**Nobody gets knocked down** — impulses accumulate only inside
`accumulationWindow`, and `ImpactRelay.minimumImpulse` filters resting contacts.
Lower `knockdownImpulse` first.

---

## Further reading

- `Documentation~/architecture.md` — why two skeletons, the floating capsule,
  mass and joint limit tables, networking.
- `Documentation~/interactions.md` — grab/throw/weapon layer, the one-code-path
  rule, ownership transfer for co-op.
- `Documentation~/animation.md` — clip list, blend tree structure, foot-sliding fixes.

---

## Status

Compiles, and the wizard produces a character. Verified in Unity 6.2. Known
remaining risk areas, in likelihood order:

1. **`ConfigurableJoint` anchor math in `PhysicsHand.Grab()`** — the local-space
   anchor conversion is fiddly and easy to get a frame off.
2. **Capsule collider direction** on unusual rigs — `AddCollider` infers the
   bone's dominant local axis, but a badly exported FBX can still fool it.
3. **Tuning.** The defaults assume a ~66 kg, 1.8 m humanoid. A stylised or
   child-proportioned character will need the profile adjusted.

### A note on file layout

Every MonoBehaviour and ScriptableObject lives in a file named after it. This is
not style — Unity cannot resolve a MonoBehaviour whose class name differs from
its filename, and the failure is silent at compile time and only shows up as a
missing script at runtime. If you add classes to this package, keep the rule.

The build order in `Documentation~/interactions.md` is designed to surface those
early: props → throwing → impacts → weapons → characters. If throwing an NPC at
another NPC needs any new code, something upstream is wrong.
