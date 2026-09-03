using System.Collections.Generic;
using UnityEngine;

namespace ActiveRagdoll
{
    /// <summary>
    /// Phase 2. Keeps the character upright over a support polygon using the linear
    /// inverted-pendulum capture point, layered ankle / hip / gravity-compensation response.
    ///
    /// Stepping (capture point outside the polygon) only raises a flag here — Phase 3 acts on
    /// it. For the Phase 2 test the feet are planted (frozen) so balance can be tested without
    /// stepping; big shoves push the capture point out of the polygon and it falls, which is
    /// correct for now.
    /// </summary>
    [RequireComponent(typeof(RagdollRig))]
    [DefaultExecutionOrder(-20)] // run before PoseMatcher so the leg-IK targets are written first
    public class BalanceController : MonoBehaviour
    {
        [Tooltip("Freeze the legs (feet+shins+thighs) into a rigid fixed stance — the spec's Phase 2 " +
                 "test base. At g=-30 a foot-only pin can't stand (ankle drive is softer than gravity), " +
                 "so the whole lower body is frozen. Turn off in Phase 3 when real stepping exists.")]
        public bool plantFeet = true;

        // Whole lower body + pelvis. At g=-30 the upper body pitches the pelvis over even when
        // the legs are anchored, so the pelvis is frozen too. This is a Phase 2 TEST scaffold —
        // Phase 3 unfreezes all of it and the character stays up by stepping instead.
        static readonly BodyPart[] StanceParts =
        {
            BodyPart.FootL, BodyPart.FootR, BodyPart.ShinL, BodyPart.ShinR,
            BodyPart.ThighL, BodyPart.ThighR, BodyPart.Hips,
        };

        [Tooltip("Inside-margin (m) below which the capture point counts as 'near the edge' → hip strategy dominates.")]
        public float edgeMargin = 0.05f;

        [Header("Stepping (Phase 3) — needs 'Plant legs' OFF")]
        [Tooltip("Enable Phase 3 stepping. The legs must be dynamic (Plant legs OFF) for it to do anything.")]
        public bool enableStepping = true;
        [Tooltip("Hold the pelvis at its start height (horizontal free) so stepping can be validated " +
                 "before full free-fall dynamics. Turn OFF for fully physical stepping.")]
        public bool supportHips = true;
        [Tooltip("Continuous crutch dial: 1 = pelvis fully held upright + at height (rung 2), " +
                 "0 = no pelvis help at all = fully physical stepping (rung 3). Walk it toward 0.")]
        [Range(0f, 1f)] public float supportFraction = 1f;
        public float supportKp = 900f;
        public float supportKd = 90f;
        [Tooltip("Downward force on the STANCE foot so it grips the ground (spec §5b foot planting). " +
                 "The pelvis crutch lifts weight off the feet, so without this they skate on shoves.")]
        public float footPlantForce = 700f;
        [Tooltip("Flip if the knees bend backwards on your rig (the classic two-bone-IK sign gotcha).")]
        public bool flipKnees = false;
        [Tooltip("VALIDATION: pin the pelvis kinematic (fixed) while the legs are dynamic, so the leg " +
                 "stepping/IK can be checked without the pelvis collapsing. Turn OFF for real dynamics.")]
        public bool pinPelvis = true;
        [Tooltip("VALIDATION: march in place (auto-alternating steps under the hips) to verify the leg " +
                 "IK / knee direction. Turn OFF to step toward the capture point when shoved.")]
        public bool marchTest = true;

        // ---- Readouts (also consumed by the tuning panel and Phase 3) ----
        public bool Grounded { get; private set; }
        public bool StepRequested { get; private set; }
        public Vector3 CapturePointWorld { get; private set; }
        public bool CaptureInside { get; private set; }
        public float CaptureSignedDistance { get; private set; }
        public Vector3 StepDirection { get; private set; }
        public Vector3 CenterOfMass { get; private set; }
        public Vector3 ComVelocity { get; private set; }
        public IReadOnlyList<Vector2> SupportHull => _hull;
        public float SupportY { get; private set; }

