using UnityEngine;

namespace NasserActiveRagdoll
{
    /// <summary>
    /// One asset holding every clip a character needs. Assign it once, build twelve NPCs
    /// from it. Without this you re-drag fifteen clip fields per character and they
    /// silently diverge.
    ///
    /// Create via Assets > Create > Active Ragdoll > Locomotion Clip Set.
    ///
    /// Only Idle and the two get-ups are truly required. Everything else improves
    /// smoothness; missing clips are omitted from the blend trees rather than erroring.
    /// </summary>
    [CreateAssetMenu(menuName = "Nasser Active Ragdoll System/Locomotion Clip Set", fileName = "LocomotionClips")]
    public class LocomotionClipSet : ScriptableObject
    {
        [Header("Idle")]
        public AnimationClip idle;

        [Header("Walk — 8-way blend")]
        [Tooltip("Only forward is required. Adding the other three turns strafing from a slide into a step.")]
        public AnimationClip walkForward;
        public AnimationClip walkBackward;
        public AnimationClip walkLeft;
        public AnimationClip walkRight;

        [Header("Run — 8-way blend")]
        public AnimationClip runForward;
        public AnimationClip runBackward;
        public AnimationClip runLeft;
        public AnimationClip runRight;

        [Header("Airborne")]
        public AnimationClip jumpStart;
        public AnimationClip fallLoop;
        public AnimationClip land;

        [Header("Recovery — required")]
        [Tooltip("Played when the character is face down.")]
        public AnimationClip getUpProne;
        [Tooltip("Played when the character is face up.")]
        public AnimationClip getUpSupine;

        [Header("Upper body layer")]
        [Tooltip("Masked to the arms and chest. Blended in when the character is carrying something.")]
        public AnimationClip carryPose;
        [Tooltip("Optional. Reaching forward, blended in while grabbing.")]
        public AnimationClip reachPose;

        [Header("Blend thresholds")]
        [Tooltip("Read each clip's own root speed and use it as its blend threshold. This is what kills foot sliding — leave it on unless your clips have no root motion.")]
        public bool autoThresholdsFromClips = true;
        [Tooltip("Fallback walk speed if a clip has no measurable root motion.")]
        public float walkSpeed = 1.6f;
        [Tooltip("Fallback run speed if a clip has no measurable root motion.")]
        public float runSpeed = 4.5f;

        [Header("Smoothing")]
        [Tooltip("Damping on the Speed parameter, in seconds. Higher is smoother and laggier.")]
        public float speedDamping = 0.12f;
        [Tooltip("Damping on the MoveX/MoveY direction parameters.")]
        public float directionDamping = 0.08f;
        [Tooltip("Crossfade duration for locomotion/airborne transitions.")]
        public float transitionDuration = 0.18f;

        public bool HasWalkStrafe => walkLeft || walkRight || walkBackward;
        public bool HasRunStrafe => runLeft || runRight || runBackward;
        public bool HasAirborne => jumpStart || fallLoop || land;
        public bool HasUpperBody => carryPose || reachPose;

        /// <summary>Human-readable list of what is missing, for the wizard.</summary>
        public string Audit()
        {
            var sb = new System.Text.StringBuilder();
            if (!idle) sb.AppendLine("• Idle is missing — the blend tree has no rest pose.");
            if (!walkForward) sb.AppendLine("• Walk Forward is missing — the character will slide while moving.");
            if (!getUpProne || !getUpSupine) sb.AppendLine("• A get-up clip is missing — recovery will snap.");
            if (!runForward) sb.AppendLine("• Run Forward is missing — no second speed tier.");
            if (!HasWalkStrafe) sb.AppendLine("• No strafe clips — sideways movement will look like forward walking.");
            return sb.Length == 0 ? null : sb.ToString().TrimEnd();
        }
    }
}
