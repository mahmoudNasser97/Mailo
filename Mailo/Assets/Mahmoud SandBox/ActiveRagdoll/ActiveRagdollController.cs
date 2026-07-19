using System.Collections;
using UnityEngine;

public enum RagdollState { Balanced, Ragdoll, GettingUp }

/// <summary>
/// Central controller for the active ragdoll.
/// Place on a root GameObject that holds both the animated reference rig and the physical ragdoll.
///
/// SETUP:
///  - animatedRoot : root of the hidden animated rig (Animator, CullingMode = Always Animate,
///                   all Rigidbodies kinematic, Animator.updateMode = AnimatePhysics)
///  - physicalHips : Rigidbody on the ragdoll hips (no ConfigurableJoint on this bone)
///  - muscles      : all Muscle components on the physical skeleton, parent-to-child order
///  - ragdollBodies: all Rigidbodies on the physical skeleton (including hips)
///  - boneColliders: all BoneCollision components (or assign manually and call Init)
/// </summary>
public class ActiveRagdollController : MonoBehaviour
{
    // ── Rigs ─────────────────────────────────────────────────────────────────
    [Header("Rigs")]
    [Tooltip("Root of the hidden animated reference rig (has Animator).")]
    public Transform animatedRoot;

    [Tooltip("Hips Rigidbody on the physical ragdoll — no joint, it is the chain root.")]
    public Rigidbody physicalHips;

    [Tooltip("Hip bone Transform on the animated reference rig — used to snap physical hips position at startup.")]
    public Transform animatedHipsTarget;

    // ── Body parts ───────────────────────────────────────────────────────────
    [Header("Ragdoll Bodies")]
    [Tooltip("All Muscle components on the physical skeleton, ordered parent → child.")]
    public Muscle[] muscles;

    [Tooltip("All Rigidbodies on the physical skeleton (including hips).")]
    public Rigidbody[] ragdollBodies;

    [Tooltip("All BoneCollision components. Init() will be called automatically.")]
    public BoneCollision[] boneColliders;

    // ── Muscle tuning ────────────────────────────────────────────────────────
    [Header("Muscle Strength")]
    [Min(1f)] public float muscleSpring    = 800f;
    [Min(1f)] public float muscleDamper    =  80f;
    [Range(0f, 1f)] public float balancedStrength = 1f;

    // ── Impact / knockdown ───────────────────────────────────────────────────
    [Header("Impact")]
    [Tooltip("Impulse (N·s) required to trigger a knockdown.")]
    public float knockdownThreshold = 40f;

    [Tooltip("Seconds the ragdoll must lie still before trying to get up.")]
    public float settleDelay = 1.5f;

    [Tooltip("Body-velocity magnitude considered 'settled'.")]
    public float settleSpeedThreshold = 0.3f;

    [Tooltip("Max seconds to wait for settling before forcing a get-up anyway.")]
    public float maxSettleWait = 5f;

    // ── Get-up ───────────────────────────────────────────────────────────────
    [Header("Get-Up")]
    [Tooltip("Duration over which muscle strength ramps from 0 → balanced during get-up.")]
    public float getUpStrengthRampDuration = 1.2f;

    [Tooltip("Animator trigger name for getting up from face-down (prone).")]
    public string getUpFrontTrigger = "GetUpFront";

    [Tooltip("Animator trigger name for getting up from face-up (supine).")]
    public string getUpBackTrigger  = "GetUpBack";

    [Tooltip("LayerMask used for ground raycasting when aligning the animated rig.")]
    public LayerMask groundMask = ~0;

    // ── Wind ─────────────────────────────────────────────────────────────────
    [Header("Wind")]
    public Vector3 windBaseDirection = Vector3.right;
    [Min(0f)] public float windBaseStrength  = 0f;
    [Min(0f)] public float windGustStrength  = 0f;
    public float windNoiseScale = 0.3f;
    public float windNoiseSpeed = 0.4f;

