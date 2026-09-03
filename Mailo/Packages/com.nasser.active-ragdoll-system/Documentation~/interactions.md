# Interaction layer — grabbing, weapons, throwing

Extends the active ragdoll package. Still no third-party assets.

New files:

```
Runtime/ImpactSystem.cs    Impact struct, IImpactReceiver, ImpactRelay, Projectile
Runtime/Grabbable.cs       Grabbable, GripPoint
Runtime/PhysicsHand.cs     One hand: reach spring, grab joint, throw velocity buffer
Runtime/CharacterBody.cs   Replaces RagdollStateMachine. Adds Grabbed + Thrown states.
Runtime/MeleeWeapon.cs     Swing damage from real contact velocity
```

`CharacterBody` supersedes `RagdollStateMachine`, and `PhysicsHand` supersedes
`GrabController`. Delete the old two once you've swapped over.

---

## The one rule

**There is exactly one code path.** If you ever write a branch that reads
"if the thing I hit is an NPC", you have started building a second system and
the two will drift apart within a week.

Concretely:

- A crate, a fire axe, and a screaming passenger are all `Grabbable`. The hand
  never asks what it's holding — only how heavy it is and where the grip is.
- A punch, a swung crowbar, a thrown suitcase, a thrown *person*, and falling
  down the stairs all produce the same `Impact` struct on the same bus.
- A thrown character is a `Projectile` like any other. When they land on someone
  standing, both go down, and no line of code says so — each side's
  `ImpactRelay` reports its own collision independently.
- An NPC is a `CharacterBody` with an AI script feeding its controller instead
  of a gamepad. That's the entire difference. Which is why a thrown NPC and a
  thrown player behave identically: they *are* identical.

---

## Setup

### Any prop you can pick up

1. Rigidbody + collider.
2. `Grabbable`. Set `throwMultiplier` and `encumbrance`.
3. `ImpactRelay` (auto-added by weapons; add manually for props you want to
   register hits).
4. Layer `Grabbable`, included in each hand's `grabbableMask`.

Add empty `GripPoint` children where it should be held. A suitcase gets one on
the handle; a crate gets one on each side with `secondaryOnly` on the second so
two-handed carries snap sensibly.

### A weapon

Same as a prop, plus `MeleeWeapon`. Set `headPoint` to an empty at the business
end and `edgeAxis` to the local direction the edge faces. Grip point on the handle.

### A character you can grab and throw

On the **pelvis** bone: add a `Grabbable` with `character` pointing at the
`CharacterBody`. Add more on chest, and on each ankle if you want drag-by-the-leg.

```
Character (CharacterBody)
├── Controller  (FloatingCapsuleController)
├── Character   (physical rig)
│   ├── Hips    ← Grabbable (character = CharacterBody), ImpactRelay
│   ├── Chest   ← Grabbable (character = CharacterBody)
│   ├── Hand_L  ← driven by PhysicsHand
│   └── Foot_L  ← Grabbable (GripPoint, secondaryOnly = false)
└── Character_Puppet
```

Set the character's `Grabbable.twoHandedMassThreshold` low (say 12) so dragging
a full-grown passenger with one arm visibly struggles.

---

## What each mechanic actually is

| You want | It is |
|---|---|
| Heavy things pull your arm down | Finite `maxReachForce` on the hand |
| Heavy things slow you down | `CharacterBody.ApplyEncumbrance` scaling `maxSpeed` |
| Crate wedged in a door stops you | Nothing. It's a joint between two rigidbodies. |
| Losing your grip in a hard turn | `Grabbable.grabBreakForce` on the joint |
| Two players fighting over a crate | Nothing. Two joints, one body. |
| Held object sags and lags | `gripSpring` scaled by `8 / mass` in `PhysicsHand.Grab` |
| Grabbed NPC struggles instead of going limp | `grabbedStrength = 0.18` |
| Grabbed NPC can't stand up | `State.Grabbed` has no recovery tick |
| Punching someone free of a grab | `BreakFreeFromGrab()` on accumulated impact |
| Weak hits accumulating into a knockdown | `accumulationWindow` |
| Headshots dropping people faster | `weakSpots` + `weakSpotMultiplier` |
| Slow swings tapping, fast swings flooring | `MeleeWeapon.ScaleAt` reading `GetPointVelocity` |
| Flat slap doing less than an edge hit | `edgeAxis` + `edgeInfluence` |
| Can't hurt yourself with your own throw | `Projectile.selfImmunity` + instigator check |
| Ragdolled character drops what it holds | `PhysicsHand.SetStrength` releasing under 0.12 |

