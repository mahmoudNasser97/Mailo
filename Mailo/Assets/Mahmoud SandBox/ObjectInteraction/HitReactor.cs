using UnityEngine;

public class HitReactor : MonoBehaviour
{
    [SerializeField] float _minThrowSpeed   = 4f;
    [SerializeField] float _forceMultiplier = 20f;

    public event System.Action<float, Vector3, Vector3> OnImpact; // impulse, direction, hitPoint

    PuppetRagdollController _ragdoll;

    void Awake()
    {
        _ragdoll = GetComponentInChildren<PuppetRagdollController>();
        if (_ragdoll == null)
            _ragdoll = GetComponentInParent<PuppetRagdollController>();
    }

    public void TakeHit(float throwSpeed, Vector3 direction, Vector3 hitPoint = default)
    {
        if (throwSpeed < _minThrowSpeed) return;

        float impulse = throwSpeed * _forceMultiplier;
        OnImpact?.Invoke(impulse, direction, hitPoint);

        if (_ragdoll != null)
        {
            _ragdoll.ReportImpact(impulse);
            return;
        }

        foreach (Rigidbody rb in GetComponentsInChildren<Rigidbody>())
        {
            if (!rb.isKinematic)
                rb.AddForce(direction * throwSpeed * _forceMultiplier, ForceMode.Impulse);
        }
    }
}