    // ── Balance assist ───────────────────────────────────────────────────────
    [Header("Balance")]
    public BalanceController balanceController;

    // ── Runtime state (read-only for other scripts) ───────────────────────────
    public RagdollState CurrentState    { get; private set; } = RagdollState.Balanced;
    public float        MuscleStrength  { get; private set; }

    Animator _referenceAnimator;
    float    _windOffset;
    bool     _knockdownPending;

    void Awake()
    {
        _referenceAnimator = animatedRoot != null
            ? animatedRoot.GetComponentInChildren<Animator>()
            : null;

        _windOffset = Random.Range(0f, 100f);

        foreach (var m in muscles)
            m.Init(muscleSpring, muscleDamper);

        foreach (var bc in boneColliders)
            bc.Init(this);

        SetStrengthInternal(balancedStrength);
    }

    // Kinematic-flip snap: freeze all bodies, wait for the AnimatePhysics animator
    // to settle into its first frame, copy world transforms, then re-enable dynamics.
    IEnumerator Start()
    {
        foreach (var rb in ragdollBodies)
            rb.isKinematic = true;

        // Two FixedUpdates: first lets PhysX settle, second lets AnimatePhysics tick.
        yield return new WaitForFixedUpdate();
        yield return new WaitForFixedUpdate();

        SnapPhysicalToAnimated();

        yield return new WaitForFixedUpdate();

        foreach (var rb in ragdollBodies)
        {
            rb.isKinematic     = false;
            rb.linearVelocity  = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
        }
    }

    void SnapPhysicalToAnimated()
    {
        // Hips has no Muscle, snap it directly by world transform.
        if (animatedHipsTarget != null)
            physicalHips.transform.SetPositionAndRotation(
                animatedHipsTarget.position, animatedHipsTarget.rotation);

        // All child bones: copy world position+rotation from animated counterpart.
        foreach (var m in muscles)
        {
            if (m.animatedTarget == null) continue;
            m.transform.SetPositionAndRotation(
                m.animatedTarget.position, m.animatedTarget.rotation);
        }

        // Tell PhysX about the transform changes before re-enabling dynamics.
        Physics.SyncTransforms();
    }

    void FixedUpdate()
    {
        ApplyWind();

        switch (CurrentState)
        {
            case RagdollState.Balanced:
                RunMuscles();
                TrackAnimatedRigToHips();
                balanceController?.ApplyUprightTorque(physicalHips);
                balanceController?.ApplyHipVelocityDrive(physicalHips);
                break;

            case RagdollState.GettingUp:
                RunMuscles();
                break;
            // RagdollState.Ragdoll: no muscle updates — let it fall freely
        }
    }

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>Called by BoneCollision components on every hard impact.</summary>
    public void OnBoneImpact(float impulse)
    {
        if (CurrentState != RagdollState.Balanced) return;
        if (impulse < knockdownThreshold) return;
        if (_knockdownPending) return;

        _knockdownPending = true;
        StartCoroutine(RagdollSequence());
    }

    /// <summary>Skip the settle wait and begin getting up immediately (useful for testing).</summary>
    public void ForceGetUp()
    {
        if (CurrentState != RagdollState.Ragdoll) return;
        StopAllCoroutines();
        _knockdownPending = false;
        StartCoroutine(GetUpSequence());
    }

    // ── Coroutines ────────────────────────────────────────────────────────────

    IEnumerator RagdollSequence()
    {
        CurrentState = RagdollState.Ragdoll;
        SetStrengthInternal(0f);

        float elapsed   = 0f;
        float stillTime = 0f;

        while (elapsed < maxSettleWait)
        {
            yield return new WaitForFixedUpdate();
            elapsed += Time.fixedDeltaTime;

            float maxSpeed = 0f;
            foreach (var rb in ragdollBodies)
                maxSpeed = Mathf.Max(maxSpeed, rb.linearVelocity.magnitude);

            stillTime = maxSpeed < settleSpeedThreshold ? stillTime + Time.fixedDeltaTime : 0f;
            if (stillTime >= settleDelay) break;
        }

        _knockdownPending = false;
        StartCoroutine(GetUpSequence());
    }

