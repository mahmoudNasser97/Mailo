#if UNITY_EDITOR
using System.Text;
using UnityEditor;
using UnityEngine;

namespace NasserActiveRagdoll.EditorTools
{
    /// <summary>
    /// Tools > Nasser Active Ragdoll System > Diagnose Selected Character.
    ///
    /// Select a built character and run this. It checks the things that fail silently
    /// -- wrong bone axes, missing components, capsule collision, unbound muscles --
    /// and prints a report instead of leaving you to infer the cause from a body on
    /// the floor. Works in edit mode and in Play mode; Play mode reports more.
    /// </summary>
    public static class CharacterDiagnostics
    {
        [MenuItem("Tools/Nasser Active Ragdoll System/Diagnose Selected Character")]
        static void Diagnose()
        {
            GameObject go = Selection.activeGameObject;
            if (!go) { Debug.LogWarning("[Nasser ARS] Select a built character first."); return; }

            CharacterBody body = go.GetComponentInParent<CharacterBody>();
            if (!body)
            {
                Debug.LogError("[Nasser ARS] No CharacterBody found. Select the '_Character' root.", go);
                return;
            }

            StringBuilder sb = new StringBuilder();
            int problems = 0;
            sb.AppendLine($"[Nasser ARS] Diagnostics for '{body.name}' ({body.role})");
            sb.AppendLine();

            // ---- components -------------------------------------------------
            problems += Check(sb, body.muscles, "ActiveRagdollMuscles");
            problems += Check(sb, body.anchor, "PelvisAnchor");
            problems += Check(sb, body.controller, "FloatingCapsuleController");
            problems += Check(sb, body.pelvis, "pelvis Rigidbody");
            problems += Check(sb, body.chest, "chest Rigidbody");
            problems += Check(sb, body.puppetAnimator, "puppet Animator");

            if (body.role == CharacterRole.Player)
            {
                problems += Check(sb, body.rig, "FirstPersonRig");
                if (!go.GetComponent<PlayerDriver>())
                { sb.AppendLine("  PROBLEM  PlayerDriver missing — nothing will read input."); problems++; }
            }

            // ---- missing scripts --------------------------------------------
            foreach (Component c in go.GetComponents<Component>())
            {
                if (c != null) continue;
                sb.AppendLine("  PROBLEM  A component on the root is a MISSING SCRIPT. " +
                              "Rebuild the character; stale components cannot be repaired.");
                problems++;
            }

            // ---- muscle binding ---------------------------------------------
            if (body.muscles)
            {
                int joints = body.muscles.physicalRoot
                    ? body.muscles.physicalRoot.GetComponentsInChildren<ConfigurableJoint>(true).Length : 0;
                int puppetBones = body.muscles.puppetRoot
                    ? body.muscles.puppetRoot.GetComponentsInChildren<Transform>(true).Length : 0;

                sb.AppendLine($"  info     {joints} ConfigurableJoints, {puppetBones} puppet transforms.");
                if (joints == 0)
                { sb.AppendLine("  PROBLEM  No joints on the physical rig."); problems++; }
                if (puppetBones == 0)
                { sb.AppendLine("  PROBLEM  Puppet root is empty or unassigned."); problems++; }

                if (Application.isPlaying)
                    sb.AppendLine($"  info     muscle strength = {body.muscles.strength:0.00} " +
                                  $"(1 = animated, ~0.06 = limp), anchor weight = {body.anchor.weight:0.00}");
            }

            // ---- bone axes ---------------------------------------------------
            if (body.chest)
            {
                float rawDot = Vector3.Dot(body.chest.transform.up, Vector3.up);
                sb.AppendLine($"  info     chest bone raw local +Y vs world up = {rawDot:0.00}");
                if (rawDot < 0.7f)
                    sb.AppendLine("  info     This rig's chest +Y is NOT world up. Handled since 0.5.2 " +
                                  "by sampling the axis at Awake — on older builds it caused instant collapse.");
                // A tilt threshold this high knocks down a character that is standing
                // perfectly upright: the test is chestUp.up < tiltFailDot, and chestUp.up
                // maxes out at 1. Works in edit mode -- it is pure config. This exact typo
                // (1 instead of the -1 that disables the test) cost a whole debugging session.
                if (body.tiltFailDot >= 0.9f)
                {
                    sb.AppendLine($"  PROBLEM  tiltFailDot = {body.tiltFailDot} knocks down even a " +
                                  "vertical character (chestUp·up maxes at 1). Sane range is ~0.2-0.5; " +
                                  "use a negative value to disable the tilt test entirely.");
                    problems++;
                }
                if (Application.isPlaying)
                {
                    float dot = Vector3.Dot(body.ChestUp, Vector3.up);
                    sb.AppendLine($"  info     corrected chest up vs world up = {dot:0.00} " +
                                  $"(knockdown below {body.tiltFailDot})");
                    // A character in the Standing state that is already past the fall angle
                    // is a contradiction -- it means it cannot hold itself up. (A genuine
                    // knockdown flips the state to Ragdolled, so this is not that.)
                    if (body.Current == CharacterBody.State.Standing && dot < body.tiltFailDot)
                    {
                        sb.AppendLine("  PROBLEM  Nominally Standing but the chest is past the knockdown " +
                                      "angle -- the character cannot support itself. Check the standing hip " +
                                      "height reported below (pelvis anchor geometry), not the muscle tuning.");
                        problems++;
                    }
                }
            }

            // ---- capsule isolation -------------------------------------------
            if (body.controller && body.muscles != null && body.muscles.physicalRoot)
            {
                Collider cap = body.controller.GetComponentInChildren<Collider>();
                Collider bone = body.pelvis ? body.pelvis.GetComponent<Collider>() : null;
                if (cap && bone)
                {
                    bool overlapping = cap.bounds.Intersects(bone.bounds);
                    sb.AppendLine(overlapping
                        ? "  info     Capsule overlaps the pelvis (normal). Per-pair collision exclusion " +
                          "is applied in CharacterBody.Start — if the character explodes on Play, that is where to look."
                        : "  info     Capsule does not overlap the pelvis.");
                }
                sb.AppendLine($"  info     rideHeight = {body.controller.rideHeight}, " +
                              $"anchor localOffset = {body.anchor.localOffset}");

                // Where the pelvis anchor will hold the hips, relative to the surface the
                // capsule hovers rideHeight above. Must clear the floor -- if it is at or
                // below zero, the anchor drags the hips into the ground and the character
                // collapses on frame one no matter how the muscles are tuned. This is the
                // fingerprint of the build-time ground ray hitting the body (localOffset.y
                // near -rideHeight); it is deterministic, so we can catch it without Play.
                float standingHip = body.controller.rideHeight + body.anchor.localOffset.y;
                sb.AppendLine($"  info     standing hip height above the capsule's ground = {standingHip:0.00} m.");

                // Do the feet actually reach the ground? A "banana" pose -- torso upright but
                // the lower body curving backward and down -- is the fingerprint of rideHeight
                // being taller than this character's real legs: the hips are held too high, the
                // feet dangle, and the legs stretch down reaching for a floor they cannot touch.
                // Measured directly here so we tune rideHeight from fact, not a guess.
                if (Application.isPlaying && body.muscles != null)
                {
                    float footBottom = float.MaxValue;
                    Rigidbody hipsRb = body.pelvis;
                    foreach (Rigidbody rb in body.muscles.Bones)
                    {
                        if (!rb) continue;
                        string n = rb.name.ToLowerInvariant();
                        if (!n.Contains("foot") && !n.Contains("toe")) continue;
                        Collider fc = rb.GetComponent<Collider>();
                        if (fc) footBottom = Mathf.Min(footBottom, fc.bounds.min.y);
                    }
                    if (footBottom < float.MaxValue && hipsRb)
                    {
                        float groundY = 0f;
                        if (Physics.Raycast(hipsRb.position + Vector3.up * 0.2f, Vector3.down,
                                            out RaycastHit gh, 5f, body.groundMask, QueryTriggerInteraction.Ignore))
                            groundY = gh.point.y;
                        float gap = footBottom - groundY;
                        float foldedSpan = hipsRb.position.y - footBottom;

                        // True leg REACH = sum of bone lengths from the puppet (the animated
                        // pose), NOT the straight-line hip→sole in the CURRENT pose. When the
                        // legs fold, the live span shrinks and blaming rideHeight reads the
                        // cause backwards -- it would advise squatting the character to match a
                        // pose that is itself the bug. Reach is what the legs CAN span.
                        float reach = LegReach(body.puppetAnimator);
                        string reachStr = reach > 0f ? $"{reach:0.00}" : "n/a";
                        sb.AppendLine($"  info     lowest foot is {gap:0.00} m above the ground; folded leg span " +
                                      $"(current pose) = {foldedSpan:0.00} m, true leg reach (bone lengths) = {reachStr} m, " +
                                      $"rideHeight = {body.controller.rideHeight:0.00}.");
                        if (reach > 0f && body.controller.rideHeight > reach * 1.05f)
                        {
                            sb.AppendLine($"  PROBLEM  rideHeight ({body.controller.rideHeight:0.00}) exceeds the legs' " +
                                          $"actual reach ({reach:0.00} m) — the capsule holds the hips higher than the " +
                                          "legs can span even fully straight, so the feet cannot plant. Lower rideHeight " +
                                          $"toward {reach:0.00}. (A folded span BELOW reach with rideHeight <= reach is a " +
                                          "leg not reaching its pose, NOT a rideHeight problem — see the knee angle below.)");
                            problems++;
                        }
                    }
                }
                if (standingHip < 0.3f)
                {
                    sb.AppendLine("  PROBLEM  The pelvis anchor would hold the hips at or below the floor " +
                                  $"({standingHip:0.00} m). localOffset.y is near -rideHeight -- the signature " +
                                  "of the build-time ground raycast hitting the character's own hip collider " +
                                  "instead of the floor. The hips get dragged down and the character collapses. " +
                                  "Rebuild with the 0.5.3 fix, or place the model on a floor collider before building.");
                    problems++;
                }
            }

            // ---- animation ----------------------------------------------------
            if (body.puppetAnimator)
            {
                Animator anim = body.puppetAnimator;
                if (!anim.runtimeAnimatorController)
                    sb.AppendLine("  info     Puppet has no AnimatorController — the character will hold its " +
                                  "bind pose. Valid for a first test, but no locomotion.");
                if (anim.cullingMode != AnimatorCullingMode.AlwaysAnimate)
                { sb.AppendLine("  PROBLEM  Puppet Animator culling is not AlwaysAnimate — it will go limp off-screen."); problems++; }

                // The character mirrors the puppet. If it holds a T-pose while standing, the
                // puppet is in its bind pose -- so the question is whether the Animator is
                // actually sampling and RETARGETING a clip onto the humanoid, or not. These
                // checks split the two causes that both look like a T-pose:
                //   (a) no clip sampled (empty state / wrong Speed) -> clip count 0
                //   (b) a clip IS sampled but not applied -> avatar not a valid Humanoid
                if (!anim.enabled)
                { sb.AppendLine("  PROBLEM  Puppet Animator is DISABLED — nothing drives the puppet, so the " +
                                "physical rig holds its bind pose (T-pose)."); problems++; }

                if (anim.avatar == null || !anim.avatar.isValid)
                { sb.AppendLine("  PROBLEM  Puppet Animator has no valid Avatar — humanoid clips cannot retarget, " +
                                "so the puppet stays in bind pose (T-pose) even with clips assigned."); problems++; }
                else if (!anim.isHuman)
                { sb.AppendLine("  PROBLEM  Puppet Avatar is not Humanoid (isHuman = false). Humanoid clips will " +
                                "not drive a Generic avatar — the puppet holds its bind pose. Re-import the model " +
                                "as Rig > Humanoid and rebuild."); problems++; }

                if (Application.isPlaying)
                {
                    AnimatorStateInfo st = anim.GetCurrentAnimatorStateInfo(0);
                    AnimatorClipInfo[] clips = anim.GetCurrentAnimatorClipInfo(0);
                    sb.AppendLine($"  info     puppet base state: {(st.IsName("Locomotion") ? "Locomotion" : "hash " + st.shortNameHash)}, " +
                                  $"Speed param = {anim.GetFloat(body.speedParam):0.00}, playback speed = {anim.speed:0.00}.");

                    // Foot-slide check: the body must not travel faster than the walk clip depicts,
                    // or the feet skate. The wizard now measures referenceClipSpeed and caps maxSpeed
                    // from the clip's foot motion, so these two should be in the same ballpark.
                    float maxSp = body.controller ? body.controller.maxSpeed : 0f;
                    sb.AppendLine($"  info     locomotion sync — referenceClipSpeed {body.referenceClipSpeed:0.00} m/s, " +
                                  $"capsule maxSpeed {maxSp:0.00} m/s.");
                    if (body.referenceClipSpeed <= 0.05f)
                        sb.AppendLine("  info     referenceClipSpeed is ~0 — never measured. Rebuild via the wizard (it measures " +
                                      "the walk clip's stride from the feet) or the feet will skate.");
                    else if (maxSp > body.referenceClipSpeed * 1.6f)
                        sb.AppendLine($"  info     maxSpeed ({maxSp:0.00}) is well above the walk stride ({body.referenceClipSpeed:0.00}) " +
                                      "with no faster tier to cover it — at top speed the playback saturates and the feet skate. " +
                                      "Add a run clip, or the wizard rebuild will cap maxSpeed to the measured stride.");
                    if (clips.Length == 0)
                    {
                        sb.AppendLine("  PROBLEM  The current animator state is sampling NO clip (0 clips). At Speed 0 " +
                                      "that usually means the Locomotion blend tree has no Idle at threshold 0 -- the " +
                                      "puppet then holds bind pose (T-pose). Assign Idle and regenerate the graph.");
                        problems++;
                    }
                    else
                    {
                        System.Text.StringBuilder cs = new System.Text.StringBuilder();
                        foreach (AnimatorClipInfo ci in clips)
                            cs.Append($"{(ci.clip ? ci.clip.name : "null")}({ci.weight:0.00}) ");
                        sb.AppendLine($"  info     sampling {clips.Length} clip(s): {cs.ToString().Trim()}. " +
                                      "If a clip IS sampled but the body is still a T-pose, the clip is not " +
                                      "retargeting -- check the Avatar (above) and that the clip is a Humanoid clip.");
                    }

                    // Does the ANIMATION ask for a straight leg, and does the PHYSICAL leg
                    // actually reach it? Compare the knee angle on the puppet (the animated
                    // reference) with the same joint on the physical rig. Straight ~= 180.
                    //   puppet straight + physical bent -> physics is not following (limits /
                    //     drive / hip orientation), NOT the animation.
                    //   puppet bent -> the retargeted clip is itself bent (even if it looks
                    //     straight in Mixamo, Unity's humanoid retarget differs), an avatar/clip
                    //     issue, not the physics.
                    Transform pUp = anim.GetBoneTransform(HumanBodyBones.LeftUpperLeg);
                    Transform pLo = anim.GetBoneTransform(HumanBodyBones.LeftLowerLeg);
                    Transform pFt = anim.GetBoneTransform(HumanBodyBones.LeftFoot);
                    Transform physRoot = body.muscles ? body.muscles.physicalRoot : null;
                    if (pUp && pLo && pFt && physRoot)
                    {
                        float puppetKnee = Vector3.Angle(pUp.position - pLo.position, pFt.position - pLo.position);
                        Transform xUp = FindDeep(physRoot, pUp.name);
                        Transform xLo = FindDeep(physRoot, pLo.name);
                        Transform xFt = FindDeep(physRoot, pFt.name);
                        float physKnee = (xUp && xLo && xFt)
                            ? Vector3.Angle(xUp.position - xLo.position, xFt.position - xLo.position) : -1f;
                        sb.AppendLine($"  info     LEFT knee angle — animation(puppet) {puppetKnee:0}°, " +
                                      $"physical {physKnee:0}° (180 = straight leg).");

                        // Hip flexion: how far the thigh is swung from straight-down (WORLD space).
                        // Kept for continuity, but note it CANNOT tell a mis-posed pelvis from a
                        // joint that missed its target -- a pelvis yaw contaminates it. The LOCAL
                        // drive error below is the number that actually separates the two.
                        float puppetHip = Vector3.Angle(pLo.position - pUp.position, Vector3.down);
                        float physHip = (xUp && xLo)
                            ? Vector3.Angle(xLo.position - xUp.position, Vector3.down) : -1f;
                        sb.AppendLine($"  info     LEFT hip flexion (thigh vs straight-down, WORLD) — animation(puppet) " +
                                      $"{puppetHip:0}°, physical {physHip:0}° (0 = thigh straight down).");

                        // ---- LOCAL drive error: the decisive, pelvis-independent number -------
                        // The muscles COMMAND each joint toward the puppet bone's LOCAL rotation
                        // (relative to its own parent). Comparing physical-local to puppet-local
                        // reads the joint's OWN success at reaching target, with the pelvis
                        // orientation -- and its large free yaw -- completely factored out. A yaw
                        // about vertical moves the WORLD flexion above but leaves this untouched, so
                        // this is what separates "the joint missed its target" (a joint/drive/limit
                        // problem) from "the joint reached target but the pelvis is mis-posed".
                        if (xUp && xLo)
                        {
                            float hipLocalErr = Quaternion.Angle(xUp.localRotation, pUp.localRotation);
                            float kneeLocalErr = Quaternion.Angle(xLo.localRotation, pLo.localRotation);
                            sb.AppendLine($"  info     LEFT LOCAL drive error (physical joint vs commanded puppet pose) — " +
                                          $"hip {hipLocalErr:0}°, knee {kneeLocalErr:0}° (0 = the joint reached its animated target).");
                            if (hipLocalErr > 12f && kneeLocalErr > 12f)
                                sb.AppendLine("  info     BOTH hip and knee are far from their targets — the whole leg is giving way, " +
                                              "not one joint. That is the fingerprint of the leg DRIVE losing to the body load near a " +
                                              "straight-leg stance (buckling), or a rideHeight at full leg reach — NOT a single joint's " +
                                              "limit/axis. Confirm with the hip-height sag line below.");
                            else if (hipLocalErr > 12f)
                                sb.AppendLine("  info     The HIP misses its target while the knee reaches it — the fold ORIGINATES at " +
                                              "the hip joint (limit/axis/drive); the knee only bends downstream to keep the foot planted.");
                            else if (kneeLocalErr > 12f)
                                sb.AppendLine("  info     The KNEE misses its target while the hip reaches it — the knee joint is the fault.");
                            else
                                sb.AppendLine("  info     Both leg joints REACH their local targets — the joints are fine; the folded LOOK " +
                                              "comes from upstream (pelvis pose / rideHeight), not the hip or knee joints.");
                        }

                        // ---- hip sag: is the body squatting (drive losing to load)? -----------
                        // If the hips sit well below rideHeight while the feet are planted, the leg
                        // has folded and the drives are HOLDING that fold -- the anchor cannot lift
                        // the hips through a leg it is not straightening. Paired with both leg joints
                        // off target, that is buckling under load, addressed by rideHeight + leg
                        // drive, not by touching the pelvis lock.
                        if (body.pelvis && body.controller)
                        {
                            float groundY2 = 0f;
                            if (Physics.Raycast(body.pelvis.position + Vector3.up * 0.2f, Vector3.down,
                                                out RaycastHit gh2, 5f, body.groundMask, QueryTriggerInteraction.Ignore))
                                groundY2 = gh2.point.y;
                            float hipH = body.pelvis.position.y - groundY2;
                            float capH = body.controller.transform.position.y - groundY2;
                            float shortfall = body.controller.rideHeight - hipH;
                            float capSag = body.controller.rideHeight - capH;
                            float pelvisBelowCap = capH - hipH;

                            sb.AppendLine($"  info     heights above ground — CAPSULE {capH:0.00} m (target rideHeight " +
                                          $"{body.controller.rideHeight:0.00}, so capsule sag {capSag:0.00} m), pelvis {hipH:0.00} m " +
                                          $"(pelvis is {pelvisBelowCap:0.00} m below the capsule).");

                            // Split the two upstream causes of a low pelvis, now that we can see both:
                            //   (a) the CAPSULE itself is below rideHeight -> the ride spring is sagging
                            //       under the capsule's own weight (a Force-mode spring settles mg/k low).
                            //       Fixed by gravity-compensating the ride spring. The pelvis then rides
                            //       low simply because it faithfully follows a low capsule.
                            //   (b) the pelvis is well BELOW the capsule -> the anchor/legs are not
                            //       holding the pelvis up to the capsule (weak anchor, or legs bearing
                            //       load and buckling).
                            if (capSag > 0.05f)
                                sb.AppendLine($"  info     The CAPSULE is sagging {capSag:0.00} m below rideHeight — the ride spring is " +
                                              "settling under the capsule's own weight (a Force-mode spring holds mg/k low). The whole " +
                                              "character rides that low and the legs bend just to keep the feet down. Gravity-compensate " +
                                              "the ride spring (FloatingCapsuleController.ApplyRideSpring) so it holds rideHeight exactly.");
                            if (pelvisBelowCap > 0.08f)
                                sb.AppendLine($"  info     The PELVIS hangs {pelvisBelowCap:0.00} m below the capsule — the anchor/legs are not " +
                                              "holding it up to the capsule (weak pelvis anchor, or the legs are bearing the body load and " +
                                              "buckling instead of the capsule carrying it).");
                            if (shortfall > 0.1f && capSag <= 0.05f && pelvisBelowCap <= 0.08f)
                                sb.AppendLine("  info     Capsule and pelvis agree and are near rideHeight, yet the hip-above-ground is short — " +
                                              "check the ground ray / rideHeight calibration.");
                        }

                        // ---- pelvis orientation: correctly separated into TILT vs YAW ---------
                        // A pelvis TILT (up-axis off vertical) deletes the pose's pelvic tilt and the
                        // thighs inherit it -> a genuine lock/upright problem. A pelvis YAW (up
                        // matches, forward rotated ABOUT vertical) does NOT: rotating a near-vertical
                        // thigh about the vertical axis leaves its angle-from-vertical unchanged, so a
                        // yaw cannot fold the leg. The pelvis here tracks the controller's facing, not
                        // the animation's, so a large yaw is expected and harmless to leg posture --
                        // the old message conflated the two and blamed the lock for a harmless yaw.
                        Transform pupHips = anim.GetBoneTransform(HumanBodyBones.Hips);
                        if (pupHips && body.pelvis)
                        {
                            float upDiff = Vector3.Angle(body.pelvis.transform.up, pupHips.up);
                            float fwdDiff = Vector3.Angle(body.pelvis.transform.forward, pupHips.forward);
                            sb.AppendLine($"  info     pelvis orientation — physical vs puppet: up (TILT) off by {upDiff:0}°, " +
                                          $"forward (YAW) off by {fwdDiff:0}°.");
                            if (upDiff > 12f)
                                sb.AppendLine($"  info     The pelvis is TILTED {upDiff:0}° off upright — the upright/lock is not matching " +
                                              "the animation's pelvic tilt and the thighs inherit the offset. THIS is a genuine lock/upright fix.");
                            else if (fwdDiff > 20f)
                                sb.AppendLine($"  info     The pelvis is UPRIGHT (tilt {upDiff:0}°) but YAWED {fwdDiff:0}° off the puppet — it " +
                                              "tracks the controller's facing, not the animation's. A yaw about vertical CANNOT fold a near-" +
                                              "vertical thigh, so this is NOT the cause of the bent legs. Judge the legs by the LOCAL drive error " +
                                              "above, not this line.");
                            else
                                sb.AppendLine("  info     The pelvis matches the animation in both tilt and yaw.");
                        }

                        if (puppetKnee > 150f && physKnee >= 0f && physKnee < 140f)
                        {
                            sb.AppendLine("  PROBLEM  The animation wants a STRAIGHT leg but the physical leg is bent " +
                                          $"({physKnee:0}°). The physics is not reaching the animated pose — read the LOCAL drive " +
                                          "error and hip-sag lines above (NOT the pelvis line) to see whether it is one joint's " +
                                          "limit/axis or the whole leg buckling under load.");
                            problems++;
                        }
                        else if (puppetKnee < 150f)
                        {
                            sb.AppendLine("  info     The puppet's own (retargeted) leg is bent, so the reference pose " +
                                          "the physics copies is bent. That is an avatar/retarget issue in-engine (Unity's " +
                                          "humanoid retarget can differ from the Mixamo preview), not the physics.");
                        }
                    }
                }
            }

            sb.AppendLine();
            sb.AppendLine(problems == 0
                ? "No blocking problems found."
                : $"{problems} problem(s) found.");

            if (problems == 0) Debug.Log(sb.ToString(), body);
            else Debug.LogError(sb.ToString(), body);
        }

