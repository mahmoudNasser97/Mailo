using UnityEngine;

namespace NasserActiveRagdoll
{
    /// <summary>
    /// The single currency of violence in the game.
    ///
    /// A punch, a swung crowbar, a thrown suitcase, a thrown NPC, and falling down the
    /// stairs all produce one of these. Nothing in the codebase special-cases
    /// "thrown crate hits passenger" -- if you find yourself writing that, you have
    /// two systems where you need one.
    /// </summary>
    public struct Impact
    {
        public Vector3 point;
        public Vector3 normal;
        public Vector3 impulse;      // newton-seconds, direction included
        public Rigidbody source;     // the thing that hit us
        public GameObject instigator;// who threw or swung it. May be null (environment)
        public float scale;          // weapon / projectile multiplier, 1 = bare collision
        public Collider receiver;    // which of OUR colliders was hit -- lets you weight headshots

        public float Magnitude => impulse.magnitude * scale;
    }

    public interface IImpactReceiver
    {
        void ReceiveImpact(in Impact impact);
    }
}
