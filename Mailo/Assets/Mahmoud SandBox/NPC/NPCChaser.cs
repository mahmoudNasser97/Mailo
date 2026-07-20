using UnityEngine;

public class NPCChaser : MonoBehaviour
{
    [SerializeField] float _chaseSpeed   = 4f;
    [SerializeField] float _rotateSpeed  = 8f;

    static readonly int _speedHash = Animator.StringToHash("Speed");

    Animator _animator;
    NPCBrain _brain;

    void Awake()
    {
        _animator = GetComponent<Animator>();
        _brain    = GetComponent<NPCBrain>();
    }

    public Vector3 GetDesiredVelocity()
    {
        if (_brain.State != NPCState.Chase || _brain.Player == null)
        {
            _animator.SetFloat(_speedHash, 0f, 0.1f, Time.deltaTime);
            return Vector3.zero;
        }

        Vector3 toPlayer = _brain.Player.position - transform.position;
        toPlayer.y = 0f;

        if (toPlayer.sqrMagnitude < 0.01f)
        {
            _animator.SetFloat(_speedHash, 0f, 0.1f, Time.deltaTime);
            return Vector3.zero;
        }

        Quaternion desired = Quaternion.LookRotation(toPlayer.normalized);
        transform.rotation = Quaternion.RotateTowards(
            transform.rotation, desired, _rotateSpeed * 360f * Time.deltaTime);

        _animator.SetFloat(_speedHash, 1f, 0.1f, Time.deltaTime);
        return toPlayer.normalized * _chaseSpeed;
    }
}
