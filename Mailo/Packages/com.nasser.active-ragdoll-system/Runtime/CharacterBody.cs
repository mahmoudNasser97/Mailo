using System;
using System.Collections.Generic;
using UnityEngine;

namespace NasserActiveRagdoll
{
    public enum CharacterRole
    {
        [Tooltip("Local player. Gets a FirstPersonRig with a camera and camera-space hand targets.")]
        Player,
        [Tooltip("AI or remote. No camera, no audio listener. Hand targets parented to the chest.")]
        Npc
    }

    /// <summary>
    /// The one component that says "this thing is a physical person".
    ///
    /// Players and NPCs use the exact same one -- the role flag only decides which rig
    /// the wizard builds and which driver is attached. Everything below this line
    /// (balance, grabbing, impacts, knockdown, throwing) has no idea which it is, which
    /// is precisely why a thrown NPC and a thrown player behave identically.
    /// </summary>
    [DefaultExecutionOrder(80)]
    public class CharacterBody : MonoBehaviour, IImpactReceiver
    {
        public enum State { Standing, Ragdolled, GettingUp, Grabbed, Thrown }

        [Header("Identity")]
        public CharacterRole role = CharacterRole.Npc;
        [Tooltip("Shared tuning asset. Values below are overwritten from it on Awake when assigned.")]
        public RagdollProfile profile;

        [Header("References")]
        public ActiveRagdollMuscles muscles;
        public PelvisAnchor anchor;
        public FloatingCapsuleController controller;
        public Animator puppetAnimator;
        public Rigidbody pelvis;
        public Rigidbody chest;
        public Rigidbody head;
        public PhysicsHand leftHand;
        public PhysicsHand rightHand;
        [Tooltip("Players only. Left null for NPCs.")]
        public FirstPersonRig rig;
        public NpcHandTargets npcHands;

        [Header("Knockdown")]
        public float knockdownImpulse = 12f;
        public string[] weakSpots = { "head", "neck" };
        public float weakSpotMultiplier = 2.5f;
        public float accumulationWindow = 0.5f;
        public float tiltFailDot = 0.35f;
        public float fallSpeedThreshold = 9f;
        [Tooltip("Grace period after standing up: for this long the character cannot be knocked down " +
                 "again and is treated as grounded, so it settles instead of falling-getting up-falling " +
                 "in an endless loop. Without it, a stale grounded flag or the collision it fell against " +
                 "immediately re-topples it.")]
        public float standingGrace = 1f;

        [Header("Down and up")]
        [Range(0f, 0.4f)] public float limpStrength = 0.06f;
        [Range(0f, 0.5f)] public float grabbedStrength = 0.18f;
        public float minimumDownTime = 1f;
        public float thrownMinimumDownTime = 1.6f;
        public float settleSpeed = 0.8f;
        public float maxDownTime = 6f;
        public float getUpDuration = 0.85f;
        public LayerMask groundMask = ~0;

        [Header("Animator parameters")]
        public string speedParam = "Speed";
        public string moveXParam = "MoveX";
        public string moveYParam = "MoveY";
        public string groundedParam = "Grounded";
        public string carryParam = "Carrying";
        public string getUpFrontState = "GetUpProne";
        public string getUpBackState = "GetUpSupine";
        public string locomotionState = "Locomotion";

        [Header("Animation smoothing")]
        [Tooltip("Damping on Speed, in seconds. This is what turns a twitchy blend into a smooth one.")]
        public float speedDamping = 0.12f;
        [Tooltip("Damping on the MoveX/MoveY direction parameters.")]
        public float directionDamping = 0.08f;
        [Tooltip("Scale playback rate to match actual ground speed. The main cure for foot sliding.")]
        public bool syncPlaybackToSpeed = true;
        [Tooltip("Clamp on that scaling. Beyond about 1.4 the gait reads as comical.")]
        public float minPlaybackRate = 0.7f, maxPlaybackRate = 1.45f;
        [Tooltip("Root speed of the walk clip, used as the reference for playback scaling. The wizard fills this in from the clip.")]
        public float referenceClipSpeed = 1.6f;

