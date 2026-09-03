using UnityEngine;

namespace NasserActiveRagdoll
{
    /// <summary>
    /// A weapon with no attack animation, no hitbox window, and no damage number.
    ///
    /// It is a grabbable rigidbody held by a physics hand. How hard it hits is the actual
    /// velocity of the actual contact point at the moment of the actual collision. Swing
    /// slowly and it taps. Swing while spinning your whole body and it flattens someone.
    /// The skill ceiling comes from the physics, not from a combo table.
    ///
    /// Put this alongside Grabbable on the weapon root.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    [RequireComponent(typeof(Grabbable))]
    public class MeleeWeapon : MonoBehaviour
    {
        [Header("Damage geometry")]
        [Tooltip("Local point where the business end is. Contacts near here hit hardest.")]
        public Transform headPoint;
        [Tooltip("Contacts this far from headPoint fall off to the minimum multiplier.")]
        public float falloffDistance = 0.6f;
        public float headMultiplier = 2.2f;
        public float handleMultiplier = 0.4f;

        [Header("Edge alignment")]
        [Tooltip("Local direction of the striking edge. A flat slap does far less than an aligned swing.")]
        public Vector3 edgeAxis = Vector3.forward;
        [Range(0f, 1f)] public float edgeInfluence = 0.5f;

        [Header("Swing")]
        [Tooltip("Below this contact speed the weapon is inert -- resting on a table hurts nobody.")]
        public float minimumSwingSpeed = 2.5f;
        public float speedReference = 9f;
        public float maximumMultiplier = 4f;

        public GameObject Holder
        {
            get
            {
                Grabbable g = _grabbable;
                if (g == null || g.HeldBy == null) return null;
                CharacterBody o = g.HeldBy.Owner;
                return o ? o.gameObject : null;
            }
        }

        Rigidbody _rb;
        Grabbable _grabbable;

        void Awake()
        {
            _rb = GetComponent<Rigidbody>();
            _grabbable = GetComponent<Grabbable>();
            if (!headPoint) headPoint = transform;

            // Weapons must report their own collisions even when nobody is holding them.
            if (!GetComponent<ImpactRelay>()) gameObject.AddComponent<ImpactRelay>();
        }

        /// <summary>
        /// Called by the victim's ImpactRelay. Returns the multiplier this weapon earns
        /// for a contact at that world point, right now.
        /// </summary>
        public float ScaleAt(Vector3 worldPoint)
        {
            Vector3 pointVel = _rb.GetPointVelocity(worldPoint);
            float speed = pointVel.magnitude;
            if (speed < minimumSwingSpeed) return 0.1f;

            // Where along the weapon did it land?
            float dist = Vector3.Distance(worldPoint, headPoint.position);
            float positional = Mathf.Lerp(headMultiplier, handleMultiplier,
                                          Mathf.Clamp01(dist / Mathf.Max(0.01f, falloffDistance)));

            // Was the edge leading, or did it land flat?
            Vector3 edge = transform.TransformDirection(edgeAxis.normalized);
            float alignment = Mathf.Abs(Vector3.Dot(edge, pointVel.normalized));
            float edgeFactor = Mathf.Lerp(1f, alignment, edgeInfluence);

            float speedFactor = speed / Mathf.Max(0.01f, speedReference);

            return Mathf.Min(maximumMultiplier, positional * edgeFactor * speedFactor);
        }

        void OnDrawGizmosSelected()
        {
            if (!headPoint) return;
            Gizmos.color = Color.red;
            Gizmos.DrawWireSphere(headPoint.position, 0.06f);
            Gizmos.color = Color.cyan;
            Gizmos.DrawRay(headPoint.position, transform.TransformDirection(edgeAxis.normalized) * 0.25f);
        }
    }
}
