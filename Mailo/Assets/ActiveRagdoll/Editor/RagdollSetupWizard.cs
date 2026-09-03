using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace ActiveRagdoll
{
    /// <summary>
    /// Phase 0 setup wizard. Takes any rigged humanoid, builds the two-skeleton active
    /// ragdoll from the spec §1 architecture:
    ///
    ///   Character
    ///   ├── AnimatedRig   (clone; Animator kept; renderers off; no physics)  ← the target
    ///   ├── PhysicalRig   (clone; Animator stripped; Rigidbody + ConfigurableJoint +
    ///   │                  collider per bone; skinned mesh stays visible)     ← chases target
    ///   └── RagdollRig / debug components
    ///
    /// Bones are resolved avatar-first (Animator humanoid Avatar → HumanBodyBones), with a
    /// name-based fallback, so it works regardless of the rig's bone naming (spec rule #6).
    /// All joint drives are left at ZERO — Phase 0's acceptance test is a clean passive
    /// collapse. Phase 1 raises the drives.
    /// </summary>
    public class RagdollSetupWizard : EditorWindow
    {
        GameObject _source;
        RagdollProfile _profile;
        bool _useAvatarMapping = true;
        Vector2 _scroll;

        [MenuItem("Tools/Active Ragdoll/Setup Wizard")]
        static void Open() => GetWindow<RagdollSetupWizard>("Active Ragdoll Setup");

        // ---- Mass table (spec §Phase 0) ------------------------------------
        static readonly Dictionary<BodyPart, float> Mass = new Dictionary<BodyPart, float>
        {
            { BodyPart.Head, 5f }, { BodyPart.Chest, 18f }, { BodyPart.Spine, 8f }, { BodyPart.Hips, 10f },
            { BodyPart.ThighL, 8f }, { BodyPart.ThighR, 8f }, { BodyPart.ShinL, 4f }, { BodyPart.ShinR, 4f },
            { BodyPart.FootL, 1.5f }, { BodyPart.FootR, 1.5f },
            { BodyPart.UpperArmL, 2.5f }, { BodyPart.UpperArmR, 2.5f },
            { BodyPart.LowerArmL, 1.5f }, { BodyPart.LowerArmR, 1.5f },
            { BodyPart.HandL, 0.5f }, { BodyPart.HandR, 0.5f },
        };

        // Bone used to size a limb/torso collider (its distal neighbour along the chain).
        static readonly Dictionary<BodyPart, BodyPart> AxialChild = new Dictionary<BodyPart, BodyPart>
        {
            { BodyPart.Hips, BodyPart.Spine }, { BodyPart.Spine, BodyPart.Chest }, { BodyPart.Chest, BodyPart.Head },
            { BodyPart.UpperArmL, BodyPart.LowerArmL }, { BodyPart.LowerArmL, BodyPart.HandL },
            { BodyPart.UpperArmR, BodyPart.LowerArmR }, { BodyPart.LowerArmR, BodyPart.HandR },
            { BodyPart.ThighL, BodyPart.ShinL }, { BodyPart.ShinL, BodyPart.FootL },
            { BodyPart.ThighR, BodyPart.ShinR }, { BodyPart.ShinR, BodyPart.FootR },
        };

        void OnGUI()
        {
            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            EditorGUILayout.LabelField("Build an active ragdoll from a rigged humanoid.", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Drag a rigged humanoid (ideally a scene instance) into 'Source'. Use a plain " +
                "model (mesh + Animator), not a full gameplay prefab with movement scripts.\n\n" +
                "The wizard leaves all joint drives at ZERO. Phase 0 test = press Play and watch " +
                "it collapse into a clean, non-exploding ragdoll and come to rest.",
                MessageType.Info);

            _source = (GameObject)EditorGUILayout.ObjectField("Source humanoid", _source, typeof(GameObject), true);
            _useAvatarMapping = EditorGUILayout.Toggle(
                new GUIContent("Avatar-first mapping", "Use the humanoid Avatar (HumanBodyBones) to find bones; fall back to names."),
                _useAvatarMapping);
            _profile = (RagdollProfile)EditorGUILayout.ObjectField(
                new GUIContent("Profile (optional)", "Leave empty to auto-find or create one."),
                _profile, typeof(RagdollProfile), false);

            EditorGUILayout.Space();
            using (new EditorGUI.DisabledScope(_source == null))
            {
                if (GUILayout.Button("Build Active Ragdoll", GUILayout.Height(32)))
                    Build();
            }

            EditorGUILayout.EndScrollView();
        }

        void Build()
        {
            if (_source == null) return;

            // A working scene instance we can map + clone from.
            bool sourceIsAsset = !_source.scene.IsValid();
            GameObject working = sourceIsAsset ? (GameObject)PrefabUtility.InstantiatePrefab(_source) : _source;
            if (working == null)
            {
                EditorUtility.DisplayDialog("Active Ragdoll", "Could not instantiate the source prefab.", "OK");
                return;
            }

            var sourceMap = ResolveSourceBones(working);
            if (!sourceMap.ContainsKey(BodyPart.Hips))
            {
                if (sourceIsAsset) DestroyImmediate(working);
                EditorUtility.DisplayDialog("Active Ragdoll",
                    "Could not find a Hips bone. Ensure the model has a humanoid Avatar, or bones named " +
                    "with a standard convention (Mixamo / Unity).", "OK");
                return;
            }

            _profile = EnsureProfile(_profile);

            // --- Build the character hierarchy ---
            string baseName = _source.name;
            var characterGO = new GameObject(baseName + "_ActiveRagdoll");
            Undo.RegisterCreatedObjectUndo(characterGO, "Create Active Ragdoll");
            characterGO.transform.SetPositionAndRotation(working.transform.position, working.transform.rotation);

            var animatedGO = Instantiate(working);
            animatedGO.name = "AnimatedRig";
            animatedGO.transform.SetParent(characterGO.transform, true);

            var physicalGO = Instantiate(working);
            physicalGO.name = "PhysicalRig";
            physicalGO.transform.SetParent(characterGO.transform, true);

            // Map BodyPart -> clone transform via structural index paths (name-collision proof).
            var physMap = new Dictionary<BodyPart, Transform>();
            var animMap = new Dictionary<BodyPart, Transform>();
            foreach (var kv in sourceMap)
            {
                var path = IndexPath(kv.Value, working.transform);
                var pt = Resolve(physicalGO.transform, path);
                var at = Resolve(animatedGO.transform, path);
                if (pt != null) physMap[kv.Key] = pt;
                if (at != null) animMap[kv.Key] = at;
            }

            StripAnimatedRig(animatedGO);
            StripPhysicalRig(physicalGO);

            float height = HeightOf(physMap.Values);
            var physSet = new HashSet<Transform>(physMap.Values);

            // Pass 1: rigidbodies + colliders. AddCollider sizes limbs from _lastPhysMap.
            _lastPhysMap = physMap;
            foreach (var kv in physMap)
                AddBody(kv.Key, kv.Value, height);

            // Pass 2: joints (need parent rigidbodies to exist first).
            var bones = new List<RagdollBone>();
            foreach (var kv in physMap)
            {
                BodyPart part = kv.Key;
                Transform pt = kv.Value;

                var bone = new RagdollBone
                {
                    part = part,
                    physical = pt,
                    target = animMap.TryGetValue(part, out var at) ? at : null,
                    body = pt.GetComponent<Rigidbody>(),
                    startLocalRotation = pt.localRotation,
                };

                if (part != BodyPart.Hips)
                {
                    Rigidbody parentBody = NearestMappedAncestorBody(pt, physicalGO.transform, physSet);
                    if (parentBody != null)
                        bone.joint = AddJoint(part, pt, parentBody);
                }

                bones.Add(bone);
            }

            var rig = characterGO.AddComponent<RagdollRig>();
            rig.profile = _profile;
            rig.animatedRoot = animatedGO.transform;
            rig.physicalRoot = physicalGO.transform;
            rig.bones = bones;
            rig.RebuildLookup();

            characterGO.AddComponent<RagdollDebugDraw>();
            characterGO.AddComponent<RagdollTuningPanel>();
            characterGO.AddComponent<PoseMatcher>();        // Phase 1
            characterGO.AddComponent<BalanceController>();   // Phase 2
            characterGO.AddComponent<PoiseController>();     // Phase 4
            characterGO.AddComponent<RecoveryController>();  // Phase 5a
            characterGO.AddComponent<GrabController>();      // Phase 5c

            // Tidy up the originals so the ragdoll copy doesn't overlap them.
            if (sourceIsAsset)
            {
                DestroyImmediate(working);
            }
            else
            {
                Undo.RegisterFullObjectHierarchyUndo(_source, "Deactivate source");
                _source.SetActive(false);
            }

            Selection.activeGameObject = characterGO;
            EditorGUIUtility.PingObject(characterGO);

            Report(bones, physMap);
        }

        // ---- Bone resolution ------------------------------------------------

        Dictionary<BodyPart, Transform> ResolveSourceBones(GameObject working)
        {
            var map = new Dictionary<BodyPart, Transform>();
            var anim = working.GetComponentInChildren<Animator>();

            if (_useAvatarMapping && anim != null && anim.avatar != null && anim.avatar.isHuman)
            {
                void A(BodyPart part, HumanBodyBones b)
                {
                    var t = anim.GetBoneTransform(b);
                    if (t != null) map[part] = t;
                }
                A(BodyPart.Hips, HumanBodyBones.Hips);
                A(BodyPart.Spine, HumanBodyBones.Spine);
                A(BodyPart.Chest, HumanBodyBones.Chest);
                if (!map.ContainsKey(BodyPart.Chest)) A(BodyPart.Chest, HumanBodyBones.UpperChest);
                A(BodyPart.Head, HumanBodyBones.Head);
                A(BodyPart.UpperArmL, HumanBodyBones.LeftUpperArm);
                A(BodyPart.UpperArmR, HumanBodyBones.RightUpperArm);
                A(BodyPart.LowerArmL, HumanBodyBones.LeftLowerArm);
                A(BodyPart.LowerArmR, HumanBodyBones.RightLowerArm);
                A(BodyPart.HandL, HumanBodyBones.LeftHand);
                A(BodyPart.HandR, HumanBodyBones.RightHand);
                A(BodyPart.ThighL, HumanBodyBones.LeftUpperLeg);
                A(BodyPart.ThighR, HumanBodyBones.RightUpperLeg);
                A(BodyPart.ShinL, HumanBodyBones.LeftLowerLeg);
                A(BodyPart.ShinR, HumanBodyBones.RightLowerLeg);
                A(BodyPart.FootL, HumanBodyBones.LeftFoot);
                A(BodyPart.FootR, HumanBodyBones.RightFoot);
            }

            // Name fallback for anything still missing.
            NameFallback(working.transform, map);
            return map;
        }

        static void NameFallback(Transform root, Dictionary<BodyPart, Transform> map)
        {
            var all = root.GetComponentsInChildren<Transform>();
            foreach (var t in all)
            {
                string n = t.name.ToLowerInvariant();
                if (n.Contains("nub") || n.EndsWith("end") || n.Contains("ik") || n.Contains("twist")) continue;
                bool l = IsLeft(n), r = IsRight(n);

                TryName(map, BodyPart.Hips, t, n.Contains("hips") || n.Contains("pelvis"));
                TryName(map, BodyPart.Head, t, n.Contains("head") && !n.Contains("headtop"));
                TryName(map, BodyPart.Chest, t, n.Contains("chest") || n.Contains("spine2") || n.Contains("upperchest"));
                TryName(map, BodyPart.Spine, t, (n.Contains("spine") && !n.Contains("spine1") && !n.Contains("spine2")) || n == "spine");

                bool fore = n.Contains("fore") || n.Contains("lowerarm");
                TryName(map, BodyPart.UpperArmL, t, l && n.Contains("arm") && !fore && !n.Contains("hand") && !n.Contains("shoulder"));
                TryName(map, BodyPart.UpperArmR, t, r && n.Contains("arm") && !fore && !n.Contains("hand") && !n.Contains("shoulder"));
                TryName(map, BodyPart.LowerArmL, t, l && fore);
                TryName(map, BodyPart.LowerArmR, t, r && fore);
                TryName(map, BodyPart.HandL, t, l && n.Contains("hand") && !HasDigit(n));
                TryName(map, BodyPart.HandR, t, r && n.Contains("hand") && !HasDigit(n));

                bool upleg = n.Contains("upleg") || n.Contains("upperleg") || n.Contains("thigh");
                bool loleg = n.Contains("lowerleg") || n.Contains("calf") || n.Contains("shin") ||
                             (n.Contains("leg") && !upleg);
                TryName(map, BodyPart.ThighL, t, l && upleg);
                TryName(map, BodyPart.ThighR, t, r && upleg);
                TryName(map, BodyPart.ShinL, t, l && loleg);
                TryName(map, BodyPart.ShinR, t, r && loleg);
                TryName(map, BodyPart.FootL, t, l && n.Contains("foot") && !n.Contains("toe"));
                TryName(map, BodyPart.FootR, t, r && n.Contains("foot") && !n.Contains("toe"));
            }
        }

        static void TryName(Dictionary<BodyPart, Transform> map, BodyPart part, Transform t, bool match)
        {
            if (match && !map.ContainsKey(part)) map[part] = t;
        }

        static bool IsLeft(string n) => n.Contains("left") || n.EndsWith("_l") || n.EndsWith(".l") ||
                                        n.EndsWith(" l") || n.EndsWith("-l") || n.StartsWith("l_");
        static bool IsRight(string n) => n.Contains("right") || n.EndsWith("_r") || n.EndsWith(".r") ||
                                         n.EndsWith(" r") || n.EndsWith("-r") || n.StartsWith("r_");
        static bool HasDigit(string n) => n.Any(char.IsDigit);

        // ---- Physics construction ------------------------------------------

        void AddBody(BodyPart part, Transform t, float height)
        {
            var rb = t.GetComponent<Rigidbody>();
            if (rb == null) rb = t.gameObject.AddComponent<Rigidbody>();
            rb.mass = Mass.TryGetValue(part, out var m) ? m : 1f;
            rb.linearDamping = 0.05f;
            rb.angularDamping = 0.05f;
            rb.useGravity = true;
            rb.isKinematic = false;
            rb.interpolation = part == BodyPart.Hips ? RigidbodyInterpolation.Interpolate : RigidbodyInterpolation.None;
            rb.collisionDetectionMode = CollisionDetectionMode.Discrete;
            rb.maxAngularVelocity = 30f;   // spec: default 7 is far too low
            rb.solverIterations = 20;

            AddCollider(part, t, height);
        }

        void AddCollider(BodyPart part, Transform t, float height)
        {
            foreach (var c in t.GetComponents<Collider>()) DestroyImmediate(c);

            float torsoR = height * 0.09f;
            float limbR = height * 0.045f;
            float headR = height * 0.075f;

            bool hasChild = AxialChild.TryGetValue(part, out var childPart);
            Transform childT = null;
            if (hasChild && _lastPhysMap != null) _lastPhysMap.TryGetValue(childPart, out childT);

            switch (part)
            {
                case BodyPart.Head:
                    var sc = t.gameObject.AddComponent<SphereCollider>();
                    sc.radius = headR;
                    sc.center = t.InverseTransformDirection(Vector3.up) * headR;
                    break;

                case BodyPart.Hips:
                case BodyPart.Chest:
                    var bc = t.gameObject.AddComponent<BoxCollider>();
                    if (childT != null)
                    {
                        Vector3 cl = t.InverseTransformPoint(childT.position);
                        int ax = DominantAxis(cl);
                        float len = Mathf.Max(cl.magnitude, torsoR);
                        Vector3 size = Vector3.one * (torsoR * 2f);
                        size[ax] = len;
                        bc.size = size;
                        bc.center = cl * 0.5f;
                    }
                    else bc.size = Vector3.one * (torsoR * 2f);
                    break;

                case BodyPart.FootL:
                case BodyPart.FootR:
                    var fb = t.gameObject.AddComponent<BoxCollider>();
                    fb.size = new Vector3(limbR * 1.6f, limbR * 1.1f, limbR * 3.2f);
                    fb.center = t.InverseTransformDirection(Vector3.down) * limbR * 0.5f;
                    break;

                case BodyPart.HandL:
                case BodyPart.HandR:
                    var hb = t.gameObject.AddComponent<BoxCollider>();
                    hb.size = Vector3.one * (limbR * 1.6f);
                    break;

                default: // limbs -> capsule from bone toward its axial child
                    var cap = t.gameObject.AddComponent<CapsuleCollider>();
                    cap.radius = limbR;
                    if (childT != null)
                    {
                        Vector3 cl = t.InverseTransformPoint(childT.position);
                        cap.direction = DominantAxis(cl);
                        cap.height = Mathf.Max(cl.magnitude, limbR * 2.1f);
                        cap.center = cl * 0.5f;
                    }
                    else cap.height = limbR * 4f;
                    break;
            }
        }

        ConfigurableJoint AddJoint(BodyPart part, Transform t, Rigidbody connectedTo)
        {
            var j = t.gameObject.AddComponent<ConfigurableJoint>();
            j.connectedBody = connectedTo;
            j.configuredInWorldSpace = false;
            j.autoConfigureConnectedAnchor = true;
            j.anchor = Vector3.zero;
            j.swapBodies = false;

            j.xMotion = j.yMotion = j.zMotion = ConfigurableJointMotion.Locked;
            j.angularXMotion = j.angularYMotion = j.angularZMotion = ConfigurableJointMotion.Limited;

            var (lowX, highX, y, z) = LimitsFor(part);
            j.lowAngularXLimit = new SoftJointLimit { limit = lowX };
            j.highAngularXLimit = new SoftJointLimit { limit = highX };
            j.angularYLimit = new SoftJointLimit { limit = y };
            j.angularZLimit = new SoftJointLimit { limit = z };

            // Slerp drive present but ZERO for Phase 0 — Phase 1 raises spring/damper.
            j.rotationDriveMode = RotationDriveMode.Slerp;
            j.slerpDrive = new JointDrive { positionSpring = 0f, positionDamper = 0f, maximumForce = _profile.maxForce };
            j.targetRotation = Quaternion.identity;

            // Stability against joint separation / blow-ups (spec §7).
            j.enablePreprocessing = false;
            j.projectionMode = JointProjectionMode.PositionAndRotation;
            j.projectionDistance = 0.1f;
            j.projectionAngle = 20f;
            j.enableCollision = false; // adjacent (connected) bones never collide
            return j;
        }

        static (float lowX, float highX, float y, float z) LimitsFor(BodyPart part)
        {
            switch (part)
            {
                case BodyPart.Spine:
                case BodyPart.Chest: return (-20f, 20f, 20f, 20f);
                case BodyPart.Head: return (-30f, 30f, 30f, 30f);
                case BodyPart.UpperArmL:
                case BodyPart.UpperArmR: return (-60f, 60f, 60f, 45f);
                case BodyPart.LowerArmL:
                case BodyPart.LowerArmR: return (-70f, 70f, 8f, 8f);   // elbow (symmetric: sign-agnostic)
                case BodyPart.HandL:
                case BodyPart.HandR: return (-30f, 30f, 20f, 20f);
                case BodyPart.ThighL:
                case BodyPart.ThighR: return (-50f, 50f, 40f, 25f);
                case BodyPart.ShinL:
                case BodyPart.ShinR: return (-80f, 80f, 8f, 8f);       // knee
                case BodyPart.FootL:
                case BodyPart.FootR: return (-30f, 30f, 15f, 15f);
                default: return (-20f, 20f, 20f, 20f);
            }
        }

        // ---- Hierarchy helpers ---------------------------------------------

        Dictionary<BodyPart, Transform> _lastPhysMap; // used by AddCollider for axial-child sizing

        static Rigidbody NearestMappedAncestorBody(Transform t, Transform stopAt, HashSet<Transform> mapped)
        {
            var p = t.parent;
            while (p != null && p != stopAt.parent)
            {
                if (mapped.Contains(p)) return p.GetComponent<Rigidbody>();
                p = p.parent;
            }
            return null;
        }

        static int DominantAxis(Vector3 v)
        {
            v = new Vector3(Mathf.Abs(v.x), Mathf.Abs(v.y), Mathf.Abs(v.z));
            if (v.x >= v.y && v.x >= v.z) return 0;
            return v.y >= v.z ? 1 : 2;
        }

        static float HeightOf(IEnumerable<Transform> ts)
        {
            float min = float.MaxValue, max = float.MinValue;
            foreach (var t in ts) { min = Mathf.Min(min, t.position.y); max = Mathf.Max(max, t.position.y); }
            float h = max - min;
            return h > 0.01f ? h : 1.7f;
        }

        static List<int> IndexPath(Transform node, Transform root)
        {
            var list = new List<int>();
            var t = node;
            while (t != null && t != root) { list.Add(t.GetSiblingIndex()); t = t.parent; }
            list.Reverse();
            return list;
        }

        static Transform Resolve(Transform root, List<int> path)
        {
            var t = root;
            foreach (var i in path)
            {
                if (t == null || i < 0 || i >= t.childCount) return null;
                t = t.GetChild(i);
            }
            return t;
        }

        static void StripAnimatedRig(GameObject go)
        {
            foreach (var r in go.GetComponentsInChildren<Renderer>(true)) r.enabled = false;
            foreach (var c in go.GetComponentsInChildren<Collider>(true)) DestroyImmediate(c);
            foreach (var j in go.GetComponentsInChildren<Joint>(true)) DestroyImmediate(j);
            foreach (var rb in go.GetComponentsInChildren<Rigidbody>(true)) DestroyImmediate(rb);
            // Animator is kept — it is the pose source for Phase 1.
        }

        static void StripPhysicalRig(GameObject go)
        {
            foreach (var a in go.GetComponentsInChildren<Animator>(true)) DestroyImmediate(a);
            foreach (var j in go.GetComponentsInChildren<Joint>(true)) DestroyImmediate(j);
            foreach (var rb in go.GetComponentsInChildren<Rigidbody>(true)) DestroyImmediate(rb);
            // Renderers stay — the physical rig is what you see.
        }

        // ---- Profile + report ----------------------------------------------

        static RagdollProfile EnsureProfile(RagdollProfile given)
        {
            if (given != null) return given;

            var found = AssetDatabase.FindAssets("t:RagdollProfile");
            if (found.Length > 0)
                return AssetDatabase.LoadAssetAtPath<RagdollProfile>(AssetDatabase.GUIDToAssetPath(found[0]));

            const string dir = "Assets/ActiveRagdoll";
            if (!AssetDatabase.IsValidFolder(dir)) AssetDatabase.CreateFolder("Assets", "ActiveRagdoll");
            var profile = CreateInstance<RagdollProfile>();
            AssetDatabase.CreateAsset(profile, dir + "/RagdollProfile.asset");
            AssetDatabase.SaveAssets();
            return profile;
        }

        static void Report(List<RagdollBone> bones, Dictionary<BodyPart, Transform> physMap)
        {
            float total = bones.Where(b => b.body != null).Sum(b => b.body.mass);
            var missing = System.Enum.GetValues(typeof(BodyPart)).Cast<BodyPart>()
                .Where(p => !physMap.ContainsKey(p)).ToList();

            var sb = new StringBuilder();
            sb.AppendLine($"[Active Ragdoll] Built {bones.Count}/16 bones, total mass {total:0.0} kg.");
            if (missing.Count > 0)
                sb.AppendLine($"[Active Ragdoll] Unmapped parts (check rig/names): {string.Join(", ", missing)}");
            sb.AppendLine("[Active Ragdoll] Phase 0 test: press Play. All drives are zero — it should " +
                          "collapse into a clean, non-exploding ragdoll and come to rest.");
            Debug.Log(sb.ToString());
        }
    }
}
