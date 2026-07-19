# Grappling Hook System Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a grappling hook to `ThirdPersonPuppet (1)` — press E to latch onto the nearest `GrappleAnchor` in range, get pulled to it by spring physics, auto-detach on arrival.

**Architecture:** `GrappleAnchor` is an empty marker MonoBehaviour dropped on scene objects. `GrappleController` on the `Character Controller` child disables RootMotion's `UserControlThirdPerson` + `CharacterThirdPerson` while grappling, drives the `CharacterController` directly with spring velocity, and draws a `LineRenderer` rope.

**Tech Stack:** Unity 6 C#, RootMotion PuppetMaster demo scripts (`RootMotion.Demos.UserControlThirdPerson`, `RootMotion.Demos.CharacterThirdPerson`), Unity `CharacterController`, `LineRenderer`.

## Global Constraints

- Unity 6 API only — use `Object.FindObjectsByType<T>(FindObjectsSortMode.None)` not the deprecated `FindObjectsOfType<T>()`
- Use `rb.linearVelocity` not `rb.velocity` for any Rigidbody access
- No comments explaining what code does — only add a comment if the WHY is non-obvious
- Scripts go in `Assets/Mahmoud SandBox/GrapplingHook/`
- No editor tools, no extra abstractions — YAGNI
- `CharacterThirdPerson` and `UserControlThirdPerson` are in namespace `RootMotion.Demos`

---

### Task 1: GrappleAnchor marker component

**Files:**
- Create: `Assets/Mahmoud SandBox/GrapplingHook/GrappleAnchor.cs`

**Interfaces:**
- Produces: `public class GrappleAnchor : MonoBehaviour` — used by `GrappleController.FindNearest()` in Task 2

- [ ] **Step 1: Create the folder and file**

Create `Assets/Mahmoud SandBox/GrapplingHook/GrappleAnchor.cs` with this exact content:

```csharp
using UnityEngine;

public class GrappleAnchor : MonoBehaviour
{
    void OnDrawGizmos()
    {
        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(transform.position, 0.4f);
    }
}
```

- [ ] **Step 2: Verify it compiles**

Switch to Unity Editor. The console should show **no errors**. If `GrappleAnchor` appears as a component you can add via Add Component → Scripts → GrappleAnchor, it's working.

- [ ] **Step 3: Place a test anchor in the scene**

In `MainGamePlayScene`, select any scene object (a wall, lamp post, or an empty GameObject near `ThirdPersonPuppet (1)`). Add Component → `GrappleAnchor`. In Scene view you should see a cyan wire sphere at that object's position.

- [ ] **Step 4: Commit**

```bash
git add "Assets/Mahmoud SandBox/GrapplingHook/GrappleAnchor.cs"
git commit -m "feat: add GrappleAnchor marker component"
```

---

### Task 2: GrappleController — spring pull + rope visual

**Files:**
- Create: `Assets/Mahmoud SandBox/GrapplingHook/GrappleController.cs`

**Interfaces:**
- Consumes: `GrappleAnchor` (Task 1) — found via `FindObjectsByType`
- Consumes: `RootMotion.Demos.UserControlThirdPerson` — disabled during grapple
- Consumes: `RootMotion.Demos.CharacterThirdPerson` — disabled during grapple
- Consumes: `CharacterController` — driven directly during grapple

- [ ] **Step 1: Create GrappleController.cs**

Create `Assets/Mahmoud SandBox/GrapplingHook/GrappleController.cs`:

