# Third-Person Camera Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a professional Cinemachine 3 third-person orbit camera with mouse control, right shoulder offset, wall/ground collision avoidance, aim zoom on throwable, and cursor lock.

**Architecture:** `CameraPivot` (world-root Transform) tracks player position each `LateUpdate` and stores yaw/pitch rotation from mouse input. `NormalCamera` and `AimCamera` both use `ThirdPersonFollow` with `CameraTarget` (child of `CameraPivot` at chest height) as their Follow target. Cinemachine blends between them when aim activates. `CameraController` drives orbit, cursor lock, and aim state; `PhysicsCharacterController` is modified to move camera-relative and lock facing direction when aiming.

**Tech Stack:** Unity 6.2, Cinemachine 3 (`Unity.Cinemachine`), C#

## Global Constraints
- Cinemachine namespace: `Unity.Cinemachine` (NOT `Cinemachine` — that is CM2 and will not compile)
- New scripts go in `Mahmoud SandBox/Camera/`
- No scroll zoom, no lock-on, no cutscene cameras (out of scope)
- Aim only activates when BOTH conditions are true: player holds a `Pickupable` AND holds RMB
- Collision/ground avoidance: built into `ThirdPersonFollow` — do NOT add a separate `CinemachineCollider`
- `CameraPivot` must NOT be parented to the Player (see spec for why)

---

## File Map

| Action | Path |
|---|---|
| Create | `Mahmoud SandBox/Camera/CameraController.cs` |
| Modify | `Mahmoud SandBox/ObjectInteraction/ObjectGrabController.cs` |
| Modify | `Mahmoud SandBox/PhysicsCharacter/PhysicsCharacterController.cs` |
| Scene setup | Manual Unity Editor steps (Task 4) |

---

### Task 1: Expose `IsHoldingObject` on `ObjectGrabController`

**Files:**
- Modify: `Mahmoud SandBox/ObjectInteraction/ObjectGrabController.cs`

**Interfaces:**
- Produces: `public bool IsHoldingObject` — returns `true` when `_held != null`

- [ ] **Step 1: Add the property**

Open `ObjectGrabController.cs`. After the private field `bool _canThrow;` (line 36), add one line:

```csharp
public bool IsHoldingObject => _held != null;
```

The field block should now read:
```csharp
Rigidbody    _held;
Collider     _heldCollider;
Coroutine    _returnRoutine;
LineRenderer _arc;
Vector3      _throwVelocity;
bool         _canThrow;

public bool IsHoldingObject => _held != null;
```

- [ ] **Step 2: Verify compilation in Unity**

Switch to the Unity Editor. Wait for the status bar to finish compiling. Open the Console window — confirm zero errors. `ObjectGrabController` now publicly exposes whether it is holding an object.

- [ ] **Step 3: Commit**

```bash
git add "Mahmoud SandBox/ObjectInteraction/ObjectGrabController.cs"
git commit -m "feat: expose IsHoldingObject property on ObjectGrabController"
```

---

### Task 2: Camera-Relative Movement + Aim Override in PhysicsCharacterController

**Files:**
- Modify: `Mahmoud SandBox/PhysicsCharacter/PhysicsCharacterController.cs`

**Interfaces:**
- Consumes: `Camera.main.transform.forward/right` for movement direction
- Produces:
  - `public bool IsAiming { get; set; }` — when `true`, character faces `CameraYaw` instead of move direction
  - `public float CameraYaw { get; set; }` — current camera yaw, set by `CameraController` each frame

- [ ] **Step 1: Add public aim properties**

In `PhysicsCharacterController.cs`, after the `static readonly` animator hash fields (after `static readonly int _moveYHash`), add:

```csharp
public bool  IsAiming  { get; set; }
public float CameraYaw { get; set; }
```

- [ ] **Step 2: Replace HandleMovement() with camera-relative version**

Replace the entire `HandleMovement()` method (lines 125–149) with:

