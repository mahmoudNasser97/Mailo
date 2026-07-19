using UnityEngine;

/// <summary>
/// Place on every bone Rigidbody created by the Ragdoll Wizard.
/// Reports collision impulses to PhysicsCharacterController for knockdown detection.
/// PhysicsCharacterController.Awake() calls Init() automatically via GetComponentsInChildren.
/// </summary>
[RequireComponent(typeof(Rigidbody))]
public class PhysicsImpactDetector : MonoBehaviour
{
    PhysicsCharacterController _controller;

    public void Init(PhysicsCharacterController controller)
        => _controller = controller;

    void OnCollisionEnter(Collision col)
        => _controller?.OnImpact(col.impulse.magnitude);
}
