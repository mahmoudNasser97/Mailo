using UnityEngine;

public static class ConfigurableJointExtensions
{
    // Converts a desired local-space rotation into joint-space and writes joint.targetRotation.
    // startLocalRotation must be the transform's localRotation captured at the moment the joint was created.
    public static void SetTargetRotationLocal(this ConfigurableJoint joint,
                                              Quaternion targetLocalRotation,
                                              Quaternion startLocalRotation)
    {
        var right   = joint.axis;
        var forward = Vector3.Cross(joint.axis, joint.secondaryAxis).normalized;
        var up      = Vector3.Cross(forward, right).normalized;
        var toJoint = Quaternion.LookRotation(forward, up);

        // Convert the delta-rotation from start→target into joint space.
        joint.targetRotation = Quaternion.Inverse(toJoint)
                             * Quaternion.Inverse(targetLocalRotation)
                             * startLocalRotation
                             * toJoint;
    }
}
