using System.Collections;
using UnityEngine;

public class SeesawParticipant : MonoBehaviour
{
    [SerializeField] float _weightKg = 70f;

    public float     WeightKg => _weightKg;
    public Rigidbody Rb       { get; private set; }

    PhysicsCharacterController _physicsCtrl;
    PuppetRagdollController    _puppetCtrl;

    void Awake()
    {
        Rb           = GetComponent<Rigidbody>() ?? GetComponentInParent<Rigidbody>();
        _physicsCtrl = GetComponent<PhysicsCharacterController>()
                    ?? GetComponentInParent<PhysicsCharacterController>()
                    ?? GetComponentInChildren<PhysicsCharacterController>();
        _puppetCtrl  = GetComponent<PuppetRagdollController>()
                    ?? GetComponentInParent<PuppetRagdollController>()
                    ?? GetComponentInChildren<PuppetRagdollController>();
    }

    public void ApplyLaunch(Vector3 impulse)
    {
        if (_physicsCtrl != null)
        {
            Rb.AddForce(impulse, ForceMode.Impulse);
            _physicsCtrl.ForceRagdoll();
            return;
        }

        if (_puppetCtrl != null)
        {
            StartCoroutine(LaunchNPC(impulse));
            return;
        }

        // Generic fallback
        if (Rb != null)
            Rb.AddForce(impulse, ForceMode.Impulse);
    }

    IEnumerator LaunchNPC(Vector3 impulse)
    {
        // Force ragdoll first (sets pm.pinWeight = 0 immediately)
        _puppetCtrl.ReportImpact(Mathf.Max(impulse.magnitude, _puppetCtrl.knockdownThreshold + 1f));
        // Wait one physics step for PuppetMaster to release pin
        yield return new WaitForFixedUpdate();
        // Apply force to hips so ragdoll body flies
        Rigidbody hips = _puppetCtrl.muscleBodies != null && _puppetCtrl.muscleBodies.Length > 0
            ? _puppetCtrl.muscleBodies[0]
            : Rb;
        if (hips != null)
            hips.AddForce(impulse, ForceMode.Impulse);
    }
}
