# Gate Pull System Design

## Goal

A pull-to-open gate mechanic: a glowing neon X on the gate acts as both visual indicator and rope anchor. The player presses X to throw a rope to it, then mashes X to drag the gate down to the ground. Releasing mid-pull snaps the gate back; finishing locks it open permanently.

## Architecture

Three scripts in `Assets/Mahmoud SandBox/Gate/`:

- **`GateNeonMarker.cs`** — on the glowing X child object of the gate. Pulses emission at idle. Exposes `void SetProgress(float t)` (0–1) which drives scale toward `_maxScale`. Exposes its Transform as the rope attachment point. Deactivates when gate is fully open.
- **`PullableGate.cs`** — on the gate root. Holds a serialized reference to `GateNeonMarker`. Tracks press count. Each frame calls `_marker.SetProgress(normalizedProgress)`. Lerps gate rotation toward `(progress / required) × _openAngle` on local X while pulling. Lerps back toward 0° when not pulling. Locks at `_openAngle` permanently when fully open.
- **`GatePullController.cs`** — on the player. Sphere overlap each frame to find a `PullableGate` in range. Manages the full interaction state machine: idle → throw → attached → pulling → released / fully open.

## Interaction Sequence

1. **Idle** — player enters range. Neon X pulses. No UI prompt.
2. **Throw** — player presses X. `ThrowRope` animator trigger fires. LineRenderer extends from player hand bone to the neon X over `_ropeThrowDuration` (lerp each frame).
3. **Attached** — rope reaches neon X. `Pulling = true` on Animator. Pull loop begins.
4. **Mashing** — each X press:
   - `PullableGate.RegisterPress()` increments `_currentPresses`
   - Gate lerps to `(_currentPresses / _pressesRequired) × _openAngle` on local X
   - Player steps back by `_stepBackDistance` via `CharacterController.Move`
   - Neon X scales up toward `_maxScale`
5. **Release mid-pull** — player leaves range. Rope hides. `Pulling = false`. Gate lerps back to 0° at `_snapBackSpeed` deg/s. `_currentPresses` resets to 0.
6. **Fully open** — `_currentPresses >= _pressesRequired`. Gate locks at `_openAngle`. Rope hides. `Pulling = false`. Neon X deactivates. `PullableGate` rotation logic disables itself.

## Snap-Back Behaviour

Progress is not held between interactions. The moment the player leaves range (or the rope throw is interrupted), `_currentPresses` resets to 0 and the gate lerps back to closed at `_snapBackSpeed`. There is no partial-progress save.

## Rope Visual

Uses a `LineRenderer` created at runtime (same pattern as `GrappleController`). Hidden by default. On throw: position 0 = player hand bone, position 1 = neon X position, lerped from position 0 over `_ropeThrowDuration`. On pull: both positions update each frame (player hand moves as player steps back). On release or fully open: LineRenderer disabled.

## Animator Requirements

The player Animator needs:
- `ThrowRope` — trigger, fires once when throw begins
- `Pulling` — bool, true during pull loop, false on release or completion

The controller returns to its default locomotion state when `Pulling` is false.

## Tuning Parameters

### GatePullController (on player)
| Parameter | Default | Description |
|---|---|---|
| `_interactionRadius` | 3 m | Sphere overlap radius to detect gate |
| `_ropeThrowDuration` | 0.4 s | Time for LineRenderer to extend to neon X |
| `_stepBackDistance` | 0.15 m | Player nudge backward per press |
| `_ropeWidth` | 0.05 | LineRenderer width |
| `_ropeColor` | gray | LineRenderer color |
| `_handBone` | (Transform ref) | Origin of the rope on the player |
| `_throwAnimTrigger` | `"ThrowRope"` | Animator trigger name |
| `_pullingAnimBool` | `"Pulling"` | Animator bool name |

### PullableGate (on gate root)
| Parameter | Default | Description |
|---|---|---|
| `_pressesRequired` | 10 | Total X presses to fully open |
| `_openAngle` | –90° | Target local X rotation when fully open |
| `_snapBackSpeed` | 45 deg/s | Gate return speed when not being pulled |
| `_pullLerpSpeed` | 8 | Lerp speed toward target angle while pulling |

### GateNeonMarker (on glowing X child)
| Parameter | Default | Description |
|---|---|---|
| `_idlePulseSpeed` | 2 | Emission pulse cycles per second |
| `_idlePulseMin` | 0.3 | Min emission intensity multiplier |
| `_idlePulseMax` | 1.0 | Max emission intensity multiplier |
| `_maxScale` | 2× | Scale at 100% pull progress |

## Global Constraints

- Unity 6: use `rb.linearVelocity` not `rb.velocity`
- All files go in `Assets/Mahmoud SandBox/Gate/`
- No editor tools — pure runtime scripts, Inspector drag-and-drop setup
- Input key: `KeyCode.X`
- Rope visual: `LineRenderer` created at runtime in `Awake`, same pattern as `GrappleController`
- Player step-back: `CharacterController.Move` — no Rigidbody force on player
- No RayFire, no PuppetMaster involvement on the gate itself