        /// <summary>
        /// The legs' true reach: sum of the puppet's bone lengths (hip→knee→ankle→toe),
        /// which is how far the leg CAN span. Use this, not the folded live pose, to judge
        /// whether rideHeight is genuinely too tall. Returns -1 if the bones aren't found.
        /// </summary>
        static float LegReach(Animator anim)
        {
            if (!anim || !anim.isHuman) return -1f;
            Transform u = anim.GetBoneTransform(HumanBodyBones.LeftUpperLeg);
            Transform l = anim.GetBoneTransform(HumanBodyBones.LeftLowerLeg);
            Transform f = anim.GetBoneTransform(HumanBodyBones.LeftFoot);
            if (!u || !l || !f) return -1f;
            float r = Vector3.Distance(u.position, l.position) + Vector3.Distance(l.position, f.position);
            Transform toe = anim.GetBoneTransform(HumanBodyBones.LeftToes);
            if (toe) r += Vector3.Distance(f.position, toe.position);
            return r;
        }

        /// <summary>First descendant transform with this exact name, or null.</summary>
        static Transform FindDeep(Transform root, string name)
        {
            if (!root) return null;
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
                if (t.name == name) return t;
            return null;
        }

        static int Check(StringBuilder sb, Object o, string label)
        {
            if (o) return 0;
            sb.AppendLine($"  PROBLEM  {label} is not assigned.");
            return 1;
        }
    }
}
#endif
