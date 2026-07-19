using UnityEngine;

/// <summary>
/// Gait / stepping state machine for the active ragdoll.
///
/// SETUP CHECKLIST:
///  1. Assign balance, controller, ikDriver, leftAnkleMuscle, rightAnkleMuscle,
///     leftFootBody, rightFootBody in the Inspector.
///  2. ikDriver MUST be on the same GameObject as the reference rig Animator
///     and that Animator's Base Layer must have "IK Pass" enabled.
///  3. Set DesiredMoveDirection + DesiredMoveSpeed each frame from your input code.
///
/// BEHAVIOUR:
///  - A gait clock fires alternating left/right steps at gaitFrequency Hz.
///  - BalanceController.ShouldStep() can override the clock for reactive balance.
///  - During swing: IK arcs the foot from start → target with a sine-curve height.
///  - During stance: the ankle Muscle is locked at maximum spring so it resists sliding.
///  - Only one leg swings at a time (prevents both feet leaving the ground).
///  - Pauses automatically when ActiveRagdollController is not in Balanced state.
/// </summary>
public class GaitController : MonoBehaviour
{
    // ── References ────────────────────────────────────────────────────────────
    [Header("SETUP REQUIRED")]
    [Tooltip("BalanceController that computes CoM and step triggers.")]
    public BalanceController balance;

    [Tooltip("The ActiveRagdollController — gait pauses when not Balanced.")]
    public ActiveRagdollController controller;

    [Tooltip("GaitIKDriver on the reference rig Animator's GameObject. IK Pass MUST be ON.")]
    public GaitIKDriver ikDriver;

    [Tooltip("Muscle on the physical ragdoll's left ankle / foot joint.")]
    public Muscle leftAnkleMuscle;

    [Tooltip("Muscle on the physical ragdoll's right ankle / foot joint.")]
    public Muscle rightAnkleMuscle;

    [Tooltip("Left foot Rigidbody (used to read current world-space foot position).")]
    public Rigidbody leftFootBody;

    [Tooltip("Right foot Rigidbody.")]
    public Rigidbody rightFootBody;

    // ── Tunables ──────────────────────────────────────────────────────────────
    [Header("Step Shape")]
    [Tooltip("Duration of one swing phase in seconds.")]
    [Min(0.05f)] public float stepDuration   = 0.35f;

    [Tooltip("Peak height (metres) of the foot arc during swing.")]
    [Min(0f)]    public float stepHeight      = 0.25f;

    [Tooltip("Seconds at the end of swing over which IK weight fades to zero (plant blend).")]
    [Min(0f)]    public float plantBlendTime  = 0.10f;

    [Tooltip("Controls IK influence during swing (0→1 on X, 0→1 on Y). "
           + "A simple 0→1 ramp works; the plant fade is handled separately by plantBlendTime.")]
    public AnimationCurve ikWeightCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

    [Header("Gait Timing")]
    [Tooltip("Steps per second during normal walking (gait clock frequency).")]
    [Min(0f)] public float gaitFrequency  = 1.8f;

    [Tooltip("Minimum time (seconds) between successive steps on the same leg.")]
    [Min(0f)] public float minStepInterval = 0.15f;

    [Header("Walking Direction")]
    [Tooltip("Lateral half-width (metres) to offset each foot from the support centre.")]
    [Min(0f)] public float hipWidth = 0.20f;

    // ── External input (set each frame by your player / AI controller) ─────────
    /// <summary>Normalised movement direction in world space.</summary>
    public Vector3 DesiredMoveDirection { get; set; }

    /// <summary>Desired movement speed in m/s.</summary>
    public float DesiredMoveSpeed { get; set; }

    // ── Internal leg state ────────────────────────────────────────────────────

    enum LegState { Stance, Swing }

    class LegData
    {
        public LegState   state        = LegState.Stance;
        public float      swingT       = 0f;
        public float      lastStepTime = -999f;
        public Vector3    startPos;
        public Vector3    target;
        public Quaternion targetRot    = Quaternion.identity;
    }

    readonly LegData _left  = new LegData();
    readonly LegData _right = new LegData();

    float _gaitClock   = 0f;
    bool  _lastClockWasLeft = false;

    bool AnyLegSwinging => _left.state == LegState.Swing || _right.state == LegState.Swing;

    // ── Unity messages ────────────────────────────────────────────────────────

    void FixedUpdate()
    {
        if (controller == null || controller.CurrentState != RagdollState.Balanced)
        {
            // Release stance locks and clear IK so the ragdoll falls freely.
            SetLocked(leftAnkleMuscle,  false);
            SetLocked(rightAnkleMuscle, false);
            ZeroAllIK();
            return;
        }

        // Push desired velocity into BalanceController so ShouldStep is walking-aware.
        balance.DesiredVelocity = DesiredMoveDirection * DesiredMoveSpeed;

        // ── Gait clock ────────────────────────────────────────────────────────
        _gaitClock += gaitFrequency * Time.fixedDeltaTime;
        if (_gaitClock >= 1f)
        {
            _gaitClock -= 1f;
            TickGaitClock();
        }

        // ── Balance-driven step override ──────────────────────────────────────
        if (!AnyLegSwinging && balance.ShouldStep(out Vector3 balanceTarget, out bool balanceLeft))
            TryInitiateStep(balanceLeft, balanceTarget);

        // ── Advance each leg ─────────────────────────────────────────────────
        UpdateLeg(_left,  leftAnkleMuscle,  leftFootBody,
                  AvatarIKGoal.LeftFoot,  isLeft: true);

        UpdateLeg(_right, rightAnkleMuscle, rightFootBody,
                  AvatarIKGoal.RightFoot, isLeft: false);
    }

