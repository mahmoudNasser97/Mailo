# Third-Person Camera Design Spec
**Date:** 2026-07-20
**Status:** Approved
**Scope:** Cinemachine 3 (Unity 6.2) third-person orbit camera with right shoulder offset, collision avoidance, aim zoom, cursor lock

---

## Summary

Add a professional third-person camera to the game using Cinemachine 3's `ThirdPersonFollow` body algorithm. Mouse controls full horizontal and vertical orbit. The character always faces the camera's forward direction. A second aim camera zooms in over-the-shoulder when the player holds a throwable and presses RMB. The cursor is hidden and locked during play; ESC toggles it. Camera never clips through objects or the ground — handled natively by Cinemachine's collision system.

**Stack:**
- Cinemachine 3.x (`Unity.Cinemachine` — ships with Unity 6.2)
- Two `CinemachineCamera` GameObjects (Normal + Aim)
- One new script: `CameraController.cs`
- One modification: `PhysicsCharacterController.HandleMovement()`

---

## 1. Architecture Overview

```
Scene Hierarchy
├── Main Camera
│   └── CinemachineBrain          ← blends between virtual cameras
│
├── CameraPivot                   ← world-space Transform (NOT parented to Player)
│   └── CameraTarget              ← child at local (0, 1.4, 0) — what cameras Follow
│
├── NormalCamera (CinemachineCamera, priority 10)
│   └── Body: ThirdPersonFollow → Follow = CameraTarget
│
├── AimCamera (CinemachineCamera, priority 0 → 20 on aim)
│   └── Body: ThirdPersonFollow → Follow = CameraTarget (same reference)
│
└── Player
    ├── PhysicsCharacterController  (modified: camera-relative movement, aim yaw lock)
    └── CameraController.cs         (new: mouse orbit, cursor lock, aim state)
```

**Why CameraPivot is NOT a child of Player:**
`ThirdPersonFollow` uses the Follow target's rotation to determine the "behind" direction. If `CameraTarget` were parented to the Player, the Player's own rotation (which changes with movement direction) would fight the camera orbit. `CameraPivot` lives at the scene root; `CameraController` sets its world position to `player.position` every `LateUpdate`, then applies yaw/pitch rotation independently.

**Data flow:**
1. `CameraController` reads raw mouse delta every `LateUpdate` → accumulates `yaw` (unclamped) and `pitch` (clamped -30° to +60°)
2. It sets `CameraPivot.position = player.position` and `CameraPivot.rotation = Quaternion.Euler(pitch, yaw, 0)`
3. Both cameras follow `CameraTarget` (child of `CameraPivot`) via `ThirdPersonFollow`
4. `PhysicsCharacterController.HandleMovement()` projects WASD onto camera-forward/right plane
5. On aim (RMB held + pickupable in hand): `AimCamera` priority raised to 20 → `CinemachineBrain` blends in 0.2s
6. Cursor locked on start; ESC toggles lock

---

## 2. Normal Camera

**Component:** `CinemachineCamera` + `ThirdPersonFollow` body

| Property | Value | Notes |
|---|---|---|
| Follow target | `CameraTarget` (chest Transform) | Tracks upper body, not feet |
| Shoulder Offset | `(0.5, 0.0, 0.0)` | Right-of-player offset, tunable |
| Vertical Arm Length | `0.3` | Lifts camera above shoulder |
| Camera Distance | `4.0` | Fixed — no scroll zoom |
| Camera Radius | `0.2` | Collision sphere size |
| Collision Filter | Everything except Player layer | Prevents self-collision |
| Field of View | `65°` | Standard third-person FOV |
| Priority | `10` | Active by default |

**Collision & ground avoidance:**
- Cinemachine 3's `ThirdPersonFollow` pushes the camera toward the player when the collision sphere hits geometry — no clipping through walls
- Automatically prevents the camera from going below terrain — no ground clipping
- No extra `CinemachineCollider` component needed; it's built into the body algorithm

**Orbit input:**
- Mouse X → `yaw` += `mouseX * sensitivity` (unlimited rotation)
- Mouse Y → `pitch` = `Clamp(pitch - mouseY * sensitivity, -30f, 60f)`
- Sensitivity: `2.0` (configurable on `CameraController`)
- Input provided via `CinemachineInputAxisController` override to avoid conflicts with Cinemachine's own input system

---

## 3. Aim Camera

**Component:** `CinemachineCamera` + `ThirdPersonFollow` body (separate GameObject)

