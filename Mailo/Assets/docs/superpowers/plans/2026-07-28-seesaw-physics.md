# Seesaw Physics System Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Three scripts that make a flat trigger-zone seesaw — characters jumping on Side A launch the character on Side B upward with ragdoll, scaled by combined weight.

**Architecture:** `SeesawParticipant` (data + launch logic on each character) → `SeesawSide` (trigger tracker + jump detector on each zone) → `SeesawCoordinator` (weight summer + force applier on the seesaw root). Side A jump triggers the chain; Side B occupant gets the impulse.

**Tech Stack:** Unity 6, C#, Rigidbody physics, PuppetMaster (RootMotion.Dynamics), `ForceMode.Impulse`

## Global Constraints

- Unity 6: use `rb.linearVelocity` NOT `rb.velocity`; use `rb.linearDamping` NOT `rb.drag`
- All files go in `Assets/Mahmoud SandBox/Seesaw/`
- No editor tools — pure runtime scripts, Inspector drag-and-drop setup
- `PuppetRagdollController.ReportImpact(float impulse)` — one parameter only
- `PhysicsCharacterController.ForceRagdoll()` — no parameters
- No existing test infrastructure — testing is manual Play Mode verification

---

### Task 1: SeesawParticipant — weight data + launch dispatch

**Files:**
- Create: `Assets/Mahmoud SandBox/Seesaw/SeesawParticipant.cs`

**Interfaces:**
- Produces:
  - `float WeightKg` — property
  - `void ApplyLaunch(Vector3 impulse)` — called by SeesawCoordinator
  - `Rigidbody Rb` — public property, used by SeesawSide for velocity check

- [ ] **Step 1: Create the file with cached references**

```csharp
using System.Collections;
using UnityEngine;

public class SeesawParticipant : MonoBehaviour
{
    [SerializeField] float _weightKg = 70f;

    public float     WeightKg => _weightKg;
    public Rigidbody Rb       { get; private set; }

    PhysicsCharacterController _physicsCtrl;
    PuppetRagdollController    _puppetCtrl;

    void Awake()
    {
        Rb           = GetComponent<Rigidbody>() ?? GetComponentInParent<Rigidbody>();
        _physicsCtrl = GetComponent<PhysicsCharacterController>()
                    ?? GetComponentInParent<PhysicsCharacterController>()
                    ?? GetComponentInChildren<PhysicsCharacterController>();
        _puppetCtrl  = GetComponent<PuppetRagdollController>()
                    ?? GetComponentInParent<PuppetRagdollController>()
                    ?? GetComponentInChildren<PuppetRagdollController>();
    }

    public void ApplyLaunch(Vector3 impulse)
    {
        if (_physicsCtrl != null)
        {
            Rb.AddForce(impulse, ForceMode.Impulse);
            _physicsCtrl.ForceRagdoll();
            return;
        }

        if (_puppetCtrl != null)
        {
            StartCoroutine(LaunchNPC(impulse));
            return;
        }

        // Generic fallback
        if (Rb != null)
            Rb.AddForce(impulse, ForceMode.Impulse);
    }

    IEnumerator LaunchNPC(Vector3 impulse)
    {
        // Force ragdoll first (sets pm.pinWeight = 0 immediately)
        _puppetCtrl.ReportImpact(Mathf.Max(impulse.magnitude, _puppetCtrl.knockdownThreshold + 1f));
        // Wait one physics step for PuppetMaster to release pin
        yield return new WaitForFixedUpdate();
        // Apply force to hips so ragdoll body flies
        Rigidbody hips = _puppetCtrl.muscleBodies != null && _puppetCtrl.muscleBodies.Length > 0
            ? _puppetCtrl.muscleBodies[0]
            : Rb;
        if (hips != null)
            hips.AddForce(impulse, ForceMode.Impulse);
    }
}
```

