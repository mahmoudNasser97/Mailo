using UnityEngine;

namespace NasserActiveRagdoll
{
    /// <summary>
    /// The locomotion body. A single Rigidbody capsule that HOVERS above the ground on a
    /// raycast spring instead of resting on it. This is the trick that makes a fully
    /// dynamic character handle stairs, slopes and moving platforms without a
    /// CharacterController, and it costs nothing but a raycast.
    ///
    /// It never falls over, so the game is never unplayable. The ragdoll is a passenger
    /// bolted to it by PelvisAnchor; CharacterBody cuts that bolt on impact.
    ///
    /// Because the ride spring pushes back on whatever it stands on, standing on a rolling
    /// service cart or a tilting cabin floor works for free.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public class FloatingCapsuleController : MonoBehaviour
    {
        [Header("Ride spring")]
        public float rideHeight = 0.9f;
        public float rayLength = 1.4f;
        public float rideSpring = 4000f;
        public float rideDamper = 250f;
        public LayerMask groundMask = ~0;

        [Header("Movement")]
        public float maxSpeed = 4.5f;
        public float acceleration = 120f;
        public float maxAccelForce = 1800f;
        [Tooltip("X = dot(desiredDirection, currentVelocityDirection), Y = acceleration multiplier. Low values on the left give snappy direction changes.")]
        public AnimationCurve accelerationFactorFromDot =
            AnimationCurve.Linear(-1f, 2f, 1f, 1f);
        public float airControl = 0.25f;

        [Header("Jump")]
        public float jumpVelocity = 8f;
        public float coyoteTime = 0.12f;

        [Header("Turning")]
        [Tooltip("Yaw is driven directly; the capsule's rotation is frozen on X/Z by the Rigidbody constraints.")]
        public float yawLerp = 25f;

        [Header("Collision knockdown")]
        [Tooltip("Ramming into an obstacle at or above this relative speed (m/s) knocks the character " +
                 "down (and it then gets up). Only HORIZONTAL hits count, so landing a jump does not " +
                 "topple you. Set a bit below maxSpeed to topple on a full-speed run into a box; raise " +
                 "it so only genuinely hard impacts do; 0 disables.")]
        public float knockdownSpeed = 2.5f;
        CharacterBody _body;

        public bool IsGrounded { get; private set; }
        public Rigidbody Body { get; private set; }
        public Vector3 GroundVelocity { get; private set; }

        Vector3 _moveInput;      // world-space, magnitude 0..1
        float _targetYaw;
        float _lastGroundedTime;
        bool _jumpQueued;
        Vector3 _goalVel;

        void Awake()
        {
            Body = GetComponent<Rigidbody>();
            Body.constraints = RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ;
            Body.interpolation = RigidbodyInterpolation.Interpolate;
            Body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            Body.useGravity = true;
            _targetYaw = transform.eulerAngles.y;
        }

        /// <summary>Call from your input layer. dir is world-space and should be clamped to length 1.</summary>
        public void SetMoveInput(Vector3 worldDir) => _moveInput = Vector3.ClampMagnitude(worldDir, 1f);
        public void SetYaw(float yawDegrees) => _targetYaw = yawDegrees;
        public void QueueJump() => _jumpQueued = true;

        void FixedUpdate()
        {
            RaycastHit hit;
            IsGrounded = Physics.Raycast(transform.position, Vector3.down, out hit,
                                         rayLength, groundMask, QueryTriggerInteraction.Ignore);

            if (IsGrounded)
            {
                _lastGroundedTime = Time.time;
                ApplyRideSpring(hit);
                GroundVelocity = hit.rigidbody ? hit.rigidbody.GetPointVelocity(hit.point) : Vector3.zero;
            }
            else
            {
                GroundVelocity = Vector3.zero;
            }

            ApplyMovement();
            ApplyYaw();
            ApplyJump();
        }

        void ApplyRideSpring(RaycastHit hit)
        {
            Vector3 vel = Body.Vel();
            Vector3 otherVel = hit.rigidbody ? hit.rigidbody.Vel() : Vector3.zero;

            float relVel = Vector3.Dot(Vector3.down, vel) - Vector3.Dot(Vector3.down, otherVel);
            float x = hit.distance - rideHeight;
            float springForce = (x * rideSpring) - (relVel * rideDamper);

            Body.AddForce(Vector3.down * springForce);

            // Gravity compensation. Without it a Force-mode spring settles where its restoring
            // force balances the capsule's own weight -- i.e. mg/k BELOW rideHeight (~0.1 m for a
            // 40 kg capsule at spring 4000). That sag drops the whole character into a permanent
            // crouch: the pelvis (anchored to this capsule) sits low and the legs bend just to keep
            // the feet on the floor. Cancelling the weight here makes the spring hold rideHeight
            // EXACTLY, so the capsule truly hovers and the legs can stand straight. Grounded-only,
            // so a fall still falls.
            Body.AddForce(Vector3.up * (Body.mass * -Physics.gravity.y));

            // Newton's third law: push the platform we are standing on (the spring part only; the
            // gravity-compensation term is reaction-less against the world, not the platform).
            if (hit.rigidbody)
                hit.rigidbody.AddForceAtPosition(Vector3.down * -springForce, hit.point);
        }

        void ApplyMovement()
        {
            Vector3 vel = Body.Vel();
            Vector3 unitVel = _goalVel.normalized;
            float velDot = Vector3.Dot(_moveInput, unitVel);
            float accel = acceleration * accelerationFactorFromDot.Evaluate(velDot);
            if (!IsGrounded) accel *= airControl;

            Vector3 desiredVel = _moveInput * maxSpeed + GroundVelocity;
            _goalVel = Vector3.MoveTowards(_goalVel, desiredVel, accel * Time.fixedDeltaTime);

            Vector3 neededAccel = (_goalVel - vel) / Time.fixedDeltaTime;
            neededAccel.y = 0f;
            neededAccel = Vector3.ClampMagnitude(neededAccel, maxAccelForce);

            Body.AddForce(neededAccel * Body.mass);
        }

        void ApplyYaw()
        {
            Quaternion target = Quaternion.Euler(0f, _targetYaw, 0f);
            Body.MoveRotation(Quaternion.Slerp(Body.rotation, target, yawLerp * Time.fixedDeltaTime));
        }

        void ApplyJump()
        {
            if (!_jumpQueued) return;
            _jumpQueued = false;
            if (Time.time - _lastGroundedTime > coyoteTime) return;

            Vector3 v = Body.Vel();
            v.y = jumpVelocity;
            Body.SetVel(v);
            _lastGroundedTime = -999f;
        }

        // Ram-into-a-wall knockdown. The capsule is what hits obstacles (it surrounds the torso),
        // so its collision is the cleanest place to catch it. Only horizontal hits count -- landing
        // a jump is a vertical hit and must not topple you. The Knockdown() call self-guards: it does
        // nothing if the character is already down, or (harmlessly) while the capsule is kinematic.
        void OnCollisionEnter(Collision c)
        {
            if (knockdownSpeed <= 0f || c.contactCount == 0) return;
            if (c.relativeVelocity.magnitude < knockdownSpeed) return;
            if (Mathf.Abs(c.GetContact(0).normal.y) > 0.5f) return;   // floor/ceiling, not a wall

            if (!_body) _body = GetComponentInParent<CharacterBody>();
            if (_body) _body.Knockdown();
        }

        /// <summary>Called by CharacterBody while the character is down.</summary>
        public void SetActive(bool active)
        {
            enabled = active;
            Body.isKinematic = !active;
            Body.detectCollisions = active;
            if (active) { _goalVel = Vector3.zero; _moveInput = Vector3.zero; }
        }

        void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.cyan;
            Gizmos.DrawLine(transform.position, transform.position + Vector3.down * rayLength);
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(transform.position + Vector3.down * rideHeight, 0.06f);
        }
    }
}
