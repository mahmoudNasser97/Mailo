# Advanced NPC Chasing System Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build a complete NPC AI system with patrol/wander, chase, object throwing, hit reactions with VFX, RVO crowd avoidance, and a wave-based spawner — all new scripts alongside the existing `NPCChaseController.cs`.

**Architecture:** `NPCBrain` owns a four-state machine (Patrol→Chase→Throw→HitReact) and coordinates five focused sub-components. `NPCRVOAgent` implements 2D ORCA so groups of NPCs naturally avoid each other. `NPCSpawner` drives wave-based spawning with separation enforcement.

**Tech Stack:** Unity C# (no NavMesh, no Cinemachine), PuppetMaster (RootMotion), CharacterController, existing `HitReactor`/`Pickupable`/`PuppetRagdollController`.

## Global Constraints

- All new NPC scripts: `Assets/Mahmoud SandBox/NPC/` (editor scripts in `Editor/` sub-folder)
- Existing scripts untouched **except**: `HitReactor.cs` (add event), `Pickupable.cs` (pass hitPoint), `ObjectGrabController.cs` (use ThrowMath)
- Animator parameters: `Speed` (Float), `Throw` (Trigger), `HitReact` (Trigger) — same casing as existing characters
- CharacterController.Move called only from `NPCBrain.Update` — sub-components return velocity, never move directly
- NPC animated target = `Ch06_nonPBR` child; NPC root = `Ch06_nonPBR Root` parent (same hierarchy as player)
- `_ragdoll.knockdownThreshold` for NPCs = 80 (set by setup tool); player threshold stays 200
- `FindObjectsByType<T>(FindObjectsSortMode.None)` — already used in codebase (Unity 2023+)
- `rb.linearVelocity` not `rb.velocity` — already used in codebase (Unity 6)

---

### Task 1: ThrowMath — Shared Arc Utility

Extract `TryCalculateVelocity` from `ObjectGrabController` into a shared static helper so both player throw and NPC throw use identical physics.

**Files:**
- Create: `Assets/Mahmoud SandBox/ObjectInteraction/ThrowMath.cs`
- Modify: `Assets/Mahmoud SandBox/ObjectInteraction/ObjectGrabController.cs`

**Interfaces:**
- Produces: `ThrowMath.TryCalculateVelocity(Vector3 from, Vector3 to, float angleDeg, out Vector3 velocity) → bool`

- [ ] **Step 1: Create ThrowMath.cs**

```csharp
using UnityEngine;

public static class ThrowMath
{
    public static bool TryCalculateVelocity(Vector3 from, Vector3 to, float angleDeg, out Vector3 velocity)
    {
        velocity    = Vector3.zero;
        Vector3 dir = to - from;
        float   h   = dir.y;
        dir.y       = 0f;
        float dist  = dir.magnitude;
        if (dist < 0.01f) return false;

        float angle       = angleDeg * Mathf.Deg2Rad;
        float denominator = dist * Mathf.Sin(2f * angle) - 2f * h * Mathf.Cos(angle) * Mathf.Cos(angle);
        if (denominator <= 0f) return false;

        float speed = Mathf.Sqrt(Physics.gravity.magnitude * dist * dist / denominator);
        velocity    = dir.normalized * speed * Mathf.Cos(angle)
                    + Vector3.up     * speed * Mathf.Sin(angle);
        return true;
    }
}
```

- [ ] **Step 2: Update ObjectGrabController to use ThrowMath**

Replace the private `TryCalculateVelocity` method and its call site. In `ObjectGrabController.cs`, delete the private method (lines 141–158) and update the call at line 114:

Old (line 114):
```csharp
_canThrow = TryCalculateVelocity(start, target, _throwAngle, out _throwVelocity);
```

New:
```csharp
_canThrow = ThrowMath.TryCalculateVelocity(start, target, _throwAngle, out _throwVelocity);
```

Delete the private method block:
```csharp
bool TryCalculateVelocity(Vector3 from, Vector3 to, float angleDeg, out Vector3 velocity)
{
    velocity    = Vector3.zero;
    Vector3 dir = to - from;
    float   h   = dir.y;
    dir.y       = 0f;
    float dist  = dir.magnitude;
    if (dist < 0.01f) return false;

    float angle       = angleDeg * Mathf.Deg2Rad;
    float denominator = dist * Mathf.Sin(2f * angle) - 2f * h * Mathf.Cos(angle) * Mathf.Cos(angle);
    if (denominator <= 0f) return false;

    float speed = Mathf.Sqrt(Physics.gravity.magnitude * dist * dist / denominator);
    velocity = dir.normalized * speed * Mathf.Cos(angle)
             + Vector3.up     * speed * Mathf.Sin(angle);
    return true;
}
```

- [ ] **Step 3: Verify — open Unity, check Console has zero errors**

Expected: no compiler errors. ObjectGrabController throw arc still works in Play Mode.

- [ ] **Step 4: Commit**

```bash
git add "Assets/Mahmoud SandBox/ObjectInteraction/ThrowMath.cs" "Assets/Mahmoud SandBox/ObjectInteraction/ObjectGrabController.cs"
git commit -m "refactor: extract ThrowMath.TryCalculateVelocity shared arc utility"
```

---

### Task 2: HitReactor Event + Pickupable HitPoint

Add an `OnImpact` C# event to `HitReactor` so `NPCHitReaction` can subscribe without modifying the existing hit flow. Update `Pickupable` to pass the collision contact point.

**Files:**
- Modify: `Assets/Mahmoud SandBox/ObjectInteraction/HitReactor.cs`
- Modify: `Assets/Mahmoud SandBox/ObjectInteraction/Pickupable.cs`

**Interfaces:**
- Produces: `HitReactor.OnImpact` event `System.Action<float, Vector3, Vector3>` — `(impulse, direction, hitPoint)`

- [ ] **Step 1: Update HitReactor.cs — full file replacement**

```csharp
using UnityEngine;

public class HitReactor : MonoBehaviour
{
    [SerializeField] float _minThrowSpeed   = 4f;
    [SerializeField] float _forceMultiplier = 20f;

    public event System.Action<float, Vector3, Vector3> OnImpact; // impulse, direction, hitPoint

    PuppetRagdollController _ragdoll;

    void Awake()
    {
        _ragdoll = GetComponentInChildren<PuppetRagdollController>();
        if (_ragdoll == null)
            _ragdoll = GetComponentInParent<PuppetRagdollController>();
    }

    public void TakeHit(float throwSpeed, Vector3 direction, Vector3 hitPoint = default)
    {
        if (throwSpeed < _minThrowSpeed) return;

        float impulse = throwSpeed * _forceMultiplier;
        OnImpact?.Invoke(impulse, direction, hitPoint);

        if (_ragdoll != null)
        {
            _ragdoll.ReportImpact(impulse);
            return;
        }

        foreach (Rigidbody rb in GetComponentsInChildren<Rigidbody>())
        {
            if (!rb.isKinematic)
                rb.AddForce(direction * throwSpeed * _forceMultiplier, ForceMode.Impulse);
        }
    }
}
```

