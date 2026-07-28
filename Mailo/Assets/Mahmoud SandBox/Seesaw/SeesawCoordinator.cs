using System.Collections;
using UnityEngine;

public class SeesawCoordinator : MonoBehaviour
{
    [Header("Sides")]
    [SerializeField] SeesawSide _sideA;
    [SerializeField] SeesawSide _sideB;

    [Header("Launch Tuning")]
    [Tooltip("Base launch speed (m/s) when Side A and Side B weights are equal. Scale up/down to taste.")]
    [SerializeField] float _baseForce       = 8.0f;
    [SerializeField] float _horizontalBias  = 0.3f;
    [SerializeField] float _cooldownSeconds = 2.0f;

    bool _onCooldown;

    void Awake()
    {
        _sideA.Init(this);
        _sideB.Init(this);
    }

    public void NotifyJump(SeesawParticipant jumper)
    {
        Debug.Log($"[SeesawCoordinator] NotifyJump — jumper={jumper.name} cooldown={_onCooldown} sideBCount={_sideB.Occupants.Count}");
        if (_onCooldown) return;
        if (_sideB.Occupants.Count == 0) return;

        // Side A: jumper + anyone still standing on Side A
        float sideAWeight = jumper.WeightKg;
        foreach (var p in _sideA.Occupants)
            sideAWeight += p.WeightKg;

        // Side B: sum of all targets — heavier Side B resists the launch
        float sideBWeight = 0f;
        foreach (var target in _sideB.Occupants)
            sideBWeight += target.WeightKg;
        if (sideBWeight <= 0f) sideBWeight = 1f;

        // Ratio drives the result: A heavier than B → launches far, B heavier → barely moves
        float weightRatio = sideAWeight / sideBWeight;

        // Direction: up + outward from A toward B
        Vector3 awayDir = (_sideB.transform.position - _sideA.transform.position);
        awayDir.y = 0f;
        awayDir = awayDir.sqrMagnitude > 0f ? awayDir.normalized : Vector3.forward;

        Vector3 launchDir    = (Vector3.up + awayDir * _horizontalBias).normalized;
        Vector3 launchVector = launchDir * (weightRatio * _baseForce);

        Debug.Log($"[SeesawCoordinator] sideA={sideAWeight}kg sideB={sideBWeight}kg ratio={weightRatio:F2} speed={launchVector.magnitude:F1}m/s");

        foreach (var target in _sideB.Occupants)
            target.ApplyLaunch(launchVector);

        StartCoroutine(Cooldown());
    }

    IEnumerator Cooldown()
    {
        _onCooldown = true;
        yield return new WaitForSeconds(_cooldownSeconds);
        _onCooldown = false;
    }
}
