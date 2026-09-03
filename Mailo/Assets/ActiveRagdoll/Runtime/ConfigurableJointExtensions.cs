using UnityEngine;

namespace ActiveRagdoll
{
    public static class ConfigurableJointExtensions
    {
        /// <summary>
        /// Sets a ConfigurableJoint's target rotation from a desired LOCAL rotation.
        ///
        /// ConfigurableJoint.targetRotation is expressed in JOINT space, not local space,
        /// and joint space depends on the joint's axis / secondaryAxis. This is the exact
        /// conversion from the spec (§Phase 1). Do NOT "simplify" it: a subtly wrong version
        /// produces a rig that *almost* poses correctly and costs days of blaming the balance
        /// controller.
        ///
        /// <paramref name="startLocalRotation"/> is the joint transform's localRotation captured
        /// at setup (RagdollBone.startLocalRotation) — the reference pose the target is measured
        /// against.
        /// </summary>
        public static void SetTargetRotationLocal(this ConfigurableJoint joint,
                                                  Quaternion targetLocalRotation,
                                                  Quaternion startLocalRotation)
        {
            if (joint.configuredInWorldSpace)
                Debug.LogError("SetTargetRotationLocal requires joint to be configured in local space.");

            Vector3 right = joint.axis;
            Vector3 forward = Vector3.Cross(joint.axis, joint.secondaryAxis).normalized;
            Vector3 up = Vector3.Cross(forward, right).normalized;
            Quaternion worldToJointSpace = Quaternion.LookRotation(forward, up);

            Quaternion resultRotation = Quaternion.Inverse(worldToJointSpace)
                                      * Quaternion.Inverse(targetLocalRotation)
                                      * startLocalRotation
                                      * worldToJointSpace;

            joint.targetRotation = resultRotation;
        }
    }
}
