using System.Collections;
using UnityEngine;

[RequireComponent(typeof(CharacterController))]
public class GatePullController : MonoBehaviour
{
    [Header("Detection")]
    [SerializeField] float _interactionRadius  = 3f;
    [SerializeField] float _pullBreakDistance  = 6f;  // max distance before rope breaks while pulling

    [Header("Rope Visual")]
    [SerializeField] Transform _handBone;
    [SerializeField] float     _ropeWidth         = 0.05f;
    [SerializeField] Color     _ropeColor          = Color.gray;
    [SerializeField] float     _ropeThrowDuration  = 0.4f;

    [Header("Pull")]
    [SerializeField] float _stepBackDistance = 0.15f;

    [Header("Animation")]
    [SerializeField] Animator _animator;
    [SerializeField] string   _throwAnimTrigger = "ThrowRope";
    [SerializeField] string   _pullingAnimBool  = "Pulling";

    enum State { Idle, Throwing, Pulling }

    CharacterController _cc;
    LineRenderer        _rope;
    PullableGate        _gate;
    State               _state = State.Idle;

    void Awake()
    {
        _cc = GetComponent<CharacterController>();
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
        // Cancel throw if player walks away mid-throw
        if (_gate == null) return;
        float dist = Vector3.Distance(transform.position, _gate.transform.position);
        if (dist > _interactionRadius * 1.5f)
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
        _animator?.SetBool(_pullingAnimBool, true);
    }

    void UpdatePulling()
    {
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

            // Step back — away from gate on XZ plane
            Vector3 away = transform.position - _gate.transform.position;
            away.y = 0f;
            away   = away.sqrMagnitude > 0f ? away.normalized : -transform.forward;
            _cc.Move(away * _stepBackDistance);
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

    Vector3 HandPosition() =>
        _handBone != null ? _handBone.position : transform.position + Vector3.up * 1.2f;
}