        RagdollRig _rig;
        RagdollFootSensor _sensorL, _sensorR;
        Vector3 _prevCom;
        bool _hasPrevCom;
        float _lastStepLog = -1f;

        LegStepper _left, _right;
        Transform _animHips, _physHips;
        float _supportHeight;
        bool _hasSupportHeight;

        readonly List<Vector2> _points = new List<Vector2>();
        List<Vector2> _hull = new List<Vector2>();

        static readonly Vector3[] BoxSigns =
        {
            new Vector3( 1, 1, 1), new Vector3( 1, 1,-1), new Vector3( 1,-1, 1), new Vector3( 1,-1,-1),
            new Vector3(-1, 1, 1), new Vector3(-1, 1,-1), new Vector3(-1,-1, 1), new Vector3(-1,-1,-1),
        };

        void Awake()
        {
            _rig = GetComponent<RagdollRig>();
        }

        // High-friction feet so the stance foot GRIPS instead of skating — otherwise repeated
        // shoves just translate the whole character sideways (spec §5b: high friction on feet).
        void SetupFootFriction()
        {
            var mat = new PhysicsMaterial("RagdollFoot")
            {
                dynamicFriction = 1.2f,
                staticFriction = 1.4f,
                frictionCombine = PhysicsMaterialCombine.Maximum,
            };
            foreach (var foot in new[] { BodyPart.FootL, BodyPart.FootR })
                if (_rig.TryGetBone(foot, out var b) && b.physical != null)
                    foreach (var c in b.physical.GetComponents<Collider>())
                        c.sharedMaterial = mat;
        }

        RagdollFootSensor EnsureSensor(BodyPart foot)
        {
            if (!_rig.TryGetBone(foot, out var bone) || bone.physical == null) return null;
            var s = bone.physical.GetComponent<RagdollFootSensor>();
            if (s == null) s = bone.physical.gameObject.AddComponent<RagdollFootSensor>();
            s.part = foot;
            return s;
        }

        void OnEnable()
        {
            if (_rig == null) _rig = GetComponent<RagdollRig>();
            // Our OnEnable can run before RagdollRig.Awake — rebuild the bone lookup ourselves so
            // the freeze below actually finds the bodies. RebuildLookup is idempotent.
            _rig.RebuildLookup();
            // Create the foot sensors now that the bone lookup exists (Awake may run before
            // RagdollRig.Awake, so TryGetBone would find nothing there).
            _sensorL = EnsureSensor(BodyPart.FootL);
            _sensorR = EnsureSensor(BodyPart.FootR);
            SetupFootFriction();
            InitSteppers();
            // Set kinematic state from t=0 so the character never gets a frame to topple.
            ApplyBodyMode();
        }

        void InitSteppers()
        {
            _physHips = _rig.Root?.physical;
            _animHips = _rig.Root?.target;
            _left = new LegStepper(_rig, true);
            _right = new LegStepper(_rig, false);
            _hasSupportHeight = false;
        }

        void OnDisable()
        {
            SetAllKinematic(false);
        }