```csharp
void HandleMovement()
{
    float h = Input.GetAxisRaw("Horizontal");
    float v = Input.GetAxisRaw("Vertical");

    Vector3 camForward = Vector3.Scale(Camera.main.transform.forward, new Vector3(1f, 0f, 1f)).normalized;
    Vector3 camRight   = Vector3.Scale(Camera.main.transform.right,   new Vector3(1f, 0f, 1f)).normalized;
    Vector3 input      = camForward * v + camRight * h;
    if (input.sqrMagnitude > 1f) input.Normalize();
    bool moving = input.sqrMagnitude > 0.01f;

    _rb.linearDamping = moving ? movingDrag : stoppingDrag;

    if (moving)
    {
        Vector3 flatVel = new Vector3(_rb.linearVelocity.x, 0f, _rb.linearVelocity.z);
        if (flatVel.magnitude < maxMoveSpeed)
            _rb.AddForce(input * moveForce, ForceMode.Acceleration);

        Quaternion targetRot = IsAiming
            ? Quaternion.Euler(0f, CameraYaw, 0f)
            : Quaternion.LookRotation(input);
        _rb.MoveRotation(Quaternion.Slerp(transform.rotation, targetRot, rotateSpeed * Time.fixedDeltaTime));
    }
    else if (IsAiming)
    {
        _rb.MoveRotation(Quaternion.Slerp(transform.rotation,
            Quaternion.Euler(0f, CameraYaw, 0f), rotateSpeed * Time.fixedDeltaTime));
    }

    _animator.SetFloat(_speedHash, moving ? 1f : 0f,      0.1f, Time.fixedDeltaTime);
    _animator.SetFloat(_moveXHash, moving ? input.x : 0f, 0.1f, Time.fixedDeltaTime);
    _animator.SetFloat(_moveYHash, moving ? input.z : 0f, 0.1f, Time.fixedDeltaTime);
}
```

Key changes from original:
- `input` is now camera-relative (not world-axis WASD)
- Normalization happens before `if (moving)` — removed `input = input.normalized` from inside the block
- When `IsAiming`, rotation locks to `CameraYaw` whether moving or stationary
- `else if (IsAiming)` handles stationary aiming so character snaps toward camera when not walking

- [ ] **Step 3: Verify compilation**

Switch to Unity Editor. Wait for compilation. Console must show zero errors. Do not enter Play Mode yet — the camera rig does not exist yet.

- [ ] **Step 4: Commit**

```bash
git add "Mahmoud SandBox/PhysicsCharacter/PhysicsCharacterController.cs"
git commit -m "feat: camera-relative movement and aim yaw lock in PhysicsCharacterController"
```

---

### Task 3: Create CameraController.cs

**Files:**
- Create: `Mahmoud SandBox/Camera/CameraController.cs`

**Interfaces:**
- Consumes:
  - `ObjectGrabController.IsHoldingObject` (Task 1)
  - `PhysicsCharacterController.IsAiming`, `PhysicsCharacterController.CameraYaw` (Task 2)
  - `Unity.Cinemachine.CinemachineCamera`
- Produces: `CameraController` MonoBehaviour — attach to Player GameObject, wire refs in Inspector

- [ ] **Step 1: Create the Camera folder**

```bash
mkdir -p "G:/Work/Mailo/Mailo/Assets/Mahmoud SandBox/Camera"
```

- [ ] **Step 2: Write CameraController.cs**

Create `Mahmoud SandBox/Camera/CameraController.cs` with this content:

```csharp
using UnityEngine;
using Unity.Cinemachine;

public class CameraController : MonoBehaviour
{
    [Header("References")]
    [SerializeField] Transform         _cameraPivot;
    [SerializeField] CinemachineCamera _aimCamera;
    [SerializeField] GameObject        _crosshairUI;

    [Header("Orbit")]
    [SerializeField] float _sensitivity = 2.0f;
    [SerializeField] float _pitchMin    = -30f;
    [SerializeField] float _pitchMax    =  60f;

    [Header("Aim Camera Priorities")]
    [SerializeField] int _aimPriority    = 20;
    [SerializeField] int _normalPriority =  0;

    float                      _yaw;
    float                      _pitch;
    bool                       _cursorLocked = true;
    ObjectGrabController       _grab;
    PhysicsCharacterController _physics;

    void Awake()
    {
        _grab    = GetComponent<ObjectGrabController>();
        _physics = GetComponent<PhysicsCharacterController>();
    }

    void Start()
    {
        _yaw = transform.eulerAngles.y;
        SetCursorLock(true);
    }

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.Escape))
            SetCursorLock(!_cursorLocked);

        bool isAiming = _cursorLocked
            && _grab != null && _grab.IsHoldingObject
            && Input.GetMouseButton(1);

        if (_aimCamera != null)
            _aimCamera.Priority = isAiming ? _aimPriority : _normalPriority;

        if (_crosshairUI != null)
            _crosshairUI.SetActive(isAiming);

        if (_physics != null)
        {
            _physics.IsAiming  = isAiming;
            _physics.CameraYaw = _yaw;
        }
    }

    void LateUpdate()
    {
        if (!_cursorLocked) return;

        _yaw   += Input.GetAxis("Mouse X") * _sensitivity;
        _pitch  = Mathf.Clamp(_pitch - Input.GetAxis("Mouse Y") * _sensitivity,
                               _pitchMin, _pitchMax);

        if (_cameraPivot == null) return;
        _cameraPivot.position = transform.position;
        _cameraPivot.rotation = Quaternion.Euler(_pitch, _yaw, 0f);
    }

    void SetCursorLock(bool locked)
    {
        _cursorLocked    = locked;
        Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
        Cursor.visible   = !locked;
    }
}
```

