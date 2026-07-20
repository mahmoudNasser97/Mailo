using UnityEngine;

public enum NPCState { Patrol, Chase, Throw, HitReact }

[RequireComponent(typeof(CharacterController))]
[RequireComponent(typeof(Animator))]
public class NPCBrain : MonoBehaviour
{
    [Header("Ranges")]
    [SerializeField] float _chaseRange   = 15f;
    [SerializeField] float _throwRange   = 6f;
    [SerializeField] float _gravity      = 20f;

    [Header("Player")]
    [Tooltip("Assign the player Transform directly. If left empty, will search for the object tagged 'Player' at startup.")]
    [SerializeField] Transform _playerTarget;

    [Header("Sub-components")]
    [SerializeField] NPCPatroller   _patroller;
    [SerializeField] NPCChaser      _chaser;
    [SerializeField] NPCThrower     _thrower;
    [SerializeField] NPCHitReaction _hitReaction;
    [SerializeField] NPCRVOAgent    _rvoAgent;
    [SerializeField] NPCHitVFX      _hitVFX;

    CharacterController     _cc;
    PuppetRagdollController _ragdoll;
    float                   _verticalVelocity;
    NPCState                _preHitState = NPCState.Patrol;

    public NPCState State  { get; private set; } = NPCState.Patrol;
    public Transform Player { get; private set; }

    void Awake()
    {
        _cc      = GetComponent<CharacterController>();
        var anim = GetComponent<Animator>();
        anim.applyRootMotion = false;

        _ragdoll = GetComponentInParent<PuppetRagdollController>()
                ?? transform.root.GetComponentInChildren<PuppetRagdollController>();

        if (_playerTarget != null)
        {
            Player = _playerTarget;
        }
        else
        {
            GameObject p = GameObject.FindWithTag("Player");
            if (p != null) Player = p.transform;
        }
    }

    void Start()
    {
        if (Player == null)
            Debug.LogWarning($"[NPCBrain] '{name}': No player found. Assign the Player Transform in the Inspector under 'Player Target', or tag your player GameObject with 'Player'.");
    }

    void Update()
    {
        _verticalVelocity = _cc.isGrounded
            ? -1f
            : _verticalVelocity - _gravity * Time.deltaTime;

        bool knocked = _ragdoll != null && _ragdoll.State != PuppetPhysicsState.Balanced;
        if (knocked)
        {
            _cc.Move(Vector3.up * _verticalVelocity * Time.deltaTime);
            return;
        }

        // Always apply gravity even if player isn't found
        if (Player == null)
        {
            _cc.Move(Vector3.up * _verticalVelocity * Time.deltaTime);
            return;
        }

        if (State != NPCState.HitReact)
            UpdateStateTransitions();

        Vector3 desired = GetDesiredVelocity();
        Vector3 moved   = _rvoAgent != null
            ? _rvoAgent.ComputeAvoidanceVelocity(desired)
            : desired;

        moved.y = _verticalVelocity;
        _cc.Move(moved * Time.deltaTime);
    }

    void UpdateStateTransitions()
    {
        Vector3 selfXZ   = new Vector3(transform.position.x, 0f, transform.position.z);
        Vector3 playerXZ = new Vector3(Player.position.x, 0f, Player.position.z);
        float   dist     = Vector3.Distance(selfXZ, playerXZ);

        switch (State)
        {
            case NPCState.Patrol:
                if (dist <= _chaseRange) ChangeState(NPCState.Chase);
                break;
            case NPCState.Chase:
                if (dist > _chaseRange)      ChangeState(NPCState.Patrol);
                else if (dist <= _throwRange) ChangeState(NPCState.Throw);
                break;
            case NPCState.Throw:
                if (dist > _throwRange) ChangeState(NPCState.Chase);
                break;
        }
    }

    void ChangeState(NPCState next)
    {
        State = next;
    }

    Vector3 GetDesiredVelocity()
    {
        return State switch
        {
            NPCState.Patrol   => _patroller  != null ? _patroller.GetDesiredVelocity()  : Vector3.zero,
            NPCState.Chase    => _chaser     != null ? _chaser.GetDesiredVelocity()      : Vector3.zero,
            NPCState.Throw    => Vector3.zero,
            NPCState.HitReact => Vector3.zero,
            _                 => Vector3.zero,
        };
    }

    public void ReportHit(float impulse, Vector3 hitPoint, Vector3 hitDir)
    {
        if (State == NPCState.HitReact) return;
        _preHitState = State;
        ChangeState(NPCState.HitReact);
        _hitVFX?.PlayHitEffects(hitPoint, hitDir);
    }

    public void RecoverFromHit() => ChangeState(_preHitState);

    void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, _chaseRange);
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position, _throwRange);
    }
}
