using System.Collections.Generic;
using UnityEngine;

namespace NasserActiveRagdoll
{
    /// <summary>
    /// Box trigger that shakes everything loose inside it.
    ///
    /// IMPORTANT for vehicle interiors: do NOT physically move the vehicle. Keep the
    /// fuselage/hull at world origin and fake motion by moving the skybox, clouds and
    /// terrain. The solver works in world space and does not care about your hierarchy;
    /// simulating a rigidbody character inside a fast-moving parent transform will
    /// jitter forever. Turbulence is then just forces inside a stationary tube.
    /// </summary>
    public class TurbulenceField : MonoBehaviour
    {
        [Header("Intensity")]
        [Range(0f, 1f)] public float intensity = 0f;
        public float linearForce = 6f;
        public float angularForce = 3f;

        [Header("Noise")]
        public float noiseSpeed = 1.2f;
        public float airPocketChancePerSecond = 0.15f;
        public float airPocketImpulse = 4.5f;

        [Header("Character response")]
        public float knockdownImpulse = 3.5f;

        readonly HashSet<Rigidbody> _inside = new HashSet<Rigidbody>();
        readonly List<CharacterBody> _characters = new List<CharacterBody>();
        float _seed;

        void Awake() => _seed = Random.value * 1000f;

        void OnTriggerEnter(Collider other)
        {
            Rigidbody rb = other.attachedRigidbody;
            if (rb && !rb.isKinematic) _inside.Add(rb);

            CharacterBody c = other.GetComponentInParent<CharacterBody>();
            if (c && !_characters.Contains(c)) _characters.Add(c);
        }

        void OnTriggerExit(Collider other)
        {
            Rigidbody rb = other.attachedRigidbody;
            if (rb) _inside.Remove(rb);
        }

        void FixedUpdate()
        {
            if (intensity <= 0.001f) return;

            float t = Time.time * noiseSpeed + _seed;
            Vector3 dir = new Vector3(
                Mathf.PerlinNoise(t, 0f) - 0.5f,
                Mathf.PerlinNoise(0f, t) - 0.5f,
                Mathf.PerlinNoise(t, t) - 0.5f) * 2f;

            bool pocket = Random.value < airPocketChancePerSecond * intensity * Time.fixedDeltaTime;
            Vector3 pocketVec = pocket
                ? Vector3.down * airPocketImpulse * intensity * Random.Range(0.6f, 1.4f)
                : Vector3.zero;

            _inside.RemoveWhere(rb => rb == null);

            foreach (Rigidbody rb in _inside)
            {
                rb.AddForce(dir * linearForce * intensity, ForceMode.Acceleration);
                rb.AddTorque(dir * angularForce * intensity, ForceMode.Acceleration);
                if (pocket) rb.AddForce(pocketVec, ForceMode.VelocityChange);
            }

            if (!pocket || pocketVec.magnitude <= knockdownImpulse) return;

            _characters.RemoveAll(c => c == null);
            foreach (CharacterBody c in _characters)
                if (c.Current == CharacterBody.State.Standing) c.Knockdown(pocketVec * 0.5f);
        }
    }
}
