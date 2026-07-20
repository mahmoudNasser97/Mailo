# Advanced NPC Chasing System — Design Spec
**Date:** 2026-07-20
**Project:** Mailo / Mahmoud SandBox
**Status:** Approved

---

## Overview

A new, self-contained NPC AI system built alongside (not replacing) the existing `NPCChaseController.cs`. The system features patrol, chase, throwing, hit reactions with VFX, and multi-NPC crowd avoidance using RVO/ORCA. All scripts live under `Assets/Mahmoud SandBox/NPC/`.

---

## 1. State Machine & Brain

`NPCBrain.cs` owns a four-state machine and is the single public API for the NPC.

### States

| State | Active When |
|---|---|
| `Patrol` | Player distance > `chaseRange` (default 15m) |
| `Chase` | Player within `chaseRange`, distance > `throwRange` |
| `Throw` | Player within `throwRange` (default 6m) |
| `HitReact` | Hit impulse received above `hitThreshold` — interrupts any state |

### Transitions

```
Patrol ──(player enters chaseRange)──▶ Chase ──(player enters throwRange)──▶ Throw
  ▲                                      ▲                                      │
  └──────(player exits chaseRange)───────┴──────(player exits throwRange)───────┘

Any state ──(hit received)──▶ HitReact ──(hitRecoverTime elapsed)──▶ previous state
```

### NPCBrain Responsibilities
- Holds `[SerializeField]` references to all sub-components
- Finds the player by `"Player"` tag at startup
- Updates the active state each `FixedUpdate`
- Provides `ReportHit(float impulse, Vector3 hitPoint, Transform hitBone)` — called by `NPCHitReaction`
- Provides `GetDesiredVelocity()` — queried by `NPCRVOAgent` to compute avoidance
- Applies final `CharacterController.Move()` after RVO adjustment

---

## 2. Components

### NPCPatroller.cs
- **Waypoints mode:** walks between assigned `patrolPoints[]` Transforms in order; reverses or loops (configurable)
- **Random wander mode:** picks a random point within `wanderRadius` (default 8m) from spawn position, moves there, waits `wanderPauseTime` (default 2s), repeats
- **Priority:** waypoints if `patrolPoints.Length > 0`, otherwise random wander
- Returns desired velocity vector to `NPCBrain`
- Drives Animator `"Speed"` parameter (walk speed during patrol)

### NPCChaser.cs
- Moves NPC toward player's position each frame
- `chaseSpeed` (default 4m/s), separate from patrol speed (default 2.5m/s)
- Drives Animator `"Speed"` parameter (run speed during chase)
- Rotates NPC to face player via `Quaternion.RotateTowards`

### NPCThrower.cs
- **Object acquisition (priority order):**
  1. Scans for `Pickupable` within `pickupRadius` (default 4m) — moves briefly to grab it
  2. Falls back to instantiating a random prefab from `throwablePrefabs[]` at the hand bone
- **Throw sequence:**
  1. Face player
  2. Trigger `"Throw"` animation parameter
  3. At animation event — release object, apply arc launch velocity using the same ballistic arc formula as `ObjectGrabController.cs` (the formula will be extracted into a static `ThrowMath.CalculateLaunchVelocity` helper shared by both)
  4. Thrown object retains `Pickupable` component — player can catch and throw it back
- `throwCooldown` (default 2.5s) between throws
- Returns to `Chase` if player exits `throwRange` during cooldown

### NPCHitReaction.cs
- Listens to the existing `HitReactor` component on the NPC root
- Filters impulses below `hitThreshold` (default 20) — ignores minor brushes
- Calls `NPCBrain.ReportHit()` on valid hits
- If impulse > `knockdownThreshold` — defers to `PuppetRagdollController` for full ragdoll
- `hitRecoverTime` (default 0.8s) before returning to previous state
- Triggers `"HitReact"` Animator parameter

### NPCRVOAgent.cs
- Implements 2D ORCA (Optimal Reciprocal Collision Avoidance) — no NavMesh required
- **Per-frame update:**
  1. Query all `NPCRVOAgent` instances within `neighborRadius` (default 5m)
  2. For each neighbor compute velocity obstacle cone using `timeHorizon` (default 2s) and `agentRadius` (default 0.5m)
  3. Solve for minimum velocity adjustment satisfying all ORCA half-planes
  4. Return adjusted velocity to `NPCBrain` for `CharacterController.Move()`
- Each NPC pair shares responsibility 50/50 — neither dominates
- Works during both Patrol and Chase states
- `maxSpeed` caps adjusted velocity magnitude

### NPCHitVFX.cs
- Three simultaneous effects triggered by `NPCHitReaction`:

**1. Material flash**
- Caches original materials from all `SkinnedMeshRenderer` components on NPC
- Swaps to a solid hit material (white or red, assigned in Inspector)
- Restores after `flashDuration` (default 0.15s)
- Pure material swap — no shader modification needed

**2. Particle burst**
- Instantiates `hitParticlePrefab` (ParticleSystem) at hit bone world position
- Plays once, auto-destroys on completion
- Prefab is Inspector-assigned — can be sparks, dust, blood, etc.