None of those are features you write. They're consequences of the joint being real.

---

## Throwing

The one non-obvious piece. **Never throw with instantaneous hand velocity** —
`rb.velocity` on the release frame is dominated by solver noise and you'll get
wild inconsistency between throws that felt identical.

`PhysicsHand` keeps a ring buffer of the last 6 FixedUpdate velocities and
throws with the average. Impulse is:

```
impulse = clamp(avgVelocity * throwForceMultiplier, maxThrowSpeed) * mass * throwMultiplier
```

Multiplying by mass means light junk flies and dense cargo lobs, which is
correct — you're imparting a velocity, not a force.

On release the object is armed as a `Projectile` for 4 seconds or until its
speed drops below 3.5 m/s. Armed objects carry `impactMultiplier` and an
instigator. They disarm after the first solid hit so a crate doesn't mow down
an entire cabin as it rolls.

---

## Tuning table

```
CharacterBody
  knockdownImpulse         12      ← for a ~66kg rig. Scale with mass.
  weakSpotMultiplier       2.5
  accumulationWindow       0.5
  limpStrength             0.06
  grabbedStrength          0.18
  thrownMinimumDownTime    1.6     ← longer than a normal fall, so no mid-air recovery
  encumberedSpeedFactor    0.45

PhysicsHand
  reachSpring              420
  maxReachForce           1100     ← the ceiling that makes heavy things heavy
  gripSpring             30000     ← scaled down by 8/mass at grab time
  velocitySamples            6
  throwForceMultiplier     1.4
  maxThrowSpeed             16

Grabbable
  twoHandedMassThreshold    18     ← 12 for characters
  grabBreakForce          2400
  projectileMultiplier     1.6

MeleeWeapon
  minimumSwingSpeed        2.5
  speedReference             9
  maximumMultiplier          4
```

Start `knockdownImpulse` too low. A game where everyone falls over constantly is
funny and you can tune up; a game where nothing reacts reads as broken.

---

## Layers

```
Character        collides with: Default, Character, Grabbable, Weapon
Grabbable        collides with: everything
Weapon           collides with: everything
HandTarget       collides with: nothing (targets are empties, no colliders)
```

Do **not** disable Character-vs-Character in the matrix. Characters bumping into
each other is most of the emergent comedy, and it's also how a thrown body
knocks someone down.

---

## Networking notes

The grab system is where naive netcode falls apart, because a grab creates a
joint between two bodies that may be simulated by different clients.

- **Ownership transfers on grab.** Whoever grabs a prop becomes its simulation
  owner; everyone else interpolates. Request → authoritative grant → create the
  joint. Never create the joint optimistically on both ends.
- **Grabbing a character transfers *their* ragdoll ownership to the grabber**
  for the duration. This is the counterintuitive one, but it's the only way the
  joint has a single solver. Hand it back on release.
- **Contested grabs**: two clients grabbing the same crate in the same tick.
  Server picks one by arrival order; the loser gets a rejection and plays a
  whiff. Don't try to support genuinely simultaneous two-owner grabs.
- **Throws are events, not state.** Replicate `(objectId, impulse, instigator,
  serverTick)` and let each client apply it. Replicating the resulting velocity
  desyncs immediately.
- **Impacts are authoritative on the victim's owner.** The client who owns the
  character decides whether they got knocked down and broadcasts the state
  change. Otherwise two clients disagree about who is on the floor.

---

## Order of work

1. `Grabbable` + `PhysicsHand` on props only. Grab and drop a crate.
2. Throwing with the velocity buffer. Tune `throwForceMultiplier` until it feels
   like your arm.
3. `ImpactSystem` + `Projectile`. Thrown crate knocks down a standing character.
4. `MeleeWeapon`. Swing a crowbar.
5. `Grabbable` on character bones. Grab an NPC.
6. Throw the NPC at another NPC. If steps 1–5 were done as one system, this step
   requires **zero new code** — that's the test that you built it right.
