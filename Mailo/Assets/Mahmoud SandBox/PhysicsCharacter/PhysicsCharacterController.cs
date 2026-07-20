using System.Collections;
using UnityEngine;

public enum CharacterPhysicsState { Balanced, Ragdoll, GettingUp }

/// <summary>
/// Fully physics-based character controller.
/// Root has Rigidbody + CapsuleCollider for movement and environmental interaction.
/// Bone Rigidbodies (from Ragdoll Wizard) switch kinematic/dynamic for ragdoll.
///
/// SETUP:
///  1. Add Rigidbody (mass 70, drag 0) to Testing_Player root.
///  2. Add CapsuleCollider (height 1.8, radius 0.3, center Y 0.9) to root.
///  3. Add Animator to root, assign CharacterAnimaiton.controller, disable Apply Root Motion.
///  4. Run Unity Ragdoll Wizard (GameObject > 3D Object > Ragdoll...) on the Mixamo bones.
///  5. Add PhysicsImpactDetector to every bone the Wizard gave a Rigidbody.
///     (The first entry in ragdollBodies must be the Hips Rigidbody.)
///  6. Add this script to root — it auto-finds PhysicsImpactDetectors on children.
/// </summary>
[RequireComponent(typeof(Rigidbody))]
[RequireComponent(typeof(CapsuleCollider))]
[RequireComponent(typeof(Animator))]
public class PhysicsCharacterController : MonoBehaviour
{
    // ── Movement ──────────────────────────────────────────────────────────────
    [Header("Movement")]
    public float moveForce       = 25f;
    public float maxMoveSpeed    = 5f;
    public float rotateSpeed     = 12f;
    public float aimRotateSpeed  = 720f;   // degrees/sec snap when aiming
    public float movingDrag      = 2f;
    public float stoppingDrag    = 8f;

    // ── Ground ────────────────────────────────────────────────────────────────
    [Header("Ground Check")]
    public float     groundCheckDist = 0.15f;
    public LayerMask groundMask      = ~0;

    // ── Ragdoll ───────────────────────────────────────────────────────────────
    [Header("Ragdoll")]
    [Tooltip("All Rigidbodies on the ragdoll bones. First entry = Hips.")]
    public Rigidbody[] ragdollBodies;

    [Tooltip("Impulse (N·s) needed to trigger a knockdown.")]
    public float knockdownThreshold   = 25f;

    [Tooltip("Seconds the body must lie still before attempting get-up.")]
    public float settleDelay          = 1.5f;
    public float settleSpeedThreshold = 0.3f;
    public float maxSettleWait        = 5f;

    [Tooltip("Duration of the get-up animation phase.")]
    public float getUpDuration = 1.5f;

    [Tooltip("Animator trigger for get-up. Leave empty to skip.")]
    public string getUpTrigger = "";

    // ── Wind ─────────────────────────────────────────────────────────────────
    [Header("Wind  (F2=off  F3=medium  F4=strong)")]
    public Vector3      windDirection  = Vector3.right;
    [Min(0f)] public float windStrength = 0f;
    [Min(0f)] public float windGust     = 0f;
    public float        windNoiseSpeed = 0.4f;

    // ── Public state ──────────────────────────────────────────────────────────
    public CharacterPhysicsState State { get; private set; } = CharacterPhysicsState.Balanced;

    // ── Private ───────────────────────────────────────────────────────────────
    Rigidbody       _rb;
    Animator        _animator;
    CapsuleCollider _capsule;
    bool            _knockdownPending;
    float           _windOffset;

    static readonly int _speedHash = Animator.StringToHash("Speed");
    static readonly int _moveXHash = Animator.StringToHash("MoveX");
    static readonly int _moveYHash = Animator.StringToHash("MoveY");

    public bool  IsAiming  { get; set; }
    public float CameraYaw { get; set; }

