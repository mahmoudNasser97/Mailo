# Destructible Object System — Design Spec
**Date:** 2026-07-25
**Status:** Approved

## Overview

A system that lets the player interact with destructible props in the world. When the player is near an object, a prompt appears. Pressing T plays a smash animation on the player, then RayFire shatters the object into debris. One random debris piece becomes collectible — the player walks near it, presses F, and a simple on-screen counter tracks what they've picked up.

---

## Architecture

Four scripts, each with one clear job:

| Script | Lives On | Responsibility |
|---|---|---|
| `DestructibleObject.cs` | The destructible prop | Proximity detection, T-press, player anim, RayFire demolish, shard tagging |
| `CollectibleShard.cs` | Added at runtime to one random shard | Proximity prompt, F-press to collect, notifies counter |
| `PickupCounter.cs` | A persistent UI GameObject | Tracks item counts, shows brief pickup notification |

---

## Data Flow

```
Player enters trigger collider
  → ButtonPrompt appears ("Press T to break")
    → Player presses T
      → DestructibleObject sets _canInteract = false (prevents re-trigger)
      → Player Animator fires smash trigger
        → Coroutine waits animationDelay seconds
          → rayfireRigid.Demolish() called
            → demolitionEvent fires → OnDemolished()
              → Random shard selected from rayfireRigid.fragments
              → CollectibleShard component added to that shard
                → Player walks near shard
                  → ButtonPrompt appears ("Press F to collect [itemName]")
                    → Player presses F
                      → PickupCounter.Instance.Add(itemName)
                      → Shard destroyed
```

---

## Component Details

### `DestructibleObject.cs`

**Serialized fields:**
- `string animatorTriggerName` — Animator trigger to fire on the player (e.g. `"Smash"`)
- `float animationDelay` — seconds to wait between animation start and demolition
- `string itemName` — display name of the collectible shard (e.g. "Wood Fragment")
- `ButtonPrompt buttonPromptPrefab` — world-space prompt prefab reference
- `RayfireRigid rayfireRigid` — reference to the RayFire component on this object

**Behaviour:**
- `OnTriggerEnter`: if tag == "Player", show ButtonPrompt, set `_playerInRange = true`, cache player reference
- `OnTriggerExit`: hide ButtonPrompt, set `_playerInRange = false`
- `Update`: if `_playerInRange && _canInteract && Input.GetKeyDown(KeyCode.T)` → trigger smash
- Smash sequence: set `_canInteract = false`, call `animator.SetTrigger(animatorTriggerName)`, `StartCoroutine(DemolishAfterDelay())`
- `DemolishAfterDelay`: waits `animationDelay`, calls `rayfireRigid.Demolish()`
- Subscribe to `rayfireRigid.demolitionEvent` → `OnDemolished()`
- `OnDemolished`: pick a random index from `rayfireRigid.fragments`, add `CollectibleShard` component, pass `itemName`, disable self

**Trigger setup:**
- A `SphereCollider` (isTrigger = true) sits directly on the same GameObject as `DestructibleObject.cs` — this avoids Unity trigger event routing issues that arise when the collider is on a child while RayfireRigid manages physics on the parent
- Radius is set directly on the SphereCollider in the Inspector (no script field needed)
- Filter by `tag == "Player"` in trigger callbacks

---

### `CollectibleShard.cs`

**Added at runtime** by `DestructibleObject.OnDemolished()`.

**Serialized / set-at-runtime fields:**
- `string itemName` — passed in from `DestructibleObject`
- `ButtonPrompt buttonPromptPrefab` — same prefab reference, passed in at add-time

**Behaviour:**
- `Start`: add a `SphereCollider` (isTrigger = true, radius = 1.5) to self
- `OnTriggerEnter`: if tag == "Player", show ButtonPrompt ("Press F to collect [itemName]"), set `_playerInRange = true`
- `OnTriggerExit`: hide ButtonPrompt, set `_playerInRange = false`
- `Update`: if `_playerInRange && Input.GetKeyDown(KeyCode.F)` → call `PickupCounter.Instance.Add(itemName)`, `Destroy(gameObject)`

---

### `PickupCounter.cs`

**Singleton**, `DontDestroyOnLoad`.

**Fields:**
- `Dictionary<string, int> _counts` — item name → total collected
- `TMP_Text notificationText` — single TextMeshPro label, anchored bottom-center on a Screen Space Overlay canvas
- `float notificationDuration` — how long the notification shows (default 2.0s)

**Behaviour:**
- `Add(string itemName)`: increments `_counts[itemName]`, cancels any running notification coroutine, starts a new one
- Notification coroutine: sets `notificationText.text = $"+1 {itemName}"`, fades alpha 0→1 instantly, waits `notificationDuration`, fades out over 0.3s

---

## Scene Setup

### Destructible Object Prefab
- `RayfireRigid` component: demolition type = Runtime, simulation type = Dynamic
- `DestructibleObject.cs` with all fields wired in Inspector
- `SphereCollider` (isTrigger = true) on the same GameObject as `DestructibleObject.cs` — configure radius in Inspector
- Intact mesh renderer remains until `Demolish()` — RayFire hides it automatically

### Player Requirements
- Player GameObject tag must be `"Player"`
- Player `Animator` must have a trigger parameter matching `animatorTriggerName`

### PickupCounter UI
- One Canvas (Screen Space — Overlay)
- One `TMP_Text` child, anchored bottom-center
- `PickupCounter.cs` on the Canvas GameObject
- This Canvas persists across scenes (`DontDestroyOnLoad`)

### ButtonPrompt Prefab
- Reuse existing `ButtonPrompt` prefab from TopDownEngine
- Both `DestructibleObject` and `CollectibleShard` reference the same prefab
- Instantiated in world space above the object, destroyed when player leaves range

---

## Per-Object Configuration (Inspector)

| Field | Example Value | Notes |
|---|---|---|
| `itemName` | "Wood Fragment" | Shown in pickup notification |
| `animatorTriggerName` | "Smash" | Must match Animator parameter exactly |
| `animationDelay` | 0.6 | Tune to match animation length |
| SphereCollider radius | 2.0 | Set directly on the collider in Inspector |

---

## Out of Scope

- Full inventory UI (not needed — counter only)
- Multiple collectible shards per object (one random shard only)
- Damage accumulation before breaking (T always one-shots)
- Save/load of collected item counts
