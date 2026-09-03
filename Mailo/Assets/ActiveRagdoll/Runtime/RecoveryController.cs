using UnityEngine;

namespace ActiveRagdoll
{
    /// <summary>
    /// Phase 5a. Get-up. Polls for REST (not elapsed time), then ramps poise 0→1 so the physical
    /// rig stands as the drives return. Because the physical rig chases the animation as gains rise,
    /// the get-up is itself fully simulated — drop another crate mid-recovery and poise drains again
    /// and it collapses, with no re-entrancy bug because there is no state to be in (spec §5a).
    ///
    /// If get-up clips are assigned, it CrossFades the matching one (prone vs supine) on the animated
    /// rig; otherwise the poise ramp alone stands it up.
    /// </summary>
    [RequireComponent(typeof(RagdollRig))]
    [RequireComponent(typeof(PoiseController))]
    public class RecoveryController : MonoBehaviour
    {
        [Header("Optional get-up clips (played on the animated rig)")]
        public Animator animatedAnimator;
        public string proneGetUpState = "";   // face-down get-up state name
        public string supineGetUpState = "";   // face-up get-up state name

        public bool IsRecovering { get; private set; }

        RagdollRig _rig;
        PoiseController _poise;
        float _restTimer;

        void OnEnable()
        {
            _rig = GetComponent<RagdollRig>();
            _rig.RebuildLookup();
            _poise = GetComponent<PoiseController>();
            if (animatedAnimator == null && _rig.animatedRoot != null)
                animatedAnimator = _rig.animatedRoot.GetComponentInChildren<Animator>();
        }

        void FixedUpdate()
        {
            if (_rig == null || _rig.profile == null || _poise == null) return;
            var p = _rig.profile;

            // Poll for rest (spec §5a): low kinetic energy for restDuration.
            bool settledNow = _rig.TotalKineticEnergy < p.restEnergyThreshold;
            _restTimer = settledNow ? _restTimer + Time.fixedDeltaTime : 0f;

            bool readyToGetUp = _poise.IsDown && _restTimer > p.restDuration;

            if (readyToGetUp)
            {
                if (!IsRecovering)
                {
                    IsRecovering = true;
                    TriggerGetUpClip();
                }
                // Ramp poise 0→1 over getUpRampDuration; drives + crutch rise and lift it upright.
                _poise.poise = Mathf.MoveTowards(_poise.poise, 1f, Time.fixedDeltaTime / Mathf.Max(p.getUpRampDuration, 0.1f));
            }
            else
            {
                IsRecovering = false;
            }
        }

        void TriggerGetUpClip()
        {
            if (animatedAnimator == null) return;

            // Facing: chest.up vs world up → face-up (supine) or face-down (prone).
            bool supine = true;
            if (_rig.TryGetBone(BodyPart.Chest, out var c) && c.physical != null)
                supine = Vector3.Dot(c.physical.up, Vector3.up) >= 0f;

            string state = supine ? supineGetUpState : proneGetUpState;
            if (!string.IsNullOrEmpty(state))
                animatedAnimator.CrossFade(state, 0.2f, 0); // CrossFade by state name (never SetTrigger)
        }
    }
}