        [Header("Carrying")]
        [Range(0.05f, 1f)] public float encumberedSpeedFactor = 0.45f;
        public float encumbranceReferenceMass = 40f;

        [Header("Debug")]
        [Tooltip("Pins the character in a fully-driven Standing pose and disables knockdown, " +
                 "so the balance can be watched in isolation without the knockdown/get-up loop " +
                 "moving the target. Editor testing only -- turn off for gameplay.")]
        public bool debugHoldStanding = false;
        [Tooltip("Logs what caused each knockdown (impact + source object, tilt, or fall). " +
                 "Turn on to find out what is knocking a standing character down.")]
        public bool logKnockdownCause = false;

        [Header("Auto-calibration")]
        [Tooltip("Ease rideHeight toward the PUPPET's animated standing height (hip to sole) while " +
                 "standing. The puppet is driven only by the animation, so it is a clean reference " +
                 "the physics cannot corrupt — unlike measuring the physical pose, which fed back and " +
                 "spiralled into a sit. Needs a REAL idle clip; a T-pose take measures the straight-" +
                 "leg ceiling instead of the real stance.")]
        public bool autoCalibrateRideHeight = true;

        public State Current { get; private set; } = State.Standing;
        public bool IsPlayer => role == CharacterRole.Player;
        public event Action<State> StateChanged;
        public event Action<Impact> Hit;

        float _stateTime, _accumulated, _accumulatedAt, _baseMaxSpeed;
        float _getUpClipLength;   // measured from the actual get-up clip in BeginGetUp
        float _soleOffset = 0.05f;   // how far the foot sole sits below the ankle bone
        Vector3 _chestLocalUp = Vector3.up, _chestLocalFwd = Vector3.forward;
        int _hSpeed, _hMoveX, _hMoveY, _hGrounded, _hCarry;
        PhysicsHand _grabbedBy;

        void Awake()
        {
            // Capture the rig's real orientation axes while the model is still in its
            // authored standing pose. Bone local axes are whatever the rigger exported.
            if (chest)
            {
                _chestLocalUp = chest.transform.InverseTransformDirection(Vector3.up);
                _chestLocalFwd = chest.transform.InverseTransformDirection(Vector3.forward);
            }

            ApplyProfile();
            _hSpeed = Animator.StringToHash(speedParam);
            _hMoveX = Animator.StringToHash(moveXParam);
            _hMoveY = Animator.StringToHash(moveYParam);
            _hGrounded = Animator.StringToHash(groundedParam);
            _hCarry = Animator.StringToHash(carryParam);
        }

        public void ApplyProfile()
        {
            if (!profile) return;
            RagdollProfile p = profile;

            if (muscles) { muscles.baseSpring = p.baseSpring; muscles.baseDamper = p.baseDamper; }
            if (controller)
            {
                // rideHeight and maxSpeed are NOT taken from the profile: both are per-character
                // measurements baked by the builder — rideHeight from the rig's real leg length,
                // maxSpeed from the locomotion clips' measured stride speed (so the body cannot
                // outrun the feet and slide). Overwriting them from the shared profile would
                // re-create the banana pose and the foot skating. The profile owns the springs and
                // accelerations, not the geometry or the gait speed.
                controller.rideSpring = p.rideSpring;
                controller.rideDamper = p.rideDamper;
                controller.acceleration = p.acceleration;
                controller.maxAccelForce = p.maxAccelForce;
                controller.jumpVelocity = p.jumpVelocity;
            }
            if (anchor)
            {
                anchor.positionSpring = p.pelvisSpring;
                anchor.positionDamper = p.pelvisDamper;
                anchor.uprightSpring = p.uprightSpring;
                anchor.uprightDamper = p.uprightDamper;
                anchor.leanIntoMotion = p.leanIntoMotion;
            }
            knockdownImpulse = p.ScaledKnockdown;
            weakSpotMultiplier = p.weakSpotMultiplier;
            accumulationWindow = p.accumulationWindow;
            limpStrength = p.limpStrength;
            grabbedStrength = p.grabbedStrength;
            minimumDownTime = p.minimumDownTime;
            thrownMinimumDownTime = p.thrownMinimumDownTime;
            getUpDuration = p.getUpDuration;
            encumberedSpeedFactor = p.encumberedSpeedFactor;
            encumbranceReferenceMass = p.encumbranceReferenceMass;

            ApplyHand(leftHand, p);
            ApplyHand(rightHand, p);
            if (rig) rig.Apply(p);
        }