    void Awake()
    {
        _rb       = GetComponent<Rigidbody>();
        _animator = GetComponent<Animator>();
        _capsule  = GetComponent<CapsuleCollider>();
        _windOffset = Random.Range(0f, 100f);

        _rb.constraints   = RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ;
        _rb.interpolation = RigidbodyInterpolation.Interpolate;

        _animator.applyRootMotion = false;
        _animator.updateMode      = AnimatorUpdateMode.Fixed;

        foreach (var det in GetComponentsInChildren<PhysicsImpactDetector>())
            det.Init(this);

        if (ragdollBodies == null || ragdollBodies.Length == 0)
            AutoCollectBodies();

        SetBonesKinematic(true);

        // Prevent the root capsule from colliding with bone capsules — they are the same
        // character and should never push each other (would cause flying on get-up).
        if (_capsule != null)
        {
            foreach (var col in GetComponentsInChildren<Collider>())
                if (col != _capsule) Physics.IgnoreCollision(_capsule, col, true);
        }
    }

    void AutoCollectBodies()
    {
        var dets = GetComponentsInChildren<PhysicsImpactDetector>();
        ragdollBodies = new Rigidbody[dets.Length];
        for (int i = 0; i < dets.Length; i++)
            ragdollBodies[i] = dets[i].GetComponent<Rigidbody>();
    }

    // ── Physics loop ──────────────────────────────────────────────────────────

    void FixedUpdate()
    {
        if (State != CharacterPhysicsState.Balanced) return;
        HandleMovement();
        ApplyWindToBody(_rb);
    }

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

