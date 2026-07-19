# Grappling Hook System — Design Spec

**Date:** 2026-07-19  
**Scene:** `Assets/TopDownEngine/Demos/Colonel/MainGamePlayScene.unity`  
**Character:** `ThirdPersonPuppet (1)` → child `Character Controller`

---

## Goal

Press **E** to fire a rope at the nearest tagged anchor point within range. A spring-like force pulls the character toward it. On arrival the rope auto-detaches and the character keeps their momentum. Press **E** or **Space** to cancel early.

---

## Architecture

Two new scripts in `Assets/Mahmoud SandBox/GrapplingHook/`:

```
ThirdPersonPuppet (1)
└── Character Controller           ← GrappleController lives here
      ├── UserControlThirdPerson   ← disabled while grappling
      ├── CharacterThirdPerson     ← disabled while grappling
      ├── CharacterController      ← GrappleController.Move() takes over
      └── GrappleController.cs     ← NEW

Any scene GameObject
└── GrappleAnchor.cs               ← NEW: empty marker, no logic
```

A `LineRenderer` is added to `Character Controller` by `GrappleController.Awake()` — no manual setup needed.

---

## New Files

### 1. `Assets/Mahmoud SandBox/GrapplingHook/GrappleAnchor.cs`

Empty `MonoBehaviour` marker. Drop it on any world object you want to be grappleable (lamp posts, walls, ledges). No fields, no logic.

### 2. `Assets/Mahmoud SandBox/GrapplingHook/GrappleController.cs`

**Requires:** `CharacterController` on the same GameObject.

**Inspector fields:**

| Field | Type | Default | Purpose |
|---|---|---|---|
| `ropeOrigin` | Transform | — | Hand bone — drag right-hand bone in Inspector |
| `maxGrappleRange` | float | 15 | Max distance (m) to find an anchor |
| `springAcceleration` | float | 20 | Pull acceleration (m/s²) |
| `maxPullSpeed` | float | 12 | Terminal speed during pull (m/s) |
| `detachDistance` | float | 1.5 | Auto-detach radius (m) |
| `ropeWidth` | float | 0.05 | LineRenderer width |
| `ropeColor` | Color | gray | LineRenderer color |

**State machine:** `Idle` ↔ `Grappling`

**Entering grapple (E pressed, state = Idle):**
1. Collect all `GrappleAnchor` instances via `FindObjectsByType`
2. Pick the nearest one within `maxGrappleRange` — if none, do nothing
3. Disable `UserControlThirdPerson` + `CharacterThirdPerson` on the same GameObject
4. Zero `_grappleVelocity`, enable LineRenderer, store anchor Transform → state = `Grappling`

**Each frame while grappling (Update):**
```
dir = anchor.position − transform.position
_grappleVelocity += dir.normalized × springAcceleration × Time.deltaTime
_grappleVelocity  = Vector3.ClampMagnitude(_grappleVelocity, maxPullSpeed)
_grappleVelocity.y -= gravity × Time.deltaTime
_cc.Move(_grappleVelocity × Time.deltaTime)
lineRenderer.SetPosition(0, ropeOrigin.position)
lineRenderer.SetPosition(1, anchor.position)
if Vector3.Distance(transform.position, anchor.position) < detachDistance → Detach()
```

**Cancelling (E or Space pressed, or auto-detach):**
1. Disable LineRenderer
2. Re-enable `UserControlThirdPerson` + `CharacterThirdPerson`
3. State → `Idle`

Momentum carries over naturally: `CharacterThirdPerson` resumes with no velocity reset.

---

## Input

| Key | Action |
|---|---|
| E | Fire grapple (Idle) / Cancel grapple (Grappling) |
| Space | Cancel grapple (Grappling only) |

Uses `Input.GetKeyDown(KeyCode.E)` and `Input.GetKeyDown(KeyCode.Space)` — no Input Manager axis required.

---

## Tuning Guide

| Problem | Field | Direction |
|---|---|---|
| Rope reaches anchors too far away | `maxGrappleRange` | Decrease |
| Pull feels too slow / too fast | `springAcceleration` | Increase / Decrease |
| Character overshoots anchor badly | `maxPullSpeed` | Decrease |
| Character detaches too early | `detachDistance` | Decrease |
| Character doesn't detach soon enough | `detachDistance` | Increase |

---

## Setup (after implementation)

1. Add `GrappleController` to the `Character Controller` child of `ThirdPersonPuppet (1)`
2. Drag the character's right-hand bone Transform into `Rope Origin`
3. Add `GrappleAnchor` to any scene objects you want as grapple points
4. Press Play → press **E** near an anchor
