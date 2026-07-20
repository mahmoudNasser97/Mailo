using UnityEngine;
using Unity.Cinemachine;

public class CameraController : MonoBehaviour
{
    [Header("References")]
    [SerializeField] Camera     _camera;
    [SerializeField] Transform  _followTarget;
    [SerializeField] GameObject _crosshairUI;

    [Header("Orbit")]
    [SerializeField] float _sensitivity    = 2.0f;
    [SerializeField] float _pitchMin       = -30f;
    [SerializeField] float _pitchMax       =  60f;

    [Header("Normal View")]
    [SerializeField] float _normalDistance = 3.5f;
    [SerializeField] float _normalFOV      = 65f;

    [Header("Aim View")]
    [SerializeField] float _aimDistance      = 2.5f;
    [SerializeField] float _aimFOV           = 50f;
    [SerializeField] float _aimShoulderOffset = 0.7f;

    [Header("Position")]
    [SerializeField] float _shoulderOffset = 0.4f;
    [SerializeField] float _heightOffset   = 1.2f;

    float _yaw, _pitch;
    float _currentDistance;
    float _currentFOV;
    float _currentShoulder;
    bool  _cursorLocked = true;

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

        // Search full hierarchy then fall back to scene-wide search
        Transform root = _followTarget;
        _grab    = root.GetComponentInParent<ObjectGrabController>()
                ?? root.GetComponentInChildren<ObjectGrabController>(true)
                ?? FindFirstObjectByType<ObjectGrabController>();
        _physics = root.GetComponentInParent<PhysicsCharacterController>()
                ?? root.GetComponentInChildren<PhysicsCharacterController>(true)
                ?? FindFirstObjectByType<PhysicsCharacterController>();

        _yaw             = _followTarget.eulerAngles.y;
        _currentDistance = _normalDistance;
        _currentFOV      = _normalFOV;
        _currentShoulder = _shoulderOffset;
        SetCursorLock(true);
    }

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.Escape))
            SetCursorLock(!_cursorLocked);

        bool isAiming = _cursorLocked
            && _grab != null && _grab.IsHoldingObject
            && Input.GetMouseButton(1);

        float targetDist     = isAiming ? _aimDistance       : _normalDistance;
        float targetFOV      = isAiming ? _aimFOV            : _normalFOV;
        float targetShoulder = isAiming ? _aimShoulderOffset  : _shoulderOffset;

        float t = Time.deltaTime * 12f;
        _currentDistance = Mathf.Lerp(_currentDistance, targetDist,     t);
        _currentFOV      = Mathf.Lerp(_currentFOV,      targetFOV,      t);
        _currentShoulder = Mathf.Lerp(_currentShoulder, targetShoulder, t);

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

        Quaternion rot        = Quaternion.Euler(_pitch, _yaw, 0f);
        Vector3    focusPoint = _followTarget.position + Vector3.up * _heightOffset;
        Vector3    rightShift = rot * Vector3.right * _currentShoulder;
        Vector3    desiredPos = focusPoint + rightShift + rot * (Vector3.back * _currentDistance);

        // Collision — exclude character's own layer so the linecast never hits the player
        int     charLayer    = _followTarget.gameObject.layer;
        int     mask         = ~(1 << charLayer);
        Vector3 origin       = focusPoint + rightShift;

        if (Physics.Linecast(origin, desiredPos, out RaycastHit hit, mask))
            _camera.transform.position = hit.point + hit.normal * 0.15f;
        else
            _camera.transform.position = desiredPos;

        _camera.transform.LookAt(focusPoint + rightShift * 0.4f);
    }

    void SetCursorLock(bool locked)
    {
        _cursorLocked    = locked;
        Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
        Cursor.visible   = !locked;
    }
}
