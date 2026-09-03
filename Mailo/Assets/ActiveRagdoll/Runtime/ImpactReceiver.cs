using UnityEngine;

namespace ActiveRagdoll
{
    /// <summary>
    /// Phase 4. Sits on every physical bone and turns collisions into poise impacts.
    ///
    /// Uses <see cref="Collision.impulse"/> directly (spec §4a) — NOT a force computed from
    /// relativeVelocity — because c.impulse already accounts for both masses and the solver's
    /// actual resolution, so a heavy slow crate and a light fast ball scale correctly with no
    /// extra work. Collisions with static geometry (no rigidbody) are ignored so footsteps and
    /// landing on the ground don't drain poise.
    /// </summary>
    public class ImpactReceiver : MonoBehaviour
    {
        RagdollRig _rig;
        PoiseController _poise;
        BodyPart _part;
        Rigidbody _rb;
        float _lastImpactTime = -1f;

        public void Init(RagdollRig rig, PoiseController poise, BodyPart part, Rigidbody rb)
        {
            _rig = rig; _poise = poise; _part = part; _rb = rb;
        }

        void OnCollisionEnter(Collision c)
        {
            if (_rig == null || _rig.profile == null || _poise == null) return;
            if (c.rigidbody == null) return; // static ground / world — not an impact (skip footsteps)

            var p = _rig.profile;

            Vector3 J = c.impulse;
            if (c.contactCount > 0 && Vector3.Dot(J, c.GetContact(0).normal) < 0f) J = -J; // normalise sign

            float severity = J.magnitude;
            if (severity < p.ignoreThreshold) return;               // brushing past a crate
            if (Time.time - _lastImpactTime < p.impactCooldown) return; // per-bone cooldown

            severity = Mathf.Min(severity, p.maxImpulse);            // anti-tunnelling clamp
            _lastImpactTime = Time.time;

            _poise.RegisterImpact(new Impact
            {
                point = c.contactCount > 0 ? c.GetContact(0).point : (_rb != null ? _rb.worldCenterOfMass : transform.position),
                impulse = J,
                body = _rb,
                bodyPart = _part,
                severity = severity,
            });
        }
    }
}
