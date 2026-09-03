using UnityEngine;

namespace ActiveRagdoll
{
    /// <summary>
    /// All tuning for the active ragdoll. Spec rule #5: every tuning value is a
    /// serialised field here, never a constant in code. The F1 runtime panel edits
    /// this asset live.
    ///
    /// Phase 0 only reads the drive springs/dampers indirectly (the setup wizard leaves
    /// all joint drives at zero for the passive-collapse test). Everything else is
    /// consumed by later phases and is here now only so no phase has to add fields to
    /// a shared asset later.
    /// </summary>
    [CreateAssetMenu(fileName = "RagdollProfile", menuName = "Active Ragdoll/Ragdoll Profile")]
    public class RagdollProfile : ScriptableObject
    {
        // ---- [Drives] -------------------------------------------------------
        // Non-uniform per-group stiffness. A single global gain is the #1 cause of a
        // rig that looks like a mannequin on strings (spec §Phase 1 / §7). Defaults
        // mirror the Phase 1 table.
        [Header("Drives")]
        public float springHips = 2000f;   // Hips + Chest group
        public float springSpine = 1200f;  // Spine + Neck/Head group
        public float springLegs = 3000f;   // Thighs + Shins
        public float springFeet = 1500f;
        public float springArms = 500f;     // Upper/lower arm + hand
        [Tooltip("Damper = spring * damperScale. The Phase 1 table dampers sit around 0.07-0.10 of spring.")]
        public float damperScale = 0.08f;
        [Tooltip("JointDrive.maximumForce cap. Effectively unlimited by default.")]
        public float maxForce = 100000f;

        // ---- [Balance] (Phase 2) -------------------------------------------
        [Header("Balance (Phase 2)")]
        public float hipKp = 400f;
        public float hipKd = 40f;
        public float ankleKp = 100f;
        [Range(0f, 1f)] public float gravityCompensation = 0.5f;
        [Range(0f, 1f)] public float comVelocitySmoothing = 0.2f;

        // ---- [Stepping] (Phase 3) ------------------------------------------
        [Header("Stepping (Phase 3)")]
        public float stepDuration = 0.3f;
        public float stepHeight = 0.15f;
        public float maxStanceOffset = 0.25f;
        public float minStanceTime = 0.15f;
        public float maxStanceTime = 0.6f;
        public float stepOutwardBias = 0.08f;
        public float maxStepDistance = 0.6f;

        // ---- [Impact] (Phase 4) --------------------------------------------
        [Header("Impact (Phase 4)")]
        public float ignoreThreshold = 1.0f;   // brushing past a crate
        public float maxImpulse = 40f;          // anti-tunnelling clamp
        public float poiseCapacity = 30f;
        public float spinWeight = 0.5f;
        public float regenRate = 0.5f;
        public float impactCooldown = 0.1f;

        // partMultipliers[] (spec §4c), grouped to keep the F1 panel legible.
        [Header("Impact — part multipliers (Phase 4)")]
        public float multHead = 2.0f;
        public float multChest = 1.0f;
        public float multSpine = 1.0f;
        public float multHips = 1.2f;
        public float multLegs = 1.4f;   // thigh + shin — legs matter for takedowns
        public float multFoot = 1.0f;
        public float multArms = 0.4f;   // upper/lower arm + hand

        // ---- [Tiers] (Phase 4) ---------------------------------------------
        [Header("Tiers (Phase 4)")]
        [Range(0f, 1f)] public float flinchThreshold = 0.9f;
        [Range(0f, 1f)] public float staggerThreshold = 0.5f;
        [Range(0f, 1f)] public float knockdownThreshold = 0.15f;
        public float driveRampDuration = 0.15f;
        public float legRampDelay = 0f;
        public float armRampDelay = 0.08f;

        // ---- [Recovery] (Phase 5) ------------------------------------------
        [Header("Recovery (Phase 5)")]
        public float restEnergyThreshold = 3.0f;
        public float restDuration = 0.4f;
        public float getUpRampDuration = 0.9f;

        // ---- [Grab] (Phase 5) ----------------------------------------------
        [Header("Grab (Phase 5)")]
        public float grabBreakForce = 2000f;
        public float grabBreakTorque = 2000f;
        public float grabRadius = 0.15f;

        // -------------------------------------------------------------------
        // Lookups used by later phases. Grouping here keeps the drive table in
        // one place instead of scattered switch statements.
        // -------------------------------------------------------------------

        /// <summary>Slerp-drive positionSpring for a part, before the poise multiplier.</summary>
        public float SpringFor(BodyPart part)
        {
            switch (part)
            {
                case BodyPart.Hips:
                case BodyPart.Chest:
                    return springHips;
                case BodyPart.Spine:
                case BodyPart.Head:
                    return springSpine;
                case BodyPart.ThighL:
                case BodyPart.ThighR:
                case BodyPart.ShinL:
                case BodyPart.ShinR:
                    return springLegs;
                case BodyPart.FootL:
                case BodyPart.FootR:
                    return springFeet;
                default:
                    return springArms; // arms + hands
            }
        }

        /// <summary>Slerp-drive positionDamper for a part.</summary>
        public float DamperFor(BodyPart part) => SpringFor(part) * damperScale;

        /// <summary>Poise drain multiplier for a struck part (spec §4c).</summary>
        public float PartMultiplier(BodyPart part)
        {
            switch (part)
            {
                case BodyPart.Head: return multHead;
                case BodyPart.Chest: return multChest;
                case BodyPart.Spine: return multSpine;
                case BodyPart.Hips: return multHips;
                case BodyPart.ThighL:
                case BodyPart.ThighR:
                case BodyPart.ShinL:
                case BodyPart.ShinR:
                    return multLegs;
                case BodyPart.FootL:
                case BodyPart.FootR:
                    return multFoot;
                default:
                    return multArms; // arms + hands
            }
        }
    }
}
