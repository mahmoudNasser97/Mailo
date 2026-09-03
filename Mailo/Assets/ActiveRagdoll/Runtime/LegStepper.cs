using UnityEngine;

namespace ActiveRagdoll
{
    /// <summary>
    /// Phase 3. One per leg. A Stance/Swing state machine that, when told to step, swings the
    /// foot along an arc to a target and solves two-bone (hip+knee) IK to reach it.
    ///
    /// CRITICAL (spec §Phase 3): the IK writes rotations into the ANIMATED rig's leg bones, so
    /// they flow through PoseMatcher as joint targets. It never writes physical leg transforms —
    /// that is what preserves reactivity mid-step. For the world-space IK here to map onto the
    /// physical foot, BalanceController slaves the animated hips onto the physical hips each step.
    /// </summary>
    public class LegStepper
    {
        public enum Phase { Stance, Swing }
        public Phase State { get; private set; } = Phase.Stance;

        public readonly bool isLeft;
        public int kneeSign = 1; // flip if knees bend backwards (rig-dependent)

        readonly RagdollRig _rig;
        readonly Transform _animThigh, _animShin, _animFoot;
        readonly Transform _physThigh, _physFoot;
        readonly float _thighLen, _shinLen, _reach;

        public Vector3 PlantedPos { get; private set; }
        public Vector3 FootTarget { get; private set; }
        public Vector3 SwingStart { get; private set; }
        public Vector3 SwingTarget { get; private set; }
        public float StateTime { get; private set; }
        public bool IsSwinging => State == Phase.Swing;
        public bool Valid { get; private set; }

        public LegStepper(RagdollRig rig, bool isLeft)
        {
            _rig = rig;
            this.isLeft = isLeft;

            rig.TryGetBone(isLeft ? BodyPart.ThighL : BodyPart.ThighR, out var t);
            rig.TryGetBone(isLeft ? BodyPart.ShinL : BodyPart.ShinR, out var s);
            rig.TryGetBone(isLeft ? BodyPart.FootL : BodyPart.FootR, out var f);

            _animThigh = t?.target; _animShin = s?.target; _animFoot = f?.target;
            _physThigh = t?.physical; _physFoot = f?.physical;

            Valid = _animThigh && _animShin && _animFoot && _physThigh && _physFoot;
            if (!Valid) return;

            _thighLen = Vector3.Distance(_animThigh.position, _animShin.position);
            _shinLen = Vector3.Distance(_animShin.position, _animFoot.position);
            _reach = _thighLen + _shinLen;
            PlantedPos = _physFoot.position;
            FootTarget = PlantedPos;
        }

        /// <summary>Begin a swing toward the capture point (clamped to reach + max step distance).</summary>
        public void StartSwing(Vector3 captureWorld, Vector3 outwardDir)
        {
            if (!Valid || State == Phase.Swing) return;
            StartSwingTo(captureWorld + outwardDir * _rig.profile.stepOutwardBias);
        }

        /// <summary>Begin a swing to an explicit world target (clamped to reach + max step distance).</summary>
        public void StartSwingTo(Vector3 worldTarget)
        {
            if (!Valid || State == Phase.Swing) return;
            var p = _rig.profile;

            Vector3 hip = _physThigh.position;
            Vector3 fromHip = worldTarget - hip; fromHip.y = 0f;
            float maxR = Mathf.Min(p.maxStepDistance, _reach * 0.85f);
            if (fromHip.magnitude > maxR)
                worldTarget = new Vector3(hip.x, worldTarget.y, hip.z) + fromHip.normalized * maxR;

            SwingStart = PlantedPos;
            SwingTarget = GroundAt(worldTarget);
            State = Phase.Swing;
            StateTime = 0f;
        }

        public void Tick(float dt, Vector3 poleDir)
        {
            if (!Valid) return;
            StateTime += dt;
            var p = _rig.profile;

            if (State == Phase.Swing)
            {
                float tt = Mathf.Clamp01(StateTime / Mathf.Max(p.stepDuration, 0.05f));
                float eased = tt * tt * (3f - 2f * tt);           // smoothstep in/out
                Vector3 flat = Vector3.Lerp(SwingStart, SwingTarget, eased);
                float h = p.stepHeight * Mathf.Sin(Mathf.PI * tt); // parabolic lift
                FootTarget = flat + Vector3.up * h;

                if (tt >= 1f)
                {
                    State = Phase.Stance;
                    StateTime = 0f;
                    PlantedPos = SwingTarget;
                    FootTarget = SwingTarget;
                }
            }
            else
            {
                FootTarget = PlantedPos; // glue the stance foot to the ground as the body moves
            }

            SolveIK(FootTarget, poleDir);
        }

        // Aim-based two-bone IK, rig-agnostic (uses current bone→child directions, no assumptions
        // about bone local axes). Writes world rotations to the ANIMATED bones only.
        void SolveIK(Vector3 footTarget, Vector3 poleDir)
        {
            Vector3 hip = _animThigh.position;
            Vector3 toT = footTarget - hip;
            float d = Mathf.Clamp(toT.magnitude, Mathf.Abs(_thighLen - _shinLen) + 0.02f, _reach - 0.02f);
            Vector3 dir = toT.sqrMagnitude > 1e-6f ? toT.normalized : Vector3.down;

            float cosA = Mathf.Clamp((_thighLen * _thighLen + d * d - _shinLen * _shinLen) / (2f * _thighLen * d), -1f, 1f);
            float a = Mathf.Acos(cosA) * Mathf.Rad2Deg;

            Vector3 bendAxis = Vector3.Cross(dir, poleDir);
            if (bendAxis.sqrMagnitude < 1e-6f) bendAxis = Vector3.Cross(dir, Vector3.forward);
            bendAxis.Normalize();

            Vector3 thighDir = Quaternion.AngleAxis(a * kneeSign, bendAxis) * dir;

            Vector3 curThigh = (_animShin.position - _animThigh.position).normalized;
            if (curThigh.sqrMagnitude > 1e-6f)
                _animThigh.rotation = Quaternion.FromToRotation(curThigh, thighDir) * _animThigh.rotation;

            Vector3 shinDir = (footTarget - _animShin.position);
            Vector3 curShin = (_animFoot.position - _animShin.position);
            if (shinDir.sqrMagnitude > 1e-6f && curShin.sqrMagnitude > 1e-6f)
                _animShin.rotation = Quaternion.FromToRotation(curShin.normalized, shinDir.normalized) * _animShin.rotation;
        }

        Vector3 GroundAt(Vector3 p)
        {
            Vector3 from = p + Vector3.up * 2f;
            var hits = Physics.SphereCastAll(from, 0.08f, Vector3.down, 4f, ~0, QueryTriggerInteraction.Ignore);
            float bestY = 0f, bestDist = float.MaxValue;
            bool found = false;
            foreach (var h in hits)
            {
                if (_rig.physicalRoot != null && h.collider.transform.IsChildOf(_rig.physicalRoot)) continue; // skip self
                if (h.distance < bestDist) { bestDist = h.distance; bestY = h.point.y; found = true; }
            }
            return new Vector3(p.x, found ? bestY : 0f, p.z);
        }
    }
}