        static void ApplyHand(PhysicsHand h, RagdollProfile p)
        {
            if (!h) return;
            h.reachSpring = p.reachSpring;
            h.reachDamper = p.reachDamper;
            h.maxReachForce = p.maxReachForce;
            h.gripSpring = p.gripSpring;
            h.throwForceMultiplier = p.throwForceMultiplier;
            h.maxThrowSpeed = p.maxThrowSpeed;
            h.grabRadius = p.grabRadius;
        }

        void Start()
        {
            _baseMaxSpeed = controller ? controller.maxSpeed : 4.5f;
            foreach (Rigidbody rb in muscles.Bones)
                if (!rb.GetComponent<ImpactRelay>()) rb.gameObject.AddComponent<ImpactRelay>();

            IsolateBonesFromEachOther();
            IsolateControllerFromBones();
            _soleOffset = MeasureSoleOffset();
            SetState(State.Standing, force: true);
        }

        /// <summary>
        /// How far the foot sole (collider bottom) sits below the ankle bone, in the bind pose.
        /// The puppet only exposes bone transforms, so measuring standing height from the puppet
        /// gives hip-to-ANKLE; add this to get hip-to-SOLE, which is the real rideHeight.
        /// </summary>
        float MeasureSoleOffset()
        {
            if (muscles == null) return 0.05f;
            float best = 0f;
            foreach (Rigidbody rb in muscles.Bones)
            {
                if (!rb) continue;
                if (!rb.name.ToLowerInvariant().Contains("foot")) continue;
                Collider c = rb.GetComponent<Collider>();
                if (c) best = Mathf.Max(best, rb.transform.position.y - c.bounds.min.y);
            }
            return best > 0.001f ? best : 0.05f;
        }

        /// <summary>
        /// Turn off collision between EVERY pair of this character's own bones.
        ///
        /// The ConfigurableJoints only disable collision between DIRECTLY connected bones
        /// (thigh↔hips, shin↔thigh). That leaves every non-adjacent pair free to collide,
        /// and on a humanoid those pairs overlap constantly: the thighs sit right against
        /// the spine and against each other, and the feet cross when walking. Each overlap
        /// is ejected by the solver with a large depenetration impulse that the impact bus
        /// reads as a real blow -- so the character knocks ITSELF down the instant its legs
        /// move, which looks exactly like walking into an invisible wall and collapsing.
        ///
        /// A driven active ragdoll must not self-collide; the animation is what keeps the
        /// limbs apart. Bones still collide with OTHER characters and props, so grabbing,
        /// hitting and being hit are unaffected -- this is per-collider-pair, not a layer.
        /// </summary>
        void IsolateBonesFromEachOther()
        {
            if (muscles == null) return;

            List<Collider> cols = new List<Collider>();
            foreach (Rigidbody rb in muscles.Bones)
            {
                if (!rb) continue;
                foreach (Collider c in rb.GetComponentsInChildren<Collider>())
                    if (c && c.enabled) cols.Add(c);
            }

            for (int i = 0; i < cols.Count; i++)
                for (int j = i + 1; j < cols.Count; j++)
                    Physics.IgnoreCollision(cols[i], cols[j], true);
        }