- [ ] **Step 2: Update Pickupable.cs — pass contact point**

Replace `OnCollisionEnter`:

Old:
```csharp
void OnCollisionEnter(Collision collision)
{
    if (!_thrown) return;
    _thrown = false;

    HitReactor reactor = collision.gameObject.GetComponentInParent<HitReactor>();
    if (reactor != null)
        reactor.TakeHit(_thrownVelocity.magnitude, _thrownVelocity.normalized);
}
```

New:
```csharp
void OnCollisionEnter(Collision collision)
{
    if (!_thrown) return;
    _thrown = false;

    HitReactor reactor = collision.gameObject.GetComponentInParent<HitReactor>();
    if (reactor != null)
    {
        Vector3 hitPoint = collision.contacts.Length > 0 ? collision.contacts[0].point : collision.transform.position;
        reactor.TakeHit(_thrownVelocity.magnitude, _thrownVelocity.normalized, hitPoint);
    }
}
```

- [ ] **Step 3: Verify — Unity Console zero errors, existing HitReactor behaviour unchanged**

Throw an object at a character in Play Mode — still triggers ragdoll as before.

- [ ] **Step 4: Commit**

```bash
git add "Assets/Mahmoud SandBox/ObjectInteraction/HitReactor.cs" "Assets/Mahmoud SandBox/ObjectInteraction/Pickupable.cs"
git commit -m "feat: add OnImpact event to HitReactor, pass hitPoint from Pickupable"
```

---

### Task 3: NPCBrain — State Machine Core

The central coordinator. Owns the state machine, finds the player by tag, drives `CharacterController.Move` with RVO-adjusted velocity, and provides the public API all other components use.

**Files:**
- Create: `Assets/Mahmoud SandBox/NPC/NPCBrain.cs`

**Interfaces:**
- Consumes: `NPCPatroller.GetDesiredVelocity()`, `NPCChaser.GetDesiredVelocity()`, `NPCRVOAgent.ComputeAvoidanceVelocity(Vector3)`, `NPCHitVFX.PlayHitEffects(Vector3, Vector3)`
- Produces:
  - `NPCBrain.State → NPCState`
  - `NPCBrain.Player → Transform`
  - `NPCBrain.ReportHit(float impulse, Vector3 hitPoint, Vector3 hitDir)` — called by NPCHitReaction
  - `NPCBrain.RecoverFromHit()` — called by NPCHitReaction after hitRecoverTime

- [ ] **Step 1: Create NPCBrain.cs**

```csharp
using UnityEngine;

public enum NPCState { Patrol, Chase, Throw, HitReact }

[RequireComponent(typeof(CharacterController))]
[RequireComponent(typeof(Animator))]
public class NPCBrain : MonoBehaviour
{
    [Header("Ranges")]
    [SerializeField] float _chaseRange   = 15f;
    [SerializeField] float _throwRange   = 6f;
    [SerializeField] float _gravity      = 20f;

    [Header("Sub-components")]
    [SerializeField] NPCPatroller   _patroller;
    [SerializeField] NPCChaser      _chaser;
    [SerializeField] NPCThrower     _thrower;
    [SerializeField] NPCHitReaction _hitReaction;
    [SerializeField] NPCRVOAgent    _rvoAgent;
    [SerializeField] NPCHitVFX      _hitVFX;

    CharacterController     _cc;
    PuppetRagdollController _ragdoll;
    float                   _verticalVelocity;
    NPCState                _preHitState = NPCState.Patrol;

    public NPCState State  { get; private set; } = NPCState.Patrol;
    public Transform Player { get; private set; }

    void Awake()
    {
        _cc      = GetComponent<CharacterController>();
        var anim = GetComponent<Animator>();
        anim.applyRootMotion = false;

        _ragdoll = GetComponentInParent<PuppetRagdollController>()
                ?? transform.root.GetComponentInChildren<PuppetRagdollController>();

        GameObject p = GameObject.FindWithTag("Player");
        if (p != null) Player = p.transform;
    }

    void Update()
    {
        _verticalVelocity = _cc.isGrounded
            ? -1f
            : _verticalVelocity - _gravity * Time.deltaTime;

        bool knocked = _ragdoll != null && _ragdoll.State != PuppetPhysicsState.Balanced;
        if (knocked)
        {
            _cc.Move(Vector3.up * _verticalVelocity * Time.deltaTime);
            return;
        }

        if (Player == null) return;

        if (State != NPCState.HitReact)
            UpdateStateTransitions();

        Vector3 desired = GetDesiredVelocity();
        Vector3 moved   = _rvoAgent != null
            ? _rvoAgent.ComputeAvoidanceVelocity(desired)
            : desired;

        moved.y = _verticalVelocity;
        _cc.Move(moved * Time.deltaTime);
    }

    void UpdateStateTransitions()
    {
        Vector3 selfXZ   = new Vector3(transform.position.x, 0f, transform.position.z);
        Vector3 playerXZ = new Vector3(Player.position.x, 0f, Player.position.z);
        float   dist     = Vector3.Distance(selfXZ, playerXZ);

        switch (State)
        {
            case NPCState.Patrol:
                if (dist <= _chaseRange) ChangeState(NPCState.Chase);
                break;
            case NPCState.Chase:
                if (dist > _chaseRange)      ChangeState(NPCState.Patrol);
                else if (dist <= _throwRange) ChangeState(NPCState.Throw);
                break;
            case NPCState.Throw:
                if (dist > _throwRange) ChangeState(NPCState.Chase);
                break;
        }
    }

    void ChangeState(NPCState next)
    {
        State = next;
    }

    Vector3 GetDesiredVelocity()
    {
        return State switch
        {
            NPCState.Patrol   => _patroller  != null ? _patroller.GetDesiredVelocity()  : Vector3.zero,
            NPCState.Chase    => _chaser     != null ? _chaser.GetDesiredVelocity()      : Vector3.zero,
            NPCState.Throw    => Vector3.zero,
            NPCState.HitReact => Vector3.zero,
            _                 => Vector3.zero,
        };
    }

    public void ReportHit(float impulse, Vector3 hitPoint, Vector3 hitDir)
    {
        if (State == NPCState.HitReact) return;
        _preHitState = State;
        ChangeState(NPCState.HitReact);
        _hitVFX?.PlayHitEffects(hitPoint, hitDir);
    }

    public void RecoverFromHit() => ChangeState(_preHitState);

    void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, _chaseRange);
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position, _throwRange);
    }
}
```

- [ ] **Step 2: Verify — Unity Console zero errors**

Script compiles. Note: sub-components don't exist yet — Unity will show missing-script warnings in Inspector only, no errors.

- [ ] **Step 3: Commit**

```bash
git add "Assets/Mahmoud SandBox/NPC/NPCBrain.cs"
git commit -m "feat: add NPCBrain state machine (Patrol/Chase/Throw/HitReact)"
```

---

### Task 4: NPCPatroller — Patrol + Wander

Walks between waypoints (if assigned) or wanders randomly within a radius. Returns the desired XZ velocity vector to Brain each frame.

**Files:**
- Create: `Assets/Mahmoud SandBox/NPC/NPCPatroller.cs`

