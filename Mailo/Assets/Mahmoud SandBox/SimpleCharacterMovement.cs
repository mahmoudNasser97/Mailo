using UnityEngine;

/// <summary>
/// WASD movement + animation for a standalone Rigidbody character.
/// Attach to the root GameObject that has a Rigidbody and an Animator.
/// Uses CharacterAnimaiton.controller parameters: Speed, MoveX, MoveY.
/// </summary>
[RequireComponent(typeof(Rigidbody))]
[RequireComponent(typeof(Animator))]
public class SimpleCharacterMovement : MonoBehaviour
{
    [Header("Movement")]
    public float moveSpeed   = 5f;
    public float rotateSpeed = 12f;

    Rigidbody _rb;
    Animator  _animator;

    static readonly int _speedHash = Animator.StringToHash("Speed");
    static readonly int _moveXHash = Animator.StringToHash("MoveX");
    static readonly int _moveYHash = Animator.StringToHash("MoveY");

    void Awake()
    {
        _rb      = GetComponent<Rigidbody>();
        _animator = GetComponent<Animator>();

        // Only freeze X and Z rotation so the character can't tip over.
        // Never freeze position — that blocks WASD movement.
        _rb.constraints = RigidbodyConstraints.FreezeRotationX
                        | RigidbodyConstraints.FreezeRotationZ;

        _rb.interpolation = RigidbodyInterpolation.Interpolate;
    }

    void FixedUpdate()
    {
        float h = Input.GetAxisRaw("Horizontal"); // A = -1, D = +1
        float v = Input.GetAxisRaw("Vertical");   // S = -1, W = +1

        Vector3 move = new Vector3(h, 0f, v);
        bool    moving = move.sqrMagnitude > 0.01f;

        if (moving)
        {
            move = move.normalized;

            // Apply horizontal velocity, preserve Y so gravity still works
            _rb.linearVelocity = new Vector3(
                move.x * moveSpeed,
                _rb.linearVelocity.y,
                move.z * moveSpeed
            );

            // Rotate to face the movement direction
            Quaternion targetRot = Quaternion.LookRotation(new Vector3(move.x, 0f, move.z));
            _rb.MoveRotation(Quaternion.Slerp(transform.rotation, targetRot, rotateSpeed * Time.fixedDeltaTime));
        }
        else
        {
            // Stop horizontal movement, keep Y (gravity / wind)
            _rb.linearVelocity = new Vector3(0f, _rb.linearVelocity.y, 0f);
        }

        // Drive animator
        _animator.SetFloat(_speedHash, moving ? 1f : 0f,       0.1f, Time.fixedDeltaTime);
        _animator.SetFloat(_moveXHash, moving ? move.x : 0f,   0.1f, Time.fixedDeltaTime);
        _animator.SetFloat(_moveYHash, moving ? move.z : 0f,   0.1f, Time.fixedDeltaTime);
    }
}
