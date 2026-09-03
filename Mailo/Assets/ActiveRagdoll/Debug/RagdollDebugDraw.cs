using UnityEngine;

namespace ActiveRagdoll
{
    /// <summary>
    /// Phase 0 debug gizmos: centre of mass (sphere), CoM velocity (ray), and a marker
    /// per physical bone. Support polygon and capture-point gizmos arrive in Phase 2.
    /// Uses only UnityEngine gizmos so it lives in the runtime assembly.
    /// </summary>
    [RequireComponent(typeof(RagdollRig))]
    public class RagdollDebugDraw : MonoBehaviour
    {
        public bool draw = true;
        public Color comColor = new Color(1f, 0.85f, 0.1f);
        public Color velocityColor = new Color(0.2f, 0.8f, 1f);
        public Color boneColor = new Color(1f, 1f, 1f, 0.35f);
        public float comRadius = 0.06f;
        public float boneRadius = 0.025f;

        RagdollRig _rig;
        RagdollRig Rig => _rig != null ? _rig : (_rig = GetComponent<RagdollRig>());

        void OnDrawGizmos()
        {
            if (!draw || Rig == null || Rig.bones == null) return;

            // Bones.
            Gizmos.color = boneColor;
            foreach (var b in Rig.bones)
            {
                if (b?.physical == null) continue;
                Gizmos.DrawSphere(b.physical.position, boneRadius);
                if (b.joint != null && b.joint.connectedBody != null)
                    Gizmos.DrawLine(b.physical.position, b.joint.connectedBody.transform.position);
            }

            // Centre of mass — only meaningful once rigidbodies exist.
            if (Rig.TotalMass <= 0f) return;
            Vector3 com = Rig.CenterOfMass;

            Gizmos.color = comColor;
            Gizmos.DrawSphere(com, comRadius);

            Gizmos.color = velocityColor;
            Gizmos.DrawLine(com, com + Rig.CenterOfMassVelocity * 0.25f);
        }
    }
}