**Interfaces:**
- Consumes: `NPCBrain.State` (checked internally to idle when not Patrol)
- Produces: `NPCPatroller.GetDesiredVelocity() → Vector3`

- [ ] **Step 1: Create NPCPatroller.cs**

```csharp
using System.Collections;
using UnityEngine;

public class NPCPatroller : MonoBehaviour
{
    [Header("Waypoints")]
    [SerializeField] Transform[] _patrolPoints;
    [SerializeField] bool        _loopWaypoints = true;

    [Header("Wander (fallback)")]
    [SerializeField] float _wanderRadius    = 8f;
    [SerializeField] float _wanderPauseTime = 2f;

    [Header("Movement")]
    [SerializeField] float _patrolSpeed   = 2.5f;
    [SerializeField] float _rotateSpeed   = 5f;
    [SerializeField] float _arrivedRadius = 0.5f;

    static readonly int _speedHash = Animator.StringToHash("Speed");

    Animator  _animator;
    NPCBrain  _brain;
    Vector3   _spawnPos;
    Vector3   _target;
    int       _wpIndex;
    bool      _reversing;
    bool      _waiting;

    void Awake()
    {
        _animator = GetComponent<Animator>();
        _brain    = GetComponent<NPCBrain>();
        _spawnPos = transform.position;
    }

    void OnEnable() => PickNextTarget();

    public Vector3 GetDesiredVelocity()
    {
        if (_brain.State != NPCState.Patrol)
        {
            _animator.SetFloat(_speedHash, 0f, 0.1f, Time.deltaTime);
            return Vector3.zero;
        }

        if (_waiting)
        {
            _animator.SetFloat(_speedHash, 0f, 0.1f, Time.deltaTime);
            return Vector3.zero;
        }

        Vector3 toTarget = _target - transform.position;
        toTarget.y = 0f;

        if (toTarget.magnitude < _arrivedRadius)
        {
            OnArrived();
            return Vector3.zero;
        }

        Quaternion desired = Quaternion.LookRotation(toTarget.normalized);
        transform.rotation = Quaternion.RotateTowards(
            transform.rotation, desired, _rotateSpeed * 360f * Time.deltaTime);

        float normalized = Mathf.Clamp01(_patrolSpeed / 4f);
        _animator.SetFloat(_speedHash, normalized, 0.1f, Time.deltaTime);
        return toTarget.normalized * _patrolSpeed;
    }

    void PickNextTarget()
    {
        if (_patrolPoints != null && _patrolPoints.Length > 0)
        {
            _target = _patrolPoints[_wpIndex].position;
        }
        else
        {
            Vector2 rand = Random.insideUnitCircle * _wanderRadius;
            _target = _spawnPos + new Vector3(rand.x, 0f, rand.y);
        }
    }

    void OnArrived()
    {
        if (_patrolPoints != null && _patrolPoints.Length > 0)
        {
            AdvanceWaypoint();
            _target = _patrolPoints[_wpIndex].position;
        }
        else
        {
            StartCoroutine(WanderPause());
        }
    }

    void AdvanceWaypoint()
    {
        if (_loopWaypoints)
        {
            _wpIndex = (_wpIndex + 1) % _patrolPoints.Length;
            return;
        }

        if (!_reversing)
        {
            if (_wpIndex < _patrolPoints.Length - 1) _wpIndex++;
            else _reversing = true;
        }
        else
        {
            if (_wpIndex > 0) _wpIndex--;
            else _reversing = false;
        }
    }

    IEnumerator WanderPause()
    {
        _waiting = true;
        yield return new WaitForSeconds(_wanderPauseTime);
        _waiting = false;
        PickNextTarget();
    }

    void OnDrawGizmosSelected()
    {
        if (_patrolPoints == null || _patrolPoints.Length == 0)
        {
            Gizmos.color = Color.green;
            Gizmos.DrawWireSphere(Application.isPlaying ? _spawnPos : transform.position, _wanderRadius);
        }
        else
        {
            Gizmos.color = Color.green;
            for (int i = 0; i < _patrolPoints.Length; i++)
            {
                if (_patrolPoints[i] == null) continue;
                Gizmos.DrawSphere(_patrolPoints[i].position, 0.2f);
                if (i < _patrolPoints.Length - 1 && _patrolPoints[i + 1] != null)
                    Gizmos.DrawLine(_patrolPoints[i].position, _patrolPoints[i + 1].position);
            }
        }
    }
}
```

- [ ] **Step 2: Verify — zero compiler errors in Unity Console**

- [ ] **Step 3: Commit**

```bash
git add "Assets/Mahmoud SandBox/NPC/NPCPatroller.cs"
git commit -m "feat: add NPCPatroller with waypoint and random wander modes"
```

---

### Task 5: NPCChaser — Chase Movement

Moves NPC toward the player and rotates to face them. Returns the desired velocity to Brain.

**Files:**
- Create: `Assets/Mahmoud SandBox/NPC/NPCChaser.cs`

**Interfaces:**
- Consumes: `NPCBrain.Player`, `NPCBrain.State`
- Produces: `NPCChaser.GetDesiredVelocity() → Vector3`

- [ ] **Step 1: Create NPCChaser.cs**

```csharp
using UnityEngine;

public class NPCChaser : MonoBehaviour
{
    [SerializeField] float _chaseSpeed   = 4f;
    [SerializeField] float _rotateSpeed  = 8f;

    static readonly int _speedHash = Animator.StringToHash("Speed");

    Animator _animator;
    NPCBrain _brain;

    void Awake()
    {
        _animator = GetComponent<Animator>();
        _brain    = GetComponent<NPCBrain>();
    }

    public Vector3 GetDesiredVelocity()
    {
        if (_brain.State != NPCState.Chase || _brain.Player == null)
        {
            _animator.SetFloat(_speedHash, 0f, 0.1f, Time.deltaTime);
            return Vector3.zero;
        }

        Vector3 toPlayer = _brain.Player.position - transform.position;
        toPlayer.y = 0f;

        if (toPlayer.sqrMagnitude < 0.01f)
        {
            _animator.SetFloat(_speedHash, 0f, 0.1f, Time.deltaTime);
            return Vector3.zero;
        }

        Quaternion desired = Quaternion.LookRotation(toPlayer.normalized);
        transform.rotation = Quaternion.RotateTowards(
            transform.rotation, desired, _rotateSpeed * 360f * Time.deltaTime);

        _animator.SetFloat(_speedHash, 1f, 0.1f, Time.deltaTime);
        return toPlayer.normalized * _chaseSpeed;
    }
}
```

- [ ] **Step 2: Verify — zero compiler errors**

- [ ] **Step 3: Commit**

```bash
git add "Assets/Mahmoud SandBox/NPC/NPCChaser.cs"
git commit -m "feat: add NPCChaser movement and rotation toward player"
```

---

### Task 6: NPCRVOAgent — ORCA 2D Crowd Avoidance

Implements the full RVO2 ORCA algorithm in 2D (XZ plane). All agents register themselves in a static list on enable. Each agent computes ORCA half-planes against neighbors and solves a 2D LP to find the velocity closest to desired that avoids all constraints.

