using System;
using UnityEngine;

namespace NasserActiveRagdoll
{
    /// <summary>
    /// The one component that makes something interactive.
    ///
    /// A cargo crate, a fire axe, and a screaming passenger are all just Grabbables with
    /// different masses. That is the whole point: the hand code never asks "is this a
    /// person". It asks "how heavy is this and where do I hold it".
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public class Grabbable : MonoBehaviour
    {
        [Header("Body")]
        public Rigidbody body;
        [Tooltip("Leave empty to grab at the contact point.")]
        public GripPoint[] gripPoints;

        [Header("Handling")]
        [Tooltip("Above this mass, one hand is not enough -- the grab joint is weakened until both hands are on it.")]
        public float twoHandedMassThreshold = 18f;
        public float grabBreakForce = 2400f;
        public float grabBreakTorque = 1400f;
        [Tooltip("Scales throw impulse. Light junk flies, dense cargo lobs.")]
        public float throwMultiplier = 1f;
        [Tooltip("How hard this drags on the carrier's movement speed. 0 = weightless.")]
        public float encumbrance = 1f;

        [Header("If this is a person")]
        [Tooltip("Set when the grabbable is a bone on a character. Grabbing it puts them in the Grabbed state.")]
        public CharacterBody character;

        [Header("Thrown behaviour")]
        public float projectileMultiplier = 1.6f;

        public event Action<PhysicsHand> Grabbed;
        public event Action<PhysicsHand> Released;

        public bool IsHeld => HeldBy != null;
        public PhysicsHand HeldBy { get; private set; }
        public PhysicsHand SecondHand { get; private set; }
        public bool IsCharacter => character != null;
        public bool NeedsTwoHands => body && body.mass > twoHandedMassThreshold;

        void Reset() => body = GetComponent<Rigidbody>();
        void Awake() { if (!body) body = GetComponent<Rigidbody>(); }

        public void NotifyGrabbed(PhysicsHand hand)
        {
            if (HeldBy == null) HeldBy = hand;
            else if (SecondHand == null) SecondHand = hand;

            if (character != null && HeldBy == hand) character.EnterGrabbed(hand);
            Grabbed?.Invoke(hand);
        }

        public void NotifyReleased(PhysicsHand hand, Vector3 throwImpulse)
        {
            if (SecondHand == hand) SecondHand = null;
            else if (HeldBy == hand) { HeldBy = SecondHand; SecondHand = null; }

            if (character != null && HeldBy == null)
                character.ExitGrabbed(throwImpulse, hand ? hand.Owner : null);

            Released?.Invoke(hand);
        }

        /// <summary>Closest usable grip, or null to grab wherever the hand is.</summary>
        public GripPoint BestGrip(Vector3 handPosition, bool allowSecondary)
        {
            GripPoint best = null;
            float bestScore = float.MaxValue;

            if (gripPoints == null) return null;
            foreach (GripPoint g in gripPoints)
            {
                if (!g) continue;
                if (g.secondaryOnly && !allowSecondary) continue;
                float score = (g.transform.position - handPosition).sqrMagnitude - g.priority;
                if (score < bestScore) { bestScore = score; best = g; }
            }
            return best;
        }

        /// <summary>Arms this object as a projectile. Called by the hand on release.</summary>
        public void ArmAsProjectile(GameObject instigator)
        {
            Projectile p = body.GetComponent<Projectile>();
            if (!p) p = body.gameObject.AddComponent<Projectile>();
            p.impactMultiplier = projectileMultiplier;
            p.Arm(instigator);
        }
    }
}
