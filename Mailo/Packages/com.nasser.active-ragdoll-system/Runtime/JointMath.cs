using UnityEngine;

namespace NasserActiveRagdoll
{
    /// <summary>
    /// Unity 6 renamed Rigidbody.velocity -> linearVelocity. This keeps the rest
    /// of the package compiling on both 2021/2022 LTS and Unity 6.
    /// </summary>
    public static class RbCompat
    {
        public static Vector3 Vel(this Rigidbody rb)
        {
#if UNITY_6000_0_OR_NEWER
            return rb.linearVelocity;
#else
            return rb.velocity;
#endif
        }

        public static void SetVel(this Rigidbody rb, Vector3 v)
        {
#if UNITY_6000_0_OR_NEWER
            rb.linearVelocity = v;
#else
            rb.velocity = v;
#endif
        }
    }

    /// <summary>
    /// ConfigurableJoint.targetRotation is expressed in the joint's own basis and is
    /// INVERTED relative to what you would expect. Getting this wrong is the single
    /// most common reason an active ragdoll flails or explodes, so it lives in one place.
    ///
    /// Note: if joint.axis == (1,0,0) and joint.secondaryAxis == (0,1,0) (Unity's default)
    /// the basis conversion collapses to identity and this reduces to
    ///     Inverse(targetLocalRotation) * startLocalRotation
    /// The general form below is kept because ragdoll wizards often rotate the axes.
    /// </summary>
    public static class ConfigurableJointExtensions
    {
        public static void SetTargetRotationLocal(this ConfigurableJoint joint,
                                                  Quaternion targetLocalRotation,
                                                  Quaternion startLocalRotation)
        {
            if (joint.configuredInWorldSpace)
            {
                Debug.LogError("SetTargetRotationLocal on a world-space joint.", joint);
                return;
            }

            Vector3 right   = joint.axis;
            Vector3 forward = Vector3.Cross(joint.axis, joint.secondaryAxis).normalized;
            Vector3 up      = Vector3.Cross(forward, right).normalized;

            Quaternion worldToJoint = Quaternion.LookRotation(forward, up);
            Quaternion result = Quaternion.Inverse(worldToJoint);
            result *= Quaternion.Inverse(targetLocalRotation) * startLocalRotation;
            result *= worldToJoint;

            joint.targetRotation = result;
        }
    }
}
