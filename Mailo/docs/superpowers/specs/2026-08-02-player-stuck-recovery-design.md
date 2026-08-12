# Player Stuck-Recovery ("Refresh") — Design

**Date:** 2026-08-02
**Status:** Approved
**Scene/target:** Demo_Island player (PuppetMaster / CharacterThirdPerson setup)

## Problem
When the character is knocked into ragdoll and gets stuck (can't stand back up — wedged in geometry, on a slope, off a ledge), there's no way to recover without restarting. We want a key that "refreshes" the player back to a nearby safe spot, standing.

## Behavior
The game continuously remembers the last position where the player stood safely on ground. If the player is stuck ragdolling and presses **R**, they snap back upright to that last safe spot. Pressing R while standing/walking normally does nothing.

## Design

### Component & placement
One new MonoBehaviour, `PlayerStuckRecovery`, added to the **`Character Controller`** GameObject (the movement root — it already holds `CharacterPuppet`, `CharacterThirdPerson`, `UserControlThirdPerson`, and the Unity `CharacterController`).

File: `Assets/Mahmoud SandBox/PhysicsCharacter/PlayerStuckRecovery.cs`

References (auto-found in `Awake`/`Start`, Inspector-overridable):
- `PuppetMaster` — for `Teleport(...)`
- `BehaviourPuppet` — for `state` and `SetState(...)` (reached via the sibling `Behaviours` hierarchy under the character root)
- The movement-root `Transform` + Unity `CharacterController` (this component's own object)

### Recording the safe spot
Every frame, if **all** are true — `puppet.state == BehaviourPuppet.State.Puppet`, the character is grounded, and horizontal speed is below a small threshold — store the movement root's position and upright (yaw-only) rotation as `lastSafe`. Initialized to the spawn transform in `Start`, so a valid target always exists.

### Stuck detection
Accumulate time while `puppet.state != Puppet`. Once it exceeds `stuckThreshold` (default **1.5 s**), the player is considered "stuck" and the R key becomes active. While in `Puppet` state the timer resets and R is ignored.

### Refresh action (R pressed while stuck)
1. Disable the `CharacterController`, set the movement root's `position`/`rotation` to `lastSafe`, re-enable it (so the CC doesn't fight the move).
2. `puppetMaster.Teleport(lastSafePos, lastSafeRot, moveToTarget: true)` — snaps the ragdoll and its animation target to the new spot with no spring artifact.
3. `puppet.SetState(BehaviourPuppet.State.Puppet)` — re-pins the character upright/animated.
4. Zero out residual velocity so it doesn't immediately topple; reset the stuck timer.

### Defaults (all Inspector-exposed)
- `recoverKey` = `KeyCode.R` (verified: no clash with existing character scripts, which only use F2/F3/F4).
- `stuckThreshold` = `1.5f` s.
- `groundedSpeedThreshold` for "settled" recording ≈ `0.5f`.
- No safe spot recorded yet → falls back to the spawn transform.

### Out of scope (v1 / YAGNI)
- Screen fade, SFX, or VFX on refresh (easy to add later).
- Multiple checkpoints / placed respawn markers (chose automatic last-safe-spot instead).
- Cooldown (the stuck-gate already prevents spam).

## Testing (manual, in Play mode)
1. Play → walk around so safe spots record.
2. Jump into a pit / off a ledge until the character is stuck ragdolling.
3. After ~1.5 s, press **R** → character snaps upright at the last safe spot, no physics blow-up, control restored.
4. Press R while walking normally → nothing happens.