**Files:**
- Create: `Assets/Mahmoud SandBox/NPC/NPCRVOAgent.cs`

**Interfaces:**
- Produces: `NPCRVOAgent.ComputeAvoidanceVelocity(Vector3 desired) → Vector3`

- [ ] **Step 1: Create NPCRVOAgent.cs**

```csharp
using System.Collections.Generic;
using UnityEngine;

public class NPCRVOAgent : MonoBehaviour
{
    [SerializeField] public float agentRadius    = 0.5f;
    [SerializeField] float _neighborRadius       = 5f;
    [SerializeField] float _timeHorizon          = 2f;
    [SerializeField] float _maxSpeed             = 5f;

    static readonly List<NPCRVOAgent> s_All = new List<NPCRVOAgent>();

    Vector3 _velocity;

    public Vector3 Velocity => _velocity;
    public Vector3 Position => transform.position;

    void OnEnable()  => s_All.Add(this);
    void OnDisable() => s_All.Remove(this);

    public Vector3 ComputeAvoidanceVelocity(Vector3 desiredVelocity)
    {
        _velocity = desiredVelocity;

        var lines = new List<OrcaLine>();

        foreach (NPCRVOAgent other in s_All)
        {
            if (other == this) continue;

            Vector2 relPos      = XZ(other.Position - Position);
            float   combinedRad = agentRadius + other.agentRadius;
            if (relPos.magnitude > _neighborRadius + combinedRad) continue;

            Vector2 relVel = XZ(desiredVelocity) - XZ(other._velocity);
            float   dist   = relPos.magnitude;

            Vector2 u, n;

            if (dist > combinedRad)
            {
                Vector2 w    = relVel - relPos / _timeHorizon;
                float   wLen = w.magnitude;
                if (wLen < 1e-5f) continue;
                Vector2 unitW = w / wLen;

                if (Vector2.Dot(w, relPos) < combinedRad / _timeHorizon * wLen)
                {
                    u = (combinedRad / _timeHorizon - wLen) * unitW;
                    n = unitW;
                }
                else
                {
                    float leg = Mathf.Sqrt(Mathf.Max(0f, dist * dist - combinedRad * combinedRad));
                    if (Cross2D(relPos, w) > 0f)
                        n = new Vector2(relPos.x * leg - relPos.y * combinedRad,
                                        relPos.x * combinedRad + relPos.y * leg) / (dist * dist);
                    else
                        n = -(new Vector2( relPos.x * leg + relPos.y * combinedRad,
                                          -relPos.x * combinedRad + relPos.y * leg)) / (dist * dist);
                    u = (Vector2.Dot(relVel, n) - Vector2.Dot(relPos / _timeHorizon, n)) * n;
                }
            }
            else
            {
                float   inv  = 1f / Time.deltaTime;
                Vector2 w    = relVel - relPos * inv;
                float   wLen = w.magnitude;
                n = wLen > 1e-5f ? w / wLen : Vector2.up;
                u = (combinedRad * inv - wLen) * n;
            }

            lines.Add(new OrcaLine
            {
                point     = XZ(desiredVelocity) + 0.5f * u,
                direction = new Vector2(-n.y, n.x)
            });
        }

        Vector2 result = LP2(XZ(desiredVelocity), _maxSpeed, lines);
        return new Vector3(result.x, 0f, result.y);
    }

    // ---- LP solver (RVO2 algorithm) ----

    static Vector2 LP2(Vector2 preferred, float maxSpeed, List<OrcaLine> lines)
    {
        Vector2 result = Vector2.ClampMagnitude(preferred, maxSpeed);
        for (int i = 0; i < lines.Count; i++)
        {
            if (Cross2D(lines[i].direction, lines[i].point - result) > 0f)
            {
                if (!LP1(preferred, maxSpeed, lines, i, out Vector2 candidate))
                {
                    result = LP3(preferred, maxSpeed, lines, i);
                    break;
                }
                result = candidate;
            }
        }
        return result;
    }

    static bool LP1(Vector2 preferred, float maxSpeed,
        List<OrcaLine> lines, int lineNo, out Vector2 result)
    {
        OrcaLine line     = lines[lineNo];
        float    dotProd  = Vector2.Dot(line.point, line.direction);
        float    disc     = dotProd * dotProd + maxSpeed * maxSpeed - line.point.sqrMagnitude;

        if (disc < 0f) { result = default; return false; }

        float sqrt   = Mathf.Sqrt(disc);
        float tLeft  = -dotProd - sqrt;
        float tRight = -dotProd + sqrt;

        for (int i = 0; i < lineNo; i++)
        {
            float denom = Cross2D(line.direction, lines[i].direction);
            float num   = Cross2D(lines[i].direction, line.point - lines[i].point);

            if (Mathf.Abs(denom) < 1e-6f)
            {
                if (num < 0f) { result = default; return false; }
                continue;
            }
            float t = num / denom;
            if (denom > 0f) tRight = Mathf.Min(tRight, t);
            else             tLeft  = Mathf.Max(tLeft,  t);
            if (tLeft > tRight) { result = default; return false; }
        }

        float tPref = Vector2.Dot(line.direction, preferred - line.point);
        result = line.point + Mathf.Clamp(tPref, tLeft, tRight) * line.direction;
        return true;
    }

    static Vector2 LP3(Vector2 preferred, float maxSpeed,
        List<OrcaLine> lines, int numLines)
    {
        float   distance = 0f;
        Vector2 result   = Vector2.ClampMagnitude(preferred, maxSpeed);

        for (int i = numLines; i < lines.Count; i++)
        {
            if (Cross2D(lines[i].direction, lines[i].point - result) <= distance) continue;

            var proj = new List<OrcaLine>();
            for (int j = 0; j < i; j++)
            {
                float denom = Cross2D(lines[i].direction, lines[j].direction);
                if (Mathf.Abs(denom) < 1e-6f)
                {
                    if (Vector2.Dot(lines[i].direction, lines[j].direction) > 0f) continue;
                    proj.Add(new OrcaLine
                    {
                        point     = 0.5f * (lines[i].point + lines[j].point),
                        direction = (lines[i].direction + lines[j].direction).normalized
                    });
                    continue;
                }
                float t = Cross2D(lines[j].direction, lines[i].point - lines[j].point) / denom;
                proj.Add(new OrcaLine
                {
                    point     = lines[i].point + t * lines[i].direction,
                    direction = (lines[i].direction - lines[j].direction).normalized
                });
            }
            proj.Add(new OrcaLine
            {
                point     = lines[i].point,
                direction = new Vector2(-lines[i].direction.y, lines[i].direction.x)
            });

            Vector2 preferred3 = new Vector2(-lines[i].direction.y, lines[i].direction.x) * maxSpeed;
            result   = LP2(preferred3, maxSpeed, proj);
            distance = Cross2D(lines[i].direction, lines[i].point - result);
        }
        return result;
    }

    static float   Cross2D(Vector2 a, Vector2 b) => a.x * b.y - a.y * b.x;
    static Vector2 XZ(Vector3 v)                 => new Vector2(v.x, v.z);

    struct OrcaLine { public Vector2 point, direction; }
}
```