        /// <summary>
        /// The controller capsule lives INSIDE the torso and shares the Character layer
        /// with every ragdoll bone. Left alone, the solver sees deep interpenetration on
        /// frame one and blasts the limbs apart -- which looks like a ragdoll collapse
        /// but is actually the capsule fighting its own body.
        ///
        /// Layer tricks cannot fix this: bones must still collide with OTHER characters,
        /// so the exclusion has to be per-collider-pair.
        /// </summary>
        void IsolateControllerFromBones()
        {
            if (!controller || muscles == null) return;

            Collider[] capsuleColliders = controller.GetComponentsInChildren<Collider>();
            if (capsuleColliders.Length == 0) return;

            foreach (Rigidbody rb in muscles.Bones)
            {
                if (!rb) continue;
                foreach (Collider bone in rb.GetComponentsInChildren<Collider>())
                {
                    if (!bone) continue;
                    foreach (Collider cap in capsuleColliders)
                        if (cap) Physics.IgnoreCollision(bone, cap, true);
                }
            }
        }

        /// <summary>Chest world-up using the rig's real axis, not an assumed one.</summary>
        public Vector3 ChestUp => chest ? chest.transform.TransformDirection(_chestLocalUp) : Vector3.up;
        public Vector3 ChestForward => chest ? chest.transform.TransformDirection(_chestLocalFwd) : Vector3.forward;

        void Update()
        {
            _stateTime += Time.deltaTime;
            DriveAnimator();
        }

        void FixedUpdate()
        {
            ApplyEncumbrance();
            SyncHandStrength();

            // Debug harness: pin the character in a fully-driven Standing state and never
            // knock it down, so balance can be observed in isolation. The knockdown/get-up
            // loop otherwise turns every test into "it fell once and is now thrashing".
            if (debugHoldStanding) { HoldStanding(); return; }

            switch (Current)
            {
                case State.Standing:  TickStanding();  break;
                case State.Ragdolled:
                case State.Thrown:    TickDown();      break;
                case State.GettingUp: TickGettingUp(); break;
                case State.Grabbed:   TickGrabbed();   break;
            }
        }

        /// <summary>
        /// Forces full standing every physics step: strength 1, anchor 1, controller and
        /// puppet on, state Standing, no knockdown. A switch for tuning the balance without
        /// the knockdown/get-up loop moving the target. Not for gameplay.
        /// </summary>
        void HoldStanding()
        {
            if (Current != State.Standing) SetState(State.Standing);
            muscles.strength = 1f;
            anchor.weight = 1f;
            if (controller && !controller.enabled) controller.SetActive(true);
            if (puppetAnimator && !puppetAnimator.enabled) puppetAnimator.enabled = true;
            CalibrateRideHeight();
        }

