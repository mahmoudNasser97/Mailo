using UnityEngine;

/// <summary>
/// Simple WASD movement for the PuppetMaster animated target.
/// The physical puppet follows wherever this character moves.
/// Add to the animated character root (NOT the PuppetMaster object).
/// </summary>
[RequireComponent(typeof(CharacterController))]
[RequireComponent(typeof(Animator))]
public class PuppetMoverSimple : MonoBehaviour
{
    [Header("Movement")]
    public float moveSpeed   = 4f;
    public float rotateSpeed = 10f;
    public float gravity     = 20f;

    CharacterController _cc;
    Animator            _animator;
    float               _verticalVelocity;
    Vector3             _lastInput;

    static readonly int _speedHash = Animator.StringToHash("Speed");
    static readonly int _moveXHash = Animator.StringToHash("MoveX");
    static readonly int _moveYHash = Animator.StringToHash("MoveY");

    void Awake()
    {
        _cc      = GetComponent<CharacterController>();
        _animator = GetComponent<Animator>();
        _animator.applyRootMotion = false;
    }

    void Update()
    {
        float h = Input.GetAxisRaw("Horizontal");
        float v = Input.GetAxisRaw("Vertical");
        Vector3 input = new Vector3(h, 0f, v);
        bool moving = input.sqrMagnitude > 0.01f;

        if (moving)
        {
            _lastInput = input.normalized;

            Quaternion targetRot = Quaternion.LookRotation(_lastInput);
            transform.rotation = Quaternion.Slerp(transform.rotation, targetRot,
                                                   rotateSpeed * Time.deltaTime);
        }

        _verticalVelocity = _cc.isGrounded ? -1f : _verticalVelocity - gravity * Time.deltaTime;

        Vector3 move = (moving ? _lastInput * moveSpeed : Vector3.zero);
        move.y = _verticalVelocity;
        _cc.Move(move * Time.deltaTime);

        _animator.SetFloat(_speedHash, moving ? 1f : 0f,      0.1f, Time.deltaTime);
        _animator.SetFloat(_moveXHash, moving ? _lastInput.x : 0f, 0.1f, Time.deltaTime);
        _animator.SetFloat(_moveYHash, moving ? _lastInput.z : 0f, 0.1f, Time.deltaTime);
    }
}
