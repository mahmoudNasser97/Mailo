using UnityEngine;

namespace NasserActiveRagdoll
{
    /// <summary>
    /// Attached to anything that has just been thrown. Not a bullet -- it is still a
    /// normal rigidbody obeying normal physics. This component only answers the question
    /// "is this dangerous right now, and who is responsible".
    ///
    /// It disarms after the first solid hit so a crate cannot mow down a whole cabin
    /// as it rolls, and it ignores the thrower briefly so you cannot bonk yourself.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public class Projectile : MonoBehaviour
    {
        public float impactMultiplier = 1.6f;
        public float armDuration = 4f;
        public float minimumArmSpeed = 3.5f;
        public float selfImmunity = 0.3f;

        public GameObject Instigator { get; private set; }
        public bool Armed { get; private set; }

        Rigidbody _rb;
        float _armedAt;

        void Awake() => _rb = GetComponent<Rigidbody>();

        public void Arm(GameObject instigator)
        {
            Instigator = instigator;
            Armed = true;
            _armedAt = Time.time;
            enabled = true;
        }

        public void Disarm() { Armed = false; Instigator = null; }

        void FixedUpdate()
        {
            if (!Armed) return;
            if (Time.time - _armedAt > armDuration || _rb.Vel().magnitude < minimumArmSpeed)
                Disarm();
        }

        void OnCollisionEnter(Collision c)
        {
            if (!Armed) return;
            if (Instigator && Time.time - _armedAt < selfImmunity &&
                c.collider.transform.IsChildOf(Instigator.transform)) return;

            // One good hit per throw.
            if (c.impulse.magnitude > 2f) Disarm();
        }
    }
}
