using UnityEngine;

namespace NasserActiveRagdoll
{
    /// <summary>
    /// One physical hand. There is no "hold slot", no parenting, no kinematic snapping.
    /// The hand is a rigidbody dragged toward a target by a spring, and a grab is a real
    /// joint between two real bodies.
    ///
    /// Consequences you get for free and would otherwise have to fake:
    ///   - heavy things pull your arm down and slow your turn
    ///   - a crate wedged in a doorway stops you walking
    ///   - two players can grab the same crate and fight over it
    ///   - the grab breaks when the load exceeds grabBreakForce
    /// </summary>
    public class PhysicsHand : MonoBehaviour
    {
        [Header("Bodies")]
        public Rigidbody handBody;
        [Tooltip("Empty transform parented under the camera. This is where the hand wants to be.")]
        public Transform reachTarget;
        [Tooltip("The character that owns this hand. Used for instigator credit and self-grab rejection.")]
        public CharacterBody owner;

        [Header("Reach")]
        public float reachSpring = 420f;
        public float reachDamper = 26f;
        public float maxReachForce = 1100f;
        public float rotationSpring = 45f;
        public float rotationDamper = 5f;
        [Tooltip("ON = the hand is ALWAYS dragged to its target (classic always-visible first-person " +
                 "hands). That constant forward pull on a soft arm also drags the torso into a lean, so " +
                 "the character never stands straight. OFF (default) = the hand only reaches while you " +
                 "are actively reaching (grab button held) or holding something; otherwise the arm hangs " +
                 "and follows the animation, and the character stands naturally.")]
        public bool alwaysReach = false;

        [Header("Grab")]
        public float grabRadius = 0.2f;
        public LayerMask grabbableMask = ~0;
        [Tooltip("Spring on the grab joint. Below infinity the held object lags and sags -- which is what sells weight.")]
        public float gripSpring = 30000f;
        public float gripDamper = 800f;

        [Header("Throw")]
        [Tooltip("Frames of hand velocity averaged for the throw. Instantaneous velocity is far too noisy.")]
        public int velocitySamples = 6;
        public float throwForceMultiplier = 1.4f;
        public float maxThrowSpeed = 16f;

        public Grabbable Held { get; private set; }
        public CharacterBody Owner => owner;
        public float HeldMass => Held && Held.body ? Held.body.mass * Held.encumbrance : 0f;

        ConfigurableJoint _joint;
        Vector3[] _velBuffer;
        int _velIndex;
        float _strength = 1f;
        bool _reaching;

        /// <summary>Driver sets this true while the player holds the grab button (or an NPC is
        /// carrying), so the arm raises to its target. False lets the arm hang and follow the
        /// animation. Held objects always keep the reach on regardless.</summary>
        public void SetReaching(bool r) => _reaching = r;
        public bool IsReaching => _reaching || Held != null || alwaysReach;

        void Awake()
        {
            _velBuffer = new Vector3[Mathf.Max(2, velocitySamples)];
        }

        /// <summary>Muscle strength gate. A ragdolled character cannot hold on.</summary>
        public void SetStrength(float s)
        {
            _strength = Mathf.Clamp01(s);
            if (Held != null && _strength < 0.12f) Release(throwIt: false);
        }

        void FixedUpdate()
        {
            RecordVelocity();
            DriveReach();

            // A joint that broke this frame leaves us holding nothing.
            if (Held != null && _joint == null) FinishRelease(Vector3.zero);
        }

        void RecordVelocity()
        {
            _velBuffer[_velIndex] = handBody.Vel();
            _velIndex = (_velIndex + 1) % _velBuffer.Length;
        }

        Vector3 AverageVelocity()
        {
            Vector3 sum = Vector3.zero;
            foreach (Vector3 v in _velBuffer) sum += v;
            return sum / _velBuffer.Length;
        }

        void DriveReach()
        {
            if (!reachTarget || !handBody) return;

            // Arm hangs and follows the animation unless we are actively reaching or holding.
            // This is what lets the character stand straight instead of being dragged forward by
            // a permanent reach. Held items keep the reach on so you don't drop what you carry.
            if (!IsReaching) return;

            Vector3 delta = reachTarget.position - handBody.position;
            Vector3 force = delta * reachSpring - handBody.Vel() * reachDamper;
            force = Vector3.ClampMagnitude(force, maxReachForce) * _strength;
            handBody.AddForce(force);

            Quaternion d = reachTarget.rotation * Quaternion.Inverse(handBody.rotation);
            d.ToAngleAxis(out float angle, out Vector3 axis);
            if (angle > 180f) angle -= 360f;
            if (float.IsNaN(axis.x)) return;

            Vector3 torque = axis.normalized * (angle * Mathf.Deg2Rad * rotationSpring)
                             - handBody.angularVelocity * rotationDamper;
            handBody.AddTorque(torque * _strength, ForceMode.Acceleration);
        }