    IEnumerator GetUpSequence()
    {
        CurrentState = RagdollState.GettingUp;

        // MUST align animated rig before ramping strength or pose snaps.
        AlignAnimatedRigToRagdoll();

        // Choose get-up animation based on orientation of the ragdoll.
        bool prone = Vector3.Dot(physicalHips.transform.up, Vector3.down) > 0.3f;
        if (_referenceAnimator != null)
            _referenceAnimator.SetTrigger(prone ? getUpFrontTrigger : getUpBackTrigger);

        float t = 0f;
        while (t < getUpStrengthRampDuration)
        {
            t += Time.fixedDeltaTime;
            SetStrengthInternal(Mathf.Lerp(0f, balancedStrength, t / getUpStrengthRampDuration));
            yield return new WaitForFixedUpdate();
        }

        SetStrengthInternal(balancedStrength);
        CurrentState = RagdollState.Balanced;
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    void RunMuscles()
    {
        foreach (var m in muscles)
        {
            m.UpdateTarget();
            m.SetStrength(MuscleStrength);
        }
    }

    void SetStrengthInternal(float strength)
    {
        MuscleStrength = Mathf.Clamp01(strength);
        foreach (var m in muscles)
            m.SetStrength(MuscleStrength);
    }

    void TrackAnimatedRigToHips()
    {
        if (animatedRoot == null) return;

        // Keep reference rig's XZ position locked to physical hips every frame so
        // muscle targets never drift away from the ragdoll as it moves.
        Vector3 hipsPos = physicalHips.transform.position;
        Vector3 pos     = animatedRoot.position;
        pos.x = hipsPos.x;
        pos.z = hipsPos.z;

        // Keep yaw aligned so animations face the right direction.
        Vector3 fwd = physicalHips.transform.forward;
        fwd.y = 0f;
        if (fwd.sqrMagnitude > 0.001f)
            animatedRoot.SetPositionAndRotation(pos, Quaternion.LookRotation(fwd.normalized));
        else
            animatedRoot.position = pos;
    }

    void AlignAnimatedRigToRagdoll()
    {
        if (animatedRoot == null) return;

        Vector3 hipPos = physicalHips.transform.position;

        // Raycast down from hip to find actual ground level (uneven terrain support).
        float groundY = 0f;
        if (Physics.Raycast(hipPos + Vector3.up * 0.5f, Vector3.down, out RaycastHit hit, 5f, groundMask))
            groundY = hit.point.y;

        Vector3 groundPos = new Vector3(hipPos.x, groundY, hipPos.z);

        // Extract yaw only — ignore ragdoll pitch/roll so the animated rig stands level.
        Vector3 fwd = physicalHips.transform.forward;
        fwd.y = 0f;
        if (fwd.sqrMagnitude < 0.001f) fwd = Vector3.forward;

        animatedRoot.SetPositionAndRotation(groundPos, Quaternion.LookRotation(fwd.normalized));
    }

    void ApplyWind()
    {
        float nx = Mathf.PerlinNoise(_windOffset + Time.time * windNoiseSpeed, 0f) - 0.5f;
        float nz = Mathf.PerlinNoise(0f, _windOffset + Time.time * windNoiseSpeed) - 0.5f;
        float ng = Mathf.PerlinNoise(_windOffset * 1.3f, Time.time * windNoiseSpeed * 0.7f);

        Vector3 dir   = (windBaseDirection + new Vector3(nx, 0f, nz)).normalized;
        Vector3 force = dir * (windBaseStrength + ng * windGustStrength);

        // Push hips only — joints transmit the force to the whole body naturally.
        // Pushing individual bones tears the ragdoll apart via joint conflicts.
        physicalHips.AddForce(force, ForceMode.Force);
    }
}
