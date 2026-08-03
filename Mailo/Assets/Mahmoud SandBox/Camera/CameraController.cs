using UnityEngine;
using Unity.Cinemachine;

public class CameraController : MonoBehaviour
{
    [Header("References")]
    [SerializeField] Camera     _camera;
    [SerializeField] Transform  _followTarget;
    [SerializeField] GameObject _crosshairUI;

    [Header("Orbit")]
    [SerializeField] float _sensitivity = 2.0f;
    [SerializeField] float _pitchMin    = -30f;
    [SerializeField] float _pitchMax    =  60f;

    // Offset is in camera-relative space: X = right, Y = up, Z = forward
    [Header("Normal View")]
    [SerializeField] Vector3 _normalOffset   = new Vector3(0.4f, 1.2f, 0f);
    [SerializeField] float   _normalDistance = 3.5f;
    [SerializeField] float   _normalFOV      = 65f;

    [Header("Aim View")]
    [SerializeField] Vector3 _aimOffset   = new Vector3(0.7f, 1.2f, 0f);
    [SerializeField] float   _aimDistance = 2.5f;
    [SerializeField] float   _aimFOV      = 50f;

    [Header("Transition")]
    [SerializeField] float _transitionSpeed = 12f;

    [Header("Spring Arm")]
    [Tooltip("SmoothDamp time (seconds) for position follow. 0 = rigid, no lag.")]
    [SerializeField] float _followLag             = 0.08f;
    [Tooltip("Sphere radius of the collision probe.")]
    [SerializeField] float _probeRadius           = 0.2f;
    [Tooltip("How fast the arm eases back out after a wall clears.")]
    [SerializeField] float _collisionReturnSpeed  = 6f;
    [Tooltip("If the target moves farther than this in one frame, snap instead of lagging.")]
    [SerializeField] float _teleportSnapThreshold = 8f;

    Vector3 _currentOffset;
    float   _currentDistance;
    float   _currentFOV;
    float   _yaw, _pitch;
    bool    _cursorLocked = true;

    Vector3 _smoothFollowPos;
    Vector3 _followVel;
    float   _currentArmLength;

    ObjectGrabController       _grab;
    PhysicsCharacterController _physics;

    void Awake()
    {
        if (_camera == null)
            _camera = Camera.main;

        if (_crosshairUI == null)
            _crosshairUI = GameObject.Find("CrosshairCanvas");

        if (_camera != null)
        {
            var brain = _camera.GetComponent<CinemachineBrain>();
            if (brain != null) brain.enabled = false;
        }
    }

    void Start()
    {
        if (_followTarget == null) _followTarget = transform;

        Transform root = _followTarget;
        _grab    = root.GetComponentInParent<ObjectGrabController>()
                ?? root.GetComponentInChildren<ObjectGrabController>(true)
                ?? FindFirstObjectByType<ObjectGrabController>();
        _physics = root.GetComponentInParent<PhysicsCharacterController>()
                ?? root.GetComponentInChildren<PhysicsCharacterController>(true)
                ?? FindFirstObjectByType<PhysicsCharacterController>();

        _yaw             = _followTarget.eulerAngles.y;
        _currentOffset   = _normalOffset;
        _currentDistance = _normalDistance;
        _currentFOV      = _normalFOV;

        _smoothFollowPos  = _followTarget.position;
        _followVel        = Vector3.zero;
        _currentArmLength = _normalDistance;

        SetCursorLock(true);
    }

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.Escape))
            SetCursorLock(!_cursorLocked);

        bool isAiming = _cursorLocked
            && _grab != null && _grab.IsHoldingObject
            && Input.GetMouseButton(1);

        float t = Time.deltaTime * _transitionSpeed;
        _currentOffset   = Vector3.Lerp(_currentOffset,   isAiming ? _aimOffset   : _normalOffset,   t);
        _currentDistance = Mathf.Lerp (_currentDistance,  isAiming ? _aimDistance : _normalDistance,  t);
        _currentFOV      = Mathf.Lerp (_currentFOV,       isAiming ? _aimFOV      : _normalFOV,       t);

        if (_camera != null)
            _camera.fieldOfView = _currentFOV;

        if (_crosshairUI != null)
            _crosshairUI.SetActive(isAiming);

        if (_physics != null)
        {
            _physics.IsAiming  = isAiming;
            _physics.CameraYaw = _yaw;
        }
    }

    void LateUpdate()
    {
        if (!_cursorLocked || _camera == null) return;

        _yaw   += Input.GetAxis("Mouse X") * _sensitivity;
        _pitch  = Mathf.Clamp(_pitch - Input.GetAxis("Mouse Y") * _sensitivity,
                               _pitchMin, _pitchMax);

        Quaternion rot = Quaternion.Euler(_pitch, _yaw, 0f);

        // Camera lag: smooth-follow the target so physics jitter never reaches the camera.
        // Snap instead of glide when the target teleports (respawn, stuck-recovery, get-up).
        if (Vector3.Distance(_smoothFollowPos, _followTarget.position) > _teleportSnapThreshold)
        {
            _smoothFollowPos = _followTarget.position;
            _followVel       = Vector3.zero;
        }
        else
        {
            _smoothFollowPos = Vector3.SmoothDamp(_smoothFollowPos, _followTarget.position,
                                                  ref _followVel, _followLag);
        }

        // World-space anchor: Y is always world-up, X/Z are camera-relative
        Vector3 anchor = _smoothFollowPos
                       + Vector3.up * _currentOffset.y
                       + rot * new Vector3(_currentOffset.x, 0f, _currentOffset.z);

        // Collision — sphere-probe from anchor outward along the arm, exclude the character's own layer
        Vector3 armDir     = rot * Vector3.back;
        float   targetDist = _currentDistance;
        int     mask       = ~(1 << _followTarget.gameObject.layer);
        if (Physics.SphereCast(anchor, _probeRadius, armDir, out RaycastHit hit, _currentDistance, mask))
            targetDist = hit.distance;

        // Shorten instantly (never clip through a wall); ease back out when clear (no pop).
        if (targetDist < _currentArmLength)
            _currentArmLength = targetDist;
        else
            _currentArmLength = Mathf.Lerp(_currentArmLength, targetDist,
                                           Time.deltaTime * _collisionReturnSpeed);

        _camera.transform.position = anchor + armDir * _currentArmLength;

        // Look at character at height Y (ignoring X/Z offset so char stays in frame)
        Vector3 lookTarget = _smoothFollowPos + Vector3.up * _currentOffset.y;
        _camera.transform.LookAt(lookTarget);
    }

    void SetCursorLock(bool locked)
    {
        _cursorLocked    = locked;
        Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
        Cursor.visible   = !locked;
    }
}
