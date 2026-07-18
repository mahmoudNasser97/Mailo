using UnityEngine;

[RequireComponent(typeof(Rigidbody))]
public class PuppetImpactReporter : MonoBehaviour
{
    [SerializeField] PuppetRagdollController _controller;

    public void Init(PuppetRagdollController controller) => _controller = controller;

    void OnCollisionEnter(Collision col) => _controller?.ReportImpact(col.impulse.magnitude);
}