        /// <summary>
        /// Feeds the blend trees. Three things here matter more than the graph itself:
        ///
        /// 1. Velocity is measured relative to FACING, not world space. Without this,
        ///    turning while walking swings MoveX wildly and the strafe blend thrashes.
        /// 2. Every parameter is damped. Raw values make even a perfect blend tree
        ///    look twitchy, because physics velocity is noisy frame to frame.
        /// 3. Playback rate is scaled to actual ground speed, so the feet travel as far
        ///    as the body does. This is the real cure for sliding -- damping only hides it.
        /// </summary>
        void DriveAnimator()
        {
            if (!puppetAnimator || !puppetAnimator.enabled || !puppetAnimator.runtimeAnimatorController) return;

            Vector3 v = controller ? controller.Body.Vel() : Vector3.zero;
            v.y = 0f;
            float speed = v.magnitude;

            // Facing-relative, so a turn does not read as a strafe.
            Transform basis = controller ? controller.transform : transform;
            Vector3 local = basis.InverseTransformDirection(v);

            // MoveX/MoveY encode DIRECTION ONLY -- a unit vector -- so a strafe plays the pure
            // strafe clip at full weight no matter how fast you go. Normalising by maxSpeed instead
            // (the old bug) made a walk-speed strafe read as ~0.4 on the axis, which the 2D blend
            // treats as half strafe + half forward -- the legs step forward while you slide sideways.
            // Speed (magnitude, below) still selects the gait tier independently.
            float dirNorm = Mathf.Max(0.05f, speed);

            puppetAnimator.SetFloat(_hSpeed, speed, speedDamping, Time.deltaTime);
            puppetAnimator.SetFloat(_hMoveX, Mathf.Clamp(local.x / dirNorm, -1f, 1f), directionDamping, Time.deltaTime);
            puppetAnimator.SetFloat(_hMoveY, Mathf.Clamp(local.z / dirNorm, -1f, 1f), directionDamping, Time.deltaTime);
            // While getting up the controller is inactive, so its IsGrounded is stale (false, left
            // over from the fall). Reporting that lets the Locomotion->airborne transition fire and
            // the character plays the JUMP/FALL clip in the middle of standing up. It is on the
            // ground recovering, so force grounded during get-up.
            bool grounded = (controller && controller.IsGrounded) || Current == State.GettingUp
                            || (Current == State.Standing && _stateTime < standingGrace);
            puppetAnimator.SetBool(_hGrounded, grounded);
            puppetAnimator.SetBool(_hCarry, CarriedMass > 0.5f);

            SyncPlayback(speed);

            // NPCs have no grab button, so gate their reach on whether they are carrying: arms hang
            // and follow the animation when idle (so the NPC stands naturally, same as the player),
            // and reach only to hold what they carry. Players drive their own hand reaching from input.
            if (npcHands)
            {
                bool npcReach = CarriedMass > 0.5f;
                npcHands.SetReaching(npcReach);
                if (leftHand) leftHand.SetReaching(npcReach);
                if (rightHand) rightHand.SetReaching(npcReach);
            }
        }

        void SyncPlayback(float speed)
        {
            // Only while upright and grounded -- scaling a get-up clip looks broken.
            if (!syncPlaybackToSpeed || Current != State.Standing ||
                (controller && !controller.IsGrounded) || speed < 0.25f)
            {
                puppetAnimator.speed = 1f;
                return;
            }

            float rate = speed / Mathf.Max(0.05f, referenceClipSpeed);
            // Above roughly one clip-speed we are in the run tier, which has its own
            // stride length, so fold the rate back toward 1 instead of doubling it.
            if (rate > 1f) rate = 1f + (rate - 1f) * 0.35f;
            puppetAnimator.speed = Mathf.Clamp(rate, minPlaybackRate, maxPlaybackRate);
        }

        public float CarriedMass =>
            (leftHand ? leftHand.HeldMass : 0f) + (rightHand ? rightHand.HeldMass : 0f);

        // ------------------------------------------------------------------ impacts

