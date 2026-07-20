using System.Collections;
using UnityEngine;

public class NPCThrower : MonoBehaviour
{
    [Header("Acquisition")]
    [SerializeField] float        _pickupRadius = 4f;
    [SerializeField] GameObject[] _throwablePrefabs;
    [SerializeField] Transform    _handBone;

    [Header("Throw")]
    [SerializeField] float _throwAngle        = 45f;
    [SerializeField] float _throwCooldown     = 2.5f;
    [SerializeField] float _throwReleaseDelay = 0.3f;
    [SerializeField] float _rotateSpeed       = 8f;

    static readonly int _throwHash = Animator.StringToHash("Throw");

    Animator  _animator;
    NPCBrain  _brain;
    Rigidbody _held;
    Collider  _heldCollider;
    float     _nextThrowTime;
    bool      _throwing;

    void Awake()
    {
        _animator = GetComponent<Animator>();
        _brain    = GetComponent<NPCBrain>();
    }

    void Update()
    {
        if (_brain.State != NPCState.Throw) return;
        if (_throwing) return;
        if (Time.time < _nextThrowTime) return;
        if (_brain.Player == null) return;

        // Face player before throwing
        Vector3 toPlayer = _brain.Player.position - transform.position;
        toPlayer.y = 0f;
        if (toPlayer.sqrMagnitude > 0.01f)
            transform.rotation = Quaternion.RotateTowards(
                transform.rotation,
                Quaternion.LookRotation(toPlayer.normalized),
                _rotateSpeed * 360f * Time.deltaTime);

        StartCoroutine(ThrowSequence());
    }

    void OnDisable()
    {
        if (_held != null) DropHeld();
        _throwing = false;
    }

    IEnumerator ThrowSequence()
    {
        _throwing = true;

        if (!TryGrabSceneObject())
            TrySpawnThrowable();

        if (_held == null)
        {
            _throwing      = false;
            _nextThrowTime = Time.time + _throwCooldown;
            yield break;
        }

        _animator.SetTrigger(_throwHash);
        yield return new WaitForSeconds(_throwReleaseDelay);

        ReleaseThrowable();
        _nextThrowTime = Time.time + _throwCooldown;
        _throwing      = false;
    }

    bool TryGrabSceneObject()
    {
        Pickupable best     = null;
        float      bestDist = _pickupRadius;

        foreach (Pickupable p in Object.FindObjectsByType<Pickupable>(FindObjectsSortMode.None))
        {
            if (p.transform.parent != null) continue; // already held
            float d = Vector3.Distance(transform.position, p.transform.position);
            if (d < bestDist) { best = p; bestDist = d; }
        }

        if (best == null) return false;

        best.MarkPickedUp();
        Grab(best.GetComponent<Rigidbody>(), best.GetComponent<Collider>());
        return _held != null;
    }

    void TrySpawnThrowable()
    {
        if (_throwablePrefabs == null || _throwablePrefabs.Length == 0) return;

        GameObject prefab  = _throwablePrefabs[Random.Range(0, _throwablePrefabs.Length)];
        Transform  anchor  = _handBone != null ? _handBone : transform;
        GameObject spawned = Instantiate(prefab, anchor.position, anchor.rotation);

        if (spawned.GetComponent<Pickupable>() == null)
            spawned.AddComponent<Pickupable>();
        if (spawned.GetComponent<Rigidbody>() == null)
            spawned.AddComponent<Rigidbody>();

        spawned.GetComponent<Pickupable>().MarkPickedUp();
        Grab(spawned.GetComponent<Rigidbody>(), spawned.GetComponent<Collider>());
    }

    void Grab(Rigidbody rb, Collider col)
    {
        if (rb == null) return;
        _held             = rb;
        _heldCollider     = col;
        _held.isKinematic = true;
        if (_heldCollider != null) _heldCollider.enabled = false;

        Transform anchor = _handBone != null ? _handBone : transform;
        _held.transform.SetParent(anchor);
        _held.transform.localPosition = Vector3.zero;
        _held.transform.localRotation = Quaternion.identity;
    }

    // Called by Animator event on Throw clip keyframe, or by coroutine timer
    public void ReleaseThrowable()
    {
        if (_held == null) return;

        Rigidbody  toThrow  = _held;
        Pickupable pickable = _held.GetComponent<Pickupable>();

        DoRelease();

        if (_brain.Player != null)
        {
            Vector3 from = toThrow.transform.position;
            Vector3 to   = _brain.Player.position + Vector3.up * 1f;

            if (ThrowMath.TryCalculateVelocity(from, to, _throwAngle, out Vector3 vel))
            {
                toThrow.linearVelocity  = vel;
                toThrow.angularVelocity = Random.insideUnitSphere * 5f;
                pickable?.MarkThrown(vel);
            }
        }

        _held         = null;
        _heldCollider = null;
    }

    void DropHeld()
    {
        if (_held == null) return;
        DoRelease();
        _held         = null;
        _heldCollider = null;
    }

    void DoRelease()
    {
        _held.transform.SetParent(null);
        _held.isKinematic = false;
        if (_heldCollider != null) _heldCollider.enabled = true;
    }
}
