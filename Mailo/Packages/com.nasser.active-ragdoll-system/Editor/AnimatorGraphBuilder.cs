#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace NasserActiveRagdoll.EditorTools
{
    /// <summary>
    /// Generates the full animator graph from a LocomotionClipSet.
    ///
    /// Structure produced:
    ///
    ///   Base Layer
    ///     Locomotion            1D blend on "Speed"
    ///       ├ Idle              threshold 0
    ///       ├ WalkTree          2D Freeform Directional on MoveX / MoveY
    ///       └ RunTree           2D Freeform Directional on MoveX / MoveY
    ///     Airborne              JumpStart -> Fall -> Land, driven by "Grounded"
    ///     GetUpProne            played by name, exits to Locomotion
    ///     GetUpSupine           played by name, exits to Locomotion
    ///
    ///   UpperBody Layer         AvatarMask: both arms + chest, Override, weight 0..1
    ///     Empty / Carry         driven by "Carrying"
    ///
    /// A nested 1D-of-2D tree is used rather than one flat 2D tree on purpose. A flat
    /// tree has to interpolate between walk and run clips that have different contact
    /// timings, which produces the floaty half-step everyone recognises as "blend tree
    /// locomotion". Splitting speed from direction keeps each 2D blend between clips
    /// that share a gait.
    /// </summary>
    public static class AnimatorGraphBuilder
    {
        public const string P_SPEED = "Speed";
        public const string P_MOVEX = "MoveX";
        public const string P_MOVEY = "MoveY";
        public const string P_GROUNDED = "Grounded";
        public const string P_CARRYING = "Carrying";

        // Measured foot-stride speeds passed into a Build() call, used as blend thresholds when
        // the clips are in-place (no root motion for averageSpeed to read). 0 = fall back to Tier.
        static float _walkOverride, _runOverride;

        public static AnimatorController Build(LocomotionClipSet set, string path,
                                              float walkSpeedOverride = 0f, float runSpeedOverride = 0f)
        {
            if (set == null || string.IsNullOrEmpty(path)) return null;
            _walkOverride = walkSpeedOverride;
            _runOverride = runSpeedOverride;

            AnimatorController ac = AnimatorController.CreateAnimatorControllerAtPath(path);
            ac.AddParameter(P_SPEED, AnimatorControllerParameterType.Float);
            ac.AddParameter(P_MOVEX, AnimatorControllerParameterType.Float);
            ac.AddParameter(P_MOVEY, AnimatorControllerParameterType.Float);
            ac.AddParameter(P_GROUNDED, AnimatorControllerParameterType.Bool);
            ac.AddParameter(P_CARRYING, AnimatorControllerParameterType.Bool);

            AnimatorStateMachine sm = ac.layers[0].stateMachine;
            sm.entryPosition = new Vector3(-260, 0);
            sm.anyStatePosition = new Vector3(-260, 120);
            sm.exitPosition = new Vector3(560, 0);

            AnimatorState locomotion = BuildLocomotion(ac, set);
            locomotion.writeDefaultValues = false;
            sm.defaultState = locomotion;

            BuildGetUps(sm, set, locomotion);
            if (set.HasAirborne) BuildAirborne(sm, set, locomotion);
            if (set.HasUpperBody) BuildUpperBody(ac, set, path);

            EditorUtility.SetDirty(ac);
            AssetDatabase.SaveAssets();
            return ac;
        }

        // ------------------------------------------------------------------ locomotion

        static AnimatorState BuildLocomotion(AnimatorController ac, LocomotionClipSet set)
        {
            AnimatorState state = ac.CreateBlendTreeInController("Locomotion", out BlendTree root, 0);
            root.blendType = BlendTreeType.Simple1D;
            root.blendParameter = P_SPEED;
            root.useAutomaticThresholds = false;
            root.name = "Locomotion";

            if (set.idle) root.AddChild(set.idle, 0f);

            float walkAt = Tier(set, walk: true);
            float runAt = Tier(set, walk: false);

            // Never let the two tiers collide, or the 1D blend degenerates.
            if (runAt <= walkAt + 0.15f) runAt = walkAt + 1.2f;

            if (set.walkForward)
            {
                BlendTree walk = root.CreateBlendTreeChild(walkAt);
                walk.name = "Walk";
                Directional(walk, set.walkForward, set.walkBackward, set.walkLeft, set.walkRight);
            }
            if (set.runForward)
            {
                BlendTree run = root.CreateBlendTreeChild(runAt);
                run.name = "Run";
                Directional(run, set.runForward, set.runBackward, set.runLeft, set.runRight);
            }
            return state;
        }

        static void Directional(BlendTree tree, AnimationClip fwd, AnimationClip back,
                                AnimationClip left, AnimationClip right)
        {
            tree.blendType = BlendTreeType.FreeformDirectional2D;
            tree.blendParameter = P_MOVEX;
            tree.blendParameterY = P_MOVEY;

            if (fwd) tree.AddChild(fwd, new Vector2(0f, 1f));
            if (back) tree.AddChild(back, new Vector2(0f, -1f));
            if (left) tree.AddChild(left, new Vector2(-1f, 0f));
            if (right) tree.AddChild(right, new Vector2(1f, 0f));

            // Freeform Directional needs a point at the origin or it misbehaves near
            // zero input. Reuse forward at the centre if we have nothing better.
            if (fwd && !back && !left && !right) return;
            if (fwd) tree.AddChild(fwd, new Vector2(0f, 0.02f));
        }

        /// <summary>
        /// Reads the clip's own root speed and uses it as the blend threshold.
        /// This is the single biggest anti-foot-sliding measure available: if the
        /// threshold says 1.6 m/s but the clip actually travels 1.1 m/s, the feet
        /// skate by 45% and no amount of damping hides it.
        /// </summary>
        static float Tier(LocomotionClipSet set, bool walk)
        {
            AnimationClip c = walk ? set.walkForward : set.runForward;
            float fallback = walk ? set.walkSpeed : set.runSpeed;

            // A foot-measured depicted speed (from MeasureDepictedSpeed, passed into Build) wins:
            // it works for in-place clips, where root motion is zero and the reading below fails.
            float measuredFeet = walk ? _walkOverride : _runOverride;
            if (measuredFeet > 0.05f) return measuredFeet;

            if (!set.autoThresholdsFromClips || c == null) return fallback;

            float measured = new Vector2(c.averageSpeed.x, c.averageSpeed.z).magnitude;
            if (measured < 0.05f)
            {
                Debug.LogWarning($"[Nasser ARS] '{c.name}' has no measurable root motion and no foot-stride " +
                                 $"measurement was supplied. Using the fallback threshold {fallback}. Build via the " +
                                 "wizard (it measures stride from the feet) or enable Root Transform Position (XZ) " +
                                 "> Bake Into Pose = OFF on the clip to fix foot sliding.");
                return fallback;
            }
            return measured;
        }

        /// <summary>
        /// The ground speed a locomotion clip DEPICTS, measured from the FEET rather than root
        /// motion — so it works for in-place clips (Mixamo's default), which have no root motion
        /// for averageSpeed to read. Samples the clip on a throwaway copy of the model and averages
        /// the backward speed of whichever foot is planted (the one moving backward relative to the
        /// hips, along the character's forward axis). That is exactly the speed the character must
        /// travel for the feet not to slide. Returns 0 if it cannot measure.
        /// </summary>
        public static float MeasureDepictedSpeed(GameObject model, AnimationClip clip)
        {
            if (!model || !clip || clip.length < 0.05f) return 0f;
            Animator src = model.GetComponent<Animator>();
            if (!src || !src.isHuman) return 0f;

            GameObject temp = Object.Instantiate(model);
            temp.hideFlags = HideFlags.HideAndDontSave;
            try
            {
                Animator anim = temp.GetComponent<Animator>();
                Transform hips = anim ? anim.GetBoneTransform(HumanBodyBones.Hips) : null;
                Transform lf = anim ? anim.GetBoneTransform(HumanBodyBones.LeftFoot) : null;
                Transform rf = anim ? anim.GetBoneTransform(HumanBodyBones.RightFoot) : null;
                if (!hips || !lf || !rf) return 0f;

                Vector3 fwd = temp.transform.forward;   // character forward (standard +Z humanoid import)
                const int N = 32;
                float dt = clip.length / N;
                float prevL = 0f, prevR = 0f, total = 0f;
                int count = 0;
                for (int i = 0; i <= N; i++)
                {
                    clip.SampleAnimation(temp, i * dt);
                    // Foot position along forward, RELATIVE to the hips — removes any root/hip
                    // translation, so an in-place clip reads the same as a root-motion one.
                    float lz = Vector3.Dot(lf.position - hips.position, fwd);
                    float rz = Vector3.Dot(rf.position - hips.position, fwd);
                    if (i > 0)
                    {
                        float lv = (lz - prevL) / dt;
                        float rv = (rz - prevR) / dt;
                        float backward = -Mathf.Min(lv, rv);   // planted foot moves backward at ground speed
                        if (backward > 0.01f) { total += backward; count++; }
                    }
                    prevL = lz; prevR = rz;
                }
                return count > 0 ? total / count : 0f;
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[Nasser ARS] Stride measurement failed for '{clip.name}': {e.Message}");
                return 0f;
            }
            finally
            {
                Object.DestroyImmediate(temp);
            }
        }

        // ------------------------------------------------------------------ recovery

        static void BuildGetUps(AnimatorStateMachine sm, LocomotionClipSet set, AnimatorState locomotion)
        {
            AnimatorState prone = sm.AddState("GetUpProne", new Vector3(260, -110));
            prone.motion = set.getUpProne;
            prone.writeDefaultValues = false;

            AnimatorState supine = sm.AddState("GetUpSupine", new Vector3(260, -30));
            supine.motion = set.getUpSupine;
            supine.writeDefaultValues = false;

            // CharacterBody plays these by name, so no entry transition is needed --
            // only the exit back to locomotion.
            foreach (AnimatorState s in new[] { prone, supine })
            {
                AnimatorStateTransition t = s.AddTransition(locomotion);
                t.hasExitTime = true;
                t.exitTime = 0.9f;
                t.duration = 0.2f;
                t.interruptionSource = TransitionInterruptionSource.None;
            }
        }

        // ------------------------------------------------------------------ airborne

        static void BuildAirborne(AnimatorStateMachine sm, LocomotionClipSet set, AnimatorState locomotion)
        {
            float d = set.transitionDuration;

            AnimatorState fall = sm.AddState("Fall", new Vector3(260, 90));
            fall.motion = set.fallLoop ? set.fallLoop : set.jumpStart;
            fall.writeDefaultValues = false;

            AnimatorState jump = null;
            if (set.jumpStart)
            {
                jump = sm.AddState("JumpStart", new Vector3(60, 90));
                jump.motion = set.jumpStart;
                jump.writeDefaultValues = false;

                AnimatorStateTransition toFall = jump.AddTransition(fall);
                toFall.hasExitTime = true;
                toFall.exitTime = 0.75f;
                toFall.duration = 0.12f;
            }

            AnimatorState leaveGround = jump ? jump : fall;
            AnimatorStateTransition up = locomotion.AddTransition(leaveGround);
            up.hasExitTime = false;
            up.duration = 0.08f;   // leaving the ground should be instant
            up.AddCondition(AnimatorConditionMode.IfNot, 0, P_GROUNDED);

            AnimatorState landing = locomotion;
            if (set.land)
            {
                AnimatorState land = sm.AddState("Land", new Vector3(460, 90));
                land.motion = set.land;
                land.writeDefaultValues = false;

                AnimatorStateTransition outOfLand = land.AddTransition(locomotion);
                outOfLand.hasExitTime = true;
                outOfLand.exitTime = 0.7f;
                outOfLand.duration = d;
                landing = land;
            }

            AnimatorStateTransition down = fall.AddTransition(landing);
            down.hasExitTime = false;
            down.duration = d;
            down.AddCondition(AnimatorConditionMode.If, 0, P_GROUNDED);
        }

        // ------------------------------------------------------------------ upper body

        static void BuildUpperBody(AnimatorController ac, LocomotionClipSet set, string controllerPath)
        {
            AvatarMask mask = BuildArmMask(controllerPath);

            AnimatorStateMachine sm = new AnimatorStateMachine
            {
                name = "UpperBody",
                hideFlags = HideFlags.HideInHierarchy
            };
            AssetDatabase.AddObjectToAsset(sm, ac);

            AnimatorState empty = sm.AddState("Empty", new Vector3(60, 0));
            empty.writeDefaultValues = false;
            sm.defaultState = empty;

            AnimatorState carry = sm.AddState("Carry", new Vector3(300, 0));
            carry.motion = set.carryPose ? set.carryPose : set.reachPose;
            carry.writeDefaultValues = false;

            AnimatorStateTransition on = empty.AddTransition(carry);
            on.hasExitTime = false;
            on.duration = 0.25f;
            on.AddCondition(AnimatorConditionMode.If, 0, P_CARRYING);

            AnimatorStateTransition off = carry.AddTransition(empty);
            off.hasExitTime = false;
            off.duration = 0.3f;
            off.AddCondition(AnimatorConditionMode.IfNot, 0, P_CARRYING);

            ac.AddLayer(new AnimatorControllerLayer
            {
                name = "UpperBody",
                stateMachine = sm,
                avatarMask = mask,
                blendingMode = AnimatorLayerBlendingMode.Override,
                defaultWeight = 1f
            });
        }

        static AvatarMask BuildArmMask(string controllerPath)
        {
            string dir = System.IO.Path.GetDirectoryName(controllerPath);
            string name = System.IO.Path.GetFileNameWithoutExtension(controllerPath);
            string path = AssetDatabase.GenerateUniqueAssetPath($"{dir}/{name}_UpperBody.mask");

            AvatarMask mask = new AvatarMask();
            foreach (AvatarMaskBodyPart part in System.Enum.GetValues(typeof(AvatarMaskBodyPart)))
            {
                if (part == AvatarMaskBodyPart.LastBodyPart) continue;
                bool on = part == AvatarMaskBodyPart.LeftArm
                       || part == AvatarMaskBodyPart.RightArm
                       || part == AvatarMaskBodyPart.LeftFingers
                       || part == AvatarMaskBodyPart.RightFingers
                       || part == AvatarMaskBodyPart.Body;
                mask.SetHumanoidBodyPartActive(part, on);
            }

            AssetDatabase.CreateAsset(mask, path);
            return mask;
        }

        // ------------------------------------------------------------------ import fixups

        /// <summary>
        /// Locomotion clips need consistent import settings or blending looks wrong no
        /// matter how good the graph is. This applies the settings that matter and is
        /// offered as a button in the wizard.
        /// </summary>
        public static int FixClipImportSettings(LocomotionClipSet set)
        {
            var clips = new List<AnimationClip>
            {
                set.idle, set.walkForward, set.walkBackward, set.walkLeft, set.walkRight,
                set.runForward, set.runBackward, set.runLeft, set.runRight
            };

            var done = new HashSet<string>();
            int changed = 0;

            foreach (AnimationClip c in clips)
            {
                if (!c) continue;
                string path = AssetDatabase.GetAssetPath(c);
                if (string.IsNullOrEmpty(path) || !done.Add(path)) continue;

                if (!(AssetImporter.GetAtPath(path) is ModelImporter mi)) continue;

                ModelImporterClipAnimation[] anims = mi.clipAnimations;
                if (anims == null || anims.Length == 0) anims = mi.defaultClipAnimations;

                for (int i = 0; i < anims.Length; i++)
                {
                    anims[i].loopTime = true;
                    anims[i].lockRootRotation = true;     // keep facing under our control
                    anims[i].keepOriginalOrientation = true;
                    anims[i].lockRootHeightY = true;      // vertical bob comes from physics
                    anims[i].keepOriginalPositionY = true;
                    anims[i].heightFromFeet = false;
                    // XZ must NOT be baked into pose, or averageSpeed reads zero and
                    // auto-thresholds fall back to guesses.
                    anims[i].lockRootPositionXZ = false;
                }

                mi.clipAnimations = anims;
                mi.animationType = ModelImporterAnimationType.Human;
                EditorUtility.SetDirty(mi);
                mi.SaveAndReimport();
                changed++;
            }
            return changed;
        }
    }
}
#endif