        public void ReceiveImpact(in Impact impact)
        {
            Hit?.Invoke(impact);
            if (debugHoldStanding) return;   // frozen for balance testing: never knock down
            if (Current != State.Standing && Current != State.Grabbed) return;

            // A standing character rides the floating capsule, which cannot fall. So its own
            // feet and shins resting or scraping on the STATIC ground must never knock it
            // down -- otherwise an imperfectly-posed rig (e.g. digitigrade legs that clip the
            // floor and get ejected with a big depenetration impulse) floors itself in an
            // endless knockdown/get-up loop. Only blows from things with a rigidbody (thrown
            // props, swung weapons, other characters) are real knockdown impacts; a genuine
            // fall is caught separately by the fall-speed test in TickStanding. A static
            // collider arrives here with source == null (Collision.rigidbody is null).
            if (Current == State.Standing && impact.source == null) return;

            float mag = impact.Magnitude;
            if (impact.receiver && IsWeakSpot(impact.receiver.name)) mag *= weakSpotMultiplier;

            if (Time.time - _accumulatedAt > accumulationWindow) _accumulated = 0f;
            _accumulated += mag;
            _accumulatedAt = Time.time;

            if (_accumulated >= knockdownImpulse)
            {
                _accumulated = 0f;
                if (logKnockdownCause)
                {
                    string src = impact.source ? impact.source.name : "environment (no rigidbody)";
                    string hit = impact.receiver ? impact.receiver.name : "?";
                    Debug.Log($"[Nasser ARS] Knockdown by IMPACT: {mag:0.0} Ns on '{hit}' from " +
                              $"'{src}'. Threshold {knockdownImpulse:0.0}. If this is the ground/own legs " +
                              "while standing, the legs are generating the hit, not a real blow.", this);
                }
                if (Current == State.Grabbed) BreakFreeFromGrab();
                Knockdown();
            }
        }

        bool IsWeakSpot(string boneName)
        {
            string n = boneName.ToLowerInvariant();
            foreach (string s in weakSpots)
                if (!string.IsNullOrEmpty(s) && n.Contains(s.ToLowerInvariant())) return true;
            return false;
        }

        // ------------------------------------------------------------------ states

        public void Knockdown(Vector3 extraImpulse = default)
        {
            if (Current == State.Ragdolled || Current == State.Thrown) return;
            // Just got up: ignore knockdowns briefly so a stale grounded flag, the collision we fell
            // against, or first-frame settling jitter cannot re-topple us into an endless fall/get-up
            // loop. This single guard covers impacts, tilt, fall-speed and ram-into-wall knockdowns.
            if (Current == State.Standing && _stateTime < standingGrace) return;
            GoLimp(State.Ragdolled);
            if (extraImpulse != default) pelvis.AddForce(extraImpulse, ForceMode.Impulse);
        }

        public void EnterGrabbed(PhysicsHand hand)
        {
            _grabbedBy = hand;
            GoLimp(State.Grabbed);
            muscles.strength = grabbedStrength;
        }

        public void ExitGrabbed(Vector3 throwImpulse, CharacterBody thrower)
        {
            _grabbedBy = null;
            if (throwImpulse.sqrMagnitude > 0.01f)
            {
                GoLimp(State.Thrown);
                Grabbable g = pelvis.GetComponent<Grabbable>();
                if (g) g.ArmAsProjectile(thrower ? thrower.gameObject : null);
            }
            else GoLimp(State.Ragdolled);
        }

        void BreakFreeFromGrab()
        {
            if (_grabbedBy) _grabbedBy.Release(throwIt: false);
            _grabbedBy = null;
        }

        void GoLimp(State next)
        {
            SetState(next);
            muscles.strength = limpStrength;
            anchor.weight = 0f;
            controller.SetActive(false);
            if (puppetAnimator) puppetAnimator.enabled = false;
            DropEverything();
        }

        void DropEverything()
        {
            if (leftHand) leftHand.Release(false);
            if (rightHand) rightHand.Release(false);
        }

        void TickStanding()
        {
            CalibrateRideHeight();

            bool overTilted = Vector3.Dot(ChestUp, Vector3.up) < tiltFailDot;
            bool fallingFast = !controller.IsGrounded && controller.Body.Vel().y < -fallSpeedThreshold;
            if (overTilted || fallingFast)
            {
                if (logKnockdownCause)
                    Debug.Log($"[Nasser ARS] Knockdown by {(overTilted ? "TILT" : "FALL")}: " +
                              $"chestUp·up={Vector3.Dot(ChestUp, Vector3.up):0.00} (fail<{tiltFailDot}), " +
                              $"capsuleVy={controller.Body.Vel().y:0.0}.", this);
                Knockdown();
            }
        }

