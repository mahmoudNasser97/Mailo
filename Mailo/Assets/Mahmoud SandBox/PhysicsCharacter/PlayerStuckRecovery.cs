using UnityEngine;
using System.Collections;
using RootMotion.Dynamics;

/// <summary>
/// "Refresh" recovery for a PuppetMaster character. While the puppet is stuck in ragdoll
/// (out of the balanced Puppet state and unable to stand back up), pressing the recover key
/// stands the character back up at a grounded spot.
///
/// Recovery uses a brief KINEMATIC snap: the muscles are made kinematic so they rigidly follow
/// the standing animation and CANNOT be knocked down by surrounding objects, then return to the
/// active ragdoll. This is what makes it reliable even when the character is stuck among props.
///
/// Add this to the movement-root object (the "Character Controller" object that holds
/// CharacterPuppet / CharacterThirdPerson).
/// </summary>
public class PlayerStuckRecovery : MonoBehaviour
{
    [Header("Input")]
    public KeyCode recoverKey = KeyCode.R;

    [Header("Stuck detection")]
    [Tooltip("Seconds out of the balanced Puppet state before the recover key becomes active.")]
    public float stuckThreshold = 1.5f;

    [Header("Recovery")]
    [Tooltip("Seconds to hold the puppet kinematic (rigidly following the standing animation, immune to collisions) before returning to active ragdoll.")]
    public float kinematicSnapTime = 0.5f;
    [Tooltip("Prefer teleporting to the last safe spot (escapes a crowded stuck spot). If off, recovers in place when grounded.")]
    public bool teleportToSafeSpot = true;

    [Header("Safe-spot recording")]
    [Tooltip("Max horizontal speed (m/s) to count as 'settled' when recording a safe standing spot.")]
    public float settledSpeed = 0.5f;

    [Header("Animation reset")]
    [Tooltip("Grounded/locomotion Animator state to snap back to on recovery (base layer). Empty = skip.")]
    public string groundedState = "Grounded Directional";

    [Header("Debug")]
    public bool showDebug = true;

    [Header("References (auto-found if left empty)")]
    public PuppetMaster puppetMaster;
    public BehaviourPuppet puppet;

    CharacterController _cc;
    Rigidbody _rb;
    RootMotion.Demos.CharacterThirdPerson _character;

    Vector3 _safeRootPos;
    Quaternion _safeRootRot;
    Vector3 _safeTargetPos;
    Quaternion _safeTargetRot;
    bool _hasSafe;
    bool _recordedRealSpot;

    bool _pendingControllerSync;
    bool _recovering;
    float _stuckTimer;
    Vector3 _prevPos;

    void Awake()
    {
        _cc = GetComponent<CharacterController>();
        _rb = GetComponent<Rigidbody>();
        _character = GetComponent<RootMotion.Demos.CharacterThirdPerson>();

        Transform root = transform.parent != null ? transform.parent : transform;
        if (puppet == null) puppet = root.GetComponentInChildren<BehaviourPuppet>();
        if (puppetMaster == null)
        {
            if (puppet != null && puppet.puppetMaster != null) puppetMaster = puppet.puppetMaster;
            else puppetMaster = root.GetComponentInChildren<PuppetMaster>();
        }
    }

    void OnEnable()
    {
        if (puppetMaster != null) puppetMaster.OnTeleported += OnPuppetTeleported;
    }

    void OnDisable()
    {
        if (puppetMaster != null) puppetMaster.OnTeleported -= OnPuppetTeleported;
    }

    void Start()
    {
        _prevPos = transform.position;
        RecordSafeSpot(false);
    }

    void Update()
    {
        if (puppet == null || puppetMaster == null) return;

        bool balanced = puppet.state == BehaviourPuppet.State.Puppet;
        _stuckTimer = balanced ? 0f : _stuckTimer + Time.deltaTime;

        if (balanced && !_recovering && IsGroundedAndSettled())
            RecordSafeSpot(true);

        if (!_recovering && _stuckTimer >= stuckThreshold && Input.GetKeyDown(recoverKey))
            StartCoroutine(RecoverRoutine());

        _prevPos = transform.position;
    }

    bool IsGroundedAndSettled()
    {
        bool grounded = _character != null ? _character.onGround : (_cc == null || _cc.isGrounded);
        Vector3 d = transform.position - _prevPos;
        d.y = 0f;
        float horizSpeed = d.magnitude / Mathf.Max(Time.deltaTime, 1e-5f);
        return grounded && horizSpeed <= settledSpeed;
    }

    void RecordSafeSpot(bool real)
    {
        _safeRootPos = transform.position;
        _safeRootRot = UprightYaw(transform.rotation);
        if (real) _recordedRealSpot = true;
        _hasSafe = true;
    }