- [ ] **Step 2: Verify — zero compiler errors**

- [ ] **Step 3: Commit**

```bash
git add "Assets/Mahmoud SandBox/NPC/NPCRVOAgent.cs"
git commit -m "feat: add NPCRVOAgent with full 2D ORCA crowd avoidance"
```

---

### Task 7: NPCThrower — Object Throw System

Grabs a nearby `Pickupable` from the scene or spawns a prefab, then throws it at the player using the shared `ThrowMath` arc formula.

**Files:**
- Create: `Assets/Mahmoud SandBox/NPC/NPCThrower.cs`

**Interfaces:**
- Consumes: `NPCBrain.State`, `NPCBrain.Player`, `ThrowMath.TryCalculateVelocity`, `Pickupable.MarkPickedUp()`, `Pickupable.MarkThrown(Vector3)`
- Produces: `NPCThrower.ReleaseThrowable()` — callable from Animator event on the Throw clip

- [ ] **Step 1: Create NPCThrower.cs**

```csharp
using System.Collections;
using UnityEngine;

public class NPCThrower : MonoBehaviour
{
    [Header("Acquisition")]
    [SerializeField] float        _pickupRadius = 4f;
    [SerializeField] GameObject[] _throwablePrefabs;
    [SerializeField] Transform    _handBone;

    [Header("Throw")]
    [SerializeField] float _throwAngle        = 45f;
    [SerializeField] float _throwCooldown     = 2.5f;
    [SerializeField] float _throwReleaseDelay = 0.3f;
    [SerializeField] float _rotateSpeed       = 8f;

    static readonly int _throwHash = Animator.StringToHash("Throw");

    Animator  _animator;
    NPCBrain  _brain;
    Rigidbody _held;
    Collider  _heldCollider;
    float     _nextThrowTime;
    bool      _throwing;

    void Awake()
    {
        _animator = GetComponent<Animator>();
        _brain    = GetComponent<NPCBrain>();
    }

    void Update()
    {
        if (_brain.State != NPCState.Throw) return;
        if (_throwing) return;
        if (Time.time < _nextThrowTime) return;
        if (_brain.Player == null) return;

        // Face player before throwing
        Vector3 toPlayer = _brain.Player.position - transform.position;
        toPlayer.y = 0f;
        if (toPlayer.sqrMagnitude > 0.01f)
            transform.rotation = Quaternion.RotateTowards(
                transform.rotation,
                Quaternion.LookRotation(toPlayer.normalized),
                _rotateSpeed * 360f * Time.deltaTime);

        StartCoroutine(ThrowSequence());
    }

    void OnDisable()
    {
        if (_held != null) DropHeld();
        _throwing = false;
    }

    IEnumerator ThrowSequence()
    {
        _throwing = true;

        if (!TryGrabSceneObject())
            TrySpawnThrowable();

        if (_held == null)
        {
            _throwing      = false;
            _nextThrowTime = Time.time + _throwCooldown;
            yield break;
        }

        _animator.SetTrigger(_throwHash);
        yield return new WaitForSeconds(_throwReleaseDelay);

        ReleaseThrowable();
        _nextThrowTime = Time.time + _throwCooldown;
        _throwing      = false;
    }

    bool TryGrabSceneObject()
    {
        Pickupable best     = null;
        float      bestDist = _pickupRadius;

        foreach (Pickupable p in Object.FindObjectsByType<Pickupable>(FindObjectsSortMode.None))
        {
            if (p.transform.parent != null) continue; // already held
            float d = Vector3.Distance(transform.position, p.transform.position);
            if (d < bestDist) { best = p; bestDist = d; }
        }

        if (best == null) return false;

        best.MarkPickedUp();
        Grab(best.GetComponent<Rigidbody>(), best.GetComponent<Collider>());
        return _held != null;
    }

    void TrySpawnThrowable()
    {
        if (_throwablePrefabs == null || _throwablePrefabs.Length == 0) return;

        GameObject prefab  = _throwablePrefabs[Random.Range(0, _throwablePrefabs.Length)];
        Transform  anchor  = _handBone != null ? _handBone : transform;
        GameObject spawned = Instantiate(prefab, anchor.position, anchor.rotation);

        if (spawned.GetComponent<Pickupable>() == null)
            spawned.AddComponent<Pickupable>();
        if (spawned.GetComponent<Rigidbody>() == null)
            spawned.AddComponent<Rigidbody>();

        spawned.GetComponent<Pickupable>().MarkPickedUp();
        Grab(spawned.GetComponent<Rigidbody>(), spawned.GetComponent<Collider>());
    }

    void Grab(Rigidbody rb, Collider col)
    {
        if (rb == null) return;
        _held             = rb;
        _heldCollider     = col;
        _held.isKinematic = true;
        if (_heldCollider != null) _heldCollider.enabled = false;

        Transform anchor = _handBone != null ? _handBone : transform;
        _held.transform.SetParent(anchor);
        _held.transform.localPosition = Vector3.zero;
        _held.transform.localRotation = Quaternion.identity;
    }

    // Called by Animator event on Throw clip keyframe, or by coroutine timer
    public void ReleaseThrowable()
    {
        if (_held == null) return;

        Rigidbody  toThrow  = _held;
        Pickupable pickable = _held.GetComponent<Pickupable>();

        DoRelease();

        if (_brain.Player != null)
        {
            Vector3 from = toThrow.transform.position;
            Vector3 to   = _brain.Player.position + Vector3.up * 1f;

            if (ThrowMath.TryCalculateVelocity(from, to, _throwAngle, out Vector3 vel))
            {
                toThrow.linearVelocity  = vel;
                toThrow.angularVelocity = Random.insideUnitSphere * 5f;
                pickable?.MarkThrown(vel);
            }
        }

        _held         = null;
        _heldCollider = null;
    }

    void DropHeld()
    {
        if (_held == null) return;
        DoRelease();
        _held         = null;
        _heldCollider = null;
    }

    void DoRelease()
    {
        _held.transform.SetParent(null);
        _held.isKinematic = false;
        if (_heldCollider != null) _heldCollider.enabled = true;
    }
}
```

- [ ] **Step 2: Verify — zero compiler errors**

- [ ] **Step 3: Commit**

```bash
git add "Assets/Mahmoud SandBox/NPC/NPCThrower.cs"
git commit -m "feat: add NPCThrower with scene pickup and prefab spawn fallback"
```

---

### Task 8: NPCHitReaction — Hit Detection + State Interrupt

Subscribes to `HitReactor.OnImpact`. Light hits (below knockdown threshold) play the hit animation and temporarily interrupt the state machine. Heavy hits are handled by the existing `PuppetRagdollController` pipeline.

**Files:**
- Create: `Assets/Mahmoud SandBox/NPC/NPCHitReaction.cs`

**Interfaces:**
- Consumes: `HitReactor.OnImpact` event, `NPCBrain.ReportHit(float, Vector3, Vector3)`, `NPCBrain.RecoverFromHit()`
- Produces: nothing (leaf component)

