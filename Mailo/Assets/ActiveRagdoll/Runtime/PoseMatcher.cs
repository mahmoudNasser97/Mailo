// ---------------------------------------------------------------------------
// JOINT TECHNOLOGY DECISION (spec §1) — READ BEFORE CHANGING.
// This system uses ConfigurableJoint, NOT ArticulationBody.
// ArticulationBody is the better solver in isolation (reduced coordinates, no joint
// separation, forgiving of mass ratios) BUT it will not accept standard Unity joints
// attached to it, which kills runtime FixedJoint creation. Grab / carry / object
// interaction (Phase 5) depend on runtime FixedJoints, so ConfigurableJoint is the
// correct trade. Do not silently reverse this.
// ---------------------------------------------------------------------------

using UnityEngine;

namespace ActiveRagdoll
{
    /// <summary>
    /// Phase 1. Every FixedUpdate, drives each physical bone toward the matching animated
    /// bone: read the animated bone's localRotation, convert to joint space, write it to
    /// joint.targetRotation, and set the (non-uniform, per-group) slerp drive from the
    /// profile scaled by <see cref="poise"/>.
    ///
    /// Pose matching alone never balances — with the hips free on the ground the character
    /// will pose recognisably and then fall over. That is correct; balance is Phase 2.
    /// </summary>
    [RequireComponent(typeof(RagdollRig))]
    public class PoseMatcher : MonoBehaviour
    {
        [Tooltip("Global drive multiplier (spec's central scalar). Fixed at 1 for Phase 1; " +
                 "Phase 4's PoiseController will own it. 0 = fully limp ragdoll.")]
        [Range(0f, 1f)] public float poise = 1f;

        [Tooltip("Freeze the hips (kinematic) to suspend the character in the air — the Phase 1 " +
                 "acceptance test. Turn off to drop it on the ground (it will fall over: correct).")]
        public bool suspendHips = false;

        RagdollRig _rig;
        bool _suspendApplied;

        void Awake() => _rig = GetComponent<RagdollRig>();

        void OnEnable()
        {
            // Ensure every joint is in Slerp mode (the wizard sets this, but be defensive
            // in case PoseMatcher is added to a hand-built rig).
            if (_rig == null) _rig = GetComponent<RagdollRig>();
            foreach (var bone in _rig.bones)
                if (bone?.joint != null) bone.joint.rotationDriveMode = RotationDriveMode.Slerp;
        }

        void FixedUpdate()
        {
            if (_rig == null || _rig.profile == null) return;
            ApplySuspend();

            var profile = _rig.profile;
            foreach (var bone in _rig.bones)
            {
                if (bone?.joint == null || bone.target == null) continue;

                bone.joint.SetTargetRotationLocal(bone.target.localRotation, bone.startLocalRotation);

                // Non-uniform per-group stiffness × poise (spec §Phase 1). Both spring and
                // damper scale with poise so poise 0 is genuinely limp, not over-damped.
                var drive = bone.joint.slerpDrive;
                drive.positionSpring = profile.SpringFor(bone.part) * poise;
                drive.positionDamper = profile.DamperFor(bone.part) * poise;
                drive.maximumForce = profile.maxForce;
                bone.joint.slerpDrive = drive;
            }
        }

        void ApplySuspend()
        {
            var hips = _rig.Root;
            if (hips?.body == null || suspendHips == _suspendApplied) return;
            hips.body.isKinematic = suspendHips;
            _suspendApplied = suspendHips;
        }

        void OnDisable()
        {
            // Turning pose matching off returns the character to a limp passive ragdoll.
            if (_rig == null) return;
            foreach (var bone in _rig.bones)
            {
                if (bone?.joint == null) continue;
                var drive = bone.joint.slerpDrive;
                drive.positionSpring = 0f;
                drive.positionDamper = 0f;
                bone.joint.slerpDrive = drive;
            }
            if (_suspendApplied && _rig.Root?.body != null)
            {
                _rig.Root.body.isKinematic = false;
                _suspendApplied = false;
            }
        }
    }
}
