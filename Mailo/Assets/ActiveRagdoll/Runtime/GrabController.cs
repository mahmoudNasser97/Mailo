using UnityEngine;

namespace ActiveRagdoll
{
    /// <summary>
    /// Phase 5c. Grab and carry. On grab input, if a hand overlaps a grabbable rigidbody, creates a
    /// runtime <see cref="FixedJoint"/> from the hand to that object with a break force/torque.
    ///
    /// That one component gives grab, carry, two-handed hold, and "it got yanked out of my hands"
    /// during a takedown for free — because the joint breaks under the same impulses that drain poise.
    /// This is why the whole system uses ConfigurableJoint, not ArticulationBody (which rejects
    /// runtime joints) — decided back in Phase 0/1.
    ///
    /// Unity destroys a joint when it breaks, so a null joint reference == released (no OnJointBreak
    /// plumbing needed).
    /// </summary>
    [RequireComponent(typeof(RagdollRig))]
    public class GrabController : MonoBehaviour
    {
        public KeyCode grabKey = KeyCode.E;

        RagdollRig _rig;
        FixedJoint _jointL, _jointR;

        public bool Holding => _jointL != null || _jointR != null;

        void OnEnable()
        {
            _rig = GetComponent<RagdollRig>();
            _rig.RebuildLookup();
        }

        void Update()
        {
            if (Input.GetKeyDown(grabKey))
            {
                if (Holding) Release();
                else Grab();
            }
        }

        void Grab()
        {
            _jointL = TryGrab(BodyPart.HandL);
            _jointR = TryGrab(BodyPart.HandR);
        }

        FixedJoint TryGrab(BodyPart hand)
        {
            if (!_rig.TryGetBone(hand, out var b) || b.physical == null || b.body == null) return null;
            var p = _rig.profile;

            var hits = Physics.OverlapSphere(b.physical.position, p.grabRadius, ~0, QueryTriggerInteraction.Ignore);
            Rigidbody best = null;
            float bestDist = float.MaxValue;
            foreach (var col in hits)
            {
                var rb = col.attachedRigidbody;
                if (rb == null || rb.isKinematic) continue;
                if (_rig.physicalRoot != null && rb.transform.IsChildOf(_rig.physicalRoot)) continue; // not our own bones
                float d = Vector3.Distance(b.physical.position, rb.worldCenterOfMass);
                if (d < bestDist) { bestDist = d; best = rb; }
            }
            if (best == null) return null;

            var joint = b.physical.gameObject.AddComponent<FixedJoint>();
            joint.connectedBody = best;
            joint.breakForce = p.grabBreakForce;
            joint.breakTorque = p.grabBreakTorque;
            joint.enableCollision = false;
            return joint;
        }

        void Release()
        {
            if (_jointL != null) Destroy(_jointL);
            if (_jointR != null) Destroy(_jointR);
            _jointL = _jointR = null;
        }
    }
}
