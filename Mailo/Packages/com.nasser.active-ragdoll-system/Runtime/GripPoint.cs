using UnityEngine;

namespace NasserActiveRagdoll
{
    /// <summary>
    /// Optional hint transform on a grabbable. A crowbar has one at the handle so it is
    /// always held the right way round; a suitcase has one on the grip and a secondary
    /// on the far end for two-handed carries. A character has one on the collar and one
    /// on each ankle -- which is how you end up dragging a passenger by the leg.
    /// </summary>
    public class GripPoint : MonoBehaviour
    {
        [Tooltip("Only usable as the SECOND hand on a two-handed carry.")]
        public bool secondaryOnly;
        [Tooltip("Higher wins when two grips are equally close.")]
        public float priority = 0f;
    }
}