            Quaternion nextRot = IsAiming
                ? Quaternion.RotateTowards(transform.rotation, targetRot, aimRotateSpeed * Time.fixedDeltaTime)
                : Quaternion.Slerp(transform.rotation, targetRot, rotateSpeed * Time.fixedDeltaTime);
            _rb.MoveRotation(nextRot);
        }
        else if (IsAiming)
        {
            Quaternion aimTarget = Quaternion.Euler(0f, CameraYaw, 0f);
            _rb.MoveRotation(Quaternion.RotateTowards(transform.rotation, aimTarget, aimRotateSpeed * Time.fixedDeltaTime));
        }

        _animator.SetFloat(_speedHash, moving ? 1f : 0f,      0.1f, Time.fixedDeltaTime);
        _animator.SetFloat(_moveXHash, moving ? input.x : 0f, 0.1f, Time.fixedDeltaTime);
        _animator.SetFloat(_moveYHash, moving ? input.z : 0f, 0.1f, Time.fixedDeltaTime);
    }

    void ApplyWindToBody(Rigidbody target)
    {
        if (windStrength <= 0f && windGust <= 0f) return;
        float nx = Mathf.PerlinNoise(_windOffset + Time.time * windNoiseSpeed, 0f) - 0.5f;
        float nz = Mathf.PerlinNoise(0f, _windOffset + Time.time * windNoiseSpeed) - 0.5f;
        float ng = Mathf.PerlinNoise(_windOffset * 1.3f, Time.time * windNoiseSpeed * 0.7f);
        Vector3 dir   = (windDirection + new Vector3(nx, 0f, nz) * 0.3f).normalized;
        Vector3 force = dir * (windStrength + ng * windGust);
        target.AddForce(force, ForceMode.Acceleration);
    }

    // ── Impact / knockdown ────────────────────────────────────────────────────

    /// <summary>Called by PhysicsImpactDetector on each bone collision.</summary>
    public void OnImpact(float impulse)
    {
        if (State != CharacterPhysicsState.Balanced || _knockdownPending) return;
        if (impulse < knockdownThreshold) return;
        _knockdownPending = true;
        StartCoroutine(RagdollSequence());
    }

    public void ForceRagdoll()
    {
        if (State == CharacterPhysicsState.Balanced && !_knockdownPending)
        {
            _knockdownPending = true;
            StartCoroutine(RagdollSequence());
        }
    }

    // ── Coroutines ────────────────────────────────────────────────────────────

    IEnumerator RagdollSequence()
    {
        State = CharacterPhysicsState.Ragdoll;
        _animator.enabled = false;

        Vector3 vel = _rb.linearVelocity;

        // Disable the root capsule so it can't block or push the falling bones.
        if (_capsule != null) _capsule.enabled = false;
        _rb.isKinematic = true;
        SetBonesKinematic(false);

        foreach (var rb in ragdollBodies)
            if (rb != null) rb.linearVelocity = vel;

        float elapsed   = 0f;
        float stillTime = 0f;

        while (elapsed < maxSettleWait)
        {
            yield return new WaitForFixedUpdate();
            elapsed += Time.fixedDeltaTime;

            // Wind keeps pushing even while ragdolled (hips only to avoid joint conflicts).
            if (ragdollBodies.Length > 0 && ragdollBodies[0] != null)
                ApplyWindToBody(ragdollBodies[0]);

            float maxSpd = 0f;
            foreach (var rb in ragdollBodies)
                if (rb != null) maxSpd = Mathf.Max(maxSpd, rb.linearVelocity.magnitude);

            stillTime = maxSpd < settleSpeedThreshold ? stillTime + Time.fixedDeltaTime : 0f;
            if (stillTime >= settleDelay) break;
        }

        _knockdownPending = false;
        StartCoroutine(GetUpSequence());
    }

    IEnumerator GetUpSequence()
    {
        State = CharacterPhysicsState.GettingUp;

        // Capture hip world position BEFORE snapping bones kinematic (position changes after).
        Vector3 hipPos = (ragdollBodies != null && ragdollBodies.Length > 0 && ragdollBodies[0] != null)
            ? ragdollBodies[0].position
            : transform.position;

        // Snap ragdoll bones kinematic so Animator can drive them again.
        SetBonesKinematic(true);
        yield return new WaitForFixedUpdate(); // let PhysX acknowledge kinematic state

        // Re-enable root capsule then reposition it on the ground.
        if (_capsule != null) _capsule.enabled = true;

        // Cast from 3 m above hip — avoids hitting bone capsules that are near floor level.
        Vector3 castOrigin = new Vector3(hipPos.x, hipPos.y + 3f, hipPos.z);
        Vector3 groundPos;
        if (Physics.Raycast(castOrigin, Vector3.down, out RaycastHit hit, 10f, groundMask))
            groundPos = hit.point;
        else
            groundPos = new Vector3(hipPos.x, 0f, hipPos.z);

        // Preserve current yaw, force pitch/roll to zero so character stands level.
        Quaternion standRot = Quaternion.Euler(0f, transform.eulerAngles.y, 0f);
        transform.SetPositionAndRotation(groundPos, standRot);

        // Notify PhysX of the teleport BEFORE re-enabling dynamics — prevents launch impulse.
        Physics.SyncTransforms();
        yield return new WaitForFixedUpdate();

        _rb.isKinematic     = false;
        _rb.linearVelocity  = Vector3.zero;
        _rb.angularVelocity = Vector3.zero;

        _animator.enabled = true;
        if (!string.IsNullOrEmpty(getUpTrigger))
            _animator.SetTrigger(getUpTrigger);

        yield return new WaitForSeconds(getUpDuration);
        State = CharacterPhysicsState.Balanced;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    void SetBonesKinematic(bool kinematic)
    {
        foreach (var rb in ragdollBodies)
            if (rb != null) rb.isKinematic = kinematic;
    }

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.F2)) { windStrength = 0f;  windGust = 0f;  }
        if (Input.GetKeyDown(KeyCode.F3)) { windStrength = 5f;  windGust = 3f;  }
        if (Input.GetKeyDown(KeyCode.F4)) { windStrength = 15f; windGust = 8f;  }
    }
}
