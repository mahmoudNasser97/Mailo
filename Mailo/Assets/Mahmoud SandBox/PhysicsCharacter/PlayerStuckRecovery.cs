using UnityEngine;
using System.Collections;
using RootMotion.Dynamics;

/// <summary>
/// Stuck-recovery for a PuppetMaster character. When the puppet has been out of its balanced
/// (Puppet) state for longer than <see cref="stuckThreshold"/> seconds, it stands the character
/// back up IN PLACE, on the real ground directly under the fallen ragdoll:
///   1. Raycast down from the ragdoll's hips to the ground it is lying on.
///   2. Drop any held object (its weight would otherwise knock the stand over).
///   3. Force the animator into a grounded/standing pose.
///   4. Teleport the target + muscles onto that ground spot, then hold the muscles KINEMATIC for a
///      moment so they rigidly plant in the standing pose before active ragdoll physics resumes.
///
/// A plain "teleport + SetState(Puppet)" cannot stand a PuppetMaster up — the kinematic plant in
/// step 4 is what makes it reliable. Recovering in place (not at remembered "safe spots") avoids
/// the bad-destination / re-fall loop.
///
/// Add this to the movement-root ("Character Controller") object that holds CharacterThirdPerson.
/// </summary>
public class PlayerStuckRecovery : MonoBehaviour
{
    [Header("Stuck detection")]
    [Tooltip("Seconds out of the balanced Puppet state before the character auto-recovers.")]
    public float stuckThreshold = 1.5f;

    [Header("Recovery")]
    [Tooltip("Seconds to hold the muscles kinematic (rigidly following the standing pose, immune to knock-down) before returning to active ragdoll.")]
    public float kinematicPlantTime = 0.4f;
    [Tooltip("Grounded/idle Animator state (base layer) forced before standing. MUST match your Animator or the character plants in the wrong pose.")]
    public string groundedState = "Grounded Directional";
    [Tooltip("Drop any object the character is holding when recovering.")]
    public bool dropHeldOnRecover = true;

    [Header("Fallback")]
    [Tooltip("Used only if there is no ground under the ragdoll (e.g. it fell off the map). Leave empty to recover at the ragdoll's position anyway.")]
    public Transform fixedRespawnPoint;

    [Header("Debug")]
    [Tooltip("Show an on-screen HUD of puppet/mode/anim/stuck state. Turn off once diagnosed.")]
    public bool showDebug = true;

    [Header("References (auto-found if left empty)")]
    public PuppetMaster puppetMaster;
    public BehaviourPuppet puppet;

    Rigidbody _rb;
    CharacterController _cc;
    RootMotion.Demos.CharacterThirdPerson _character;
    ObjectGrabController _grab;

    bool    _recovering;
    float   _stuckTimer;
    Vector3 _prevPos;

    // Diagnostics
    string _lastEvent = "(none)";
    float  _lastEventTime;

    void Awake()
    {
        _rb        = GetComponent<Rigidbody>();
        _cc        = GetComponent<CharacterController>();
        _character = GetComponent<RootMotion.Demos.CharacterThirdPerson>();

        Transform root = transform.parent != null ? transform.parent : transform;
        if (puppet == null) puppet = root.GetComponentInChildren<BehaviourPuppet>();
        if (puppetMaster == null)
        {
            if (puppet != null && puppet.puppetMaster != null) puppetMaster = puppet.puppetMaster;
            else puppetMaster = root.GetComponentInChildren<PuppetMaster>();
        }
        _grab = GetComponent<ObjectGrabController>()
             ?? root.GetComponentInChildren<ObjectGrabController>(true);
    }

    void Start()
    {
        _prevPos = transform.position;
    }

    void Update()
    {
        if (puppet == null || puppetMaster == null) return;

        bool balanced = puppet.state == BehaviourPuppet.State.Puppet;
        _stuckTimer = balanced ? 0f : _stuckTimer + Time.deltaTime;

        if (!_recovering && _stuckTimer >= stuckThreshold)
            StartCoroutine(RecoverRoutine());

        _prevPos = transform.position;
    }

