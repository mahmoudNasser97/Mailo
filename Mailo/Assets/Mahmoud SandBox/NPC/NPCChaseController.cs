using UnityEngine;

[RequireComponent(typeof(CharacterController))]
[RequireComponent(typeof(Animator))]
public class NPCChaseController : MonoBehaviour
{
    [Header("Chase")]
    [SerializeField] Transform _target;
    [SerializeField] float     _chaseRange   = 15f;
    [SerializeField] float     _stopDistance = 1.8f;
    [SerializeField] float     _moveSpeed    = 3.5f;
    [SerializeField] float     _rotateSpeed  = 8f;
    [SerializeField] float     _gravity      = 20f;

    CharacterController     _cc;
    Animator                _animator;
    PuppetRagdollController _ragdoll;
    float                   _verticalVelocity;

    static readonly int _speedHash = Animator.StringToHash("Speed");

    void Awake()
    {
        _cc      = GetComponent<CharacterController>();
        _animator = GetComponent<Animator>();
        _animator.applyRootMotion = false;

        // Search up first, then fall back to searching the whole root hierarchy
        _ragdoll = GetComponentInParent<PuppetRagdollController>();
        if (_ragdoll == null)
            _ragdoll = transform.root.GetComponentInChildren<PuppetRagdollController>();

        if (_target == null)
        {
            GameObject player = GameObject.FindWithTag("Player");
            if (player != null) _target = player.transform;
        }
    }

    void Update()
    {
        _verticalVelocity = _cc.isGrounded
            ? -1f
            : _verticalVelocity - _gravity * Time.deltaTime;

        bool knocked = _ragdoll != null && _ragdoll.State != PuppetPhysicsState.Balanced;
        if (knocked)
        {
            _cc.Move(new Vector3(0f, _verticalVelocity, 0f) * Time.deltaTime);
            _animator.SetFloat(_speedHash, 0f, 0.1f, Time.deltaTime);
            return;
        }

        if (_target == null) return;

        Vector3 toTarget = _target.position - transform.position;
        toTarget.y       = 0f;
        float dist       = toTarget.magnitude;

        if (dist <= _chaseRange && dist > _stopDistance)
        {
            transform.rotation = Quaternion.Slerp(
                transform.rotation,
                Quaternion.LookRotation(toTarget.normalized),
                _rotateSpeed * Time.deltaTime);

            Vector3 move = transform.forward * _moveSpeed;
            move.y = _verticalVelocity;
            _cc.Move(move * Time.deltaTime);
            _animator.SetFloat(_speedHash, 1f, 0.1f, Time.deltaTime);
        }
        else
        {
            _cc.Move(new Vector3(0f, _verticalVelocity, 0f) * Time.deltaTime);
            _animator.SetFloat(_speedHash, 0f, 0.1f, Time.deltaTime);
        }
    }
}
