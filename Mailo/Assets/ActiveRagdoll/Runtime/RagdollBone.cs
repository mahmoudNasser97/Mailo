using System;
using UnityEngine;

namespace ActiveRagdoll
{
    /// <summary>
    /// Every physical bone the system knows about. Order is stable; do not reorder
    /// (serialized data and the profile's per-part tables index against it).
    /// </summary>
    public enum BodyPart
    {
        Hips,
        Spine,
        Chest,
        Head,
        UpperArmL,
        UpperArmR,
        LowerArmL,
        LowerArmR,
        HandL,
        HandR,
        ThighL,
        ThighR,
        ShinL,
        ShinR,
        FootL,
        FootR
    }

    /// <summary>
    /// One physical bone plus its link back to the animated (target) rig.
    ///
    /// The animated rig is the target; the physical rig chases it. <see cref="target"/>
    /// is the matching bone on the animated rig, read every FixedUpdate from Phase 1 on.
    /// Nothing outside FixedUpdate should ever write <see cref="physical"/>.transform.
    /// </summary>
    [Serializable]
    public class RagdollBone
    {
        public BodyPart part;

        [Tooltip("Physical bone transform (has the Rigidbody + collider + joint).")]
        public Transform physical;

        [Tooltip("Matching bone on the invisible animated rig. This is what we chase.")]
        public Transform target;

        public Rigidbody body;

        [Tooltip("Null on the root (Hips) — the root has no joint, it is the free base.")]
        public ConfigurableJoint joint;

        [Tooltip("physical.localRotation captured at setup. Joint-space conversion in " +
                 "Phase 1 needs this reference pose; capturing it later would be wrong.")]
        public Quaternion startLocalRotation = Quaternion.identity;

        public bool IsRoot => part == BodyPart.Hips;
    }
}
