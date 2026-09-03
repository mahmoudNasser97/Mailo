using UnityEngine;

namespace ActiveRagdoll
{
    /// <summary>
    /// Runtime IMGUI tuning panel (spec §6). Toggle with F1. Shows the live readouts that
    /// matter this phase (mass, CoM height, speed, time scale) and edits the shared
    /// <see cref="RagdollProfile"/> asset directly, so tuning survives play-mode exit.
    ///
    /// Later phases add their own readouts (poise, tier, capture-point distance). The
    /// slider set already covers §8 because those values live on the profile now.
    /// </summary>
    [RequireComponent(typeof(RagdollRig))]
    public class RagdollTuningPanel : MonoBehaviour
    {
        public KeyCode toggleKey = KeyCode.F1;
        public bool visible = false;

        RagdollRig _rig;
        Vector2 _scroll;
        Rect _window = new Rect(12, 12, 340, 560);

        void Awake() => _rig = GetComponent<RagdollRig>();

        void Update()
        {
            if (Input.GetKeyDown(toggleKey)) visible = !visible;
        }

        void OnGUI()
        {
            if (!visible) return;
            _window = GUILayout.Window(GetInstanceID(), _window, DrawWindow, "Active Ragdoll — Tuning (F1)");
        }

        void DrawWindow(int id)
        {
            _scroll = GUILayout.BeginScrollView(_scroll);

            // --- Live readouts ---
            GUILayout.Label("<b>Live</b>", Rich);
            if (_rig != null)
            {
                Vector3 com = _rig.CenterOfMass;
                GUILayout.Label($"Total mass:  {_rig.TotalMass:0.0} kg");
                GUILayout.Label($"CoM height:  {com.y:0.000} m");
                GUILayout.Label($"CoM speed:   {_rig.CenterOfMassVelocity.magnitude:0.00} m/s");
                GUILayout.Label($"Kinetic E:   {_rig.TotalKineticEnergy:0.0} J");

                var pc = _rig.GetComponent<PoiseController>();
                if (pc != null)
                    GUILayout.Label($"<b>Poise:  {pc.Poise:0.00}   [{pc.Tier}]   gain {pc.Gain:0.00}</b>", Rich);
            }
            GUILayout.Label($"Time scale:  {Time.timeScale:0.00}");

            GUILayout.Space(8);

            // --- Pose matching (Phase 1) ---
            var pm = _rig != null ? _rig.GetComponent<PoseMatcher>() : null;
            if (pm != null)
            {
                Section("Pose Matching (Phase 1)");
                pm.suspendHips = GUILayout.Toggle(pm.suspendHips, " Suspend hips (in-air test)");
                pm.poise = Slider("poise", pm.poise, 0f, 1f);
            }

            var p = _rig != null ? _rig.profile : null;
            if (p == null)
            {
                GUILayout.Label("No RagdollProfile assigned on the rig.");
                GUILayout.EndScrollView();
                GUI.DragWindow();
                return;
            }

            Section("Drives");
            p.springHips = Slider("springHips", p.springHips, 0, 6000);
            p.springSpine = Slider("springSpine", p.springSpine, 0, 6000);
            p.springLegs = Slider("springLegs", p.springLegs, 0, 6000);
            p.springFeet = Slider("springFeet", p.springFeet, 0, 6000);
            p.springArms = Slider("springArms", p.springArms, 0, 3000);
            p.damperScale = Slider("damperScale", p.damperScale, 0, 0.3f);
            p.maxForce = Slider("maxForce", p.maxForce, 0, 200000);

            Section("Balance (Phase 2)");
            var bal = _rig.GetComponent<BalanceController>();
            if (bal != null)
            {
                bal.plantFeet = GUILayout.Toggle(bal.plantFeet, " Plant legs (Phase 2 rigid stance)");
                bal.enableStepping = GUILayout.Toggle(bal.enableStepping, " Enable stepping (needs Plant legs OFF)");
                bal.pinPelvis = GUILayout.Toggle(bal.pinPelvis, " Pin pelvis (validation: legs step, pelvis fixed)");
                bal.marchTest = GUILayout.Toggle(bal.marchTest, " March in place (validation: verify IK/knees)");
                bal.flipKnees = GUILayout.Toggle(bal.flipKnees, " Flip knees (if they bend backwards)");
                bal.supportHips = GUILayout.Toggle(bal.supportHips, " Support hips (only when Pin pelvis OFF)");
                bal.supportFraction = Slider("  crutch (1=held, 0=physical)", bal.supportFraction, 0f, 1f);
                string cap = !bal.Grounded ? "airborne"
                    : (bal.CaptureInside ? $"INSIDE ({-bal.CaptureSignedDistance:0.00} m margin)"
                                         : $"OUTSIDE ({bal.CaptureSignedDistance:0.00} m)");
                GUILayout.Label($"Grounded: {bal.Grounded}    Capture: {cap}");
                GUILayout.Label($"Step requested: {(bal.StepRequested ? "YES" : "no")}");
                bal.footPlantForce = Slider("footPlant grip", bal.footPlantForce, 0f, 2000f);
            }
            p.hipKp = Slider("hipKp", p.hipKp, 0, 2000);
            p.hipKd = Slider("hipKd", p.hipKd, 0, 400);
            p.ankleKp = Slider("ankleKp", p.ankleKp, 0, 1000);
            p.gravityCompensation = Slider("gravityComp", p.gravityCompensation, 0, 1);

            Section("Stepping (Phase 3)");
            p.stepDuration = Slider("stepDuration", p.stepDuration, 0.1f, 0.6f);
            p.stepHeight = Slider("stepHeight", p.stepHeight, 0, 0.4f);
            p.maxStanceOffset = Slider("maxStanceOffset", p.maxStanceOffset, 0, 0.6f);

            Section("Impact (Phase 4)");
            p.ignoreThreshold = Slider("ignoreThreshold", p.ignoreThreshold, 0, 20);
            p.maxImpulse = Slider("maxImpulse", p.maxImpulse, 0, 200);
            p.poiseCapacity = Slider("poiseCapacity", p.poiseCapacity, 1, 100);
            p.spinWeight = Slider("spinWeight", p.spinWeight, 0, 3);
            p.regenRate = Slider("regenRate", p.regenRate, 0, 3);

            Section("Recovery (Phase 5)");
            p.restEnergyThreshold = Slider("restEnergy", p.restEnergyThreshold, 0, 30);
            p.getUpRampDuration = Slider("getUpRamp", p.getUpRampDuration, 0.2f, 2f);

            GUILayout.EndScrollView();
            GUI.DragWindow();
        }

        // --- helpers ---
        static GUIStyle _rich;
        static GUIStyle Rich => _rich ??= new GUIStyle(GUI.skin.label) { richText = true };

        static void Section(string title)
        {
            GUILayout.Space(6);
            GUILayout.Label($"<b>{title}</b>", Rich);
        }

        static float Slider(string label, float value, float min, float max)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, GUILayout.Width(130));
            value = GUILayout.HorizontalSlider(value, min, max);
            GUILayout.Label(value.ToString("0.###"), GUILayout.Width(60));
            GUILayout.EndHorizontal();
            return value;
        }
    }
}