    IEnumerator RecoverRoutine()
    {
        _recovering = true;

        if (puppetMaster.state != PuppetMaster.State.Alive) puppetMaster.state = PuppetMaster.State.Alive;

        // Choose a grounded root position. Prefer the last safe spot (escapes a crowded stuck spot),
        // snapped down onto real ground so onGround registers. Fall back to the current spot.
        bool groundedNow = _character != null && _character.onGround;
        if (teleportToSafeSpot && _hasSafe)
            _safeRootPos = SnapToGround(_safeRootPos);
        else if (groundedNow || !_hasSafe)
        {
            _safeRootPos = transform.position;
            _safeRootRot = UprightYaw(transform.rotation);
        }
        else
            _safeRootPos = SnapToGround(_safeRootPos);

        // Force the animation to the grounded/idle pose so the kinematic snap stands (not falls).
        if (puppetMaster.targetAnimator != null && !string.IsNullOrEmpty(groundedState))
            puppetMaster.targetAnimator.Play(groundedState, 0, 0f);

        // Bounce to Puppet so behaviour state is clean when we return to active ragdoll.
        if (puppet.state != BehaviourPuppet.State.Unpinned)
            puppet.SetState(BehaviourPuppet.State.Unpinned);
        puppet.SetState(BehaviourPuppet.State.Puppet);

        // Teleport the puppet + target to the grounded root position.
        Transform tr = puppetMaster.targetRoot;
        Vector3 rootDelta = _safeRootPos - transform.position;
        _safeTargetPos = (tr != null ? tr.position : _safeRootPos) + rootDelta;
        _safeTargetRot = UprightYaw(tr != null ? tr.rotation : _safeRootRot);
        _pendingControllerSync = true;
        puppetMaster.Teleport(_safeTargetPos, _safeTargetRot, true);
        _stuckTimer = 0f;

        // KINEMATIC snap: muscles rigidly follow the standing animation and can't be knocked over
        // by surrounding objects. Hold briefly so it plants on the ground, then go back to ragdoll.
        yield return new WaitForFixedUpdate();
        puppetMaster.SwitchToKinematicMode();

        float t = 0f;
        while (t < kinematicSnapTime)
        {
            // Keep the animation pinned to grounded during the hold.
            if (puppetMaster.targetAnimator != null && !string.IsNullOrEmpty(groundedState))
                puppetMaster.targetAnimator.Play(groundedState, 0, 0f);
            t += Time.deltaTime;
            yield return null;
        }

        puppetMaster.SwitchToActiveMode();
        yield return null;
        // Ensure it resumes balanced/pinned.
        if (puppet.state != BehaviourPuppet.State.Puppet)
        {
            if (puppet.state != BehaviourPuppet.State.Unpinned)
                puppet.SetState(BehaviourPuppet.State.Unpinned);
            puppet.SetState(BehaviourPuppet.State.Puppet);
        }

        _recovering = false;
        _prevPos = transform.position;
    }

    // Fired by PuppetMaster right after a teleport is processed.
    void OnPuppetTeleported()
    {
        if (!_pendingControllerSync) return;
        _pendingControllerSync = false;

        if (_cc != null) _cc.enabled = false;
        transform.SetPositionAndRotation(_safeRootPos, _safeRootRot);
        if (_cc != null) _cc.enabled = true;

        if (_rb != null)
        {
            _rb.position = _safeRootPos;
            _rb.rotation = _safeRootRot;
            if (!_rb.isKinematic)
            {
                _rb.linearVelocity = Vector3.zero;
                _rb.angularVelocity = Vector3.zero;
            }
        }

        if (puppetMaster.targetRoot != null)
            puppetMaster.targetRoot.SetPositionAndRotation(_safeTargetPos, _safeTargetRot);

        _prevPos = transform.position;
    }

    // Raycast down onto the character's ground layers so a placement lands actually grounded.
    Vector3 SnapToGround(Vector3 pos)
    {
        LayerMask gl = _character != null ? _character.groundLayers : (LayerMask)(~0);
        if (Physics.Raycast(pos + Vector3.up * 2f, Vector3.down, out RaycastHit hit, 30f, gl))
            return hit.point + Vector3.up * 0.15f;
        return pos;
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
        string[] names = { "Grounded Directional", "Grounded Strafe", "Falling", "GetUpSupine", "GetUpProne", "Idle", "Walk", "Run" };
        foreach (string n in names)
            if (s.IsName(n)) return n + " t=" + s.normalizedTime.ToString("0.00");
        return "hash:" + s.shortNameHash + (a.IsInTransition(0) ? " (transitioning)" : "");
    }

    void OnGUI()
    {
        if (!showDebug || puppet == null || puppetMaster == null) return;
        GUIStyle style = new GUIStyle(GUI.skin.box) { alignment = TextAnchor.UpperLeft, fontSize = 14 };
        string txt =
            "STUCK RECOVERY" + (_recovering ? "  [RECOVERING]" : "") + "\n" +
            "puppet.state: " + puppet.state + "\n" +
            "onGround: " + (_character != null ? _character.onGround.ToString() : "n/a") + "\n" +
            "anim(base): " + CurrentAnimState() + "\n" +
            "pm.state: " + puppetMaster.state + "   mode: " + puppetMaster.mode + "\n" +
            "stuckTimer: " + _stuckTimer.ToString("0.0") + " / " + stuckThreshold +
                (_stuckTimer >= stuckThreshold ? "  [R ready]" : "") + "\n" +
            "safeSpot: " + (_recordedRealSpot ? "recorded" : "SPAWN ONLY") + "  " + _safeRootPos.ToString("0.0");
        GUI.Label(new Rect(10, 10, 470, 155), txt, style);
    }
}
