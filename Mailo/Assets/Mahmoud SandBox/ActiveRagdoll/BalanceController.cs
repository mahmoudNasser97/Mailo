using UnityEngine;

/// <summary>
/// Computes the character's centre of mass, capture point, and support polygon,
/// then provides corrective torques and step triggers.
///
/// SETUP:
///  - pelvis, leftFoot, rightFoot : Rigidbodies on the physical ragdoll
///  - allBodies : every Rigidbody on the physical ragdoll (for weighted CoM)
/// </summary>
public class BalanceController : MonoBehaviour
{
    // ── References ────────────────────────────────────────────────────────────
    [Header("Body References (physical ragdoll)")]
    public Rigidbody   pelvis;
    public Rigidbody   leftFoot;
    public Rigidbody   rightFoot;
    [Tooltip("Every Rigidbody on the physical ragdoll — used for mass-weighted CoM.")]
    public Rigidbody[] allBodies;

    // ── Capture-point model ───────────────────────────────────────────────────
    [Header("Capture-Point Balance")]
    [Tooltip("How far ahead (seconds) to extrapolate CoM velocity into the capture point.")]
    public float capturePointPredictionTime = 0.30f;

    [Tooltip("Radius of the support polygon around the feet midpoint. Step triggers when "
           + "the biased capture point exits this circle.")]
    public float supportRadius = 0.25f;

    // ── Step target ───────────────────────────────────────────────────────────
    [Header("Step Targeting")]
    [Tooltip("How far ahead of the current support centre to place the step-foot target.")]
    public float stepTargetForwardBias = 0.30f;

    [Tooltip("Stride scale when walking: step target = supportCentre + desiredVelocity * this.")]
    public float stepStrideScale = 0.40f;

    // ── Corrective torques ────────────────────────────────────────────────────
    [Header("Corrective Torques")]
    [Tooltip("Hip uprighting spring (scales with total mass so heavy chars don't topple).")]
    public float hipTorqueSpring = 120f;

    [Tooltip("Angular-velocity damper applied alongside uprighting torque.")]
    public float torqueDamper = 10f;

    [Header("Hip Velocity Drive")]
    [Tooltip("Gain on the PD controller that pushes the hips toward the desired walk velocity.")]
    public float hipVelocityGain = 60f;

    // ── Walking bias (set each frame by GaitController) ───────────────────────
    /// <summary>
    /// Current desired velocity. Added to the capture point before the support-radius check
    /// so the character steps proactively in the direction of movement.
    /// </summary>
    public Vector3 DesiredVelocity { get; set; }

    // ── Cached ────────────────────────────────────────────────────────────────
    float _totalMass;

    void Awake()
    {
        _totalMass = 0f;
        foreach (var rb in allBodies) _totalMass += rb.mass;
        if (_totalMass <= 0f) _totalMass = 1f;
    }

    // ── Public queries ────────────────────────────────────────────────────────

    public Vector3 ComputeCoM()
    {
        Vector3 com = Vector3.zero;
        foreach (var rb in allBodies)
            com += rb.worldCenterOfMass * rb.mass;
        return com / _totalMass;
    }

    public Vector3 ComputeCoMVelocity()
    {
        Vector3 vel = Vector3.zero;
        foreach (var rb in allBodies)
            vel += rb.linearVelocity * rb.mass;   // Unity 6: linearVelocity
        return vel / _totalMass;
    }

    /// <summary>
    /// Extrapolated centre of mass ("capture point" / XCoM).
    /// Walking bias is baked in: biasedCP = CoM + (CoMVel + DesiredVelocity) * predTime.
    /// </summary>
    public Vector3 CapturePoint()
    {
        Vector3 com = ComputeCoM();
        Vector3 vel = ComputeCoMVelocity() + DesiredVelocity;
        return com + vel * capturePointPredictionTime;
    }

    /// <summary>Midpoint between the two physical foot positions (projected to XZ).</summary>
    public Vector3 SupportCenter()
    {
        Vector3 l = leftFoot.worldCenterOfMass;
        Vector3 r = rightFoot.worldCenterOfMass;
        return new Vector3((l.x + r.x) * 0.5f, 0f, (l.z + r.z) * 0.5f);
    }

    /// <summary>
    /// Returns true when the character should take a corrective or walking step.
    /// target       : world-space foot placement goal (Y = 0, caller projects to ground)
    /// stepWithLeft : which leg to swing
    /// </summary>
    public bool ShouldStep(out Vector3 target, out bool stepWithLeft)
    {
        target       = Vector3.zero;
        stepWithLeft = false;

        Vector3 cp2d = new Vector3(CapturePoint().x, 0f, CapturePoint().z);
        Vector3 sc   = SupportCenter();

        float dist = Vector3.Distance(cp2d, sc);
        if (dist <= supportRadius) return false;

        // The leg further from the capture point is the one to swing.
        Vector3 lPos = new Vector3(leftFoot.worldCenterOfMass.x,  0f, leftFoot.worldCenterOfMass.z);
        Vector3 rPos = new Vector3(rightFoot.worldCenterOfMass.x, 0f, rightFoot.worldCenterOfMass.z);
        stepWithLeft = Vector3.Distance(cp2d, lPos) > Vector3.Distance(cp2d, rPos);

        // Step target: move support centre toward the capture point, plus stride bias.
        Vector3 toCP = (cp2d - sc).normalized;
        target = sc + toCP * stepTargetForwardBias
               + new Vector3(DesiredVelocity.x, 0f, DesiredVelocity.z) * stepStrideScale;

        return true;
    }

    /// <summary>
    /// Applies a velocity PD controller force to the hips toward the desired walk velocity.
    /// Without this the hips have nothing explicitly driving them forward.
    /// </summary>
    public void ApplyHipVelocityDrive(Rigidbody hips)
    {
        if (hips == null || hipVelocityGain <= 0f) return;
        float massScale = _totalMass / 70f;
        Vector3 desired = new Vector3(DesiredVelocity.x, 0f, DesiredVelocity.z);
        Vector3 current = new Vector3(hips.linearVelocity.x, 0f, hips.linearVelocity.z);
        hips.AddForce((desired - current) * hipVelocityGain * massScale, ForceMode.Acceleration);
    }

    /// <summary>
    /// Applies a mass-scaled uprighting torque to the pelvis each FixedUpdate.
    /// Keeps the character vertical for small CoM drift without scripted constraints.
    /// </summary>
    public void ApplyUprightTorque(Rigidbody hips)
    {
        if (hips == null) return;

        Vector3 hipUp     = hips.transform.up;
        Vector3 torqueDir = Vector3.Cross(hipUp, Vector3.up);
        float   spring    = torqueDir.magnitude * hipTorqueSpring;
        float   damp      = -hips.angularVelocity.magnitude * torqueDamper;

        hips.AddTorque(torqueDir.normalized * (spring + damp), ForceMode.Force);
    }
}
