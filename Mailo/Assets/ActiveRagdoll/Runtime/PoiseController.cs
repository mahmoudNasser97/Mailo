using UnityEngine;

namespace ActiveRagdoll
{
    /// <summary>One collision, packaged for the poise system.</summary>
    public struct Impact
    {
        public Vector3 point;
        public Vector3 impulse;
        public Rigidbody body;
        public BodyPart bodyPart;
        public float severity;
    }

    /// <summary>
    /// Phase 4. Owns <b>poise</b> — the single 0–1 scalar the whole system hangs off (spec §1).
    /// Poise 1 = crisp and controlled; poise 0 = limp ragdoll. Stagger, knockdown, fatigue and
    /// death are all just poise values, so there is no reaction state machine.
    ///
    /// Impacts drain poise (weighted by which bone was hit and how much rotational disruption the
    /// hit imparts). Poise scales the pose-drive strength (<see cref="PoseMatcher.poise"/>) and the
    /// pelvis support crutch (<see cref="BalanceController.supportFraction"/>) through a curve, so a
    /// hard hit collapses the character and it stands back up as poise regenerates — no code path
    /// per tier, no BalanceController disable.
    ///
    /// Build order (spec §9): this is the binary knockdown (poise scales everything through one
    /// curve). The finer per-limb asymmetric ramp is a later refinement.
    /// </summary>
    [RequireComponent(typeof(RagdollRig))]
    [DefaultExecutionOrder(-30)] // set poise before BalanceController(-20) and PoseMatcher(0) read it
    public class PoiseController : MonoBehaviour
    {
        [Range(0f, 1f)] public float poise = 1f;
        [Tooltip("Regenerate poise while grounded (character stands back up as poise recovers).")]
        public bool autoRecover = true;

        public float Poise => poise;

        [Tooltip("Seconds after a hit before poise may regenerate — lets a knockdown commit to the " +
                 "fall instead of instantly popping back up.")]
        public float recoverDelay = 0.6f;

        RagdollRig _rig;
        PoseMatcher _poseMatcher;
        BalanceController _balance;
        RecoveryController _recovery;
        float _lastImpactTime = -10f;

        void Awake() => _rig = GetComponent<RagdollRig>();

        void OnEnable()
        {
            if (_rig == null) _rig = GetComponent<RagdollRig>();
            _rig.RebuildLookup();
            _poseMatcher = GetComponent<PoseMatcher>();
            _balance = GetComponent<BalanceController>();
            _recovery = GetComponent<RecoveryController>();
            AttachReceivers();
        }

        /// <summary>Torso tipped past ~60° from vertical — knocked down rather than upright.</summary>
        public bool IsDown
        {
            get
            {
                if (_rig.TryGetBone(BodyPart.Chest, out var c) && _rig.TryGetBone(BodyPart.Hips, out var h)
                    && c.physical != null && h.physical != null)
                    return Vector3.Dot((c.physical.position - h.physical.position).normalized, Vector3.up) < 0.5f;
                return false;
            }
        }

        void AttachReceivers()
        {
            foreach (var b in _rig.bones)
            {
                if (b?.physical == null) continue;
                var r = b.physical.GetComponent<ImpactReceiver>();
                if (r == null) r = b.physical.gameObject.AddComponent<ImpactReceiver>();
                r.Init(_rig, this, b.part, b.body);
            }
        }

        /// <summary>
        /// Maps the poise pool to an effective drive/crutch gain through the tier thresholds:
        /// full (crisp) above the stagger threshold, ramping down through stagger, limp below the
        /// knockdown threshold. This is the "curve over poise, not a switch statement" (spec §4d).
        /// </summary>
        public float Gain
        {
            get
            {
                var p = _rig.profile;
                return Mathf.SmoothStep(p.knockdownThreshold, p.staggerThreshold, poise);
            }
        }

        public string Tier
        {
            get
            {
                var p = _rig.profile;
                if (poise >= p.flinchThreshold) return "Absorb";
                if (poise >= p.staggerThreshold) return "Flinch";
                if (poise >= p.knockdownThreshold) return "Stagger";
                return "Knockdown";
            }
        }

        void FixedUpdate()
        {
            if (_rig == null || _rig.profile == null) return;
            var p = _rig.profile;

            // Regenerate once settled. While DOWN, the get-up is owned by RecoveryController (proper
            // rest-timer + facing + get-up clip) — so skip regen here if one is present. Without a
            // RecoveryController, still regen from any pose so it recovers standalone.
            bool settled = _rig.TotalKineticEnergy < p.restEnergyThreshold * 4f;
            bool committed = Time.time - _lastImpactTime > recoverDelay;
            if (autoRecover && settled && committed && (_recovery == null || !IsDown))
                poise = Mathf.MoveTowards(poise, 1f, p.regenRate * Time.fixedDeltaTime);

            // Poise multiplies all drive strengths (PoseMatcher) and the pelvis crutch (Balance).
            float gain = Gain;
            if (_poseMatcher != null) _poseMatcher.poise = gain;
            if (_balance != null) _balance.supportFraction = gain;
        }

        public void RegisterImpact(Impact impact)
        {
            var p = _rig.profile;

            // Rotational disruption: a low shin sweep → huge spin about a horizontal axis → the body
            // rotates over the contact and lands on its back; a chest hit through the CoM is mostly
            // linear → stagger. "Shoved vs taken down" falls out of geometry (spec §4b).
            Vector3 r = impact.point - _rig.CenterOfMass;
            Vector3 angularImpulse = Vector3.Cross(r, impact.impulse);
            float spinFactor = angularImpulse.magnitude / Mathf.Max(_rig.TotalMass, 1f);

            poise -= impact.severity * p.PartMultiplier(impact.bodyPart) * (1f + spinFactor * p.spinWeight) / p.poiseCapacity;
            poise = Mathf.Clamp01(poise);
            _lastImpactTime = Time.time;

            // Residual impulse (spec §4e): as poise collapses, add momentum so the whole body carries
            // the hit rather than one shin rocketing away. ~30% shared to the struck bone's neighbour.
            float knock = 1f - Gain; // 0 while crisp, → 1 at knockdown
            if (knock > 0.05f && impact.body != null)
            {
                impact.body.AddForce(impact.impulse * (knock * 0.6f), ForceMode.Impulse);
                var bone = FindBone(impact.body);
                if (bone?.joint != null && bone.joint.connectedBody != null)
                    bone.joint.connectedBody.AddForce(impact.impulse * (knock * 0.2f), ForceMode.Impulse);
            }
        }

        RagdollBone FindBone(Rigidbody rb)
        {
            foreach (var b in _rig.bones)
                if (b?.body == rb) return b;
            return null;
        }
    }
}
