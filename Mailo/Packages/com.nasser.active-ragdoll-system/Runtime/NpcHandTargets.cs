using UnityEngine;

namespace NasserActiveRagdoll
{
    /// <summary>
    /// Hand targets for characters with no camera. Parented to the chest, so an NPC's
    /// arms reach in front of its own torso. Same PhysicsHand code either way.
    /// </summary>
    public class NpcHandTargets : MonoBehaviour
    {
        public Transform chest;
        public Transform leftTarget, rightTarget;
        public Vector3 leftRestLocal = new Vector3(-0.34f, -0.18f, 0.42f);
        public Vector3 rightRestLocal = new Vector3(0.34f, -0.18f, 0.42f);
        [Tooltip("Where the hands reach when this NPC is carrying or grabbing something.")]
        public Vector3 reachOffset = new Vector3(0f, 0.12f, 0.22f);
        public float reachLerp = 8f;

        float _reach;
        public void SetReaching(bool r) => _reach = Mathf.MoveTowards(_reach, r ? 1f : 0f, Time.deltaTime * reachLerp);

        void LateUpdate()
        {
            if (!chest) return;
            Vector3 extra = reachOffset * _reach;
            if (leftTarget)
            {
                leftTarget.position = chest.TransformPoint(leftRestLocal + extra);
                leftTarget.rotation = chest.rotation;
            }
            if (rightTarget)
            {
                rightTarget.position = chest.TransformPoint(rightRestLocal + extra);
                rightTarget.rotation = chest.rotation;
            }
        }
    }
}
