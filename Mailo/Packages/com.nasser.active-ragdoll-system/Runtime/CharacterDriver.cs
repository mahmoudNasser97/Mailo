using UnityEngine;

namespace NasserActiveRagdoll
{
    /// <summary>
    /// A player and an NPC are the same character. The ONLY difference is what feeds
    /// this class. Everything downstream -- balance, grabbing, knockdown, throwing --
    /// is identical, which is why a thrown NPC and a thrown player behave the same.
    ///
    /// If you ever find yourself adding an "isNPC" branch below the driver layer,
    /// something has gone wrong.
    /// </summary>
    public abstract class CharacterDriver : MonoBehaviour
    {
        public CharacterBody body;

        protected FloatingCapsuleController Controller => body ? body.controller : null;
        protected bool CanAct => body && body.Current == CharacterBody.State.Standing;

        protected virtual void Reset() => body = GetComponent<CharacterBody>();

        protected void Move(Vector3 worldDir) { if (Controller) Controller.SetMoveInput(worldDir); }
        protected void Face(float yaw) { if (Controller) Controller.SetYaw(yaw); }
        protected void Jump() { if (Controller) Controller.QueueJump(); }
    }
}
