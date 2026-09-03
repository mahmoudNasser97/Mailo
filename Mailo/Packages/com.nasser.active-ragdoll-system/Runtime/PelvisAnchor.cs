using UnityEngine;

namespace NasserActiveRagdoll
{
    /// <summary>
    /// Binds the ragdoll pelvis to the floating capsule with a critically-damped spring,
    /// and torques the chest upright. Force-based rather than a ConfigurableJoint on
    /// purpose: a joint cannot be faded smoothly, and fading is the whole point --
    /// weight 1 = standing, weight 0 = free ragdoll.
    /// </summary>
    [DefaultExecutionOrder(90)]
    public class PelvisAnchor : MonoBehaviour
    {
        [Header("References")]
        public FloatingCapsuleController controller;
        public Rigidbody pelvis;
        public Rigidbody chest;

        [Header("Position spring")]
        public Vector3 localOffset = new Vector3(0f, -0.05f, 0f);
        public float positionSpring = 900f;
        public float positionDamper = 60f;
        public float maxForce = 6000f;

        [Header("Upright torque")]
        public float uprightSpring = 900f;
        public float uprightDamper = 90f;
        [Tooltip("Extra lean into the movement direction. Sells acceleration without any animation.")]
        public float leanIntoMotion = 0.12f;

        [Header("Yaw follow")]
        public float yawSpring = 300f;
        public float yawDamper = 30f;

        [Range(0f, 1f)] public float weight = 1f;

        // Which LOCAL axis of these bones pointed at world up in the authored pose.
        // Never assume this is Vector3.up: bone orientation is whatever the rigger
        // exported, and getting it wrong makes the character think it is permanently
        // face-down and collapse on frame one.
        Vector3 _chestLocalUp = Vector3.up;
        Vector3 _pelvisLocalUp = Vector3.up;

        void Awake()
        {
            if (chest)  _chestLocalUp  = chest.transform.InverseTransformDirection(Vector3.up);
            if (pelvis) _pelvisLocalUp = pelvis.transform.InverseTransformDirection(Vector3.up);
        }

        /// <summary>The chest's current world up, using the rig's real axis.</summary>
        public Vector3 ChestUp => chest ? chest.transform.TransformDirection(_chestLocalUp) : Vector3.up;

        /// <summary>The pelvis's current world up, using the rig's real axis.</summary>
        public Vector3 PelvisUp => pelvis ? pelvis.transform.TransformDirection(_pelvisLocalUp) : Vector3.up;

        void FixedUpdate()
        {
            if (weight <= 0.001f) return;
            float w = weight * weight;

            ApplyPositionSpring(w);
            ApplyUpright(w);
            ApplyYaw(w);
        }

        void ApplyPositionSpring(float w)
        {
            Vector3 target = controller.transform.TransformPoint(localOffset);
            Vector3 delta = target - pelvis.position;
            Vector3 relVel = pelvis.Vel() - controller.Body.Vel();

            Vector3 force = delta * positionSpring - relVel * positionDamper;
            force = Vector3.ClampMagnitude(force, maxForce) * w;

            // ForceMode.Acceleration, not the default Force. The pelvis carries the whole
            // hanging body (~totalMass), not just its own ~10 kg, so a mass-dependent Force
            // sags by roughly totalMass/pelvisMass -- about 6x too far -- and the hips drop
            // to the floor. Acceleration holds the same regardless of how much rig hangs off
            // it. The defaults (spring 900, damper 60) are already critically damped in these
            // units, 2*sqrt(900) = 60, which is the tell that this was always the intent.
            pelvis.AddForce(force, ForceMode.Acceleration);
        }

        void ApplyUpright(float w)
        {
            Vector3 up = Vector3.up;

            // Lean the target up-vector against horizontal acceleration.
            Vector3 horizVel = controller.Body.Vel();
            horizVel.y = 0f;
            up = (up - horizVel * leanIntoMotion).normalized;

            // Right the PELVIS first. It is the root of the physical rig and the only bone
            // the capsule holds by position; nothing else controls its pitch and roll. If it
            // tips there is no torque to bring it back, and the spine cannot stand in for it
            // -- the spine->hips joint is limited to ~20 degrees, so a face-down pelvis pins
            // the whole body down however hard we right the chest. Righting the root is what
            // keeps the character on its feet; the chest torque on top is only posture.
            if (pelvis) UprightBody(pelvis, PelvisUp, up, w);
            if (chest)  UprightBody(chest,  ChestUp,  up, w);
        }

        /// <summary>PD torque that rotates one body's sampled up-axis toward a target up.</summary>
        void UprightBody(Rigidbody body, Vector3 currentUp, Vector3 targetUp, float w)
        {
            Quaternion delta = Quaternion.FromToRotation(currentUp, targetUp);
            delta.ToAngleAxis(out float angle, out Vector3 axis);
            if (angle > 180f) angle -= 360f;
            if (float.IsNaN(axis.x)) return;

            Vector3 torque = axis.normalized * (angle * Mathf.Deg2Rad * uprightSpring)
                             - body.angularVelocity * uprightDamper;

            body.AddTorque(torque * w, ForceMode.Acceleration);
        }

        void ApplyYaw(float w)
        {
            float current = pelvis.transform.eulerAngles.y;
            float target = controller.transform.eulerAngles.y;
            float diff = Mathf.DeltaAngle(current, target) * Mathf.Deg2Rad;

            Vector3 torque = Vector3.up * (diff * yawSpring)
                             - Vector3.up * (pelvis.angularVelocity.y * yawDamper);

            pelvis.AddTorque(torque * w, ForceMode.Acceleration);
        }
    }
}
