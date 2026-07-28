using System.Collections;
using UnityEngine;

public class SeesawCoordinator : MonoBehaviour
{
    [Header("Sides")]
    [SerializeField] SeesawSide _sideA;
    [SerializeField] SeesawSide _sideB;

    [Header("Launch Tuning")]
    [SerializeField] float _forcePerKg      = 3.0f;
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
        if (_onCooldown) return;
        if (_sideB.Occupants.Count == 0) return;

        // Sum weight: remaining Side A occupants + the jumper
        float totalWeight = jumper.WeightKg;
        foreach (var p in _sideA.Occupants)
            totalWeight += p.WeightKg;

        // Direction: up + outward from A toward B
        Vector3 awayDir = (_sideB.transform.position - _sideA.transform.position);
        awayDir.y = 0f;
        awayDir = awayDir.sqrMagnitude > 0f ? awayDir.normalized : Vector3.forward;

        Vector3 launchDir = (Vector3.up + awayDir * _horizontalBias).normalized;
        Vector3 impulse   = launchDir * (totalWeight * _forcePerKg);

        foreach (var target in _sideB.Occupants)
            target.ApplyLaunch(impulse);

        StartCoroutine(Cooldown());
    }

    IEnumerator Cooldown()
    {
        _onCooldown = true;
        yield return new WaitForSeconds(_cooldownSeconds);
        _onCooldown = false;
    }
}