- [ ] **Step 1: Create NPCHitReaction.cs**

```csharp
using System.Collections;
using UnityEngine;

[RequireComponent(typeof(NPCBrain))]
public class NPCHitReaction : MonoBehaviour
{
    [SerializeField] float _hitThreshold       = 20f;
    [SerializeField] float _knockdownThreshold = 80f;
    [SerializeField] float _hitRecoverTime     = 0.8f;

    static readonly int _hitReactHash = Animator.StringToHash("HitReact");

    NPCBrain  _brain;
    Animator  _animator;
    HitReactor _hitReactor;

    void Awake()
    {
        _brain    = GetComponent<NPCBrain>();
        _animator = GetComponent<Animator>();

        _hitReactor = GetComponentInParent<HitReactor>();
        if (_hitReactor == null)
            _hitReactor = transform.root.GetComponentInChildren<HitReactor>();
    }

    void OnEnable()
    {
        if (_hitReactor != null) _hitReactor.OnImpact += HandleImpact;
    }

    void OnDisable()
    {
        if (_hitReactor != null) _hitReactor.OnImpact -= HandleImpact;
    }

    void HandleImpact(float impulse, Vector3 direction, Vector3 hitPoint)
    {
        if (impulse < _hitThreshold)       return; // too small to react
        if (impulse >= _knockdownThreshold) return; // ragdoll handles heavy hits
        if (_brain.State == NPCState.HitReact) return; // already in hit react

        _brain.ReportHit(impulse, hitPoint, direction);
        _animator.SetTrigger(_hitReactHash);
        StartCoroutine(RecoverAfter(_hitRecoverTime));
    }

    IEnumerator RecoverAfter(float delay)
    {
        yield return new WaitForSeconds(delay);
        _brain.RecoverFromHit();
    }
}
```

- [ ] **Step 2: Verify — zero compiler errors**

- [ ] **Step 3: Commit**

```bash
git add "Assets/Mahmoud SandBox/NPC/NPCHitReaction.cs"
git commit -m "feat: add NPCHitReaction subscribing to HitReactor.OnImpact"
```

---

### Task 9: NPCHitVFX — Material Flash + Particles + Camera Shake

Three simultaneous visual effects triggered by `NPCBrain.ReportHit`.

**Files:**
- Create: `Assets/Mahmoud SandBox/NPC/NPCHitVFX.cs`

**Interfaces:**
- Consumes: called by `NPCBrain.ReportHit` via `_hitVFX.PlayHitEffects(hitPoint, hitDir)`
- Produces: `NPCHitVFX.PlayHitEffects(Vector3 hitPoint, Vector3 hitDir)`

- [ ] **Step 1: Create NPCHitVFX.cs**

```csharp
using System.Collections;
using UnityEngine;

public class NPCHitVFX : MonoBehaviour
{
    [Header("Flash")]
    [SerializeField] Material _hitMaterial;
    [SerializeField] float    _flashDuration = 0.15f;

    [Header("Particles")]
    [SerializeField] GameObject _hitParticlePrefab;

    [Header("Camera Shake")]
    [SerializeField] float _shakeDuration  = 0.2f;
    [SerializeField] float _shakeMagnitude = 0.1f;

    SkinnedMeshRenderer[] _renderers;
    Material[][]          _originalMats;
    Coroutine             _flashRoutine;
    Coroutine             _shakeRoutine;

    void Awake()
    {
        _renderers    = GetComponentsInChildren<SkinnedMeshRenderer>();
        _originalMats = new Material[_renderers.Length][];
        for (int i = 0; i < _renderers.Length; i++)
            _originalMats[i] = _renderers[i].materials;
    }

    public void PlayHitEffects(Vector3 hitPoint, Vector3 hitDir)
    {
        Vector3 pos = hitPoint != Vector3.zero ? hitPoint : transform.position + Vector3.up;

        if (_flashRoutine != null) StopCoroutine(_flashRoutine);
        if (_shakeRoutine != null) StopCoroutine(_shakeRoutine);
        _flashRoutine = StartCoroutine(FlashRoutine());
        _shakeRoutine = StartCoroutine(ShakeRoutine());
        SpawnParticle(pos, hitDir);
    }

    void SpawnParticle(Vector3 pos, Vector3 dir)
    {
        if (_hitParticlePrefab == null) return;

        Quaternion rot = dir.sqrMagnitude > 0.01f
            ? Quaternion.LookRotation(dir)
            : Quaternion.identity;

        GameObject fx = Instantiate(_hitParticlePrefab, pos, rot);
        ParticleSystem ps = fx.GetComponent<ParticleSystem>();
        if (ps != null)
        {
            ps.Play();
            Destroy(fx, ps.main.duration + ps.main.startLifetime.constantMax + 0.5f);
        }
        else Destroy(fx, 3f);
    }

    IEnumerator FlashRoutine()
    {
        if (_hitMaterial == null) yield break;

        for (int i = 0; i < _renderers.Length; i++)
        {
            var flash = new Material[_renderers[i].materials.Length];
            System.Array.Fill(flash, _hitMaterial);
            _renderers[i].materials = flash;
        }

        yield return new WaitForSeconds(_flashDuration);

        for (int i = 0; i < _renderers.Length; i++)
            _renderers[i].materials = _originalMats[i];

        _flashRoutine = null;
    }

    IEnumerator ShakeRoutine()
    {
        Camera cam = Camera.main;
        if (cam == null) yield break;

        Vector3 origin  = cam.transform.localPosition;
        float   elapsed = 0f;

        while (elapsed < _shakeDuration)
        {
            float x = (Mathf.PerlinNoise(elapsed * 10f, 0f) * 2f - 1f) * _shakeMagnitude;
            float y = (Mathf.PerlinNoise(0f, elapsed * 10f) * 2f - 1f) * _shakeMagnitude;
            cam.transform.localPosition = origin + new Vector3(x, y, 0f);
            elapsed += Time.deltaTime;
            yield return null;
        }

        cam.transform.localPosition = origin;
        _shakeRoutine = null;
    }
}
```

- [ ] **Step 2: Verify — zero compiler errors**

- [ ] **Step 3: Commit**

```bash
git add "Assets/Mahmoud SandBox/NPC/NPCHitVFX.cs"
git commit -m "feat: add NPCHitVFX with material flash, particle burst, and camera shake"
```

---

### Task 10: NPCSpawner — Wave-Based Spawning

Spawns NPCs in configurable waves at scene-placed spawn points or random positions within a radius. Enforces minimum separation between spawned NPC positions.

**Files:**
- Create: `Assets/Mahmoud SandBox/NPC/NPCSpawner.cs`

**Interfaces:**
- Produces:
  - `NPCSpawner.StartWaves()` — begin from wave 0
  - `NPCSpawner.SpawnWave(int index)` — spawn a specific wave
  - `NPCSpawner.GetLivingNPCCount() → int`

- [ ] **Step 1: Create NPCSpawner.cs**

