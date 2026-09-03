using System.Collections.Generic;
using UnityEngine;

namespace ActiveRagdoll
{
    /// <summary>
    /// Per-foot ground-contact collector (spec §Phase 2: "foot contact points from
    /// OnCollisionStay on both feet"). BalanceController adds one to each foot bone at
    /// runtime and reads <see cref="IsGrounded"/> / <see cref="Contacts"/>.
    ///
    /// Contacts are accumulated per physics step and only kept when the surface normal is
    /// roughly upward, so brushing a crate side-on doesn't count as support.
    /// </summary>
    public class RagdollFootSensor : MonoBehaviour
    {
        public BodyPart part;

        readonly List<Vector2> _contacts = new List<Vector2>();
        float _stepTime = -1f;
        float _lastContactTime = -1f;

        public IReadOnlyList<Vector2> Contacts => _contacts;

        /// <summary>Grounded if it registered an upward contact within the last physics step.</summary>
        public bool IsGrounded => Time.fixedTime - _lastContactTime <= Time.fixedDeltaTime * 1.5f;

        public float GroundY { get; private set; }

        void OnCollisionStay(Collision c)
        {
            // New physics step → start a fresh contact set.
            if (!Mathf.Approximately(_stepTime, Time.fixedTime))
            {
                _stepTime = Time.fixedTime;
                _contacts.Clear();
            }

            float sumY = 0f;
            int n = 0;
            for (int i = 0; i < c.contactCount; i++)
            {
                var cp = c.GetContact(i);
                if (cp.normal.y < 0.5f) continue; // must be standing on it, not against it
                _contacts.Add(new Vector2(cp.point.x, cp.point.z));
                sumY += cp.point.y;
                n++;
            }
            if (n > 0)
            {
                GroundY = sumY / n;
                _lastContactTime = Time.fixedTime;
            }
        }
    }
}
