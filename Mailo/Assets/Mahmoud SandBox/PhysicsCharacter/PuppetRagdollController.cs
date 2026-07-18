using System.Collections;
using UnityEngine;
using RootMotion.Dynamics;

public enum PuppetPhysicsState { Balanced, Ragdoll, GettingUp }

public class PuppetRagdollController : MonoBehaviour
{
    [Header("Ragdoll")]
    public float knockdownThreshold   = 20f;
    public float settleDelay          = 1.5f;
    public float settleSpeedThreshold = 0.4f;
    public float maxSettleWait        = 6f;
    public float pinRestoreTime       = 1.2f;

    [Header("References")]
    public PuppetMaster      pm;
    public PuppetMoverSimple mover;
    public Rigidbody[]       muscleBodies;

    public PuppetPhysicsState State { get; private set; } = PuppetPhysicsState.Balanced;

    bool _knockdownPending;

    public void ReportImpact(float impulse)
    {
        if (State != PuppetPhysicsState.Balanced || _knockdownPending) return;
        if (impulse < knockdownThreshold) return;
        _knockdownPending = true;
        StartCoroutine(RagdollSequence());
    }

    IEnumerator RagdollSequence()
    {
        State = PuppetPhysicsState.Ragdoll;
        if (mover != null) mover.enabled = false;
        pm.pinWeight = 0f;

        float elapsed   = 0f;
        float stillTime = 0f;

        while (elapsed < maxSettleWait)
        {
            yield return new WaitForFixedUpdate();
            elapsed += Time.fixedDeltaTime;

            float maxSpd = 0f;
            foreach (var rb in muscleBodies)
                if (rb != null) maxSpd = Mathf.Max(maxSpd, rb.linearVelocity.magnitude);

            stillTime = maxSpd < settleSpeedThreshold
                ? stillTime + Time.fixedDeltaTime
                : 0f;

            if (stillTime >= settleDelay) break;
        }

        _knockdownPending = false;
        StartCoroutine(GetUpSequence());
    }

    IEnumerator GetUpSequence()
    {
        State = PuppetPhysicsState.GettingUp;

        float elapsed = 0f;
        while (elapsed < pinRestoreTime)
        {
            elapsed += Time.deltaTime;
            pm.pinWeight = Mathf.Lerp(0f, 1f, elapsed / pinRestoreTime);
            yield return null;
        }
        pm.pinWeight = 1f;

        if (mover != null) mover.enabled = true;
        State = PuppetPhysicsState.Balanced;
    }
}