        void FixedUpdate()
        {
            if (_rig == null || _rig.profile == null) return;
            var p = _rig.profile;

            ApplyBodyMode();

            // 1. Centre of mass + smoothed finite-difference velocity.
            CenterOfMass = _rig.CenterOfMass;
            if (_hasPrevCom)
            {
                Vector3 raw = (CenterOfMass - _prevCom) / Time.fixedDeltaTime;
                ComVelocity = Vector3.Lerp(raw, ComVelocity, p.comVelocitySmoothing);
            }
            _prevCom = CenterOfMass;
            _hasPrevCom = true;

            // 2. Support polygon from grounded feet.
            GatherSupport();

            float g = Mathf.Abs(Physics.gravity.y);
            Vector2 capture = Vector2.zero;

            // 3. Capture point + step flag — only meaningful with a support polygon under us.
            //    (Do NOT early-return when airborne: the legs must keep solving IK so a lifted
            //     swing foot comes back down — otherwise a step deadlocks the character in the air.)
            if (Grounded && _hull.Count >= 3)
            {
                float comHeight = Mathf.Max(CenterOfMass.y - SupportY, 0.1f);
                float omega = Mathf.Sqrt(g / comHeight);
                Vector2 comXZ = new Vector2(CenterOfMass.x, CenterOfMass.z);
                Vector2 velXZ = new Vector2(ComVelocity.x, ComVelocity.z);
                capture = comXZ + velXZ / omega;
                CapturePointWorld = new Vector3(capture.x, SupportY, capture.y);

                CaptureSignedDistance = SupportPolygon.SignedDistance(_hull, capture);
                CaptureInside = CaptureSignedDistance < 0f;

                Vector2 centroid = SupportPolygon.Centroid(_hull);
                Vector2 outDir = capture - centroid;
                StepDirection = new Vector3(outDir.x, 0f, outDir.y).normalized;
                StepRequested = !CaptureInside;
            }
            else
            {
                CaptureInside = false;
                StepRequested = false;
            }

            // 4. Layered response. Runs even while briefly airborne so the pelvis/torso stay held
            //    and the legs keep stepping mid-move.
            ApplyGravityCompensation(p, g);
            ApplyHipStrategy(p);                          // no-op while the hips are kinematic
            if (plantFeet)
            {
                if (Grounded && CaptureInside) ApplyAnkleStrategy(p, capture);   // Phase 2 fixed stance
            }
            else
            {
                if (supportHips) ApplySupportHips(g);      // hold pelvis height while stepping catches it
                if (enableStepping) UpdateStepping();      // legs ALWAYS solve IK / march / catch
            }

            if (StepRequested && Time.time - _lastStepLog > 1f)
            {
                _lastStepLog = Time.time;
                Debug.Log($"[Active Ragdoll] STEP — capture {CaptureSignedDistance:0.00} m outside, dir {StepDirection}.");
            }
        }

        // ---- Stepping (Phase 3) --------------------------------------------

        void UpdateStepping()
        {
            if (_left == null || !_left.Valid || !_right.Valid) return;
            _left.kneeSign = _right.kneeSign = flipKnees ? -1 : 1;

            // Slave the animated hips onto the physical hips so world-space leg IK maps to the
            // physical foot. Children's LOCAL rotations are untouched, so PoseMatcher is unaffected.
            if (_animHips != null && _physHips != null)
                _animHips.SetPositionAndRotation(_physHips.position, _physHips.rotation);

            // Forward direction (knee pole) from the leg spread — rig-agnostic.
            Vector3 right = Vector3.right;
            if (_rig.TryGetBone(BodyPart.ThighR, out var tr) && _rig.TryGetBone(BodyPart.ThighL, out var tl)
                && tr.physical && tl.physical)
                right = tr.physical.position - tl.physical.position;
            right.y = 0f;
            if (right.sqrMagnitude < 1e-6f) right = Vector3.right;
            right.Normalize();
            Vector3 forward = Vector3.Cross(right, Vector3.up).normalized;

            bool anySwing = _left.IsSwinging || _right.IsSwinging;
            if (marchTest)
            {
                // VALIDATION: auto-alternate stepping to the desired stance under each hip, so the leg
                // IK / knee direction / swing arc can be verified without needing balance.
                if (!anySwing)
                {
                    LegStepper s = _left.StateTime >= _right.StateTime ? _left : _right;
                    if (s.StateTime >= _rig.profile.minStanceTime + _rig.profile.stepDuration)
                        s.StartSwingTo(DesiredStance(s, right));
                }
            }
            else if (StepRequested && !anySwing)
            {
                // Real stepping: the leg farther from the capture point swings to catch it.
                Vector2 cap = new Vector2(CapturePointWorld.x, CapturePointWorld.z);
                LegStepper s = FootDistXZ(_left, cap) >= FootDistXZ(_right, cap) ? _left : _right;
                if (s.StateTime >= _rig.profile.minStanceTime)
                    s.StartSwing(CapturePointWorld, s.isLeft ? -right : right);
            }

            _left.Tick(Time.fixedDeltaTime, forward);
            _right.Tick(Time.fixedDeltaTime, forward);

            ApplyFootPlant(_left);
            ApplyFootPlant(_right);
        }