    IEnumerator RecoverRoutine()
    {
        _recovering = true;

        // 1. Where is the ragdoll actually lying? Use the root/hips muscle, raycast to the real ground.
        Vector3 ragdollPos = (puppetMaster.muscles != null && puppetMaster.muscles.Length > 0)
            ? puppetMaster.muscles[0].transform.position
            : transform.position;

        Vector3    groundPos  = ragdollPos;
        Quaternion uprightRot = UprightYaw(transform.rotation);
        LayerMask  gl         = _character != null ? _character.groundLayers : (LayerMask)(~0);

        bool foundGround = Physics.Raycast(ragdollPos + Vector3.up * 2f, Vector3.down,
                                           out RaycastHit hit, 30f, gl);
        if (foundGround)
            groundPos = hit.point + Vector3.up * 0.1f;
        else if (fixedRespawnPoint != null)
        {
            groundPos  = fixedRespawnPoint.position;
            uprightRot = UprightYaw(fixedRespawnPoint.rotation);
        }

        // 2. Drop the held object so its weight can't knock the stand over.
        if (dropHeldOnRecover && _grab != null && _grab.IsHoldingObject)
            _grab.DropHeld();

        // 3. Make sure the ragdoll is alive.
        if (puppetMaster.state != PuppetMaster.State.Alive)
            puppetMaster.state = PuppetMaster.State.Alive;

        // 4. Force the animator to a grounded standing pose so the kinematic plant stands (not falls).
        if (puppetMaster.targetAnimator != null && !string.IsNullOrEmpty(groundedState))
            puppetMaster.targetAnimator.Play(groundedState, 0, 0f);

        // 5. Clean pinned state.
        if (puppet.state != BehaviourPuppet.State.Unpinned)
            puppet.SetState(BehaviourPuppet.State.Unpinned);
        puppet.SetState(BehaviourPuppet.State.Puppet);

        // 6. Teleport target + muscles onto the ground spot (clears muscle velocities).
        puppetMaster.Teleport(groundPos, uprightRot, true);
        if (_rb != null && !_rb.isKinematic)
        {
            _rb.position        = groundPos;
            _rb.rotation        = uprightRot;
            _rb.linearVelocity  = Vector3.zero;
            _rb.angularVelocity = Vector3.zero;
        }

        _lastEvent = "RECOVER " + (foundGround ? "ground" : (fixedRespawnPoint != null ? "fallback" : "in-air")) +
                     " " + groundPos.ToString("0.0");
        _lastEventTime = Time.time;
        if (showDebug) Debug.Log("[StuckRecovery] " + _lastEvent);

        // 7. Kinematic plant: hold the muscles rigidly on the standing pose so nothing knocks them down.
        yield return new WaitForFixedUpdate();
        puppetMaster.SwitchToKinematicMode();
        float t = 0f;
        while (t < kinematicPlantTime)
        {
            if (puppetMaster.targetAnimator != null && !string.IsNullOrEmpty(groundedState))
                puppetMaster.targetAnimator.Play(groundedState, 0, 0f);
            t += Time.deltaTime;
            yield return null;
        }
        puppetMaster.SwitchToActiveMode();
        yield return null;

        // 8. Ensure it resumes balanced.
        if (puppet.state != BehaviourPuppet.State.Puppet)
        {
            if (puppet.state != BehaviourPuppet.State.Unpinned)
                puppet.SetState(BehaviourPuppet.State.Unpinned);
            puppet.SetState(BehaviourPuppet.State.Puppet);
        }

        _stuckTimer = 0f;
        _prevPos    = transform.position;
        _recovering = false;
    }

    static Quaternion UprightYaw(Quaternion rot)
    {
        Vector3 fwd = rot * Vector3.forward;
        fwd.y = 0f;
        if (fwd.sqrMagnitude < 1e-6f) fwd = Vector3.forward;
        return Quaternion.LookRotation(fwd.normalized, Vector3.up);
    }

    string CurrentAnimState()
    {
        Animator a = puppetMaster != null ? puppetMaster.targetAnimator : null;
        if (a == null) return "(no animator)";
        AnimatorStateInfo s = a.GetCurrentAnimatorStateInfo(0);
        string[] names = { "Grounded Directional", "Grounded Strafe", "Falling",
                           "GetUpSupine", "GetUpProne", "Idle", "Walk", "Run", "Locomotion" };
        foreach (string n in names)
            if (s.IsName(n)) return n + " t=" + s.normalizedTime.ToString("0.00");
        return "hash:" + s.shortNameHash + (a.IsInTransition(0) ? " (transitioning)" : "");
    }

    void OnGUI()
    {
        if (!showDebug || puppet == null || puppetMaster == null) return;
        GUIStyle style = new GUIStyle(GUI.skin.box) { alignment = TextAnchor.UpperLeft, fontSize = 14 };
        bool grounded = _character != null ? _character.onGround : (_cc == null || _cc.isGrounded);
        string txt =
            "STUCK RECOVERY (in-place)" + (_recovering ? "  [RECOVERING]" : "") + "\n" +
            "puppet.state: " + puppet.state + "\n" +
            "pm.state: " + puppetMaster.state + "   mode: " + puppetMaster.mode + "\n" +
            "onGround: " + grounded + "   holding: " + (_grab != null && _grab.IsHoldingObject) + "\n" +
            "anim(base): " + CurrentAnimState() + "\n" +
            "stuckTimer: " + _stuckTimer.ToString("0.0") + " / " + stuckThreshold +
                (_stuckTimer >= stuckThreshold ? "  [firing]" : "") + "\n" +
            "lastEvent (" + (Time.time - _lastEventTime).ToString("0.0") + "s ago): " + _lastEvent;
        GUI.Label(new Rect(10, 10, 520, 155), txt, style);
    }
}