- [ ] **Step 2: Open Unity and verify the script compiles with no errors**

  Check the Unity Console — zero errors expected. If `PuppetRagdollController` is not found, confirm `using RootMotion.Dynamics;` is not needed (it's already in the same assembly).

- [ ] **Step 3: Add `SeesawParticipant` to the player in the scene**

  Select `Testing_Player` root → Add Component → `SeesawParticipant` → set `Weight Kg = 70`. Verify `Rb` is found (inspect via Debug mode in Inspector — `Rb` will show the Rigidbody reference).

- [ ] **Step 4: Commit**

```bash
git add "Assets/Mahmoud SandBox/Seesaw/SeesawParticipant.cs"
git commit -m "feat: add SeesawParticipant — weight data and launch dispatch"
```

---

### Task 2: SeesawSide — trigger tracking and jump detection

**Files:**
- Create: `Assets/Mahmoud SandBox/Seesaw/SeesawSide.cs`

**Interfaces:**
- Consumes:
  - `SeesawParticipant` on colliding GameObjects
  - `SeesawCoordinator.NotifyJump(SeesawParticipant jumper)` — called on detected jump
- Produces:
  - `IReadOnlyList<SeesawParticipant> Occupants` — current occupants (excludes jumper at notification time)
  - `void Init(SeesawCoordinator coordinator)` — called by coordinator in Awake

- [ ] **Step 1: Create the file**

```csharp
using System.Collections.Generic;
using UnityEngine;

public enum SeesawRole { Input, Launcher }

public class SeesawSide : MonoBehaviour
{
    [SerializeField] public SeesawRole role = SeesawRole.Input;
    [SerializeField] float _jumpVelocityThreshold = 1.5f;

    readonly List<SeesawParticipant> _occupants = new();
    SeesawCoordinator _coordinator;

    public IReadOnlyList<SeesawParticipant> Occupants => _occupants;

    public void Init(SeesawCoordinator coordinator)
    {
        _coordinator = coordinator;
    }

    void OnTriggerEnter(Collider other)
    {
        var p = other.GetComponentInParent<SeesawParticipant>();
        if (p != null && !_occupants.Contains(p))
            _occupants.Add(p);
    }

    void OnTriggerExit(Collider other)
    {
        var p = other.GetComponentInParent<SeesawParticipant>();
        if (p == null) return;

        _occupants.Remove(p);

        if (role == SeesawRole.Input && _coordinator != null)
        {
            if (p.Rb != null && p.Rb.linearVelocity.y > _jumpVelocityThreshold)
                _coordinator.NotifyJump(p);
        }
    }
}
```

- [ ] **Step 2: Verify compilation in Unity — zero errors**

- [ ] **Step 3: Build the seesaw GameObject in the scene**

  In the Hierarchy:
  1. Create empty GameObject → name it `Seesaw`
  2. Create child → name `SideA` → Add `BoxCollider` → tick `Is Trigger` → Add `SeesawSide` → set Role = `Input`
  3. Create child → name `SideB` → Add `BoxCollider` → tick `Is Trigger` → Add `SeesawSide` → set Role = `Launcher`
  4. Position `SideA` and `SideB` where you want the two platform zones (e.g. 3 units apart on X)
  5. Size each BoxCollider to roughly character-foot-size (e.g. X=2, Y=0.3, Z=2)

- [ ] **Step 4: Commit**

```bash
git add "Assets/Mahmoud SandBox/Seesaw/SeesawSide.cs"
git commit -m "feat: add SeesawSide — trigger tracking and jump detection"
```

---

### Task 3: SeesawCoordinator — weight sum, launch, cooldown

**Files:**
- Create: `Assets/Mahmoud SandBox/Seesaw/SeesawCoordinator.cs`

**Interfaces:**
- Consumes:
  - `SeesawSide sideA` / `SeesawSide sideB` — Inspector references
  - `SeesawSide.Occupants` — IReadOnlyList<SeesawParticipant>
  - `SeesawParticipant.WeightKg` — float
  - `SeesawParticipant.ApplyLaunch(Vector3 impulse)` — void
  - `SeesawSide.Init(SeesawCoordinator)` — called in Awake
- Produces:
  - `void NotifyJump(SeesawParticipant jumper)` — called by SeesawSide

- [ ] **Step 1: Create the file**

```csharp
using System.Collections;
using UnityEngine;

public class SeesawCoordinator : MonoBehaviour
{
    [Header("Sides")]
    [SerializeField] SeesawSide _sideA;
    [SerializeField] SeesawSide _sideB;

    [Header("Launch Tuning")]
    [SerializeField] float _forcePerKg      = 3.0f;
    [SerializeField] float _horizontalBias  = 0.3f;
    [SerializeField] float _cooldownSeconds = 2.0f;

    bool _onCooldown;

    void Awake()
    {
        _sideA.Init(this);
        _sideB.Init(this);
    }

    public void NotifyJump(SeesawParticipant jumper)
    {
        if (_onCooldown) return;
        if (_sideB.Occupants.Count == 0) return;

        // Sum weight: remaining Side A occupants + the jumper
        float totalWeight = jumper.WeightKg;
        foreach (var p in _sideA.Occupants)
            totalWeight += p.WeightKg;

        // Direction: up + outward from A toward B
        Vector3 awayDir = (_sideB.transform.position - _sideA.transform.position);
        awayDir.y = 0f;
        awayDir = awayDir.sqrMagnitude > 0f ? awayDir.normalized : Vector3.forward;

        Vector3 launchDir = (Vector3.up + awayDir * _horizontalBias).normalized;
        Vector3 impulse   = launchDir * (totalWeight * _forcePerKg);

        foreach (var target in _sideB.Occupants)
            target.ApplyLaunch(impulse);

        StartCoroutine(Cooldown());
    }

    IEnumerator Cooldown()
    {
        _onCooldown = true;
        yield return new WaitForSeconds(_cooldownSeconds);
        _onCooldown = false;
    }
}
```

- [ ] **Step 2: Verify compilation in Unity — zero errors**

- [ ] **Step 3: Wire up the Inspector**

  Select `Seesaw` root GameObject:
  1. Add Component → `SeesawCoordinator`
  2. Drag `SideA` child into the `Side A` field
  3. Drag `SideB` child into the `Side B` field
  4. Leave defaults: Force Per Kg = 3, Horizontal Bias = 0.3, Cooldown = 2

- [ ] **Step 4: Commit**

```bash
git add "Assets/Mahmoud SandBox/Seesaw/SeesawCoordinator.cs"
git commit -m "feat: add SeesawCoordinator — launch formula and cooldown"
```

---

### Task 4: Integration test — Play Mode verification

**Files:** No new files. Manual test only.

- [ ] **Step 1: Add `SeesawParticipant` to all characters that will use the seesaw**

  For each character (player + any NPC):
  - Add Component → `SeesawParticipant`
  - Set `Weight Kg` to their intended weight (e.g. player = 70, NPC = 80)

- [ ] **Step 2: Test — single character jump launches target**

  Setup: Place player on `SideA`, place NPC on `SideB`.
  Enter Play Mode → walk player onto SideA → jump.
  Expected:
  - NPC on SideB launches upward and outward
  - NPC ragdolls mid-air
  - Console: no errors

- [ ] **Step 3: Test — walk-off does NOT trigger launch**

  Setup: Player on SideA, NPC on SideB.
  Enter Play Mode → walk player off the edge of SideA slowly (no jump).
  Expected: NPC does NOT launch. Console: no errors.

- [ ] **Step 4: Test — stacked weight launches higher**

  Setup: Player (70 kg) + second character (80 kg) both on SideA, NPC on SideB.
  Enter Play Mode → player jumps.
  Expected: NPC launches noticeably higher/farther than in Step 2 (total weight 150 kg vs 70 kg = ~2× force).

- [ ] **Step 5: Test — cooldown prevents double-trigger**

  Setup: Two players on SideA, NPC on SideB.
  Enter Play Mode → both players jump within 0.5s of each other.
  Expected: NPC launches once only. Second jump notification is silently dropped.

- [ ] **Step 6: Tune if needed**

  If launch feels too weak: increase `Force Per Kg` on `SeesawCoordinator` (try 4–6).
  If NPC doesn't ragdoll: verify `SeesawParticipant` found `_puppetCtrl` — use Inspector Debug Mode to check.
  If walk-off triggers: increase `Jump Velocity Threshold` on `SeesawSide SideA` (try 2.5f).

- [ ] **Step 7: Final commit**

```bash
git add -A
git commit -m "feat: seesaw physics system — complete"
```