```csharp
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[System.Serializable]
public class WaveConfig
{
    public int        npcCount           = 3;
    public GameObject npcPrefab;
    public float      delayBetweenSpawns = 0.5f;
    public float      delayAfterWave     = 5f;
}

public class NPCSpawner : MonoBehaviour
{
    [Header("Waves")]
    [SerializeField] WaveConfig[] _waves;
    [SerializeField] bool         _autoAdvanceWaves        = true;
    [SerializeField] bool         _triggerNextWaveOnAllDead = false;

    [Header("Spawn Locations")]
    [SerializeField] Transform[] _spawnPoints;
    [SerializeField] float       _spawnRadius     = 10f;
    [SerializeField] float       _spawnSeparation = 2f;

    readonly List<GameObject> _living = new List<GameObject>();

    void Start() => StartWaves();

    public void StartWaves() => StartCoroutine(RunWaves());

    public void SpawnWave(int index)
    {
        if (index >= 0 && index < _waves.Length)
            StartCoroutine(SpawnWaveCoroutine(_waves[index]));
    }

    public int GetLivingNPCCount()
    {
        _living.RemoveAll(n => n == null);
        return _living.Count;
    }

    IEnumerator RunWaves()
    {
        for (int i = 0; i < _waves.Length; i++)
        {
            yield return StartCoroutine(SpawnWaveCoroutine(_waves[i]));

            if (_triggerNextWaveOnAllDead)
                yield return new WaitUntil(() => GetLivingNPCCount() == 0);

            if (_autoAdvanceWaves && i < _waves.Length - 1)
                yield return new WaitForSeconds(_waves[i].delayAfterWave);
        }
    }

    IEnumerator SpawnWaveCoroutine(WaveConfig wave)
    {
        if (wave.npcPrefab == null) yield break;

        var used = new List<Vector3>();

        for (int i = 0; i < wave.npcCount; i++)
        {
            Vector3    pos = ChooseSpawnPosition(used);
            used.Add(pos);
            GameObject npc = Instantiate(wave.npcPrefab, pos, Quaternion.Euler(0f, Random.Range(0f, 360f), 0f));
            _living.Add(npc);
            yield return new WaitForSeconds(wave.delayBetweenSpawns);
        }
    }

    Vector3 ChooseSpawnPosition(List<Vector3> used)
    {
        if (_spawnPoints != null && _spawnPoints.Length > 0)
        {
            foreach (Transform sp in _spawnPoints)
            {
                if (sp == null) continue;
                if (IsClear(sp.position, used)) return sp.position;
            }
        }

        for (int attempt = 0; attempt < 30; attempt++)
        {
            Vector2 rand = Random.insideUnitCircle * _spawnRadius;
            Vector3 pos  = transform.position + new Vector3(rand.x, 0f, rand.y);
            if (IsClear(pos, used)) return pos;
        }

        Vector2 fallback = Random.insideUnitCircle * _spawnRadius;
        return transform.position + new Vector3(fallback.x, 0f, fallback.y);
    }

    bool IsClear(Vector3 pos, List<Vector3> used)
    {
        foreach (Vector3 u in used)
            if (Vector3.Distance(pos, u) < _spawnSeparation)
                return false;
        return true;
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(transform.position, _spawnRadius);

        if (_spawnPoints == null) return;
        Gizmos.color = Color.blue;
        foreach (Transform sp in _spawnPoints)
            if (sp != null) Gizmos.DrawSphere(sp.position, 0.3f);
    }
}
```

- [ ] **Step 2: Verify — zero compiler errors**

- [ ] **Step 3: Commit**

```bash
git add "Assets/Mahmoud SandBox/NPC/NPCSpawner.cs"
git commit -m "feat: add NPCSpawner with wave config and spawn point/radius fallback"
```

---

### Task 11: AdvancedNPCSetupTool — One-Click Editor Wiring

Editor-only tool that duplicates the Ch06 hierarchy, strips old components, adds all new NPC components, wires SerializedObject references, sets ragdoll threshold to 80, assigns the animator controller, and creates an NPCSpawner in the scene.

**Files:**
- Create: `Assets/Mahmoud SandBox/NPC/Editor/AdvancedNPCSetupTool.cs`

**Interfaces:**
- Consumes: all NPC component types, existing `NPCSetupTool` constants (does not modify that file)
- Produces: `Tools → Advanced NPC → Setup Advanced NPC` menu item

- [ ] **Step 1: Create AdvancedNPCSetupTool.cs**