        // ------------------------------------------------------------------ grab

        public bool TryGrab()
        {
            if (Held != null || _strength < 0.3f) return false;

            Collider[] hits = Physics.OverlapSphere(handBody.position, grabRadius,
                                                    grabbableMask, QueryTriggerInteraction.Ignore);
            Grabbable best = null;
            float bestDist = float.MaxValue;

            foreach (Collider c in hits)
            {
                Grabbable g = c.GetComponentInParent<Grabbable>();
                if (g == null || g.body == null || g.body.isKinematic) continue;
                if (owner && g.transform.IsChildOf(owner.transform)) continue;   // no self-grab
                if (g.HeldBy == this || g.SecondHand == this) continue;

                float d = (g.body.worldCenterOfMass - handBody.position).sqrMagnitude;
                if (d < bestDist) { bestDist = d; best = g; }
            }

            return best != null && Grab(best);
        }

        public bool Grab(Grabbable g)
        {
            if (Held != null || g == null || g.body == null) return false;

            bool asSecond = g.IsHeld;
            GripPoint grip = g.BestGrip(handBody.position, allowSecondary: asSecond);
            Vector3 worldAnchor = grip ? grip.transform.position : handBody.position;

            _joint = handBody.gameObject.AddComponent<ConfigurableJoint>();
            _joint.connectedBody = g.body;
            _joint.autoConfigureConnectedAnchor = false;
            _joint.anchor = handBody.transform.InverseTransformPoint(worldAnchor);
            _joint.connectedAnchor = g.body.transform.InverseTransformPoint(worldAnchor);

            _joint.xMotion = _joint.yMotion = _joint.zMotion = ConfigurableJointMotion.Limited;
            _joint.angularXMotion = _joint.angularYMotion = _joint.angularZMotion = ConfigurableJointMotion.Limited;
            _joint.linearLimit = new SoftJointLimit { limit = 0.02f };
            _joint.angularYLimit = _joint.angularZLimit = new SoftJointLimit { limit = 6f };
            _joint.lowAngularXLimit = new SoftJointLimit { limit = -6f };
            _joint.highAngularXLimit = new SoftJointLimit { limit = 6f };

            // A soft grip is a feature. Infinite spring makes a 40kg crate feel like a balloon.
            float loadFactor = Mathf.Clamp01(8f / Mathf.Max(1f, g.body.mass));
            JointDrive drive = new JointDrive
            {
                positionSpring = gripSpring * loadFactor,
                positionDamper = gripDamper * loadFactor,
                maximumForce = Mathf.Infinity
            };
            _joint.xDrive = _joint.yDrive = _joint.zDrive = drive;
            _joint.rotationDriveMode = RotationDriveMode.Slerp;
            _joint.slerpDrive = drive;

            _joint.breakForce = g.grabBreakForce;
            _joint.breakTorque = g.grabBreakTorque;
            _joint.enablePreprocessing = false;
            _joint.enableCollision = true;   // you can still be hit by what you carry

            Held = g;
            g.NotifyGrabbed(this);

            // Cancel any lingering projectile arm state so you can catch things safely.
            Projectile p = g.body.GetComponent<Projectile>();
            if (p) p.Disarm();

            return true;
        }

        public void Release(bool throwIt)
        {
            if (Held == null) return;

            Vector3 impulse = Vector3.zero;
            if (throwIt)
            {
                Vector3 v = AverageVelocity();
                v = Vector3.ClampMagnitude(v * throwForceMultiplier, maxThrowSpeed);
                impulse = v * Held.body.mass * Held.throwMultiplier;
            }
            FinishRelease(impulse);
        }

        void FinishRelease(Vector3 impulse)
        {
            Grabbable g = Held;
            Held = null;

            if (_joint) Destroy(_joint);
            _joint = null;
            if (g == null) return;

            if (impulse.sqrMagnitude > 0.01f)
            {
                g.body.AddForce(impulse, ForceMode.Impulse);
                g.ArmAsProjectile(owner ? owner.gameObject : null);
            }

            g.NotifyReleased(this, impulse);
        }

        void OnDrawGizmosSelected()
        {
            if (!handBody) return;
            Gizmos.color = Held ? Color.green : Color.yellow;
            Gizmos.DrawWireSphere(handBody.position, grabRadius);
        }
    }
}
