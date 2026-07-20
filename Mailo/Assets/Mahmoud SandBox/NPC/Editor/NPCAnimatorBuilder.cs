using System;
using UnityEngine;
using UnityEditor;
using UnityEditor.Animations;

/// <summary>
/// Builds a dedicated AnimatorController for the Advanced NPC system.
/// States: Idle → Walk → Run, with Throw and HitReact interrupts.
/// Tries to auto-find animation clips already in the project.
/// </summary>
public static class NPCAnimatorBuilder
{
    public const string ControllerPath = "Assets/Mahmoud SandBox/NPC/NPCAnimatorController.controller";

    [MenuItem("Tools/Advanced NPC/Create NPC Animator Controller")]
    public static void BuildFromMenu()
    {
        var ctrl = Build();
        AssetDatabase.Refresh();
        Selection.activeObject = ctrl;
        EditorGUIUtility.PingObject(ctrl);
    }

    /// <summary>
    /// Creates (or overwrites) the NPC AnimatorController asset and returns it.
    /// Safe to call from other Editor scripts.
    /// </summary>
    public static AnimatorController Build()
    {
        var controller = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);

        // ── Parameters ─────────────────────────────────────────────────────
        controller.AddParameter("Speed",    AnimatorControllerParameterType.Float);
        controller.AddParameter("Throw",    AnimatorControllerParameterType.Trigger);
        controller.AddParameter("HitReact", AnimatorControllerParameterType.Trigger);

        var sm = controller.layers[0].stateMachine;

        // ── States ──────────────────────────────────────────────────────────
        // NPCPatroller sets Speed = patrol / 4  → 2.5/4 = 0.625
        // NPCChaser    sets Speed = 1.0
        // Threshold between walk and run: 0.8  (between 0.625 and 1.0)

        var idle     = sm.AddState("Idle");
        var walk     = sm.AddState("Walk");
        var run      = sm.AddState("Run");
        var throwSt  = sm.AddState("Throw");
        var hitReact = sm.AddState("HitReact");

        sm.defaultState = idle;

        // Visual positions in the Animator window
        sm.entryPosition    = new Vector3(-250,  10);
        sm.anyStatePosition = new Vector3(-250,  80);
        sm.exitPosition     = new Vector3(-250, 150);
        idle.position       = new Vector3(  50,   0);
        walk.position       = new Vector3( 300,   0);
        run.position        = new Vector3( 550,   0);
        throwSt.position    = new Vector3( 300, 160);
        hitReact.position   = new Vector3( 550, 160);

        // ── Assign clips ────────────────────────────────────────────────────
        idle.motion     = FindClip(
            "Idle", "idle", "T-Pose", "T Pose",
            "Standing Idle", "Standing", "Breathing Idle");

        walk.motion     = FindClipAtPath(
            "Assets/Mahmoud SandBox/Walk.anim",
            "Walk", "Walking", "Slow Walk", "SlowWalk");

        run.motion      = FindClip(
            "Run", "Running", "Jog", "Jogging",
            "Fast Run", "Sprint");

        throwSt.motion  = FindClipAtPath(
            "Assets/Mahmoud SandBox/Models/Goalie Throw.fbx",
            "Throw", "Throwing", "Goalie Throw", "Overhand Throw",
            "Swinging");

        hitReact.motion = FindClip(
            "HitReact", "Hit Reaction", "HitReaction",
            "Getting Hit", "Reaction", "Hit");

        // ── Transitions ─────────────────────────────────────────────────────

        // Idle ↔ Walk
        MakeTransition(idle, walk,  AnimatorConditionMode.Greater, 0.1f, "Speed", false, 0.2f);
        MakeTransition(walk, idle,  AnimatorConditionMode.Less,    0.1f, "Speed", false, 0.2f);

        // Walk ↔ Run
        MakeTransition(walk, run,   AnimatorConditionMode.Greater, 0.8f, "Speed", false, 0.2f);
        MakeTransition(run,  walk,  AnimatorConditionMode.Less,    0.8f, "Speed", false, 0.2f);