        /// <summary>
        /// Ease rideHeight toward the character's real standing hip height, measured from the
        /// PUPPET's animated idle pose: puppet hip world Y minus the lowest puppet foot bone
        /// world Y, plus the ankle-to-sole offset.
        ///
        /// The puppet matters. It is driven ONLY by the animation, so its stance is the clean
        /// reference the physics cannot bend -- measuring the PHYSICAL pose instead (an earlier
        /// version) fed back on itself and spiralled the character into a sit, because lowering
        /// the hips bent the already-loaded legs further and the target chased them to the floor.
        /// The puppet has no such feedback: its hip-to-foot is whatever the clip poses, full stop.
        ///
        /// Neither the bind pose (straight-leg ceiling, ~0.84) nor the folded physical pose (the
        /// symptom) is right -- the animated standing pose is. NOTE: this is only meaningful once
        /// a REAL idle clip is assigned; a T-pose take poses straight legs and measures the ceiling.
        /// Only while grounded and nearly still, so a step or walk frame never bakes a wrong height.
        /// </summary>
        void CalibrateRideHeight()
        {
            if (!autoCalibrateRideHeight || !controller) return;
            if (!puppetAnimator || !puppetAnimator.isHuman) return;
            if (Current != State.Standing) return;
            if (!controller.IsGrounded || controller.Body.Vel().magnitude > 0.6f) return;

            Transform hip = puppetAnimator.GetBoneTransform(HumanBodyBones.Hips);
            Transform lf = puppetAnimator.GetBoneTransform(HumanBodyBones.LeftFoot);
            Transform rf = puppetAnimator.GetBoneTransform(HumanBodyBones.RightFoot);
            if (!hip || (!lf && !rf)) return;

            float footY = Mathf.Min(lf ? lf.position.y : float.MaxValue,
                                    rf ? rf.position.y : float.MaxValue);
            float target = Mathf.Clamp((hip.position.y - footY) + _soleOffset, 0.2f, 2f);
            controller.rideHeight = Mathf.MoveTowards(controller.rideHeight, target, 0.5f * Time.fixedDeltaTime);
        }

        void TickGrabbed() =>
            controller.Body.MovePosition(pelvis.position + Vector3.up * controller.rideHeight);

        void TickDown()
        {
            controller.Body.MovePosition(pelvis.position + Vector3.up * controller.rideHeight);

            float minDown = Current == State.Thrown ? thrownMinimumDownTime : minimumDownTime;
            if (_stateTime < minDown) return;

            float speed = pelvis.Vel().magnitude + chest.Vel().magnitude;
            if (speed > settleSpeed && _stateTime < maxDownTime) return;

            BeginGetUp();
        }

        void BeginGetUp()
        {
            SetState(State.GettingUp);

            Vector3 groundPoint = pelvis.position;
            if (Physics.Raycast(pelvis.position + Vector3.up * 1.5f, Vector3.down,
                                out RaycastHit hit, 4f, groundMask, QueryTriggerInteraction.Ignore))
                groundPoint = hit.point;

            controller.transform.position = groundPoint + Vector3.up * controller.rideHeight;

            Vector3 flat = Vector3.ProjectOnPlane(ChestForward, Vector3.up);
            if (flat.sqrMagnitude < 0.01f) flat = Vector3.ProjectOnPlane(pelvis.transform.forward, Vector3.up);
            if (flat.sqrMagnitude > 0.01f)
            {
                float yaw = Quaternion.LookRotation(flat.normalized).eulerAngles.y;
                controller.SetYaw(yaw);
                if (rig) rig.AddLook(Vector2.zero);
            }

            _getUpClipLength = 0f;
            if (puppetAnimator && puppetAnimator.runtimeAnimatorController)
            {
                puppetAnimator.enabled = true;
                bool faceUp = Vector3.Dot(ChestUp, Vector3.up) > 0f;
                string clip = faceUp ? getUpBackState : getUpFrontState;
                if (!string.IsNullOrEmpty(clip) && puppetAnimator.HasState(0, Animator.StringToHash(clip)))
                {
                    puppetAnimator.Play(clip, 0, 0f);
                    // Force the state to become current so its length is valid THIS frame, then keep it,
                    // so TickGettingUp holds the recovery until the clip actually finishes instead of
                    // cutting it off after a fixed getUpDuration (which was ~1/4 of a real get-up).
                    puppetAnimator.Update(0f);
                    _getUpClipLength = puppetAnimator.GetCurrentAnimatorStateInfo(0).length;
                }
            }
        }

