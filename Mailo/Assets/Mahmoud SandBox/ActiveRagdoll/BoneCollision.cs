using UnityEngine;

/// <summary>
/// Add to every physical ragdoll bone. Forwards collision impulses to the controller
/// so it can decide whether to trigger a knockdown.
/// Call Init() from ActiveRagdollController.Awake().
/// </summary>
public class BoneCollision : MonoBehaviour
{
    ActiveRagdollController _controller;

    public void Init(ActiveRagdollController controller) => _controller = controller;

    void Awake()
    {
        // Fallback: find the controller in the parent hierarchy so this works
        // even if Init() was never called (e.g., after domain reload).
        if (_controller == null)
            _controller = GetComponentInParent<ActiveRagdollController>();
    }

    void OnCollisionEnter(Collision col)
    {
        if (_controller != null)
            _controller.OnBoneImpact(col.impulse.magnitude);
    }
}
