using System.Collections.Generic;
using UnityEngine;

namespace NasserActiveRagdoll
{
    /// <summary>
    /// Pairs every ConfigurableJoint on the PHYSICAL rig with the matching bone on the
    /// invisible ANIMATED PUPPET (matched by transform name), then every FixedUpdate
    /// pushes the puppet's pose into the joints as a target rotation.
    ///
    /// "Strength" is a single 0..1 float that scales all drive springs. That one value
    /// is your entire alive <-> limp axis: 1 = fully animated, 0 = dead ragdoll.
    /// </summary>
    [DefaultExecutionOrder(100)]
    public class ActiveRagdollMuscles : MonoBehaviour
    {
        [System.Serializable]
        public class MuscleGroup
        {
            [Tooltip("Any bone whose name contains one of these (case-insensitive) joins this group.")]
            public string[] nameContains;
            public float springMultiplier = 1f;
            public float damperMultiplier = 1f;
        }

        [Header("Rigs")]
        [Tooltip("Root of the physics bones (the one with colliders + ConfigurableJoints).")]
        public Transform physicalRoot;
        [Tooltip("Root of the animator-driven clone. No colliders, no rigidbodies, no renderers.")]
        public Transform puppetRoot;

        [Header("Base drive")]
        public float baseSpring = 2500f;
        public float baseDamper = 120f;
        public float maxForce = Mathf.Infinity;
        [Tooltip("Extra spring multiplier applied to the LEG bones on top of their group, because " +
                 "the legs alone carry the whole standing load near full extension and buckle into a " +
                 "squat at the group's baseline. Net leg spring = baseSpring * legGroupMult(1.2) * this. " +
                 "2.5 (=> ~7500 at baseSpring 2500) lets the character stand; push toward ~3.3 for a " +
                 "tighter hip, at the cost of some softness/jitter. Kept as a top-level field so it " +
                 "applies to already-built characters without touching the groups array or rebuilding.")]
        public float legStrengthMultiplier = 2.5f;

        [Header("Per-limb tuning")]
        public MuscleGroup[] groups = new MuscleGroup[]
        {
            new MuscleGroup { nameContains = new[]{"spine","chest","hips","pelvis"}, springMultiplier = 1.0f,  damperMultiplier = 1.0f },
            new MuscleGroup { nameContains = new[]{"neck","head"},                   springMultiplier = 0.6f,  damperMultiplier = 1.0f },
            new MuscleGroup { nameContains = new[]{"shoulder","clavicle"},           springMultiplier = 0.8f,  damperMultiplier = 1.0f },
            new MuscleGroup { nameContains = new[]{"arm","elbow"},                   springMultiplier = 0.35f, damperMultiplier = 1.0f },
            new MuscleGroup { nameContains = new[]{"hand"},                          springMultiplier = 0.15f, damperMultiplier = 1.0f },
            // NOTE: the legs need to be the STRONGEST group (they alone hold the body up near a
            // straight-leg stance, where the load torque is large). That boost is NOT baked into
            // this multiplier -- it is applied on top via `legStrengthMultiplier` below, so it can
            // reach an already-built character without editing this nested array. Net leg spring =
            // baseSpring * 1.2 * legStrengthMultiplier.
            new MuscleGroup { nameContains = new[]{"leg","thigh","calf","shin","knee"}, springMultiplier = 1.2f, damperMultiplier = 1.1f },
            new MuscleGroup { nameContains = new[]{"foot","toe","ankle"},            springMultiplier = 0.7f,  damperMultiplier = 1.0f },
        };

        [Header("Runtime")]
        [Range(0f, 1f)] public float strength = 1f;

        [Header("Debug")]
        [Tooltip("EDITOR TEST. Makes the LEG bones (thigh/shin/foot) kinematic and drives them " +
                 "straight from the animation with zero physics -- the leg becomes a rigid extension " +
                 "of the pelvis, posed exactly like the puppet. Use it to see the intended stance " +
                 "with the leg drive taken out of the equation: if the character stands cleanly with " +
                 "this ON, the whole problem is the leg PHYSICS drive; if it STILL looks wrong, the " +
                 "cause is upstream (pelvis / torso / capsule / the animation itself). Turn OFF for play.")]
        public bool debugKinematicLegs = false;

        class Binding
        {
            public ConfigurableJoint joint;
            public Rigidbody rb;
            public Transform puppetBone;
            public Quaternion startLocalRotation;
            public float spring;
            public float damper;
        }

        readonly List<Binding> _bindings = new List<Binding>();
        readonly Dictionary<string, Transform> _puppetLookup = new Dictionary<string, Transform>();
        float _appliedStrength = -1f;

        public IReadOnlyList<Rigidbody> Bones => _bones;
        readonly List<Rigidbody> _bones = new List<Rigidbody>();

        void Awake()
        {
            Bind();
        }