        // Any → HitReact  (added first = highest any-state priority)
        var anyHit = sm.AddAnyStateTransition(hitReact);
        anyHit.AddCondition(AnimatorConditionMode.If, 0, "HitReact");
        anyHit.hasExitTime         = false;
        anyHit.duration            = 0.05f;
        anyHit.canTransitionToSelf = false;

        // HitReact → Idle
        var hitExit = hitReact.AddTransition(idle);
        hitExit.hasExitTime = true;
        hitExit.exitTime    = 0.85f;
        hitExit.duration    = 0.15f;

        // Any → Throw
        var anyThrow = sm.AddAnyStateTransition(throwSt);
        anyThrow.AddCondition(AnimatorConditionMode.If, 0, "Throw");
        anyThrow.hasExitTime         = false;
        anyThrow.duration            = 0.05f;
        anyThrow.canTransitionToSelf = false;

        // Throw → Idle (returns to locomotion after clip finishes)
        var throwExit = throwSt.AddTransition(idle);
        throwExit.hasExitTime = true;
        throwExit.exitTime    = 0.85f;
        throwExit.duration    = 0.15f;

        // ── Save ────────────────────────────────────────────────────────────
        EditorUtility.SetDirty(controller);
        AssetDatabase.SaveAssets();

        LogResult(idle, walk, run, throwSt, hitReact);
        return controller;
    }

    // ── Clip search helpers ─────────────────────────────────────────────────

    /// <summary>
    /// Tries an explicit path first (for known assets), then falls back to name search.
    /// </summary>
    static AnimationClip FindClipAtPath(string primaryPath, params string[] fallbackNames)
    {
        if (!string.IsNullOrEmpty(primaryPath))
        {
            var clip = LoadFirstClipFromPath(primaryPath);
            if (clip != null) return clip;
        }
        return FindClip(fallbackNames);
    }

    /// <summary>Loads the first AnimationClip embedded in an asset at a given path.</summary>
    static AnimationClip LoadFirstClipFromPath(string path)
    {
        var assets = AssetDatabase.LoadAllAssetsAtPath(path);
        foreach (var a in assets)
            if (a is AnimationClip c && !c.name.StartsWith("__preview__"))
                return c;
        return null;
    }

    /// <summary>Searches the whole project for an AnimationClip matching any of the given names.</summary>
    static AnimationClip FindClip(params string[] names)
    {
        foreach (string name in names)
        {
            string[] guids = AssetDatabase.FindAssets($"{name} t:AnimationClip");
            foreach (string guid in guids)
            {
                string path   = AssetDatabase.GUIDToAssetPath(guid);
                var    assets = AssetDatabase.LoadAllAssetsAtPath(path);
                foreach (var a in assets)
                {
                    if (a is AnimationClip clip &&
                        !clip.name.StartsWith("__preview__") &&
                        clip.name.Equals(name, StringComparison.OrdinalIgnoreCase))
                        return clip;
                }
            }
        }
        return null;
    }

    // ── Transition helper ───────────────────────────────────────────────────

    static void MakeTransition(
        AnimatorState from, AnimatorState to,
        AnimatorConditionMode mode, float threshold, string param,
        bool hasExitTime, float duration)
    {
        var t = from.AddTransition(to);
        t.AddCondition(mode, threshold, param);
        t.hasExitTime = hasExitTime;
        t.duration    = duration;
    }

    // ── Log ────────────────────────────────────────────────────────────────

    static void LogResult(
        AnimatorState idle, AnimatorState walk, AnimatorState run,
        AnimatorState throwSt, AnimatorState hitReact)
    {
        string Slot(AnimatorState s) =>
            s.motion != null ? $"✓ {s.motion.name}" : "✗ NOT FOUND — assign manually in Animator window";

        Debug.Log(
            $"[NPCAnimator] ✓ Controller saved to '{ControllerPath}'\n" +
            $"  Idle     : {Slot(idle)}\n" +
            $"  Walk     : {Slot(walk)}\n" +
            $"  Run      : {Slot(run)}\n" +
            $"  Throw    : {Slot(throwSt)}\n" +
            $"  HitReact : {Slot(hitReact)}\n\n" +
            "  For any missing clips: open the Animator window, click the state, drag your clip in.\n" +
            "  Add an Animation Event on the Throw clip at the release frame → NPCThrower.ReleaseThrowable()");
    }
}