- [ ] **Step 3: Verify compilation**

Switch to Unity Editor. Wait for compilation. Console must show zero errors.

- [ ] **Step 4: Commit**

```bash
git add "Mahmoud SandBox/Camera/CameraController.cs"
git commit -m "feat: add CameraController with mouse orbit, cursor lock, and aim state"
```

---

### Task 4: Scene Setup in Unity Editor

**Files:**
- Manual Unity Editor configuration only — no C# changes

Follow every step in order. Do not skip ahead.

---

**Part A — CinemachineBrain on Main Camera**

- [ ] **Step 1: Add CinemachineBrain**

In the Hierarchy, select `Main Camera`. In the Inspector → **Add Component** → type `CinemachineBrain` → add it.

In the `CinemachineBrain` component:
- **Default Blend → Style:** `Ease In Out`
- **Default Blend → Time:** `0.2`

---

**Part B — CameraPivot + CameraTarget**

- [ ] **Step 2: Create CameraPivot**

In the Hierarchy, right-click on empty space → **Create Empty**. Name it `CameraPivot`. **Do NOT parent it to the Player.** It must be at the scene root. Reset its Transform: Position `(0,0,0)`, Rotation `(0,0,0)`, Scale `(1,1,1)`.

- [ ] **Step 3: Create CameraTarget**

Right-click `CameraPivot` in the Hierarchy → **Create Empty**. Name it `CameraTarget`. Set its **local** position to `(0, 1.4, 0)`. Leave rotation and scale at default. This child is what both cameras will follow.

---

**Part C — NormalCamera**

- [ ] **Step 4: Create NormalCamera GameObject**

In the Hierarchy, right-click empty space → **Create Empty**. Name it `NormalCamera`. Reset its Transform.

In the Inspector → **Add Component** → search `CinemachineCamera` (from `Unity.Cinemachine`) → add it.

- [ ] **Step 5: Configure NormalCamera**

In the `CinemachineCamera` component:
- **Priority:** `10`
- **Follow:** drag `CameraTarget` into this field
- **Position Control (Body):** select `Third Person Follow` from the dropdown

In the `Third Person Follow` settings that appear:
| Field | Value |
|---|---|
| Shoulder Offset | X = `0.5`, Y = `0`, Z = `0` |
| Vertical Arm Length | `0.3` |
| Camera Distance | `4` |
| Camera Radius | `0.2` |
| Camera Collision Filter | Click → set to **Everything**, then uncheck the `Player` layer |

- [ ] **Step 6: Set NormalCamera FOV**

Still in the `CinemachineCamera` component:
- **Lens → Field of View:** `65`

---

**Part D — AimCamera**

- [ ] **Step 7: Create AimCamera GameObject**

In the Hierarchy, right-click empty space → **Create Empty**. Name it `AimCamera`. Reset its Transform.

**Add Component** → `CinemachineCamera` → add it.

- [ ] **Step 8: Configure AimCamera**

In the `CinemachineCamera` component:
- **Priority:** `0`
- **Follow:** drag the **same** `CameraTarget` into this field (same object as NormalCamera)
- **Position Control (Body):** `Third Person Follow`

In `Third Person Follow`:
| Field | Value |
|---|---|
| Shoulder Offset | X = `0.6`, Y = `0`, Z = `0` |
| Vertical Arm Length | `0.2` |
| Camera Distance | `1.8` |
| Camera Radius | `0.2` |
| Camera Collision Filter | Same as NormalCamera (Everything minus Player) |

