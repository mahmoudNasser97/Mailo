using System.Collections.Generic;
using UnityEngine;

public enum SeesawRole { Input, Launcher }

public class SeesawSide : MonoBehaviour
{
    [SerializeField] public SeesawRole role = SeesawRole.Input;
    [SerializeField] float _jumpVelocityThreshold = 1.5f;

    readonly List<SeesawParticipant> _occupants = new();
    SeesawCoordinator _coordinator;

    public IReadOnlyList<SeesawParticipant> Occupants => _occupants;

    public void Init(SeesawCoordinator coordinator)
    {
        _coordinator = coordinator;
    }

    void OnTriggerEnter(Collider other)
    {
        var p = other.GetComponentInParent<SeesawParticipant>();
        Debug.Log($"[SeesawSide:{role}] TriggerEnter — collider={other.name} participant={p?.name ?? "NONE"}");
        if (p != null && !_occupants.Contains(p))
            _occupants.Add(p);
    }

    void OnTriggerExit(Collider other)
    {
        var p = other.GetComponentInParent<SeesawParticipant>();
        Debug.Log($"[SeesawSide:{role}] TriggerExit — collider={other.name} participant={p?.name ?? "NONE"} velocityY={p?.VelocityY:F2}");
        if (p == null) return;

        _occupants.Remove(p);

        if (role == SeesawRole.Input && _coordinator != null)
        {
            if (p.gameObject.activeInHierarchy && p.VelocityY > _jumpVelocityThreshold)
                _coordinator.NotifyJump(p);
            else
                Debug.Log($"[SeesawSide:Input] Jump NOT detected — active={p.gameObject.activeInHierarchy} velocityY={p.VelocityY:F2} threshold={_jumpVelocityThreshold}");
        }
    }
}