        public void Bind()
        {
            _bindings.Clear();
            _puppetLookup.Clear();
            _bones.Clear();

            foreach (Transform t in puppetRoot.GetComponentsInChildren<Transform>(true))
                _puppetLookup[t.name] = t;   // last one wins; keep bone names unique

            foreach (ConfigurableJoint joint in physicalRoot.GetComponentsInChildren<ConfigurableJoint>(true))
            {
                if (!_puppetLookup.TryGetValue(joint.name, out Transform puppetBone))
                {
                    Debug.LogWarning($"[Muscles] No puppet bone named '{joint.name}'.", joint);
                    continue;
                }

                joint.configuredInWorldSpace = false;
                joint.rotationDriveMode = RotationDriveMode.Slerp;
                joint.enableCollision = false;   // adjacent bones never self-collide

                float springMul = 1f, damperMul = 1f;
                string lower = joint.name.ToLowerInvariant();
                foreach (MuscleGroup g in groups)
                {
                    bool match = false;
                    foreach (string key in g.nameContains)
                        if (!string.IsNullOrEmpty(key) && lower.Contains(key.ToLowerInvariant())) { match = true; break; }
                    if (match) { springMul = g.springMultiplier; damperMul = g.damperMultiplier; break; }
                }

                // The legs bear the standing load and lose to it at the group baseline, folding the
                // character into a squat (hip+knee ~25° short, hips sagged). Boost them on top of the
                // group here. Applied by keyword, not the foot/toe group, so ankles stay soft.
                if (IsLeg(lower)) springMul *= legStrengthMultiplier;

                _bindings.Add(new Binding
                {
                    joint = joint,
                    rb = joint.GetComponent<Rigidbody>(),
                    puppetBone = puppetBone,
                    startLocalRotation = joint.transform.localRotation,
                    spring = baseSpring * springMul,
                    damper = baseDamper * damperMul
                });
            }

            foreach (Rigidbody rb in physicalRoot.GetComponentsInChildren<Rigidbody>(true))
            {
                rb.solverIterations = 20;
                rb.solverVelocityIterations = 8;
                rb.maxAngularVelocity = 40f;
                rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
                rb.interpolation = RigidbodyInterpolation.Interpolate;
                _bones.Add(rb);
            }

            _appliedStrength = -1f;
            ApplyDrives(strength);
        }

        void FixedUpdate()
        {
            if (!Mathf.Approximately(_appliedStrength, strength))
                ApplyDrives(strength);

            for (int i = 0; i < _bindings.Count; i++)
            {
                Binding b = _bindings[i];

                // Debug isolation: make the leg chain kinematic and pose it straight from the
                // animation. The bones are children of the pelvis, so copying the puppet's LOCAL
                // rotation keeps the leg attached and posed exactly like the clip, with no drive
                // sag. Restores dynamic the moment the flag goes off (isKinematic != kin).
                bool kin = debugKinematicLegs && IsLegChain(b.joint.name);
                if (b.rb && b.rb.isKinematic != kin) b.rb.isKinematic = kin;
                if (kin) b.joint.transform.localRotation = b.puppetBone.localRotation;

                // Always keep the joint target current: for a kinematic leg it makes the drive see
                // zero error, so the hip joint applies no reaction to the (dynamic) pelvis.
                b.joint.SetTargetRotationLocal(b.puppetBone.localRotation, b.startLocalRotation);
            }
        }

        void ApplyDrives(float s)
        {
            _appliedStrength = s;
            // Squared falloff feels better than linear: the character goes soft early
            // in a knockdown and only the last 20% of the ramp reads as "back on their feet".
            float k = s * s;

            for (int i = 0; i < _bindings.Count; i++)
            {
                Binding b = _bindings[i];
                b.joint.slerpDrive = new JointDrive
                {
                    positionSpring = b.spring * k,
                    positionDamper = b.damper * Mathf.Max(k, 0.05f), // keep some damping when limp, or limbs buzz
                    maximumForce = maxForce
                };
            }
        }

        /// <summary>
        /// A thigh or shin bone (upper/lower leg), matched by the same keywords as the leg group.
        /// Deliberately excludes foot/toe/ankle so the feet keep their own softer tuning.
        /// </summary>
        static bool IsLeg(string lowerName) =>
            lowerName.Contains("leg") || lowerName.Contains("thigh") ||
            lowerName.Contains("calf") || lowerName.Contains("shin") || lowerName.Contains("knee");

        /// <summary>
        /// The whole leg chain INCLUDING the foot/toe/ankle -- used by the kinematic-legs debug
        /// mode, which needs the foot rigid too so the lower body poses cleanly. (IsLeg excludes
        /// the foot on purpose, for the drive boost.)
        /// </summary>
        static bool IsLegChain(string boneName)
        {
            string n = boneName.ToLowerInvariant();
            return IsLeg(n) || n.Contains("foot") || n.Contains("toe") || n.Contains("ankle");
        }

        /// <summary>Total mass of the physical rig. Useful for scaling impact thresholds.</summary>
        public float TotalMass()
        {
            float m = 0f;
            foreach (Rigidbody rb in _bones) m += rb.mass;
            return m;
        }
    }
}
