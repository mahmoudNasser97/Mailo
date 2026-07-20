using System.Collections;
using UnityEngine;

public class ObjectGrabController : MonoBehaviour
{
    [Header("Pickup")]
    [SerializeField] Transform _holdPoint;
    [SerializeField] float     _pickupRange = 3f;

    [Header("Throw Arc")]
    [SerializeField] float _throwAngle    = 45f;
    [SerializeField] float _maxThrowRange = 30f;
    [SerializeField] float _throwSpin     = 6f;
    [SerializeField] int   _arcPoints     = 40;
    [SerializeField] float _arcTimeStep   = 0.05f;

    [Header("Arc Visual")]
    [SerializeField] float _arcWidth = 0.04f;
    [SerializeField] Color _arcColor = Color.yellow;

    [Header("Animation")]
    [SerializeField] Animator _animator;
    [SerializeField] string   _pickupStateName    = "PickUp";
    [SerializeField] string   _throwStateName     = "Throw";
    [SerializeField] string   _returnStateName    = "Locomotion";
    [SerializeField] float    _pickupDuration     = 0.8f;
    [SerializeField] float    _throwDuration      = 0.5f;
    [SerializeField] float    _crossFadeDuration  = 0.15f;

    Rigidbody    _held;
    Collider     _heldCollider;
    Coroutine    _returnRoutine;
    LineRenderer _arc;
    Vector3      _throwVelocity;
    bool         _canThrow;

    public bool IsHoldingObject => _held != null;

    void Awake()
    {
        if (_animator == null)
            _animator = GetComponentInParent<Animator>();

        GameObject arcObj   = new GameObject("ThrowArc");
        arcObj.transform.SetParent(transform);
        _arc               = arcObj.AddComponent<LineRenderer>();
        _arc.startWidth    = _arcWidth;
        _arc.endWidth      = _arcWidth * 0.2f;
        _arc.useWorldSpace = true;
        _arc.material      = new Material(Shader.Find("Sprites/Default"));
        _arc.startColor    = _arcColor;
        _arc.endColor      = new Color(_arcColor.r, _arcColor.g, _arcColor.b, 0f);
        _arc.enabled       = false;
    }

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.F))
        {
            if (_held != null) Drop();
            else               TryPickup();
        }

        if (_held != null)
        {
            UpdateArc();
            if (_canThrow && Input.GetMouseButtonDown(0))
                Throw();
        }
    }

    void TryPickup()
    {
        Pickupable[] all     = Object.FindObjectsByType<Pickupable>(FindObjectsSortMode.None);
        Pickupable   nearest = null;
        float        best    = _pickupRange;

        foreach (Pickupable p in all)
        {
            float d = Vector3.Distance(transform.position, p.transform.position);
            if (d < best) { nearest = p; best = d; }
        }

        if (nearest == null) return;

        _held             = nearest.GetComponent<Rigidbody>();
        _heldCollider     = nearest.GetComponent<Collider>();
        _held.isKinematic = true;
        nearest.MarkPickedUp();

        if (_heldCollider != null)
            _heldCollider.enabled = false;

        Transform anchor = _holdPoint != null ? _holdPoint : transform;
        nearest.transform.SetParent(anchor);
        nearest.transform.localPosition = _holdPoint != null ? Vector3.zero : new Vector3(0f, 0.4f, 0.6f);
        nearest.transform.localRotation = Quaternion.identity;

        PlayAnim(_pickupStateName, _pickupDuration);
    }

    void UpdateArc()
    {
        if (Camera.main == null || _held == null) { _arc.enabled = false; return; }

        Vector3 screenCenter = new Vector3(Screen.width * 0.5f, Screen.height * 0.5f, 0f);
        Ray     ray          = Camera.main.ScreenPointToRay(screenCenter);

        Vector3 target = Physics.Raycast(ray, out RaycastHit hit, _maxThrowRange)
            ? hit.point
            : ray.origin + ray.direction * _maxThrowRange;

        // Use the object's actual world position so the arc starts exactly where it will be released
        Vector3 start = _held.transform.position;

        _canThrow = ThrowMath.TryCalculateVelocity(start, target, _throwAngle, out _throwVelocity);

        if (!_canThrow)
        {
            _arc.enabled = false;
            return;
        }

        // Simulate arc including rigidbody drag so preview matches real physics
        Vector3[] positions = new Vector3[_arcPoints];
        Vector3   pos       = start;
        Vector3   vel       = _throwVelocity;
        float     drag      = _held.linearDamping;

        for (int i = 0; i < _arcPoints; i++)
        {
            positions[i]  = pos;
            vel          += Physics.gravity * _arcTimeStep;
            vel          *= Mathf.Clamp01(1f - drag * _arcTimeStep);
            pos          += vel * _arcTimeStep;
        }

        _arc.positionCount = _arcPoints;
        _arc.SetPositions(positions);
        _arc.enabled = true;
    }


    void Drop()
    {
        _arc.enabled = false;
        _canThrow    = false;
        Release();
        _held         = null;
        _heldCollider = null;
        ReturnToLocomotionNow();
    }

    void Throw()
    {
        _arc.enabled = false;
        _canThrow    = false;

        // Save collider ref before Release() re-enables it
        Collider thrownCol = _heldCollider;

        Release();

        // Prevent the object from bouncing off the character's own collider mid-release
        CharacterController cc = GetComponent<CharacterController>();
        if (cc != null && thrownCol != null)
            StartCoroutine(IgnoreCollisionBriefly(cc, thrownCol));

        _held.linearVelocity  = _throwVelocity;
        _held.angularVelocity = Random.insideUnitSphere * _throwSpin;

        Pickupable p = _held.GetComponent<Pickupable>();
        if (p != null) p.MarkThrown(_throwVelocity);

        _held         = null;
        _heldCollider = null;

        PlayAnim(_throwStateName, _throwDuration);
    }

    void Release()
    {
        _held.transform.SetParent(null);
        _held.isKinematic = false;

        if (_heldCollider != null)
            _heldCollider.enabled = true;
    }

    IEnumerator IgnoreCollisionBriefly(Collider a, Collider b)
    {
        Physics.IgnoreCollision(a, b, true);
        yield return new WaitForSeconds(0.5f);
        if (a != null && b != null)
            Physics.IgnoreCollision(a, b, false);
    }

    void PlayAnim(string stateName, float duration)
    {
        if (_animator == null) return;
        if (_returnRoutine != null) StopCoroutine(_returnRoutine);
        _animator.CrossFade(stateName, _crossFadeDuration);
        _returnRoutine = StartCoroutine(ReturnAfter(duration));
    }

    void ReturnToLocomotionNow()
    {
        if (_animator == null) return;
        if (_returnRoutine != null) StopCoroutine(_returnRoutine);
        _animator.CrossFade(_returnStateName, _crossFadeDuration);
        _returnRoutine = null;
    }

    IEnumerator ReturnAfter(float delay)
    {
        yield return new WaitForSeconds(delay);
        if (_animator != null)
            _animator.CrossFade(_returnStateName, _crossFadeDuration);
        _returnRoutine = null;
    }
}