**3. Camera shake**
- Finds `Camera.main`
- Applies Perlin noise positional offset for `shakeDuration` (default 0.2s) at `shakeMagnitude` (default 0.1)
- No Cinemachine dependency

---

## 3. Spawner System

### NPCSpawner.cs

**Spawn location (priority order):**
1. Assigned `spawnPoints[]` Transform array
2. Random positions within `spawnRadius` around the spawner GameObject

**Wave config (Inspector-serializable):**
```
[Serializable]
class WaveConfig {
    int npcCount
    GameObject npcPrefab
    float delayBetweenSpawns   // stagger between individual NPC spawns
    float delayAfterWave        // pause before next wave begins
}
WaveConfig[] waves
```

**Wave progression options (both configurable):**
- `autoAdvanceWaves` — next wave starts automatically after `delayAfterWave`
- `triggerNextWaveOnAllDead` — next wave starts when all NPCs from current wave are destroyed

**Collision safety:**
- `spawnSeparation` (default 2m) enforced between chosen spawn positions
- Each spawned NPC has `NPCRVOAgent` active from frame one

**Public API:**
- `StartWaves()` — begin from wave 0
- `SpawnWave(int index)` — spawn a specific wave manually
- `GetLivingNPCCount()` — query from other systems

---

## 4. Editor Tool

`AdvancedNPCSetupTool.cs` — menu item under `Tools → Advanced NPC → Setup NPC`

On click:
1. Duplicates the Ch06_nonPBR character hierarchy
2. Strips `PuppetMoverSimple`, adds `NPCBrain`, `NPCPatroller`, `NPCChaser`, `NPCThrower`, `NPCHitReaction`, `NPCRVOAgent`, `NPCHitVFX`
3. Adds `HitReactor` to root (if not present)
4. Creates a `HitImpactVFX` particle system prefab and assigns it to `NPCHitVFX`
5. Creates an `NPCSpawner` GameObject in the scene referencing the NPC prefab
6. Assigns `CharacterAnimaiton.controller` (existing asset) to the NPC Animator

---

## 5. File Layout

```
Assets/Mahmoud SandBox/NPC/
├── Editor/
│   ├── NPCSetupTool.cs               (existing — untouched)
│   └── AdvancedNPCSetupTool.cs       (new)
├── NPCChaseController.cs             (existing — untouched)
├── NPCBrain.cs                       (new)
├── NPCPatroller.cs                   (new)
├── NPCChaser.cs                      (new)
├── NPCThrower.cs                     (new)
├── NPCHitReaction.cs                 (new)
├── NPCHitVFX.cs                      (new)
├── NPCRVOAgent.cs                    (new)
└── NPCSpawner.cs                     (new)
```

---

## 6. Animator Parameters Required

The NPC Animator controller needs these parameters (same naming convention as existing characters):

| Parameter | Type | Set By |
|---|---|---|
| `Speed` | Float | NPCPatroller, NPCChaser |
| `Throw` | Trigger | NPCThrower |
| `HitReact` | Trigger | NPCHitReaction |

Animation events on the Throw animation clip must call `NPCThrower.ReleaseThrowable()`.

---

## 7. Configuration Reference

| Property | Default | Component |
|---|---|---|
| `chaseRange` | 15m | NPCBrain |
| `throwRange` | 6m | NPCBrain |
| `patrolSpeed` | 2.5 m/s | NPCPatroller |
| `chaseSpeed` | 4.0 m/s | NPCChaser |
| `wanderRadius` | 8m | NPCPatroller |
| `wanderPauseTime` | 2s | NPCPatroller |
| `pickupRadius` | 4m | NPCThrower |
| `throwCooldown` | 2.5s | NPCThrower |
| `hitThreshold` | 20 | NPCHitReaction |
| `knockdownThreshold` | 80 | NPCHitReaction |
| `hitRecoverTime` | 0.8s | NPCHitReaction |
| `flashDuration` | 0.15s | NPCHitVFX |
| `shakeDuration` | 0.2s | NPCHitVFX |
| `shakeMagnitude` | 0.1 | NPCHitVFX |
| `neighborRadius` | 5m | NPCRVOAgent |
| `timeHorizon` | 2s | NPCRVOAgent |
| `agentRadius` | 0.5m | NPCRVOAgent |
| `spawnRadius` | 10m | NPCSpawner |
| `spawnSeparation` | 2m | NPCSpawner |

---

## 8. Dependencies

- **PuppetMaster** (RootMotion plugin) — NPC uses same hybrid animated-target + physics-puppet setup as player
- **HitReactor.cs** (existing) — reused for hit detection pipeline
- **Pickupable.cs** (existing) — reused for throwable objects
- **ObjectGrabController.cs** (existing) — `CalculateLaunchVelocity` logic referenced for throw arc
- **CharacterAnimaiton.controller** (existing) — assigned to NPC Animator
- **PuppetRagdollController.cs** (existing) — invoked for knockdown above threshold

No NavMesh dependency. No Cinemachine dependency.
