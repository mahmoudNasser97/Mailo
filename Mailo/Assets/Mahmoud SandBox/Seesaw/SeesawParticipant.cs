using System.Collections;
using UnityEngine;

public class SeesawParticipant : MonoBehaviour
{
    [SerializeField] float _weightKg = 70f;

    public float     WeightKg => _weightKg;
    public Rigidbody Rb       { get; private set; }

    // Y velocity regardless of whether the character uses Rigidbody or CharacterController
    public float VelocityY
    {
        get
        {
            if (Rb != null)         return Rb.linearVelocity.y;
            if (_charCtrl != null)  return _charCtrl.velocity.y;
            return 0f;
        }
    }

    PhysicsCharacterController _physicsCtrl;
    PuppetRagdollController    _puppetCtrl;
    CharacterController        _charCtrl;

    void Awake()
    {
        Rb           = GetComponent<Rigidbody>()
                    ?? GetComponentInParent<Rigidbody>()
                    ?? GetComponentInChildren<Rigidbody>();        // ragdoll bones are children
        _charCtrl    = GetComponent<CharacterController>()
                    ?? GetComponentInParent<CharacterController>()
                    ?? GetComponentInChildren<CharacterController>();
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
            StartCoroutine(LaunchPlayer(impulse));
            return;
        }

        if (_puppetCtrl != null)
        {
            StartCoroutine(LaunchNPC(impulse));
            return;
        }

        if (Rb != null)
            Rb.AddForce(impulse, ForceMode.VelocityChange);
    }

    IEnumerator LaunchNPC(Vector3 impulse)
    {
        // Force ragdoll first (sets pm.pinWeight = 0 immediately)
        _puppetCtrl.ReportImpact(_puppetCtrl.knockdownThreshold + 1f);
        // Wait one physics step for PuppetMaster to release pin
        yield return new WaitForFixedUpdate();
        // Apply force to hips so ragdoll body flies
        Rigidbody hips = _puppetCtrl.muscleBodies != null && _puppetCtrl.muscleBodies.Length > 0
            ? _puppetCtrl.muscleBodies[0]
            : Rb;
        if (hips != null)
            hips.AddForce(impulse, ForceMode.VelocityChange);
    }

    IEnumerator LaunchPlayer(Vector3 impulse)
    {
        _physicsCtrl.ForceRagdoll();
        yield return new WaitForFixedUpdate();
        if (Rb != null)
            Rb.AddForce(impulse, ForceMode.VelocityChange);
    }
}
