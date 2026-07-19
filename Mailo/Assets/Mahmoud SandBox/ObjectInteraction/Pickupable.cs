using UnityEngine;

[RequireComponent(typeof(Rigidbody))]
public class Pickupable : MonoBehaviour
{
    bool    _thrown;
    Vector3 _thrownVelocity;

    public void MarkThrown(Vector3 velocity)
    {
        _thrown         = true;
        _thrownVelocity = velocity;
    }

    public void MarkPickedUp() => _thrown = false;

    void OnCollisionEnter(Collision collision)
    {
        if (!_thrown) return;
        _thrown = false;

        HitReactor reactor = collision.gameObject.GetComponentInParent<HitReactor>();
        if (reactor != null)
            reactor.TakeHit(_thrownVelocity.magnitude, _thrownVelocity.normalized);
    }

    void OnDrawGizmos()
    {
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireCube(transform.position, Vector3.one * 0.3f);
    }
}
