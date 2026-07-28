using System.Collections;
using UnityEngine;

public class GatePullController : MonoBehaviour
{
    [Header("Detection")]
    [SerializeField] float _interactionRadius = 5f;
    [SerializeField] float _pullBreakDistance = 8f;

    [Header("Rope Visual")]
    [SerializeField] Transform _handBone;
    [SerializeField] float     _ropeWidth         = 0.05f;
    [SerializeField] Color     _ropeColor         = Color.gray;
    [SerializeField] float     _ropeThrowDuration = 0.4f;

    [Header("Animation — exact state names in your Animator Controller")]
    [SerializeField] Animator _animator;
    [SerializeField] string   _throwState      = "ThrowRope";
    [SerializeField] string   _pullIdleState   = "PullIdle";
    [SerializeField] string   _pullActionState = "PullAction";
    [SerializeField] string   _locomotionState = "Grounded Directional";
    [SerializeField] int      _animatorLayer   = 0;
    [SerializeField] float   _maxAnimSpeed    = 3f;
    [SerializeField] float   _animSpeedDecay  = 4f;

    enum State { Idle, Throwing, Pulling }

    LineRenderer _rope;
    PullableGate _gate;
    State        _state         = State.Idle;
    float        _lastPressTime = -99f;
    float        _animSpeed     = 1f;

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
            _animator?.CrossFade(_throwState, 0.1f, _animatorLayer);
            _rope.enabled = true;
            StartCoroutine(ThrowRope());
        }
    }

    void UpdateThrowing()
    {
        if (_gate == null) return;
        if (HasMovementInput() || Vector3.Distance(transform.position, _gate.transform.position) > _interactionRadius * 1.5f)
            ExitPull();
    }

    IEnumerator ThrowRope()
    {
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

        _gate.StartPull();
        _state = State.Pulling;
        _animator?.CrossFade(_pullIdleState, 0.2f, _animatorLayer);
    }

    void UpdatePulling()
    {
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

        _rope.SetPosition(0, HandPosition());
        _rope.SetPosition(1, _gate.MarkerTransform.position);

        _animSpeed = Mathf.MoveTowards(_animSpeed, 1f, _animSpeedDecay * Time.deltaTime);
        if (_animator != null)
        {
            _animator.speed = _animSpeed;
            var info = _animator.GetCurrentAnimatorStateInfo(_animatorLayer);
            if (info.shortNameHash == Animator.StringToHash(_pullActionState) && info.normalizedTime >= 0.75f)
                _animator.CrossFade(_pullIdleState, 0.15f, _animatorLayer);
        }

        if (Input.GetKeyDown(KeyCode.X))
        {
            float interval  = Mathf.Max(Time.time - _lastPressTime, 0.05f);
            _lastPressTime  = Time.time;
            _animSpeed      = Mathf.Clamp(0.25f / interval, 1f, _maxAnimSpeed);
            _gate.RegisterPress();
            _animator?.CrossFade(_pullActionState, 0.05f, _animatorLayer);
        }
    }

    void ExitPull()
    {
        StopAllCoroutines();
        if (_gate != null && !_gate.IsFullyOpen)
            _gate.StopPull();
        _gate         = null;
        _state        = State.Idle;
        _animSpeed    = 1f;
        _rope.enabled = false;
        if (_animator != null) _animator.speed = 1f;
        _animator?.CrossFade(_locomotionState, 0.2f, _animatorLayer);
    }

    static bool HasMovementInput() =>
        Mathf.Abs(Input.GetAxisRaw("Horizontal")) > 0.1f ||
        Mathf.Abs(Input.GetAxisRaw("Vertical"))   > 0.1f;

    Vector3 HandPosition() =>
        _handBone != null ? _handBone.position : transform.position + Vector3.up * 1.2f;
}
