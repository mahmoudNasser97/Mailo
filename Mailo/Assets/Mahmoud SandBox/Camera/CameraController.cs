using UnityEngine;
using Unity.Cinemachine;

public class CameraController : MonoBehaviour
{
    [Header("References")]
    [SerializeField] Transform         _cameraPivot;
    [SerializeField] CinemachineCamera _aimCamera;
    [SerializeField] GameObject        _crosshairUI;

    [Header("Orbit")]
    [SerializeField] float _sensitivity = 2.0f;
    [SerializeField] float _pitchMin    = -30f;
    [SerializeField] float _pitchMax    =  60f;

    [Header("Aim Camera Priorities")]
    [SerializeField] int _aimPriority    = 20;
    [SerializeField] int _normalPriority =  0;

    float                      _yaw;
    float                      _pitch;
    bool                       _cursorLocked = true;
    ObjectGrabController       _grab;
    PhysicsCharacterController _physics;

    void Awake()
    {
        _grab    = GetComponent<ObjectGrabController>();
        _physics = GetComponent<PhysicsCharacterController>();
    }

    void Start()
    {
        _yaw = transform.eulerAngles.y;
        SetCursorLock(true);
    }

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.Escape))
            SetCursorLock(!_cursorLocked);

        bool isAiming = _cursorLocked
            && _grab != null && _grab.IsHoldingObject
            && Input.GetMouseButton(1);

        if (_aimCamera != null)
            _aimCamera.Priority = isAiming ? _aimPriority : _normalPriority;

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
        if (!_cursorLocked) return;

        _yaw   += Input.GetAxis("Mouse X") * _sensitivity;
        _pitch  = Mathf.Clamp(_pitch - Input.GetAxis("Mouse Y") * _sensitivity,
                               _pitchMin, _pitchMax);

        if (_cameraPivot == null) return;
        _cameraPivot.position = transform.position;
        _cameraPivot.rotation = Quaternion.Euler(_pitch, _yaw, 0f);
    }

    void SetCursorLock(bool locked)
    {
        _cursorLocked    = locked;
        Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
        Cursor.visible   = !locked;
    }
}