        // Press the planted foot into the ground so it grips (more normal force → more friction).
        // Scaled by supportFraction (= poise), so a knocked-down character stops planting and
        // ragdolls freely instead of gripping the floor mid-tumble.
        void ApplyFootPlant(LegStepper s)
        {
            if (s == null || !s.Valid || s.IsSwinging) return; // stance foot only
            BodyPart foot = s.isLeft ? BodyPart.FootL : BodyPart.FootR;
            if (_rig.TryGetBone(foot, out var b) && b.body != null && !b.body.isKinematic)
                b.body.AddForce(Vector3.down * (footPlantForce * supportFraction), ForceMode.Force);
        }

        Vector3 DesiredStance(LegStepper s, Vector3 right)
        {
            if (!_rig.TryGetBone(s.isLeft ? BodyPart.ThighL : BodyPart.ThighR, out var thigh) || thigh.physical == null)
                return s.PlantedPos;
            Vector3 hip = thigh.physical.position;
            Vector3 outward = (s.isLeft ? -right : right) * 0.08f;
            return new Vector3(hip.x, 0f, hip.z) + outward; // StartSwingTo's GroundAt sets the Y
        }

        static float FootDistXZ(LegStepper s, Vector2 cap)
        {
            Vector3 f = s.PlantedPos;
            return Vector2.Distance(new Vector2(f.x, f.z), cap);
        }

        void ApplySupportHips(float g)
        {
            var hips = _rig.Root;
            if (hips?.body == null || hips.body.isKinematic) return;
            if (!_hasSupportHeight) { _supportHeight = hips.body.position.y; _hasSupportHeight = true; }

            if (supportFraction <= 0.01f) return; // fully physical — no pelvis help

            // Vertical PD holds the pelvis at its start height (cancel gravity + spring/damp),
            // scaled by the crutch dial. Horizontal is free, so shoves move it and stepping catches.
            float err = _supportHeight - hips.body.position.y;
            float ay = supportFraction * (g + supportKp * err - supportKd * hips.body.linearVelocity.y);
            hips.body.AddForce(Vector3.up * ay, ForceMode.Acceleration);
        }

        // ---- Response layers ------------------------------------------------

        static readonly BodyPart[] UpperBody =
        {
            BodyPart.Chest, BodyPart.Spine, BodyPart.Head,
            BodyPart.UpperArmL, BodyPart.UpperArmR, BodyPart.LowerArmL, BodyPart.LowerArmR,
            BodyPart.HandL, BodyPart.HandR,
        };

        void ApplyGravityCompensation(RagdollProfile p, float g)
        {
            if (p.gravityCompensation <= 0f) return;

            // Feed-forward upward force = fraction of EACH upper-body bone's own weight, applied
            // per-bone. Lifting only the chest still lets the head/arms above it droop; offsetting
            // each bone's weight where it acts is what actually holds posture. This lets us hold
            // the torso upright at g=-30 without cranking the pose springs into instability.
            foreach (var part in UpperBody)
                if (_rig.TryGetBone(part, out var b) && b.body != null && !b.body.isKinematic)
                    b.body.AddForce(Vector3.up * (p.gravityCompensation * b.body.mass * g), ForceMode.Force);
        }