```csharp
using UnityEngine;
using RootMotion.Demos;

[RequireComponent(typeof(CharacterController))]
public class GrappleController : MonoBehaviour
{
    [Header("Rope Visual")]
    [SerializeField] Transform _ropeOrigin;
    [SerializeField] float     _ropeWidth = 0.05f;
    [SerializeField] Color     _ropeColor = Color.gray;

    [Header("Pull Settings")]
    [SerializeField] float _maxGrappleRange    = 15f;
    [SerializeField] float _springAcceleration = 20f;
    [SerializeField] float _maxPullSpeed       = 12f;
    [SerializeField] float _detachDistance     = 1.5f;
    [SerializeField] float _pullGravity        = 9.8f;

    CharacterController    _cc;
    UserControlThirdPerson _userControl;
    CharacterThirdPerson   _charMotor;
    LineRenderer           _rope;

    bool    _grappling;
    Transform _anchor;
    Vector3 _grappleVelocity;

    void Awake()
    {
        _cc          = GetComponent<CharacterController>();
        _userControl = GetComponent<UserControlThirdPerson>();
        _charMotor   = GetComponent<CharacterThirdPerson>();

        _rope                = gameObject.AddComponent<LineRenderer>();
        _rope.positionCount  = 2;
        _rope.startWidth     = _ropeWidth;
        _rope.endWidth       = _ropeWidth;
        _rope.material       = new Material(Shader.Find("Sprites/Default"));
        _rope.startColor     = _ropeColor;
        _rope.endColor       = _ropeColor;
        _rope.enabled        = false;
    }

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.E))
        {
            if (_grappling) Detach();
            else            TryAttach();
        }

        if (_grappling && Input.GetKeyDown(KeyCode.Space))
            Detach();

        if (_grappling)
            Pull();
    }

    void TryAttach()
    {
        GrappleAnchor nearest = FindNearest();
        if (nearest == null) return;

        _anchor          = nearest.transform;
        _grappleVelocity = Vector3.zero;
        _grappling       = true;
        _rope.enabled    = true;

        if (_userControl != null) _userControl.enabled = false;
        if (_charMotor   != null) _charMotor.enabled   = false;
    }

    void Detach()
    {
        _grappling    = false;
        _rope.enabled = false;
        _anchor       = null;

        if (_userControl != null) _userControl.enabled = true;
        if (_charMotor   != null) _charMotor.enabled   = true;
    }

    void Pull()
    {
        Vector3 dir  = _anchor.position - transform.position;
        float   dist = dir.magnitude;

        _grappleVelocity += dir.normalized * _springAcceleration * Time.deltaTime;
        _grappleVelocity  = Vector3.ClampMagnitude(_grappleVelocity, _maxPullSpeed);
        _grappleVelocity.y -= _pullGravity * Time.deltaTime;

        _cc.Move(_grappleVelocity * Time.deltaTime);

        Vector3 ropeStart = _ropeOrigin != null ? _ropeOrigin.position : transform.position;
        _rope.SetPosition(0, ropeStart);
        _rope.SetPosition(1, _anchor.position);

        if (dist < _detachDistance)
            Detach();
    }

    GrappleAnchor FindNearest()
    {
        GrappleAnchor[] all      = Object.FindObjectsByType<GrappleAnchor>(FindObjectsSortMode.None);
        GrappleAnchor   nearest  = null;
        float           bestDist = _maxGrappleRange;

        foreach (GrappleAnchor a in all)
        {
            float d = Vector3.Distance(transform.position, a.transform.position);
            if (d < bestDist) { nearest = a; bestDist = d; }
        }
        return nearest;
    }
}
```

- [ ] **Step 2: Verify it compiles**

Switch to Unity Editor — console must show **zero errors**. If there are errors about `UserControlThirdPerson` or `CharacterThirdPerson` not found, check that the `using RootMotion.Demos;` line is present and that the RootMotion plugin is imported.

- [ ] **Step 3: Add GrappleController to the character**

In the Hierarchy, expand `ThirdPersonPuppet (1)` → select the child named **`Character Controller`**.  
Add Component → `GrappleController`.  
In the Inspector, you will see the Rope Visual and Pull Settings groups. Leave `_ropeOrigin` empty for now (the script falls back to `transform.position` if it's null).

- [ ] **Step 4: Verify LineRenderer is auto-added**

Press Play. Select `Character Controller` in the Hierarchy during Play mode — the Inspector should show a `LineRenderer` component that was added by `Awake()`. Press E and watch for the cyan gizmo on any `GrappleAnchor` in the scene. The console must be silent (no null-ref errors).

- [ ] **Step 5: Test full grapple loop**

Make sure at least one `GrappleAnchor` is placed within 15 m of `ThirdPersonPuppet (1)`.

1. Press Play
2. Walk the character near a `GrappleAnchor`
3. Press **E** — the `LineRenderer` rope appears and the character is pulled toward the anchor
4. Character reaches the anchor → rope disappears, character resumes normal movement with momentum
5. Press **E** again mid-pull → rope disappears, normal movement resumes immediately
6. Press **Space** mid-pull → same as above

If the character doesn't move toward the anchor: check that `GrappleController` is on the same object as `CharacterController` (the `Character Controller` child, not the root).

If the rope draws from the wrong position: drag the right-hand bone Transform into the `_ropeOrigin` field in the Inspector.

- [ ] **Step 6: Assign rope origin (optional but recommended)**

In the Hierarchy, expand `ThirdPersonPuppet (1)` deeply until you find the hand bone (look for a bone named `mixamorig:RightHand` or similar). Drag that bone Transform into the `_ropeOrigin` slot on `GrappleController` in the Inspector. Save the scene (`Ctrl+S`).

- [ ] **Step 7: Commit**

```bash
git add "Assets/Mahmoud SandBox/GrapplingHook/GrappleController.cs"
git add "Assets/TopDownEngine/Demos/Colonel/MainGamePlayScene.unity"
git commit -m "feat: add GrappleController with spring pull and LineRenderer rope"
```
