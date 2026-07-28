using System.Collections;
using UnityEngine;

public class GatePullController : MonoBehaviour
{
    [Header("Detection")]
    [SerializeField] float _interactionRadius  = 5f;
    [SerializeField] float _pullBreakDistance  = 8f;

    [Header("Rope Visual")]
    [SerializeField] Transform _handBone;
    [SerializeField] float     _ropeWidth         = 0.05f;
    [SerializeField] Color     _ropeColor         = Color.gray;
    [SerializeField] float     _ropeThrowDuration = 0.4f;

    [Header("Animation")]
    [SerializeField] Animator _animator;
    [SerializeField] string   _throwAnimTrigger  = "ThrowRope";
    [SerializeField] string   _pullingAnimBool   = "Pulling";
    [SerializeField] string   _pullActionTrigger = "PullAction";

    enum State { Idle, Throwing, Pulling }

    LineRenderer _rope;
    PullableGate _gate;
    State        _state = State.Idle;

    void Awake()
    {
        if (_animator == null)
            _animator = GetComponentInChildren<Animator>()
                     ?? GetComponentInParent<Animator>();

        GameObject ropeObj  = new GameObject("GatePullRope");
        ropeObj.transform.SetParent(transform);
        _rope               = ropeObj.AddComponent<LineRenderer>();
        _rope.positionCount = 2;
        _rope.startWidth    = _ropeWidth;
        _rope.endWidth      = _ropeWidth;
        _rope.useWorldSpace = true;
        _rope.material      = new Material(Shader.Find("Sprites/Default"));
        _rope.startColor    = _ropeColor;
        _rope.endColor      = _ropeColor;
        _rope.enabled       = false;
    }

    void Update()
    {
        switch (_state)
        {
            case State.Idle:     UpdateIdle();     break;
            case State.Throwing: UpdateThrowing(); break;
            case State.Pulling:  UpdatePulling();  break;
        }
    }

    void UpdateIdle()
    {
        Collider[] hits = Physics.OverlapSphere(transform.position, _interactionRadius);
        PullableGate found = null;
        foreach (var h in hits)
        {
            var g = h.GetComponentInParent<PullableGate>();
            if (g != null && !g.IsFullyOpen) { found = g; break; }
        }

        if (found == null) return;

        if (Input.GetKeyDown(KeyCode.X))
        {
            _gate  = found;
            _state = State.Throwing;
            _animator?.SetTrigger(_throwAnimTrigger);
            _rope.enabled = true;
            StartCoroutine(ThrowRope());
        }
    }

    void UpdateThrowing()
    {
        if (_gate == null) return;

        // Movement input or walking away cancels the throw
        if (HasMovementInput() || Vector3.Distance(transform.position, _gate.transform.position) > _interactionRadius * 1.5f)
            ExitPull();
    }

    IEnumerator ThrowRope()
    {
        // Extend rope visually toward the neon X over _ropeThrowDuration seconds
        float elapsed = 0f;
        while (elapsed < _ropeThrowDuration)
        {
            elapsed += Time.deltaTime;
            float   t         = elapsed / _ropeThrowDuration;
            Vector3 ropeStart = HandPosition();
            Vector3 ropeEnd   = Vector3.Lerp(ropeStart, _gate.MarkerTransform.position, t);
            _rope.SetPosition(0, ropeStart);
            _rope.SetPosition(1, ropeEnd);
            yield return null;
        }

        if (_gate == null) yield break;

        // Rope is now visually connected — enter pull state.
        // The Animator handles ThrowRope → Pull Idle via "Has Exit Time" on that transition.
        _gate.StartPull();
        _state = State.Pulling;
        _animator?.SetBool(_pullingAnimBool, true);
    }

    void UpdatePulling()
    {
        // Movement input exits pull
        if (HasMovementInput())
        {
            ExitPull();
            return;
        }

        float dist = Vector3.Distance(transform.position, _gate.transform.position);
        if (dist > _pullBreakDistance || _gate.IsFullyOpen)
        {
            ExitPull();
            return;
        }

        // Keep rope live
        _rope.SetPosition(0, HandPosition());
        _rope.SetPosition(1, _gate.MarkerTransform.position);

        if (Input.GetKeyDown(KeyCode.X))
        {
            _gate.RegisterPress();
            _animator?.SetTrigger(_pullActionTrigger);
        }
    }

    void ExitPull()
    {
        StopAllCoroutines();
        if (_gate != null && !_gate.IsFullyOpen)
            _gate.StopPull();
        _gate         = null;
        _state        = State.Idle;
        _rope.enabled = false;
        _animator?.SetBool(_pullingAnimBool, false);
    }

    static bool HasMovementInput() =>
        Mathf.Abs(Input.GetAxisRaw("Horizontal")) > 0.1f ||
        Mathf.Abs(Input.GetAxisRaw("Vertical"))   > 0.1f;

    Vector3 HandPosition() =>
        _handBone != null ? _handBone.position : transform.position + Vector3.up * 1.2f;
}