    // ── Gait clock ────────────────────────────────────────────────────────────

    void TickGaitClock()
    {
        bool stepLeft = !_lastClockWasLeft;
        _lastClockWasLeft = stepLeft;

        if (AnyLegSwinging) return; // let the current swing finish

        LegData leg = stepLeft ? _left : _right;
        if (leg.state == LegState.Stance)
        {
            Vector3 target = ComputeProceduralTarget(stepLeft);
            TryInitiateStep(stepLeft, target);
        }
    }

    // ── Step initiation ───────────────────────────────────────────────────────

    void TryInitiateStep(bool isLeft, Vector3 target)
    {
        LegData leg = isLeft ? _left : _right;
        if (leg.state == LegState.Swing) return; // already swinging
        if (AnyLegSwinging) return;               // wait for stance

        float timeSinceLast = Time.time - leg.lastStepTime;
        if (timeSinceLast < minStepInterval) return;

        Rigidbody footBody = isLeft ? leftFootBody : rightFootBody;
        leg.startPos  = footBody != null ? footBody.worldCenterOfMass : transform.position;
        leg.target    = new Vector3(target.x, 0f, target.z);
        leg.targetRot = Quaternion.identity; // flat foot; override per-terrain if desired
        leg.swingT    = 0f;
        leg.state     = LegState.Swing;

        // Unlock ankle so the foot can lift freely.
        SetLocked(isLeft ? leftAnkleMuscle : rightAnkleMuscle, false);
    }

    Vector3 ComputeProceduralTarget(bool isLeft)
    {
        Vector3 sc = balance.SupportCenter();

        // Stride forward in the movement direction, proportional to speed.
        Vector3 strideOffset = DesiredMoveDirection * (DesiredMoveSpeed * stepDuration);

        // Lateral offset to keep feet approximately hip-width apart.
        Vector3 right = balance != null && balance.pelvis != null
            ? balance.pelvis.transform.right
            : Vector3.right;
        right.y = 0f;
        right = right.normalized;
        Vector3 lateral = (isLeft ? -right : right) * hipWidth;

        return sc + strideOffset + lateral;
    }

    // ── Per-leg update ────────────────────────────────────────────────────────

    void UpdateLeg(LegData leg, Muscle ankleMuscle, Rigidbody footBody,
                   AvatarIKGoal goal, bool isLeft)
    {
        if (leg.state == LegState.Stance)
        {
            SetLocked(ankleMuscle, true);
            WriteIK(goal, Vector3.zero, Quaternion.identity, 0f, 0f);
            return;
        }

        // Advance swing timer.
        leg.swingT += Time.fixedDeltaTime / stepDuration;
        leg.swingT  = Mathf.Clamp01(leg.swingT);
        float t     = leg.swingT;

        // Arc trajectory: flat lerp + sine-curved height.
        Vector3 flatPos  = Vector3.Lerp(leg.startPos, leg.target, t);
        float   liftY    = Mathf.Sin(t * Mathf.PI) * stepHeight;
        Vector3 footPos  = new Vector3(flatPos.x, leg.target.y + liftY, flatPos.z);

        // IK weight: ramp from curve, then fade over plant blend window.
        float ikW   = ikWeightCurve.Evaluate(t);
        float plantT = Mathf.Max(plantBlendTime / stepDuration, 0.001f);
        if (t > 1f - plantT)
            ikW *= (1f - t) / plantT;

        Quaternion footRot = Quaternion.Slerp(leg.startPos != Vector3.zero
            ? Quaternion.LookRotation(DesiredMoveDirection == Vector3.zero ? Vector3.forward : DesiredMoveDirection)
            : Quaternion.identity,
            leg.targetRot, t);

        WriteIK(goal, footPos, footRot, ikW, ikW * 0.5f);

        // Plant when swing completes.
        if (t >= 1f)
        {
            leg.state       = LegState.Stance;
            leg.lastStepTime = Time.time;
            SetLocked(ankleMuscle, true);
            WriteIK(goal, Vector3.zero, Quaternion.identity, 0f, 0f);
        }
    }

    // ── IK / lock helpers ─────────────────────────────────────────────────────

    void WriteIK(AvatarIKGoal goal, Vector3 pos, Quaternion rot, float posW, float rotW)
    {
        if (ikDriver == null) return;

        if (goal == AvatarIKGoal.LeftFoot)
        {
            ikDriver.leftFootPos  = pos;
            ikDriver.leftFootRot  = rot;
            ikDriver.leftFootPosW = posW;
            ikDriver.leftFootRotW = rotW;
        }
        else
        {
            ikDriver.rightFootPos  = pos;
            ikDriver.rightFootRot  = rot;
            ikDriver.rightFootPosW = posW;
            ikDriver.rightFootRotW = rotW;
        }
    }

    void ZeroAllIK()
    {
        WriteIK(AvatarIKGoal.LeftFoot,  Vector3.zero, Quaternion.identity, 0f, 0f);
        WriteIK(AvatarIKGoal.RightFoot, Vector3.zero, Quaternion.identity, 0f, 0f);
    }

    static void SetLocked(Muscle m, bool locked)
    {
        if (m != null) m.SetLocked(locked);
    }
}