        void ApplyHipStrategy(RagdollProfile p)
        {
            var hips = _rig.Root;
            if (hips?.body == null || hips.body.isKinematic) return; // hips frozen in the Phase 2 scaffold

            // Torso "up" from hips→chest (rig-agnostic; doesn't assume a bone axis).
            Vector3 torsoUp = Vector3.up;
            if (_rig.TryGetBone(BodyPart.Chest, out var chest) && chest.physical != null)
                torsoUp = (chest.physical.position - hips.physical.position).normalized;
            else if (_rig.TryGetBone(BodyPart.Spine, out var spine) && spine.physical != null)
                torsoUp = (spine.physical.position - hips.physical.position).normalized;

            // PD on upright error, mass-independent (ForceMode.Acceleration), scaled by poise/support
            // so the pelvis loses its will to stay upright as poise drains → it tips and collapses.
            Vector3 axis = Vector3.Cross(torsoUp, Vector3.up);       // ∝ sin(tilt), points along correction axis
            Vector3 torque = supportFraction * (p.hipKp * axis - p.hipKd * hips.body.angularVelocity);
            hips.body.AddTorque(torque, ForceMode.Acceleration);
        }

        void ApplyAnkleStrategy(RagdollProfile p, Vector2 capture)
        {
            if (p.ankleKp <= 0f) return;
            Vector2 centroid = SupportPolygon.Centroid(_hull);
            Vector2 offset = capture - centroid; // want to shift CoP toward the capture point
            Vector3 offsetDir = new Vector3(offset.x, 0f, offset.y);
            if (offsetDir.sqrMagnitude < 1e-6f) return;

            // Small tilt torque at the shins to nudge the centre of pressure. Ankle strategy is
            // deliberately gentle (0.25 scale + a hard cap); the hip strategy does the heavy
            // lifting, and an over-eager ankle term is a classic source of standing oscillation.
            float mag = Mathf.Min(p.ankleKp * 0.25f * Mathf.Min(offset.magnitude, 0.2f), 15f);
            Vector3 tilt = Vector3.Cross(Vector3.up, offsetDir.normalized) * mag;
            ApplyToShin(BodyPart.ShinL, tilt);
            ApplyToShin(BodyPart.ShinR, tilt);
        }

        void ApplyToShin(BodyPart shin, Vector3 torque)
        {
            if (_rig.TryGetBone(shin, out var b) && b.body != null && !b.body.isKinematic)
                b.body.AddTorque(torque, ForceMode.Acceleration);
        }

        // ---- Support polygon ------------------------------------------------

        void GatherSupport()
        {
            _points.Clear();
            float sumY = 0f;
            int footCount = 0;

            AddFoot(BodyPart.FootL, _sensorL, ref sumY, ref footCount);
            AddFoot(BodyPart.FootR, _sensorR, ref sumY, ref footCount);

            Grounded = footCount > 0;
            SupportY = Grounded ? sumY / footCount : CenterOfMass.y;
            _hull = SupportPolygon.ConvexHull(_points);
        }

        void AddFoot(BodyPart foot, RagdollFootSensor sensor, ref float sumY, ref int footCount)
        {
            bool grounded = plantFeet || (sensor != null && sensor.IsGrounded);
            if (!grounded) return;
            if (!_rig.TryGetBone(foot, out var bone) || bone.physical == null) return;

            var col = bone.physical.GetComponent<Collider>();
            if (col == null) return;

            float minY = float.MaxValue;
            if (col is BoxCollider box)
            {
                Vector3 c = box.center, e = box.size * 0.5f;
                foreach (var s in BoxSigns)
                {
                    Vector3 w = bone.physical.TransformPoint(c + Vector3.Scale(e, s));
                    _points.Add(new Vector2(w.x, w.z));
                    minY = Mathf.Min(minY, w.y);
                }
            }
            else
            {
                var b = col.bounds;
                _points.Add(new Vector2(b.min.x, b.min.z));
                _points.Add(new Vector2(b.max.x, b.min.z));
                _points.Add(new Vector2(b.min.x, b.max.z));
                _points.Add(new Vector2(b.max.x, b.max.z));
                minY = b.min.y;
            }
            sumY += minY;
            footCount++;
        }

        float MassOf(BodyPart part) => _rig.TryGetBone(part, out var b) && b.body != null ? b.body.mass : 0f;

        // ---- Freeze / unfreeze feet ----------------------------------------

