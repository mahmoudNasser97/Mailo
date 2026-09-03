using UnityEngine;

namespace NasserActiveRagdoll
{
    /// <summary>
    /// Every tuning number for a character, in one asset.
    ///
    /// Without this you end up hand-editing eight components per character and your
    /// twelve NPCs drift apart until nobody can remember which one is "correct".
    /// Make one profile per archetype (Crew, Passenger, Heavy) and share it.
    ///
    /// Create via Assets > Create > Active Ragdoll > Profile.
    /// </summary>
    [CreateAssetMenu(menuName = "Nasser Active Ragdoll System/Profile", fileName = "RagdollProfile")]
    public class RagdollProfile : ScriptableObject
    {
        [Header("Body")]
        [Tooltip("Total mass of the physical rig in kg. Distributed across bones by the wizard.")]
        public float totalMass = 66f;
        [Tooltip("Controller capsule mass. Keep it heavy enough that a flailing ragdoll cannot drag it around.")]
        public float controllerMass = 40f;

        [Header("Muscles")]
        public float baseSpring = 2500f;
        public float baseDamper = 120f;
        [Range(0.05f, 1f)] public float armStrength = 0.35f;
        [Range(0.05f, 2f)] public float legStrength = 1.2f;

        [Header("Locomotion")]
        public float rideHeight = 0.9f;
        public float rideSpring = 4000f;
        public float rideDamper = 250f;
        public float maxSpeed = 4.5f;
        public float acceleration = 120f;
        public float maxAccelForce = 1800f;
        public float jumpVelocity = 8f;

        [Header("Balance")]
        public float pelvisSpring = 900f;
        public float pelvisDamper = 60f;
        public float uprightSpring = 900f;
        public float uprightDamper = 90f;
        public float leanIntoMotion = 0.12f;

        [Header("Knockdown")]
        [Tooltip("Newton-seconds. Scaled automatically if totalMass differs from 66.")]
        public float knockdownImpulse = 12f;
        public float weakSpotMultiplier = 2.5f;
        public float accumulationWindow = 0.5f;
        [Range(0f, 0.4f)] public float limpStrength = 0.06f;
        [Range(0f, 0.5f)] public float grabbedStrength = 0.18f;
        public float minimumDownTime = 1f;
        public float thrownMinimumDownTime = 1.6f;
        public float getUpDuration = 0.85f;

        [Header("Hands")]
        public float reachSpring = 420f;
        public float reachDamper = 26f;
        public float maxReachForce = 1100f;
        public float gripSpring = 30000f;
        public float throwForceMultiplier = 1.4f;
        public float maxThrowSpeed = 16f;
        public float grabRadius = 0.2f;

        [Header("Carrying")]
        [Range(0.05f, 1f)] public float encumberedSpeedFactor = 0.45f;
        public float encumbranceReferenceMass = 40f;

        [Header("Camera (players only)")]
        public float lookSensitivity = 2f;
        [Range(0f, 1f)] public float standingTiltFollow = 0.15f;
        [Range(0f, 1f)] public float ragdolledTiltFollow = 0.9f;

        /// <summary>Knockdown threshold scaled for this rig's actual mass.</summary>
        public float ScaledKnockdown => knockdownImpulse * (totalMass / 66f);
    }
}
