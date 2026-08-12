using System.Collections;
using UnityEngine;

public class NPCPatroller : MonoBehaviour
{
    [Header("Waypoints")]
    [SerializeField] Transform[] _patrolPoints;
    [SerializeField] bool        _loopWaypoints = true;

    [Header("Wander (fallback)")]
    [SerializeField] float _wanderRadius    = 8f;
    [SerializeField] float _wanderPauseTime = 2f;

    [Header("Movement")]
    [SerializeField] float _patrolSpeed   = 2.5f;
    [SerializeField] float _rotateSpeed   = 5f;
    [SerializeField] float _arrivedRadius = 0.5f;

    static readonly int _speedHash = Animator.StringToHash("Speed");

    Animator  _animator;
    NPCBrain  _brain;
    Vector3   _spawnPos;
    Vector3   _target;
    int       _wpIndex;
    bool      _reversing;
    bool      _waiting;

    void Awake()
    {
        _animator = GetComponent<Animator>();
        _brain    = GetComponent<NPCBrain>();
        _spawnPos = transform.position;
    }

    void OnEnable() => PickNextTarget();

    public Vector3 GetDesiredVelocity()
    {
        if (_brain.State != NPCState.Patrol)
        {
            _animator.SetFloat(_speedHash, 0f, 0.1f, Time.deltaTime);
            return Vector3.zero;
        }

        if (_waiting)
        {
            _animator.SetFloat(_speedHash, 0f, 0.1f, Time.deltaTime);
            return Vector3.zero;
        }

        Vector3 toTarget = _target - transform.position;
        toTarget.y = 0f;

        if (toTarget.magnitude < _arrivedRadius)
        {
            OnArrived();
            return Vector3.zero;
        }

        Quaternion desired = Quaternion.LookRotation(toTarget.normalized);
        transform.rotation = Quaternion.RotateTowards(
            transform.rotation, desired, _rotateSpeed * 360f * Time.deltaTime);

        float normalized = Mathf.Clamp01(_patrolSpeed / 4f);
        _animator.SetFloat(_speedHash, normalized, 0.1f, Time.deltaTime);
        return toTarget.normalized * _patrolSpeed;
    }

    void PickNextTarget()
    {
        if (_patrolPoints != null && _patrolPoints.Length > 0)
        {
            _target = _patrolPoints[_wpIndex].position;
        }
        else
        {
            Vector2 rand = Random.insideUnitCircle * _wanderRadius;
            _target = _spawnPos + new Vector3(rand.x, 0f, rand.y);
        }
    }

    void OnArrived()
    {
        if (_patrolPoints != null && _patrolPoints.Length > 0)
        {
            AdvanceWaypoint();
            _target = _patrolPoints[_wpIndex].position;
        }
        else
        {
            StartCoroutine(WanderPause());
        }
    }

    void AdvanceWaypoint()
    {
        if (_loopWaypoints)
        {
            _wpIndex = (_wpIndex + 1) % _patrolPoints.Length;
            return;
        }

        if (!_reversing)
        {
            if (_wpIndex < _patrolPoints.Length - 1) _wpIndex++;
            else _reversing = true;
        }
        else
        {
            if (_wpIndex > 0) _wpIndex--;
            else _reversing = false;
        }
    }

    IEnumerator WanderPause()
    {
        _waiting = true;
        yield return new WaitForSeconds(_wanderPauseTime);
        _waiting = false;
        PickNextTarget();
    }

    void OnDrawGizmosSelected()
    {
        if (_patrolPoints == null || _patrolPoints.Length == 0)
        {
            Gizmos.color = Color.green;
            Gizmos.DrawWireSphere(Application.isPlaying ? _spawnPos : transform.position, _wanderRadius);
        }
        else
        {
            Gizmos.color = Color.green;
            for (int i = 0; i < _patrolPoints.Length; i++)
            {
                if (_patrolPoints[i] == null) continue;
                Gizmos.DrawSphere(_patrolPoints[i].position, 0.2f);
                if (i < _patrolPoints.Length - 1 && _patrolPoints[i + 1] != null)
                    Gizmos.DrawLine(_patrolPoints[i].position, _patrolPoints[i + 1].position);
            }
        }
    }
}
