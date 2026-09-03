#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace NasserActiveRagdoll.EditorTools
{
    /// <summary>
    /// Tools > Active Ragdoll > Character Setup.
    ///
    /// Drop in a humanoid model, assign a clip set, pick Player or NPC, press Build.
    /// You get the physical rig, the animated puppet, the floating capsule, both physics
    /// hands, grab handles, a camera rig or NPC targets, a driver, and a generated
    /// animator graph with nested blend trees -- wired and ready to press Play.
    /// </summary>
    public class CharacterSetupWizard : EditorWindow
    {
        GameObject _model;
        CharacterRole _role = CharacterRole.Npc;
        RagdollProfile _profile;
        LocomotionClipSet _clips;

        bool _generateController = true;
        RuntimeAnimatorController _existingController;

        bool _buildLayers = true, _grabHandles = true, _addDriver = true, _applyPhysics = true;
        bool _showClips = true;
        Vector2 _scroll;
        string _status;
        MessageType _statusType = MessageType.Info;

        // Foot-measured depicted ground speeds of the walk/run clips, filled in BuildNow and
        // consumed by ApplyAnimationTuning to set referenceClipSpeed and maxSpeed.
        float _walkStride, _runStride;
        Editor _clipEditor;

        [MenuItem("Tools/Nasser Active Ragdoll System/Character Setup %#r")]
        public static void Open()
        {
            CharacterSetupWizard w = GetWindow<CharacterSetupWizard>("Nasser ARS");
            w.minSize = new Vector2(380, 560);
        }

        [MenuItem("Tools/Nasser Active Ragdoll System/Apply Recommended Physics Settings")]
        static void PhysicsMenu() => RagdollBuilder.ApplyRecommendedPhysics();

        void OnDisable() { if (_clipEditor) DestroyImmediate(_clipEditor); }

        void OnGUI()
        {
            _scroll = EditorGUILayout.BeginScrollView(_scroll);

            // ---------------------------------------------------------- character
            EditorGUILayout.LabelField("Character", EditorStyles.boldLabel);
            EditorGUI.BeginChangeCheck();
            _model = (GameObject)EditorGUILayout.ObjectField(
                new GUIContent("Humanoid model", "A model in the scene, imported with Rig > Humanoid."),
                _model, typeof(GameObject), true);
            if (EditorGUI.EndChangeCheck()) Revalidate();

            _role = (CharacterRole)EditorGUILayout.EnumPopup(
                new GUIContent("Role", "Player gets a first-person camera rig. NPC gets chest-parented hand targets."),
                _role);

            using (new EditorGUILayout.HorizontalScope())
            {
                _profile = (RagdollProfile)EditorGUILayout.ObjectField(
                    new GUIContent("Profile", "Shared tuning asset. Empty uses built-in defaults."),
                    _profile, typeof(RagdollProfile), false);
                if (!_profile && GUILayout.Button("New", GUILayout.Width(46)))
                    _profile = CreateAsset<RagdollProfile>("RagdollProfile");
            }

            // ---------------------------------------------------------- animation
            EditorGUILayout.Space(10);
            EditorGUILayout.LabelField("Animation", EditorStyles.boldLabel);

            _generateController = EditorGUILayout.Toggle(
                new GUIContent("Generate graph", "Builds nested blend trees, airborne states, get-ups and a masked upper-body layer."),
                _generateController);

            if (_generateController) DrawClipSection();
            else
            {
                _existingController = (RuntimeAnimatorController)EditorGUILayout.ObjectField(
                    "Controller", _existingController, typeof(RuntimeAnimatorController), false);
                EditorGUILayout.HelpBox(
                    "Your controller needs floats Speed, MoveX, MoveY; bools Grounded, Carrying; " +
                    "and states named Locomotion, GetUpProne, GetUpSupine.", MessageType.None);
            }

            // ---------------------------------------------------------- options
            EditorGUILayout.Space(10);
            EditorGUILayout.LabelField("Options", EditorStyles.boldLabel);
            _buildLayers = EditorGUILayout.Toggle(new GUIContent("Create layers", "Adds Character and Grabbable layers if missing."), _buildLayers);
            _grabHandles = EditorGUILayout.Toggle(new GUIContent("Grab handles", "Lets other characters pick this one up by hips, chest or ankles."), _grabHandles);
            _addDriver = EditorGUILayout.Toggle(new GUIContent("Add driver", "PlayerDriver or NpcDriver depending on role."), _addDriver);
            _applyPhysics = EditorGUILayout.Toggle(new GUIContent("Apply physics settings", "60Hz timestep and high solver iterations, project-wide."), _applyPhysics);

            // ---------------------------------------------------------- build
            EditorGUILayout.Space(12);
            if (!string.IsNullOrEmpty(_status)) EditorGUILayout.HelpBox(_status, _statusType);

            EditorGUI.BeginDisabledGroup(_model == null || _statusType == MessageType.Error);
            GUI.backgroundColor = new Color(0.55f, 0.78f, 0.6f);
            if (GUILayout.Button("Build character", GUILayout.Height(36))) BuildNow();
            GUI.backgroundColor = Color.white;
            EditorGUI.EndDisabledGroup();

            EditorGUILayout.Space(6);
            EditorGUILayout.HelpBox(
                "After building:\n" +
                "1. Press Play. The character should stand and stagger.\n" +
                "2. Tune the pelvis spring first — everything else depends on it.\n" +
                "3. Add Grabbable to props so the hands have something to hold.",
                MessageType.None);

            EditorGUILayout.EndScrollView();
        }

        void DrawClipSection()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUI.BeginChangeCheck();
                _clips = (LocomotionClipSet)EditorGUILayout.ObjectField(
                    new GUIContent("Clip set", "Reusable asset holding every clip. Assign once, build many characters."),
                    _clips, typeof(LocomotionClipSet), false);
                if (EditorGUI.EndChangeCheck() && _clipEditor) { DestroyImmediate(_clipEditor); _clipEditor = null; }

                if (!_clips && GUILayout.Button("New", GUILayout.Width(46)))
                    _clips = CreateAsset<LocomotionClipSet>("LocomotionClips");
            }

            if (!_clips)
            {
                EditorGUILayout.HelpBox("Create or assign a clip set to generate the graph.", MessageType.Info);
                return;
            }

            _showClips = EditorGUILayout.Foldout(_showClips, "Clips", true);
            if (_showClips)
            {
                if (!_clipEditor) _clipEditor = Editor.CreateEditor(_clips);
                EditorGUI.indentLevel++;
                _clipEditor.OnInspectorGUI();
                EditorGUI.indentLevel--;
            }

            string audit = _clips.Audit();
            EditorGUILayout.HelpBox(
                audit != null ? "Will still build, but:\n" + audit
                              : "Full clip set. You'll get 8-way walk and run blending.",
                audit != null ? MessageType.Warning : MessageType.Info);

            if (GUILayout.Button(new GUIContent("Fix clip import settings",
                "Sets loop time, locks root rotation and height, and leaves XZ root motion unbaked so thresholds can be measured.")))
            {
                int n = AnimatorGraphBuilder.FixClipImportSettings(_clips);
                _status = $"Reimported {n} animation file(s).";
                _statusType = MessageType.Info;
            }
        }

        // ------------------------------------------------------------------ build

        void BuildNow()
        {
            if (_applyPhysics) RagdollBuilder.ApplyRecommendedPhysics();

            RuntimeAnimatorController controller = _existingController;

            // Measure how fast each locomotion clip DEPICTS the ground moving, from the feet (works
            // for in-place Mixamo clips that have no root motion). These feed both the blend
            // thresholds and referenceClipSpeed / maxSpeed, so the body travels exactly as fast as
            // the feet step -- the cure for the "torso outruns the legs, feet slide" symptom.
            _walkStride = _clips ? AnimatorGraphBuilder.MeasureDepictedSpeed(_model, _clips.walkForward) : 0f;
            _runStride = _clips ? AnimatorGraphBuilder.MeasureDepictedSpeed(_model, _clips.runForward) : 0f;

            if (_generateController && _clips)
            {
                string path = EditorUtility.SaveFilePanelInProject(
                    "Save animator controller", _model.name + "_Ragdoll", "controller",
                    "Where should the generated animator graph go?");
                if (string.IsNullOrEmpty(path)) return;
                controller = AnimatorGraphBuilder.Build(_clips, path, _walkStride, _runStride);
            }

            RagdollBuilder.Options o = new RagdollBuilder.Options
            {
                model = _model,
                role = _role,
                profile = _profile,
                controller = controller,
                buildLayers = _buildLayers,
                addGrabHandles = _grabHandles,
                addDriver = _addDriver
            };

            GameObject built = RagdollBuilder.Build(o);
            if (built == null) return;

            ApplyAnimationTuning(built);

            _status = $"Built '{built.name}'. Press Play.";
            _statusType = MessageType.Info;
            _model = null;
            Selection.activeGameObject = built;
            EditorGUIUtility.PingObject(built);
        }

        /// <summary>
        /// Pushes the clip set's smoothing values onto the character and measures the
        /// walk clip's real root speed, so playback-rate sync has a correct reference.
        /// Getting this number wrong is the difference between planted feet and skating.
        /// </summary>
        void ApplyAnimationTuning(GameObject built)
        {
            if (!_clips) return;
            CharacterBody body = built.GetComponent<CharacterBody>();
            if (!body) return;

            body.speedDamping = _clips.speedDamping;
            body.directionDamping = _clips.directionDamping;

            // referenceClipSpeed = the walk clip's depicted stride speed. The foot measurement
            // (works for in-place clips) is authoritative; fall back to root motion, then the
            // asset's guess. This is the number that makes the feet plant instead of skate.
            float reference = _walkStride;
            if (reference <= 0.05f && _clips.walkForward)
            {
                Vector3 avg = _clips.walkForward.averageSpeed;
                float rootMeasured = new Vector2(avg.x, avg.z).magnitude;
                reference = rootMeasured > 0.05f ? rootMeasured : _clips.walkSpeed;
            }
            if (reference <= 0.05f) reference = _clips.walkSpeed;
            body.referenceClipSpeed = reference;

            // Cap the capsule's top speed to what the clips actually depict, so the torso cannot
            // outrun the legs. Prefer the run stride; with no run clip, allow a little over walk
            // pace (the playback sync stretches the walk that far without visible sliding). maxSpeed
            // is per-clip-set like rideHeight, so CharacterBody.ApplyProfile no longer overwrites it.
            float top = _runStride > 0.05f ? _runStride
                      : (reference > 0.05f ? reference * 1.25f : 0f);
            if (top > 0.05f && body.controller)
            {
                body.controller.maxSpeed = top;
                EditorUtility.SetDirty(body.controller);
            }

            EditorUtility.SetDirty(body);
        }

        // ------------------------------------------------------------------ helpers

        void Revalidate()
        {
            if (!_model) { _status = null; _statusType = MessageType.Info; return; }
            string problem = RagdollBuilder.Validate(_model);
            if (problem == null)
            {
                Animator a = _model.GetComponent<Animator>();
                _status = $"Humanoid rig detected. Estimated height {Height(a):0.00} m.";
                _statusType = MessageType.Info;
            }
            else
            {
                _status = problem;
                _statusType = MessageType.Error;
            }
        }

        static float Height(Animator a)
        {
            Transform h = a.GetBoneTransform(HumanBodyBones.Head);
            Transform f = a.GetBoneTransform(HumanBodyBones.LeftFoot);
            return (h && f) ? (h.position.y - f.position.y) * 1.12f : 1.8f;
        }

        static T CreateAsset<T>(string defaultName) where T : ScriptableObject
        {
            string path = EditorUtility.SaveFilePanelInProject(
                "Create asset", defaultName, "asset", "Where should it live?");
            if (string.IsNullOrEmpty(path)) return null;

            T a = CreateInstance<T>();
            AssetDatabase.CreateAsset(a, path);
            AssetDatabase.SaveAssets();
            return a;
        }
    }
}
#endif
