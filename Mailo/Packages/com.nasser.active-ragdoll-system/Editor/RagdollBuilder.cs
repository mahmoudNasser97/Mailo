#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace NasserActiveRagdoll.EditorTools
{
    /// <summary>
    /// Builds a complete active-ragdoll character from a humanoid model.
    ///
    /// The whole thing hangs off Animator.GetBoneTransform(HumanBodyBones.X). If the
    /// model is imported as Humanoid, Unity has already solved bone identification for
    /// us -- no naming conventions, no manual drag-and-drop of eleven bone fields.
    /// That is what makes this a few clicks instead of an afternoon.
    /// </summary>
    public static class RagdollBuilder
    {
        public class Options
        {
            public GameObject model;
            public CharacterRole role = CharacterRole.Npc;
            public RagdollProfile profile;
            public RuntimeAnimatorController controller;
            public bool buildLayers = true;
            public bool addGrabHandles = true;
            public bool addDriver = true;
            public string characterLayer = "Character";
            public string grabbableLayer = "Grabbable";
        }

        // name, bone, childBone (for length), massFraction, radiusRatio, isBox
        struct Seg
        {
            public HumanBodyBones bone, child;
            public float mass, radius;
            public bool box;
            public Seg(HumanBodyBones b, HumanBodyBones c, float m, float r, bool bx = false)
            { bone = b; child = c; mass = m; radius = r; box = bx; }
        }

        static readonly Seg[] SEGMENTS =
        {
            new Seg(HumanBodyBones.Hips,          HumanBodyBones.Spine,         0.16f, 0.24f, true),
            new Seg(HumanBodyBones.Spine,         HumanBodyBones.Head,          0.24f, 0.26f, true),
            new Seg(HumanBodyBones.Head,          HumanBodyBones.Head,          0.07f, 0.10f),
            new Seg(HumanBodyBones.LeftUpperArm,  HumanBodyBones.LeftLowerArm,  0.035f, 0.22f),
            new Seg(HumanBodyBones.RightUpperArm, HumanBodyBones.RightLowerArm, 0.035f, 0.22f),
            new Seg(HumanBodyBones.LeftLowerArm,  HumanBodyBones.LeftHand,      0.022f, 0.20f),
            new Seg(HumanBodyBones.RightLowerArm, HumanBodyBones.RightHand,     0.022f, 0.20f),
            new Seg(HumanBodyBones.LeftHand,      HumanBodyBones.LeftHand,      0.009f, 0.05f, true),
            new Seg(HumanBodyBones.RightHand,     HumanBodyBones.RightHand,     0.009f, 0.05f, true),
            new Seg(HumanBodyBones.LeftUpperLeg,  HumanBodyBones.LeftLowerLeg,  0.105f, 0.24f),
            new Seg(HumanBodyBones.RightUpperLeg, HumanBodyBones.RightLowerLeg, 0.105f, 0.24f),
            new Seg(HumanBodyBones.LeftLowerLeg,  HumanBodyBones.LeftFoot,      0.06f,  0.20f),
            new Seg(HumanBodyBones.RightLowerLeg, HumanBodyBones.RightFoot,     0.06f,  0.20f),
            new Seg(HumanBodyBones.LeftFoot,      HumanBodyBones.LeftFoot,      0.018f, 0.05f, true),
            new Seg(HumanBodyBones.RightFoot,     HumanBodyBones.RightFoot,     0.018f, 0.05f, true),
        };

        // child bone -> parent bone, plus angular limits (twistLow, twistHigh, swing1, swing2)
        struct Link
        {
            public HumanBodyBones child, parent;
            public float lowX, highX, y, z;
            public bool hinge;
            public Link(HumanBodyBones c, HumanBodyBones p, float lx, float hx, float y_, float z_, bool h = false)
            { child = c; parent = p; lowX = lx; highX = hx; y = y_; z = z_; hinge = h; }
        }

        static readonly Link[] LINKS =
        {
            new Link(HumanBodyBones.Spine,         HumanBodyBones.Hips,          -20f,  20f, 20f, 20f),
            new Link(HumanBodyBones.Head,          HumanBodyBones.Spine,         -30f,  30f, 30f, 25f),
            new Link(HumanBodyBones.LeftUpperArm,  HumanBodyBones.Spine,         -60f,  60f, 90f, 70f),
            new Link(HumanBodyBones.RightUpperArm, HumanBodyBones.Spine,         -60f,  60f, 90f, 70f),
            new Link(HumanBodyBones.LeftLowerArm,  HumanBodyBones.LeftUpperArm,   -5f, 130f,  5f,  0f, true),
            new Link(HumanBodyBones.RightLowerArm, HumanBodyBones.RightUpperArm,  -5f, 130f,  5f,  0f, true),
            new Link(HumanBodyBones.LeftHand,      HumanBodyBones.LeftLowerArm,  -40f,  40f, 25f, 15f),
            new Link(HumanBodyBones.RightHand,     HumanBodyBones.RightLowerArm, -40f,  40f, 25f, 15f),
            new Link(HumanBodyBones.LeftUpperLeg,  HumanBodyBones.Hips,          -60f,  40f, 45f, 30f),
            new Link(HumanBodyBones.RightUpperLeg, HumanBodyBones.Hips,          -60f,  40f, 45f, 30f),
            new Link(HumanBodyBones.LeftLowerLeg,  HumanBodyBones.LeftUpperLeg, -130f,   2f,  2f,  0f, true),
            new Link(HumanBodyBones.RightLowerLeg, HumanBodyBones.RightUpperLeg,-130f,   2f,  2f,  0f, true),
            new Link(HumanBodyBones.LeftFoot,      HumanBodyBones.LeftLowerLeg,  -30f,  30f, 20f, 15f),
            new Link(HumanBodyBones.RightFoot,     HumanBodyBones.RightLowerLeg, -30f,  30f, 20f, 15f),
        };

        public static string Validate(GameObject model)
        {
            if (!model) return "No model assigned.";
            Animator a = model.GetComponent<Animator>();
            if (!a) return "The model has no Animator component.";
            if (!a.avatar) return "The Animator has no Avatar.";
            if (!a.isHuman) return "The model is not imported as Humanoid. Set Rig > Animation Type to Humanoid.";
            if (!a.GetBoneTransform(HumanBodyBones.Hips)) return "Avatar is missing the Hips bone.";
            if (!a.GetBoneTransform(HumanBodyBones.Head)) return "Avatar is missing the Head bone.";
            if (model.GetComponentInChildren<SkinnedMeshRenderer>() == null)
                return "No SkinnedMeshRenderer found. Is this the model root?";
            return null;
        }

        public static GameObject Build(Options o)
        {
            string problem = Validate(o.model);
            if (problem != null) { Debug.LogError("[Nasser ARS] " + problem); return null; }

            if (o.buildLayers) { EnsureLayer(o.characterLayer); EnsureLayer(o.grabbableLayer); }

            Animator srcAnim = o.model.GetComponent<Animator>();
            RagdollProfile profile = o.profile ? o.profile : ScriptableObject.CreateInstance<RagdollProfile>();

            // ---- root ------------------------------------------------------
            GameObject root = new GameObject(o.model.name + "_Character");
            Undo.RegisterCreatedObjectUndo(root, "Build Active Ragdoll");
            root.transform.SetPositionAndRotation(o.model.transform.position, o.model.transform.rotation);

            GameObject physical = o.model;
            Undo.SetTransformParent(physical.transform, root.transform, "Reparent model");
            physical.name = "Physical";

            // ---- physical rig ----------------------------------------------
            Dictionary<HumanBodyBones, Rigidbody> bodies = BuildBones(srcAnim, profile.totalMass, o.characterLayer);
            BuildJoints(srcAnim, bodies);

            Rigidbody hips = bodies[HumanBodyBones.Hips];
            Rigidbody spine = bodies[HumanBodyBones.Spine];
            Rigidbody headRb = bodies[HumanBodyBones.Head];

            // ---- animated puppet -------------------------------------------
            GameObject puppet = BuildPuppet(physical, o.controller);
            puppet.transform.SetParent(root.transform, true);

            // ---- controller capsule -----------------------------------------
            GameObject capsuleGo = new GameObject("Controller");
            capsuleGo.transform.SetParent(root.transform, false);
            float height = EstimateHeight(srcAnim);
            int notCharacter = ~(1 << LayerMask.NameToLayer(o.characterLayer));

            // rideHeight is how high the capsule hovers, which is the character's STANDING
            // hip height -- i.e. its actual leg length (hip to sole), NOT a fixed human 0.9.
            // Measured from the built foot colliders. On a stylized or short-legged rig a
            // fixed 0.9 holds the hips higher than the legs can reach: the feet plant on the
            // floor and the hips get dragged down against them (or, if placed high, the feet
            // dangle), and the anchor spring fights that gap forever -- curling the lower body
            // into a "banana" while the torso stays upright. Measuring makes the capsule hold
            // the hips exactly where this character's legs put its feet.
            float legLength = MeasureLegLength(bodies, hips);
            float rideHeight = legLength > 0.05f ? Mathf.Clamp(legLength, 0.3f, 2f) : profile.rideHeight;

            // Ground under the character, so the ride ray reaches it from the build pose
            // (same mask the ride spring uses at runtime -- never the character's own layer,
            // or the ray hits the hip collider and everything downstream is wrong).
            float groundY = hips.position.y - height;
            if (Physics.Raycast(hips.position + Vector3.up * 0.2f, Vector3.down,
                                out RaycastHit groundHit, height * 2f,
                                notCharacter, QueryTriggerInteraction.Ignore))
                groundY = groundHit.point.y;

            // Place the capsule AT the hips so the anchor offset comes out ~0: the anchor then
            // holds the hips at the capsule centre, and at runtime the ride spring lowers the
            // capsule to rideHeight above the ground, carrying the hips down onto the legs.
            capsuleGo.transform.position = hips.position;

            CapsuleCollider cap = capsuleGo.AddComponent<CapsuleCollider>();
            cap.height = Mathf.Max(0.6f, height * 0.55f);
            cap.radius = Mathf.Max(0.18f, height * 0.13f);
            cap.center = Vector3.zero;
            capsuleGo.layer = LayerMask.NameToLayer(o.characterLayer);

            Rigidbody capRb = capsuleGo.AddComponent<Rigidbody>();
            capRb.mass = profile.controllerMass;
            FloatingCapsuleController fcc = capsuleGo.AddComponent<FloatingCapsuleController>();
            fcc.rideHeight = rideHeight;
            // Long enough to find the ground both while hovering (rideHeight) and from the
            // higher build pose the capsule starts at before it settles.
            fcc.rayLength = Mathf.Max(rideHeight, hips.position.y - groundY) * 1.3f;
            fcc.groundMask = notCharacter;   // same surface the capsule was placed above

            // ---- components -------------------------------------------------
            ActiveRagdollMuscles muscles = root.AddComponent<ActiveRagdollMuscles>();
            muscles.physicalRoot = physical.transform;
            muscles.puppetRoot = puppet.transform;

            PelvisAnchor anchor = root.AddComponent<PelvisAnchor>();
            anchor.controller = fcc;
            anchor.pelvis = hips;
            anchor.chest = spine;
            // Anchor offset measured from the rig itself, so the spring holds the hips
            // where this particular character's hips actually belong.
            anchor.localOffset = capsuleGo.transform.InverseTransformPoint(hips.position);

            CharacterBody body = root.AddComponent<CharacterBody>();
            body.role = o.role;
            body.profile = o.profile;
            body.muscles = muscles;
            body.anchor = anchor;
            body.controller = fcc;
            body.puppetAnimator = puppet.GetComponent<Animator>();
            body.pelvis = hips;
            body.chest = spine;
            body.head = headRb;
            body.groundMask = ~(1 << LayerMask.NameToLayer(o.characterLayer));

            // ---- hands -------------------------------------------------------
            body.leftHand = MakeHand(root, bodies[HumanBodyBones.LeftHand], body, profile, o.grabbableLayer, "LeftHand");
            body.rightHand = MakeHand(root, bodies[HumanBodyBones.RightHand], body, profile, o.grabbableLayer, "RightHand");

            // ---- rig: camera for players, chest targets for NPCs --------------
            if (o.role == CharacterRole.Player)
                BuildPlayerRig(root, body, fcc, headRb.transform, profile);
            else
                BuildNpcRig(root, body, spine.transform);

            // ---- grab handles so this character can itself be picked up --------
            if (o.addGrabHandles) AddGrabHandles(body, bodies, o.grabbableLayer);

            // ---- driver --------------------------------------------------------
            if (o.addDriver)
            {
                if (o.role == CharacterRole.Player)
                {
                    PlayerDriver d = root.AddComponent<PlayerDriver>();
                    d.body = body; d.rig = body.rig;
                }
                else
                {
                    NpcDriver d = root.AddComponent<NpcDriver>();
                    d.body = body;
                }
            }

            IsolateCapsule(cap, physical);

            body.ApplyProfile();
            Selection.activeGameObject = root;
            return root;
        }

        // ------------------------------------------------------------------ bones

        static Dictionary<HumanBodyBones, Rigidbody> BuildBones(Animator anim, float totalMass, string layer)
        {
            var map = new Dictionary<HumanBodyBones, Rigidbody>();
            int layerIndex = LayerMask.NameToLayer(layer);

            foreach (Seg s in SEGMENTS)
            {
                Transform t = anim.GetBoneTransform(s.bone);
                if (!t) continue;

                Transform child = anim.GetBoneTransform(s.child);
                float length = (child && child != t)
                    ? Vector3.Distance(t.position, child.position)
                    : EstimateLeafLength(anim, s.bone);

                if (t.gameObject.layer == 0) t.gameObject.layer = layerIndex;

                Rigidbody rb = t.gameObject.GetComponent<Rigidbody>();
                if (!rb) rb = Undo.AddComponent<Rigidbody>(t.gameObject);
                rb.mass = Mathf.Max(0.15f, totalMass * s.mass);
                rb.interpolation = RigidbodyInterpolation.Interpolate;
                rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
                rb.solverIterations = 20;
                rb.solverVelocityIterations = 8;
                rb.maxAngularVelocity = 40f;

                AddCollider(t, s, length, child);
                map[s.bone] = rb;
            }
            return map;
        }

        static void AddCollider(Transform t, Seg s, float length, Transform child)
        {
            float radius = Mathf.Max(0.02f, length * s.radius);

            if (s.box || child == null || child == t)
            {
                BoxCollider bc = t.gameObject.GetComponent<BoxCollider>();
                if (!bc) bc = Undo.AddComponent<BoxCollider>(t.gameObject);
                float size = Mathf.Max(0.04f, length);
                bc.size = new Vector3(size * 0.9f, size * 0.72f, size * 0.62f);
                bc.center = Vector3.zero;
                return;
            }

            CapsuleCollider cc = t.gameObject.GetComponent<CapsuleCollider>();
            if (!cc) cc = Undo.AddComponent<CapsuleCollider>(t.gameObject);

            // Which local axis actually points at the child? Never assume Y --
            // exported rigs disagree constantly and this is the usual cause of
            // capsules lying sideways through the torso.
            Vector3 local = t.InverseTransformPoint(child.position);
            Vector3 abs = new Vector3(Mathf.Abs(local.x), Mathf.Abs(local.y), Mathf.Abs(local.z));
            int axis = abs.x > abs.y ? (abs.x > abs.z ? 0 : 2) : (abs.y > abs.z ? 1 : 2);

            cc.direction = axis;
            cc.height = length;
            cc.radius = radius;
            cc.center = local * 0.5f;
        }

        static void BuildJoints(Animator anim, Dictionary<HumanBodyBones, Rigidbody> map)
        {
            foreach (Link l in LINKS)
            {
                if (!map.TryGetValue(l.child, out Rigidbody child)) continue;
                if (!map.TryGetValue(l.parent, out Rigidbody parent)) continue;

                ConfigurableJoint j = child.gameObject.GetComponent<ConfigurableJoint>();
                if (!j) j = Undo.AddComponent<ConfigurableJoint>(child.gameObject);

                j.connectedBody = parent;
                j.anchor = Vector3.zero;
                j.autoConfigureConnectedAnchor = true;
                j.axis = Vector3.right;
                j.secondaryAxis = Vector3.up;

                j.xMotion = j.yMotion = j.zMotion = ConfigurableJointMotion.Locked;
                j.angularXMotion = ConfigurableJointMotion.Limited;

                // A hinge's secondary axes are LIMITED to a few degrees of slop, not Locked.
                // The bend axis here is the hardcoded local X (j.axis), but a rig's real bend
                // axis is whatever the rigger exported -- and on a rig whose leg bones are
                // oriented differently from its arm bones, X is right for the elbows and a few
                // degrees off for the knees. With Y/Z Locked, that small mismatch means the
                // knee's real bend direction is partly along a locked axis, so the drive can
                // never reach the animated pose and the leg settles bent (arms, which happen to
                // match X, are fine). A little slop lets the joint find its true bend direction
                // while still reading as a hinge. This is why the legs bent 35 deg short of the
                // animation at full drive: not weak springs, a locked-out degree of freedom.
                j.angularYMotion = ConfigurableJointMotion.Limited;
                j.angularZMotion = ConfigurableJointMotion.Limited;

                j.lowAngularXLimit = new SoftJointLimit { limit = l.lowX };
                j.highAngularXLimit = new SoftJointLimit { limit = l.highX };
                j.angularYLimit = new SoftJointLimit { limit = l.hinge ? Mathf.Max(l.y, 12f) : l.y };
                j.angularZLimit = new SoftJointLimit { limit = l.hinge ? Mathf.Max(l.z, 12f) : l.z };

                j.rotationDriveMode = RotationDriveMode.Slerp;
                j.configuredInWorldSpace = false;
                j.enableCollision = false;
                j.enablePreprocessing = false;
                j.projectionMode = JointProjectionMode.PositionAndRotation;
                j.projectionDistance = 0.05f;
                j.projectionAngle = 15f;
                j.slerpDrive = new JointDrive
                { positionSpring = 0f, positionDamper = 0f, maximumForce = Mathf.Infinity };
            }
        }

        // ------------------------------------------------------------------ puppet

        static GameObject BuildPuppet(GameObject physical, RuntimeAnimatorController controller)
        {
            GameObject puppet = Object.Instantiate(physical, physical.transform.parent);
            puppet.name = physical.name + "_Puppet";
            Undo.RegisterCreatedObjectUndo(puppet, "Create puppet");

            foreach (Joint j in puppet.GetComponentsInChildren<Joint>(true)) Object.DestroyImmediate(j);
            foreach (Rigidbody rb in puppet.GetComponentsInChildren<Rigidbody>(true)) Object.DestroyImmediate(rb);
            foreach (Collider c in puppet.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(c);
            foreach (Renderer r in puppet.GetComponentsInChildren<Renderer>(true)) Object.DestroyImmediate(r);
            foreach (MonoBehaviour mb in puppet.GetComponentsInChildren<MonoBehaviour>(true))
                if (mb && !(mb is Animator)) Object.DestroyImmediate(mb);

            Animator anim = puppet.GetComponent<Animator>();
            if (anim)
            {
                anim.applyRootMotion = false;
                if (controller) anim.runtimeAnimatorController = controller;
                // With no renderers left, Unity will happily cull this Animator and the
                // character goes limp off-screen. This line is not optional.
                anim.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                // The muscles read this pose in FixedUpdate, so the puppet must be
                // evaluated on the physics clock. On the default Normal mode the pose
                // the joints chase is stale on some physics steps and fresh on others,
                // which shows up as a subtle high-frequency judder you will chase for days.
#if UNITY_2023_1_OR_NEWER
                anim.updateMode = AnimatorUpdateMode.Fixed;        // renamed from AnimatePhysics
#else
                anim.updateMode = AnimatorUpdateMode.AnimatePhysics;
#endif
            }

            Animator physAnim = physical.GetComponent<Animator>();
            if (physAnim) physAnim.enabled = false;

            return puppet;
        }

        // ------------------------------------------------------------------ rigs

        static PhysicsHand MakeHand(GameObject root, Rigidbody handBody, CharacterBody body,
                                    RagdollProfile p, string grabbableLayer, string label)
        {
            if (!handBody) return null;
            GameObject go = new GameObject(label);
            go.transform.SetParent(root.transform, false);

            PhysicsHand h = go.AddComponent<PhysicsHand>();
            h.handBody = handBody;
            h.owner = body;
            h.reachSpring = p.reachSpring;
            h.reachDamper = p.reachDamper;
            h.maxReachForce = p.maxReachForce;
            h.gripSpring = p.gripSpring;
            h.grabRadius = p.grabRadius;
            h.throwForceMultiplier = p.throwForceMultiplier;
            h.maxThrowSpeed = p.maxThrowSpeed;

            int gl = LayerMask.NameToLayer(grabbableLayer);
            h.grabbableMask = gl >= 0 ? (1 << gl) : ~0;
            return h;
        }

        static void BuildPlayerRig(GameObject root, CharacterBody body,
                                   FloatingCapsuleController fcc, Transform head, RagdollProfile p)
        {
            GameObject rigGo = new GameObject("FirstPersonRig");
            rigGo.transform.SetParent(root.transform, false);

            GameObject camGo = new GameObject("Camera");
            camGo.transform.SetParent(rigGo.transform, false);
            Camera cam = camGo.AddComponent<Camera>();
            cam.nearClipPlane = 0.02f;
            camGo.AddComponent<AudioListener>();

            Transform lt = new GameObject("LeftHandTarget").transform;
            Transform rt = new GameObject("RightHandTarget").transform;
            lt.SetParent(rigGo.transform, false);
            rt.SetParent(rigGo.transform, false);

            FirstPersonRig rig = rigGo.AddComponent<FirstPersonRig>();
            rig.headBone = head;
            rig.controller = fcc;
            rig.body = body;
            rig.cam = cam;
            rig.leftHandTarget = lt;
            rig.rightHandTarget = rt;
            rig.Apply(p);

            body.rig = rig;
            if (body.leftHand) body.leftHand.reachTarget = lt;
            if (body.rightHand) body.rightHand.reachTarget = rt;
        }

        static void BuildNpcRig(GameObject root, CharacterBody body, Transform chest)
        {
            GameObject go = new GameObject("NpcHandTargets");
            go.transform.SetParent(root.transform, false);

            Transform lt = new GameObject("LeftHandTarget").transform;
            Transform rt = new GameObject("RightHandTarget").transform;
            lt.SetParent(go.transform, false);
            rt.SetParent(go.transform, false);

            NpcHandTargets n = go.AddComponent<NpcHandTargets>();
            n.chest = chest;
            n.leftTarget = lt;
            n.rightTarget = rt;

            body.npcHands = n;
            if (body.leftHand) body.leftHand.reachTarget = lt;
            if (body.rightHand) body.rightHand.reachTarget = rt;
        }

        static void AddGrabHandles(CharacterBody body, Dictionary<HumanBodyBones, Rigidbody> map, string layer)
        {
            HumanBodyBones[] handles =
            {
                HumanBodyBones.Hips, HumanBodyBones.Spine,
                HumanBodyBones.LeftFoot, HumanBodyBones.RightFoot
            };

            foreach (HumanBodyBones b in handles)
            {
                if (!map.TryGetValue(b, out Rigidbody rb)) continue;
                Grabbable g = rb.gameObject.GetComponent<Grabbable>();
                if (!g) g = Undo.AddComponent<Grabbable>(rb.gameObject);
                g.body = rb;
                g.character = body;
                g.twoHandedMassThreshold = 12f;   // people are awkward on one arm
                g.encumbrance = 1.25f;
                g.projectileMultiplier = 1.4f;
            }
        }

        /// <summary>
        /// The capsule sits inside the torso on the same layer as the bones. Without
        /// per-pair exclusion the solver blasts the limbs apart on the first frame.
        /// CharacterBody redoes this at runtime; doing it here too means the character
        /// also behaves correctly when scrubbed in the editor.
        /// </summary>
        static void IsolateCapsule(Collider capsule, GameObject physical)
        {
            if (!capsule || !physical) return;
            foreach (Collider bone in physical.GetComponentsInChildren<Collider>(true))
                if (bone && bone != capsule) Physics.IgnoreCollision(bone, capsule, true);
        }

        // ------------------------------------------------------------------ helpers

        static float EstimateHeight(Animator a)
        {
            Transform head = a.GetBoneTransform(HumanBodyBones.Head);
            Transform foot = a.GetBoneTransform(HumanBodyBones.LeftFoot);
            if (head && foot) return Mathf.Max(0.6f, (head.position.y - foot.position.y) * 1.12f);
            return 1.8f;
        }

        /// <summary>
        /// The character's standing leg length: hip height above the sole, measured from the
        /// built foot colliders (their real bottom, not the ankle bone). This is the correct
        /// rideHeight -- the height the capsule should hold the hips at so the feet just reach
        /// the ground. Using a fixed human 0.9 on a short-legged or stylized rig holds the
        /// hips too high and curls the body into a banana. Returns -1 if no feet were built.
        /// </summary>
        static float MeasureLegLength(Dictionary<HumanBodyBones, Rigidbody> bodies, Rigidbody hips)
        {
            if (!hips) return -1f;
            float lowest = float.MaxValue;
            foreach (HumanBodyBones b in new[] { HumanBodyBones.LeftFoot, HumanBodyBones.RightFoot })
                if (bodies.TryGetValue(b, out Rigidbody rb) && rb)
                {
                    Collider c = rb.GetComponent<Collider>();
                    if (c) lowest = Mathf.Min(lowest, c.bounds.min.y);
                }
            return lowest < float.MaxValue ? hips.position.y - lowest : -1f;
        }

        static float EstimateLeafLength(Animator a, HumanBodyBones bone)
        {
            Transform t = a.GetBoneTransform(bone);
            if (!t) return 0.12f;
            if (t.childCount > 0) return Mathf.Max(0.05f, Vector3.Distance(t.position, t.GetChild(0).position));
            return Mathf.Max(0.05f, EstimateHeight(a) * 0.07f);
        }

        /// <summary>Adds a layer to the project's TagManager if it isn't already there.</summary>
        public static void EnsureLayer(string name)
        {
            if (string.IsNullOrEmpty(name) || LayerMask.NameToLayer(name) >= 0) return;

            Object asset = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0];
            SerializedObject so = new SerializedObject(asset);
            SerializedProperty layers = so.FindProperty("layers");

            for (int i = 8; i < layers.arraySize; i++)
            {
                SerializedProperty e = layers.GetArrayElementAtIndex(i);
                if (!string.IsNullOrEmpty(e.stringValue)) continue;
                e.stringValue = name;
                so.ApplyModifiedProperties();
                Debug.Log($"[Nasser ARS] Created layer '{name}' at index {i}.");
                return;
            }
            Debug.LogWarning($"[Nasser ARS] No free user layer for '{name}'. Create it manually.");
        }

        public static void ApplyRecommendedPhysics()
        {
            Time.fixedDeltaTime = 1f / 60f;
            Physics.defaultSolverIterations = 15;
            Physics.defaultSolverVelocityIterations = 4;
            Physics.defaultMaxAngularSpeed = 40f;
            Physics.sleepThreshold = 0.005f;
            Debug.Log("[Nasser ARS] Applied 60Hz timestep and high solver iteration counts.");
        }
    }
}
#endif