```csharp
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using RootMotion.Dynamics;

public static class AdvancedNPCSetupTool
{
    const string SourceRootName = "Ch06_nonPBR Root";
    const string AnimTargetName = "Ch06_nonPBR";
    const string PlayerName     = "ThirdPersonPuppet (1)";

    [MenuItem("Tools/Advanced NPC/Setup Advanced NPC")]
    static void SetupAdvancedNPC()
    {
        GameObject source = GameObject.Find(SourceRootName);
        if (source == null)
        {
            Debug.LogError($"[AdvancedNPC] '{SourceRootName}' not found. Open the scene that contains it first.");
            return;
        }

        // 1. Duplicate hierarchy
        GameObject npc = Object.Instantiate(source, source.transform.parent);
        Undo.RegisterCreatedObjectUndo(npc, "Create Advanced NPC");
        npc.name               = "AdvancedNPC_Ch06";
        npc.transform.position = source.transform.position + new Vector3(3f, 0f, 0f);

        // 2. Find animated target
        Transform animTf = npc.transform.Find(AnimTargetName);
        if (animTf == null)
        {
            Debug.LogError($"[AdvancedNPC] '{AnimTargetName}' not found under NPC root.");
            Undo.DestroyObjectImmediate(npc);
            return;
        }
        GameObject animTarget = animTf.gameObject;

        // 3. Strip old movement components
        RemoveIfPresent<PuppetMoverSimple>(animTarget);
        RemoveIfPresent<NPCChaseController>(animTarget);

        // 4. Add all new NPC components
        NPCBrain       brain   = GetOrAdd<NPCBrain>(animTarget);
        NPCPatroller   patrol  = GetOrAdd<NPCPatroller>(animTarget);
        NPCChaser      chaser  = GetOrAdd<NPCChaser>(animTarget);
        NPCThrower     thrower = GetOrAdd<NPCThrower>(animTarget);
        NPCHitReaction hitReac = GetOrAdd<NPCHitReaction>(animTarget);
        NPCRVOAgent    rvo     = GetOrAdd<NPCRVOAgent>(animTarget);
        NPCHitVFX      vfx     = GetOrAdd<NPCHitVFX>(animTarget);

        // 5. Wire Brain's SerializeField references
        var brainSO = new SerializedObject(brain);
        brainSO.FindProperty("_patroller").objectReferenceValue   = patrol;
        brainSO.FindProperty("_chaser").objectReferenceValue      = chaser;
        brainSO.FindProperty("_thrower").objectReferenceValue     = thrower;
        brainSO.FindProperty("_hitReaction").objectReferenceValue = hitReac;
        brainSO.FindProperty("_rvoAgent").objectReferenceValue    = rvo;
        brainSO.FindProperty("_hitVFX").objectReferenceValue      = vfx;
        brainSO.ApplyModifiedProperties();

        // 6. HitReactor on NPC root
        GetOrAdd<HitReactor>(npc);

        // 7. Configure ragdoll for NPC (lower threshold than player's 200)
        PuppetRagdollController ragdoll = npc.GetComponentInChildren<PuppetRagdollController>();
        if (ragdoll != null)
        {
            Undo.RecordObject(ragdoll, "Configure NPC ragdoll");
            ragdoll.mover              = null;
            ragdoll.knockdownThreshold = 80f;
            EditorUtility.SetDirty(ragdoll);
        }
        else Debug.LogWarning("[AdvancedNPC] PuppetRagdollController not found — run 'Setup Ch06 Movement' on source first.");

        // 8. Assign AnimatorController
        string[] guids = AssetDatabase.FindAssets("CharacterAnimaiton t:AnimatorController");
        if (guids.Length > 0)
        {
            var ctrl = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(
                AssetDatabase.GUIDToAssetPath(guids[0]));
            Animator anim = animTarget.GetComponent<Animator>();
            if (anim != null && ctrl != null)
            {
                Undo.RecordObject(anim, "Assign NPC AnimatorController");
                anim.runtimeAnimatorController = ctrl;
                anim.applyRootMotion           = false;
                EditorUtility.SetDirty(anim);
            }
        }
        else Debug.LogWarning("[AdvancedNPC] 'CharacterAnimaiton' animator controller not found — assign manually.");

        // 9. Create NPCSpawner in scene
        GameObject spawnerGO = new GameObject("NPCSpawner");
        Undo.RegisterCreatedObjectUndo(spawnerGO, "Create NPCSpawner");
        spawnerGO.transform.position = source.transform.position + new Vector3(0f, 0f, 8f);
        spawnerGO.AddComponent<NPCSpawner>();

        EditorSceneManager.MarkSceneDirty(npc.scene);

        Debug.Log(
            $"[AdvancedNPC] ✓ Created '{npc.name}' at {npc.transform.position}\n" +
             "  Components: NPCBrain, NPCPatroller, NPCChaser, NPCThrower, NPCHitReaction, NPCRVOAgent, NPCHitVFX\n" +
             "  HitReactor: on NPC root\n" +
             "  Ragdoll knockdownThreshold: 80\n" +
            $"  NPCSpawner: '{spawnerGO.name}' at {spawnerGO.transform.position}\n" +
             "  → In Inspector: assign Patrol Points, Throwable Prefabs, Hand Bone,\n" +
             "    Hit Material, Hit Particle Prefab on NPCHitVFX,\n" +
             "    and NPC Prefab + Wave Configs on NPCSpawner.\n" +
             "  → In Animator: add 'Throw' Trigger and 'HitReact' Trigger parameters.\n" +
             "    Optionally add animation event on Throw clip calling NPCThrower.ReleaseThrowable().");
    }

    static T GetOrAdd<T>(GameObject go) where T : Component
    {
        var c = go.GetComponent<T>();
        return c != null ? c : Undo.AddComponent<T>(go);
    }

    static void RemoveIfPresent<T>(GameObject go) where T : Component
    {
        var c = go.GetComponent<T>();
        if (c != null) Undo.DestroyObjectImmediate(c);
    }
}
```

- [ ] **Step 2: Verify — zero compiler errors, menu item `Tools → Advanced NPC → Setup Advanced NPC` appears**

- [ ] **Step 3: End-to-end smoke test**

  1. Open a scene that has `Ch06_nonPBR Root` and `ThirdPersonPuppet (1)`
  2. Click `Tools → Advanced NPC → Setup Advanced NPC`
  3. Verify in Hierarchy: `AdvancedNPC_Ch06` with `Ch06_nonPBR` child, `NPCSpawner` sibling
  4. Select `Ch06_nonPBR` — verify NPCBrain, NPCPatroller, NPCChaser, NPCThrower, NPCHitReaction, NPCRVOAgent, NPCHitVFX all present
  5. Verify NPCBrain's Inspector fields `_patroller`, `_chaser`, etc. are all wired
  6. Select `AdvancedNPC_Ch06` root — verify HitReactor present
  7. Press Play — NPC should idle/wander (Speed=0 or walk)
  8. Walk player within 15m — NPC should begin chasing (Animator Speed → 1)
  9. Walk player within 6m — NPC should face you and attempt to throw
  10. Throw an object at NPC — verify hit reaction animation triggers and VFX plays

- [ ] **Step 4: Commit**

```bash
git add "Assets/Mahmoud SandBox/NPC/Editor/AdvancedNPCSetupTool.cs"
git commit -m "feat: add AdvancedNPCSetupTool editor menu for one-click NPC wiring"
```

---

## Post-Setup Inspector Checklist

After running the setup tool, manually configure these in the Inspector:

**On `Ch06_nonPBR` (animated target):**
- `NPCPatroller._patrolPoints` — assign patrol waypoint Transforms (or leave empty for random wander)
- `NPCThrower._throwablePrefabs` — assign at least one throwable GameObject prefab (any Rigidbody object)
- `NPCThrower._handBone` — assign the hand bone (e.g. `mixamorig:RightHand`) for proper throw origin
- `NPCHitVFX._hitMaterial` — create a flat white/red Material and assign it
- `NPCHitVFX._hitParticlePrefab` — create or assign a ParticleSystem prefab

**In Animator Controller (`CharacterAnimaiton.controller`):**
- Add `Throw` parameter (Trigger)
- Add `HitReact` parameter (Trigger)
- Add transitions from Any State → HitReact state (on HitReact trigger)
- Add transitions from Any State → Throw state (on Throw trigger)
- Optional: add Animation Event on the Throw clip calling `NPCThrower.ReleaseThrowable()` for frame-perfect release

**On `NPCSpawner`:**
- Assign `_waves` array with npcCount, npcPrefab (the NPC prefab), delays
- Optionally assign `_spawnPoints` Transform array

---

## Spec Coverage Check

| Spec Requirement | Task |
|---|---|
| Chase player by "Player" tag | Task 3 (NPCBrain.Awake) |
| Animation (Speed param) | Tasks 4, 5 |
| Patrol mode with waypoints | Task 4 |
| Random wander fallback | Task 4 |
| Patrol → Chase on range | Task 3 |
| Throw when close (throwRange) | Tasks 3, 7 |
| Grab scene Pickupable | Task 7 |
| Spawn prefab fallback | Task 7 |
| Thrown objects catchable by player | Task 7 (MarkThrown) |
| ThrowMath shared arc | Task 1 |
| Hit reaction interrupt | Task 8 |
| HitReact animation trigger | Task 8 |
| Material flash VFX | Task 9 |
| Particle burst VFX | Task 9 |
| Camera shake VFX | Task 9 |
| Full ragdoll for heavy hits | Task 2 (HitReactor event) + Task 8 (threshold check) |
| NPC spawner with wave config | Task 10 |
| Spawn at points or random radius | Task 10 |
| Auto-advance + on-all-dead waves | Task 10 |
| NPC-NPC collision avoidance (RVO) | Task 6 |
| Spawn separation enforcement | Task 10 |
| Editor one-click setup | Task 11 |