| Property | Value | Notes |
|---|---|---|
| Follow target | Same `CameraTarget` | Same reference as NormalCamera |
| Shoulder Offset | `(0.6, 0.0, 0.0)` | Tighter right offset for OTS framing |
| Vertical Arm Length | `0.2` | |
| Camera Distance | `1.8` | Zoomed in close |
| Camera Radius | `0.2` | Same collision as NormalCamera |
| Field of View | `50°` | Narrower for zoom feel |
| Priority | `0` (default) → `20` (aim) | Overrides NormalCamera when active |

**Blend:** `CinemachineBrain` blends Normal ↔ Aim in **0.2 seconds** (ease-in-out curve).

**Activation conditions (both must be true):**
- Player is holding a `Pickupable` object (checked via `ObjectGrabController`)
- Player is holding `RMB`

**Aim direction:** Throw arc in `ThrowMath.TryCalculateVelocity` uses `Camera.main.transform.forward` as aim direction — no changes needed to throw math.

**Crosshair:** A simple world-space `Canvas` dot appears at screen center while aim camera is active. Toggled on/off with aim state.

**Character rotation during aim:** Character body is locked to face the camera's current yaw direction while aiming (overrides movement-direction rotation). This ensures the throw always goes where the camera points.

---

## 4. CameraController Script

**Location:** `Mahmoud SandBox/Camera/CameraController.cs`

**Fields (serializable):**
```csharp
[SerializeField] CinemachineCamera aimCamera;
[SerializeField] Transform         cameraPivot;
[SerializeField] float             sensitivity    = 2.0f;
[SerializeField] float             pitchMin       = -30f;
[SerializeField] float             pitchMax       =  60f;
[SerializeField] float             aimBlendTime   =  0.2f;
[SerializeField] GameObject        crosshairUI;
```

**Lifecycle:**

`Start()`:
- Lock cursor: `Cursor.lockState = CursorLockMode.Locked`
- Hide cursor: `Cursor.visible = false`

`Update()`:
- If ESC pressed: toggle cursor lock/visibility
- Check aim state (pickupable held + RMB held) → set `aimCamera.Priority`
- Toggle `crosshairUI` active state

`LateUpdate()`:
```
yaw   += Input.GetAxis("Mouse X") * sensitivity
pitch  = Clamp(pitch - Input.GetAxis("Mouse Y") * sensitivity, pitchMin, pitchMax)
cameraPivot.rotation = Quaternion.Euler(pitch, yaw, 0f)
```

**References acquired in `Awake()`:**
- `ObjectGrabController` on same GameObject (to check if holding a pickupable)
- `PhysicsCharacterController` on same GameObject (to pass camera-forward for movement)

---

## 5. Modification to PhysicsCharacterController

**File:** `Mahmoud SandBox/PhysicsCharacter/PhysicsCharacterController.cs`

**Method:** `HandleMovement()` — one change only.

**Before:**
```csharp
Vector3 input = new Vector3(h, 0f, v);
```

**After:**
```csharp
Vector3 camForward = Vector3.Scale(Camera.main.transform.forward, new Vector3(1,0,1)).normalized;
Vector3 camRight   = Vector3.Scale(Camera.main.transform.right,   new Vector3(1,0,1)).normalized;
Vector3 input      = (camForward * v + camRight * h);
if (input.sqrMagnitude > 1f) input.Normalize();
```

This makes WASD move relative to camera direction. The character's face-direction logic (`Quaternion.LookRotation(input)`) remains unchanged — it now rotates toward the camera-relative move direction.

**Aim rotation override** — `CameraController` sets two public properties on `PhysicsCharacterController` each frame:
- `IsAiming` (bool) — tells the controller to skip movement-direction rotation
- `CameraYaw` (float) — the current yaw to lock the character to

`PhysicsCharacterController.Update()` applies the override:
```csharp
if (IsAiming)
    transform.rotation = Quaternion.Euler(0f, CameraYaw, 0f);
```

---

## 6. New Files Summary

| File | Type | Purpose |
|---|---|---|
| `Mahmoud SandBox/Camera/CameraController.cs` | New script | Mouse orbit, cursor lock, aim state, crosshair toggle |
| `Mahmoud SandBox/Camera/Crosshair.prefab` | New prefab | Simple world-space canvas dot for aim mode |

**Modified files:**

| File | Change |
|---|---|
| `PhysicsCharacterController.cs` | `HandleMovement()` uses camera-relative input; aim rotation override |

---

## 7. Out of Scope (This Phase)

- Scroll-wheel zoom
- Camera shake (already handled by `NPCHitVFX` separately)
- Cutscene / cinematic cameras
- Lock-on / target-lock system
- Voice Activity Detection
- Multiple camera zones or room transitions
