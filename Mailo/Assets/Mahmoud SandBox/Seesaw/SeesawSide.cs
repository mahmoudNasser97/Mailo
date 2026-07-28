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

    static SeesawParticipant FindParticipant(Collider other)
    {
        // First try going up (works when SeesawParticipant is on the same object or an ancestor)
        var p = other.GetComponentInParent<SeesawParticipant>();
        // Fallback: search from the hierarchy root downward — handles PuppetMaster ragdoll bones
        // whose root is separate from the animated character object
        if (p == null)
            p = other.transform.root.GetComponentInChildren<SeesawParticipant>();
        return p;
    }

    void OnTriggerEnter(Collider other)
    {
        var p = FindParticipant(other);
        Debug.Log($"[SeesawSide:{role}] TriggerEnter — collider={other.name} participant={p?.name ?? "NONE"}");
        if (p != null && !_occupants.Contains(p))
            _occupants.Add(p);
    }

    void OnTriggerExit(Collider other)
    {
        var p = FindParticipant(other);
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