- [ ] **Step 9: Set AimCamera FOV**

- **Lens → Field of View:** `50`

---

**Part E — Crosshair UI**

- [ ] **Step 10: Create Crosshair Canvas**

In the Hierarchy, right-click → **UI → Canvas**. Name it `CrosshairCanvas`.

In the `Canvas` component:
- **Render Mode:** `Screen Space - Overlay`

Right-click `CrosshairCanvas` → **UI → Image**. Name the child `CrosshairDot`.

Select `CrosshairDot`. In **Rect Transform**:
- Click the anchor preset box (top-left of Rect Transform) → hold **Alt** → click the **center/center** preset → this sets anchor and pivot to center with Pos (0,0)
- **Width:** `8`
- **Height:** `8`

In the **Image** component:
- **Color:** White (`FFFFFFFF`)
- Remove any default sprite (leave Source Image empty for a solid white square)

- [ ] **Step 11: Deactivate CrosshairCanvas by default**

Select `CrosshairCanvas`. At the very top of the Inspector, **uncheck the active checkbox** next to the name. It starts hidden; `CameraController` activates it when aiming.

---

**Part F — Wire Up CameraController on Player**

- [ ] **Step 12: Add CameraController to Player**

Select the `Player` GameObject in the Hierarchy. **Add Component** → `CameraController`.

Fill in the serialized fields:
- **Camera Pivot:** drag `CameraPivot` from the Hierarchy
- **Aim Camera:** drag `AimCamera` from the Hierarchy
- **Crosshair UI:** drag `CrosshairCanvas` from the Hierarchy

Leave Sensitivity at `2`, pitch limits at `-30`/`60`, priorities at `20`/`0`.

---

**Part G — Verify in Play Mode**

- [ ] **Step 13: Orbit test**

Enter Play Mode. Move the mouse — the camera should orbit smoothly around the player. WASD should move the player in the direction the camera faces (W = toward camera's look direction). ESC should unlock/show the cursor; ESC again should re-lock it.

Expected: Camera follows player, rotates with mouse, character walks camera-relative.

- [ ] **Step 14: Aim test**

While in Play Mode: pick up an object with F. Hold RMB. Camera should blend smoothly to over-the-shoulder zoom in ~0.2 seconds. The crosshair dot should appear at screen center. The throw arc should point where the camera is looking. Release RMB — camera blends back to normal.

Expected: Smooth blend, crosshair visible only during aim, throw arc tracks camera.

- [ ] **Step 15: Collision test**

Walk the player into a wall. The camera should push toward the player rather than clipping through the wall. Approach the edge of a raised platform — camera should not dip below floor level.

Expected: No wall clipping, no ground clipping.

- [ ] **Step 16: Commit the scene**

```bash
git add .
git commit -m "feat: add Cinemachine third-person camera rig with orbit, aim zoom, and cursor lock"
```

---

### Task 5: Tune Values in Play Mode

No C# changes — Inspector tweaks only. Enter Play Mode for each step, adjust, exit, note the value.

- [ ] **Step 1: Tune mouse sensitivity**

On the Player's `CameraController` component, adjust **Sensitivity**. Default `2.0`. Higher = faster camera. Typical range: `1.5`–`4.0`.

- [ ] **Step 2: Tune shoulder offset**

On `NormalCamera → Third Person Follow → Shoulder Offset X`. Default `0.5`. Range: `0.3` (subtle offset) to `0.8` (strong OTS). Pick what feels natural for your level width.

- [ ] **Step 3: Tune pitch clamp**

On `CameraController`: `Pitch Min` (default `-30`) and `Pitch Max` (default `60`). Tighten if the camera hits geometry at extreme angles. Loosen if it feels too restricted.

- [ ] **Step 4: Tune aim distance and FOV**

On `AimCamera → Third Person Follow → Camera Distance`. Default `1.8`. Try `1.5`–`2.5` for different zoom intensities. Also tune `Lens → FOV` (default `50`) — lower = more zoom, higher = less zoom.

- [ ] **Step 5: Commit tuned values**

```bash
git add .
git commit -m "chore: tune camera sensitivity, shoulder offset, and aim zoom values"
```
