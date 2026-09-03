using UnityEngine;

namespace NasserActiveRagdoll
{
    /// <summary>
    /// Sits on every physics bone, every prop, every weapon. Turns Unity collisions into
    /// Impacts and routes them to the nearest IImpactReceiver up the hierarchy.
    ///
    /// Both sides of a collision get their own relay, so a thrown NPC hitting a standing
    /// NPC knocks down BOTH of them without a line of code saying so.
    /// </summary>
    public class ImpactRelay : MonoBehaviour
    {
        [Tooltip("Ignore collisions weaker than this. Stops footsteps and resting contacts spamming the bus.")]
        public float minimumImpulse = 0.5f;

        IImpactReceiver _receiver;
        bool _searched;

        public IImpactReceiver Receiver
        {
            get
            {
                if (!_searched) { _receiver = GetComponentInParent<IImpactReceiver>(); _searched = true; }
                return _receiver;
            }
        }

        void OnCollisionEnter(Collision c) => Handle(c);

        void Handle(Collision c)
        {
            float mag = c.impulse.magnitude;
            if (mag < minimumImpulse) return;

            IImpactReceiver r = Receiver;
            if (r == null) return;

            ContactPoint contact = c.GetContact(0);
            Rigidbody other = c.rigidbody;

            // Ask the incoming object whether it is currently dangerous.
            float scale = 1f;
            GameObject instigator = null;
            if (other)
            {
                Projectile p = other.GetComponent<Projectile>();
                if (p != null && p.Armed) { scale = p.impactMultiplier; instigator = p.Instigator; }

                MeleeWeapon w = other.GetComponent<MeleeWeapon>();
                if (w != null) { scale = Mathf.Max(scale, w.ScaleAt(contact.point)); instigator = w.Holder; }
            }

            // Never let something hurt the character currently holding it.
            if (instigator != null && transform.IsChildOf(instigator.transform)) return;

            r.ReceiveImpact(new Impact
            {
                point = contact.point,
                normal = contact.normal,
                impulse = c.impulse,
                source = other,
                instigator = instigator,
                scale = scale,
                receiver = contact.thisCollider
            });
        }
    }
}
