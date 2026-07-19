using UnityEngine;

public class HitReactor : MonoBehaviour
{
    [SerializeField] float _minThrowSpeed   = 4f;
    [SerializeField] float _forceMultiplier = 20f;

    PuppetRagdollController _ragdoll;

    void Awake()
    {
        _ragdoll = GetComponentInChildren<PuppetRagdollController>();
        if (_ragdoll == null)
            _ragdoll = GetComponentInParent<PuppetRagdollController>();
    }

    public void TakeHit(float throwSpeed, Vector3 direction)
    {
        if (throwSpeed < _minThrowSpeed) return;

        if (_ragdoll != null)
        {
            _ragdoll.ReportImpact(throwSpeed * _forceMultiplier);
            return;
        }

        // Generic fallback: push all active rigidbodies in the hierarchy
        foreach (Rigidbody rb in GetComponentsInChildren<Rigidbody>())
        {
            if (!rb.isKinematic)
                rb.AddForce(direction * throwSpeed * _forceMultiplier, ForceMode.Impulse);
        }
    }
}