        void TickGettingUp()
        {
            // Ramp strength/anchor over the ACTUAL get-up clip (so the physical rig follows the whole
            // lying->standing motion and stands as the clip ends), not a fixed 0.85s that cut it at ~1/4
            // and dumped it into Locomotion (and then Jump). Falls back to getUpDuration with no clip;
            // capped by maxDownTime so a bad clip can never hang the character on the floor.
            float recover = Mathf.Min(_getUpClipLength > 0.1f ? _getUpClipLength : getUpDuration, maxDownTime);
            float b = Mathf.Clamp01(_stateTime / recover);
            muscles.strength = Mathf.Lerp(limpStrength, 1f, b);
            anchor.weight = b;

            Vector3 want = pelvis.position + Vector3.up * controller.rideHeight;
            controller.Body.MovePosition(Vector3.Lerp(controller.Body.position, want, 1f - b));

            if (b < 1f) return;

            SetState(State.Standing);
            muscles.strength = 1f;
            anchor.weight = 1f;
            controller.SetActive(true);
            if (puppetAnimator && puppetAnimator.runtimeAnimatorController &&
                puppetAnimator.HasState(0, Animator.StringToHash(locomotionState)))
                puppetAnimator.Play(locomotionState, 0, 0f);
        }

        // ------------------------------------------------------------------ carrying

        void ApplyEncumbrance()
        {
            if (!controller) return;
            float f = Mathf.Lerp(1f, encumberedSpeedFactor,
                                 Mathf.Clamp01(CarriedMass / Mathf.Max(1f, encumbranceReferenceMass)));
            controller.maxSpeed = _baseMaxSpeed * f;
        }

        void SyncHandStrength()
        {
            if (leftHand) leftHand.SetStrength(muscles.strength);
            if (rightHand) rightHand.SetStrength(muscles.strength);
        }

        void SetState(State s, bool force = false)
        {
            if (Current == s && !force) return;
            Current = s;
            _stateTime = 0f;

            // Hard-lock the pelvis upright while standing: freeze its world pitch and roll
            // (yaw stays free to turn), and release it the instant it goes down. The anchored
            // body is an inverted pendulum -- its mass sits ABOVE the pelvis pivot, so any
            // lean self-accelerates and a spring torque alone loses to it, especially while
            // also dragging the legs. The character drapes to the floor. This mirrors the
            // floating capsule, which freezes its own X/Z rotation for exactly this reason:
            // standing is a guarantee, knockdown a deliberate release. The upright springs
            // still shape posture within the lock and take over the moment it is released.
            if (pelvis)
            {
                if (s == State.Standing)
                {
                    // Snap to true upright (minimal rotation, keeps facing) BEFORE locking, or
                    // the freeze faithfully preserves whatever tilt it had -- entering Standing
                    // mid-fall, or finishing a get-up at an angle -- and the whole body leans.
                    if (anchor)
                    {
                        Quaternion correction = Quaternion.FromToRotation(anchor.PelvisUp, Vector3.up);
                        pelvis.rotation = correction * pelvis.rotation;
                    }
                    pelvis.constraints = RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ;
                }
                else pelvis.constraints = RigidbodyConstraints.None;
            }

            StateChanged?.Invoke(s);
        }
    }
}
