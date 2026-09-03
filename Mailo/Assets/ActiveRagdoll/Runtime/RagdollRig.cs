using System.Collections.Generic;
using UnityEngine;

namespace ActiveRagdoll
{
    /// <summary>
    /// Registry for one active-ragdoll character. Holds the bone list the setup wizard
    /// produced, exposes the mass / centre-of-mass / kinetic-energy queries the rest of
    /// the system reads, and disables the self-collisions that would otherwise make the
    /// rig vibrate as drives fall to zero (spec §7 "Limbs vibrate during collapse").
    ///
    /// Adjacent (joint-connected) bones already ignore each other because
    /// ConfigurableJoint.enableCollision is false. This component adds the pairs a joint
    /// graph can't express: grandparent bones, the two crossing legs, and the spec's
    /// explicit hand↔thigh / forearm↔chest pairs.
    /// </summary>
    public class RagdollRig : MonoBehaviour
    {
        [Tooltip("Shared tuning asset. Every phase reads its values from here.")]
        public RagdollProfile profile;

        [Tooltip("Invisible kinematic rig the physical rig chases (Phase 1+).")]
        public Transform animatedRoot;

        [Tooltip("Physical rig root (Rigidbodies + joints + colliders + skinned mesh).")]
        public Transform physicalRoot;

        [Tooltip("Every physical bone. Populated by the setup wizard.")]
        public List<RagdollBone> bones = new List<RagdollBone>();

        readonly Dictionary<BodyPart, RagdollBone> _byPart = new Dictionary<BodyPart, RagdollBone>();
        float _cachedTotalMass = -1f;

        public IReadOnlyList<RagdollBone> Bones => bones;

        void Awake()
        {
            RebuildLookup();
        }

        void Start()
        {
            ConfigureSelfCollisions();
        }

        /// <summary>Rebuilds the part→bone dictionary. Call after editing the bone list.</summary>
        public void RebuildLookup()
        {
            _byPart.Clear();
            _cachedTotalMass = -1f;
            foreach (var b in bones)
            {
                if (b == null || b.physical == null) continue;
                _byPart[b.part] = b;
            }
        }

        public bool TryGetBone(BodyPart part, out RagdollBone bone) => _byPart.TryGetValue(part, out bone);

        public RagdollBone Root => TryGetBone(BodyPart.Hips, out var b) ? b : null;

        // ---- Queries --------------------------------------------------------

        public float TotalMass
        {
            get
            {
                if (_cachedTotalMass > 0f) return _cachedTotalMass;
                float m = 0f;
                foreach (var b in bones)
                    if (b?.body != null) m += b.body.mass;
                _cachedTotalMass = m;
                return m;
            }
        }

        /// <summary>Mass-weighted world centre of mass over all bone rigidbodies.</summary>
        public Vector3 CenterOfMass
        {
            get
            {
                Vector3 sum = Vector3.zero;
                float total = 0f;
                foreach (var b in bones)
                {
                    if (b?.body == null) continue;
                    sum += b.body.worldCenterOfMass * b.body.mass;
                    total += b.body.mass;
                }
                return total > 0f ? sum / total : transform.position;
            }
        }

        /// <summary>Mass-weighted linear velocity of the whole body.</summary>
        public Vector3 CenterOfMassVelocity
        {
            get
            {
                Vector3 sum = Vector3.zero;
                float total = 0f;
                foreach (var b in bones)
                {
                    if (b?.body == null) continue;
                    sum += b.body.linearVelocity * b.body.mass;
                    total += b.body.mass;
                }
                return total > 0f ? sum / total : Vector3.zero;
            }
        }

        /// <summary>
        /// Approximate total kinetic energy (linear + a coarse angular term). Used by the
        /// Phase 5 settle test; exact rotational energy is not needed to detect "at rest".
        /// </summary>
        public float TotalKineticEnergy
        {
            get
            {
                float e = 0f;
                foreach (var b in bones)
                {
                    if (b?.body == null) continue;
                    var rb = b.body;
                    e += 0.5f * rb.mass * rb.linearVelocity.sqrMagnitude;
                    e += 0.5f * rb.inertiaTensor.magnitude * rb.angularVelocity.sqrMagnitude;
                }
                return e;
            }
        }

        // ---- Self-collision -------------------------------------------------

        void ConfigureSelfCollisions()
        {
            // Grandparent pairs + crossing legs: derived from the joint graph so this
            // works for any rig the wizard built.
            foreach (var b in bones)
            {
                if (b?.joint == null) continue;
                var parent = FindByBody(b.joint.connectedBody);
                if (parent?.joint == null) continue;
                var grandparent = FindByBody(parent.joint.connectedBody);
                if (grandparent != null) IgnorePair(b, grandparent);
            }

            IgnorePair(BodyPart.ThighL, BodyPart.ThighR); // crotch overlap

            // Spec §Phase 0 explicit pairs.
            IgnorePair(BodyPart.HandL, BodyPart.ThighL);
            IgnorePair(BodyPart.HandL, BodyPart.ThighR);
            IgnorePair(BodyPart.HandR, BodyPart.ThighL);
            IgnorePair(BodyPart.HandR, BodyPart.ThighR);
            IgnorePair(BodyPart.LowerArmL, BodyPart.Chest);
            IgnorePair(BodyPart.LowerArmR, BodyPart.Chest);
        }

        RagdollBone FindByBody(Rigidbody rb)
        {
            if (rb == null) return null;
            foreach (var b in bones)
                if (b?.body == rb) return b;
            return null;
        }

        void IgnorePair(BodyPart a, BodyPart b)
        {
            if (TryGetBone(a, out var ba) && TryGetBone(b, out var bb))
                IgnorePair(ba, bb);
        }

        static void IgnorePair(RagdollBone a, RagdollBone b)
        {
            if (a?.physical == null || b?.physical == null || a == b) return;
            var colA = a.physical.GetComponents<Collider>();
            var colB = b.physical.GetComponents<Collider>();
            foreach (var ca in colA)
                foreach (var cb in colB)
                    if (ca != null && cb != null)
                        Physics.IgnoreCollision(ca, cb, true);
        }
    }
}