        // Kinematic (not constraints): a kinematic body is an absolute anchor for its joints.
        // Constraints alone get dragged by the solver under g=-30 and the character sinks.
        //   plantFeet    → whole lower body + pelvis frozen (Phase 2 rigid stance).
        //   pinPelvis    → only the pelvis frozen, legs dynamic (Phase 3 stepping validation).
        //   neither      → all dynamic (full physical stepping).
        void ApplyBodyMode()
        {
            bool freezeLegs = plantFeet;
            bool freezeHips = plantFeet || pinPelvis;
            SetKinematic(BodyPart.FootL, freezeLegs); SetKinematic(BodyPart.FootR, freezeLegs);
            SetKinematic(BodyPart.ShinL, freezeLegs); SetKinematic(BodyPart.ShinR, freezeLegs);
            SetKinematic(BodyPart.ThighL, freezeLegs); SetKinematic(BodyPart.ThighR, freezeLegs);
            SetKinematic(BodyPart.Hips, freezeHips);

            // The pelvis rotation-lock only holds while poise/support is HIGH (crisp standing).
            // Once support drops into the stagger band the lock releases so the pelvis can actually
            // tip over and commit to a knockdown — otherwise a locked-upright pelvis leaves it stuck
            // "on its knees" and it never fully falls. Below that, the scaled hip PD is the only
            // upright authority, and it fades to nothing at poise 0 (full ragdoll).
            bool crutch = supportHips && supportFraction > 0.5f;
            if (!freezeHips && _rig.TryGetBone(BodyPart.Hips, out var h) && h.body != null)
                h.body.constraints = crutch ? RigidbodyConstraints.FreezeRotation : RigidbodyConstraints.None;
        }

        void SetKinematic(BodyPart part, bool k)
        {
            if (_rig.TryGetBone(part, out var b) && b.body != null && b.body.isKinematic != k)
                b.body.isKinematic = k;
        }

        void SetAllKinematic(bool k)
        {
            foreach (var part in StanceParts) SetKinematic(part, k);
        }

        // ---- Gizmos ---------------------------------------------------------

        void OnDrawGizmos()
        {
            if (!Application.isPlaying) return;

            if (Grounded && _hull != null && _hull.Count >= 2)
            {
                // Support polygon outline.
                Gizmos.color = new Color(0.2f, 0.9f, 0.4f, 0.9f);
                for (int i = 0; i < _hull.Count; i++)
                {
                    Vector3 a = new Vector3(_hull[i].x, SupportY, _hull[i].y);
                    Vector3 b = new Vector3(_hull[(i + 1) % _hull.Count].x, SupportY, _hull[(i + 1) % _hull.Count].y);
                    Gizmos.DrawLine(a, b);
                }

                // Capture point — green inside, red outside.
                Gizmos.color = CaptureInside ? Color.green : Color.red;
                Gizmos.DrawSphere(CapturePointWorld, 0.05f);
                Gizmos.DrawLine(new Vector3(CenterOfMass.x, SupportY, CenterOfMass.z), CapturePointWorld);
            }

            if (!plantFeet && enableStepping)
            {
                DrawStepper(_left);
                DrawStepper(_right);
            }
        }

        void DrawStepper(LegStepper s)
        {
            if (s == null || !s.Valid) return;

            Gizmos.color = s.IsSwinging ? new Color(1f, 0.8f, 0.1f) : new Color(0.2f, 0.7f, 1f);
            Gizmos.DrawSphere(s.FootTarget, 0.04f);

            if (s.IsSwinging)
            {
                float h = _rig.profile != null ? _rig.profile.stepHeight : 0.15f;
                Vector3 prev = s.SwingStart;
                for (int i = 1; i <= 12; i++)
                {
                    float tt = i / 12f;
                    float e = tt * tt * (3f - 2f * tt);
                    Vector3 pt = Vector3.Lerp(s.SwingStart, s.SwingTarget, e) + Vector3.up * (h * Mathf.Sin(Mathf.PI * tt));
                    Gizmos.DrawLine(prev, pt);
                    prev = pt;
                }
                Gizmos.color = new Color(1f, 0.4f, 0f);
                Gizmos.DrawSphere(s.SwingTarget, 0.05f);
            }
        }
    }
}
