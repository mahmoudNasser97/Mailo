# Seesaw Physics System — Design Spec
**Date:** 2026-07-28
**Status:** Approved

---

## Overview

A seesaw-style trap/interaction system with two flat trigger zones (Side A and Side B). When one or more characters stand on Side A and **jump**, the character on Side B is launched upward + outward and ragdolls mid-air. Launch force scales with the combined weight of all characters on Side A. Fully generic — works for any character type (player or NPC).

---

## Scene Hierarchy

```
Seesaw (GameObject — SeesawCoordinator)
├── SideA (BoxCollider isTrigger — SeesawSide, role = Input)
└── SideB (BoxCollider isTrigger — SeesawSide, role = Launcher)
```

Each playable/NPC character gets a `SeesawParticipant` component. No changes to existing character scripts.

---

## Components

### `SeesawParticipant` (on every character)

Data-only component. Cached at `Awake` — no runtime lookups.

**Fields:**
- `float weightKg` — configurable per character (e.g. 60, 90, 150)

**Caches at Awake:**
- `Rigidbody _rb` — via `GetComponent / GetComponentInParent`
- `PhysicsCharacterController _physicsCtrl` — for player ragdoll
- `PuppetRagdollController _puppetCtrl` — for NPC ragdoll

**Public API:**
- `float WeightKg` — property returning `weightKg`
- `void ApplyLaunch(Vector3 impulse)` — applies force + triggers ragdoll using the lookup chain below

**Ragdoll lookup chain (in order):**
1. `PhysicsCharacterController.ForceRagdoll()` → player
2. `PuppetRagdollController.ReportImpact(impulse, dir)` → NPC
3. Raw `Rigidbody.AddForce(impulse, ForceMode.Impulse)` only → fallback

---

### `SeesawSide` (on SideA and SideB trigger GameObjects)

Tracks which `SeesawParticipant`s are currently inside the trigger zone.

**Fields:**
- `SeesawCoordinator _coordinator` — set by coordinator in `Awake`
- `SeesawRole role` — `Input` (Side A) or `Launcher` (Side B)
- `float jumpVelocityThreshold = 1.5f` — minimum upward Y velocity to count as a jump

**Behaviour:**
- `OnTriggerEnter` → register participant into `Occupants`
- `OnTriggerExit` → remove participant from `Occupants` first; then if `role == Input` and exiting character's `rb.linearVelocity.y > jumpVelocityThreshold`, call `coordinator.NotifyJump(jumper)` passing the departed participant explicitly
- `List<SeesawParticipant> Occupants` — public read-only list (does NOT include the jumper at notification time)

**Why explicit jumper parameter:** By the time `NotifyJump` is called, the jumper is already removed from `Occupants`. The coordinator receives the jumper separately and adds their weight on top of the remaining occupants.

---

### `SeesawCoordinator` (on Seesaw root)

Owns the launch logic. Listens for jump notifications from Side A.

**Fields:**
- `SeesawSide sideA` — the input trigger
- `SeesawSide sideB` — the launcher trigger
- `float forcePerKg = 3.0f` — launch impulse multiplier
- `float horizontalBias = 0.3f` — outward component added to launch direction
- `float cooldownSeconds = 2.0f` — prevents re-triggering while Side B character is airborne
- `Transform horizontalAwayDirection` — optional override; defaults to `SideB.transform.position - SideA.transform.position` (normalized XZ)

**On `NotifyJump(SeesawParticipant jumper)` from Side A:**
1. Check cooldown — if active, ignore
2. Collect remaining Side A occupants from `sideA.Occupants` + add jumper explicitly
3. Sum `totalWeight = remainingOccupants.Sum(p => p.WeightKg) + jumper.WeightKg`
4. Collect Side B occupants (typically one character)
5. Calculate `launchDir = (Vector3.up + awayDir * horizontalBias).normalized`
6. Calculate `impulse = launchDir * totalWeight * forcePerKg`
7. Call `participant.ApplyLaunch(impulse)` on each Side B occupant
8. Start cooldown coroutine

---

## Launch Formula

```
totalWeight      = sum of SeesawParticipant.weightKg for all Side A occupants at jump moment
launchForce      = totalWeight × forcePerKg
launchDirection  = normalize(Vector3.up + awayFromSeesaw × horizontalBias)
impulse          = launchDirection × launchForce    (applied as ForceMode.Impulse)
```

**Weight examples (forcePerKg = 3.0, horizontalBias = 0.3):**
| Side A total weight | Launch impulse magnitude |
|---|---|
| 60 kg (1 light char) | 180 N·s |
| 90 kg (1 heavy char) | 270 N·s |
| 150 kg (2 chars combined) | 450 N·s |

---

## Jump Detection

- Uses `OnTriggerExit` on `SeesawSide (Input)`
- A departure is a **jump** if: `rb.linearVelocity.y > jumpVelocityThreshold` (default `1.5f`)
- A departure is a **walk-off** if below threshold — ignored, no launch fires
- Jumper's weight is included in the total — passed explicitly as a parameter since they've already left the `Occupants` list by notification time

---

## Cooldown

- After a launch fires, `SeesawCoordinator` enters a `cooldownSeconds` (default `2.0f`) window
- Any jump notification during this window is silently dropped
- Prevents rapid re-triggers while the Side B character is still airborne

---

## Unity 6 API Notes

- Use `rb.linearVelocity` (not `rb.velocity`)
- Use `rb.linearDamping` (not `rb.drag`)
- Use `ForceMode.Impulse` for instant launch feel
- `Object.FindObjectsByType<T>(FindObjectsSortMode.None)` if discovery is needed

---

## Files to Create

| File | Path |
|---|---|
| `SeesawParticipant.cs` | `Assets/Mahmoud SandBox/Seesaw/SeesawParticipant.cs` |
| `SeesawSide.cs` | `Assets/Mahmoud SandBox/Seesaw/SeesawSide.cs` |
| `SeesawCoordinator.cs` | `Assets/Mahmoud SandBox/Seesaw/SeesawCoordinator.cs` |

No editor tools needed — setup is a simple drag-and-drop in the Inspector.

---

## Out of Scope (for now)

- Visual seesaw board rotation (can be added later as a separate animator-driven tilt)
- Sound/VFX on launch (can hook into existing NPCHitVFX or a new SeesawVFX component)
- Multiple characters on Side B (by design, one character is expected there)
