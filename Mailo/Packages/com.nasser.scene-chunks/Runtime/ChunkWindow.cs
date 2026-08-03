using System.Collections.Generic;
using UnityEngine;

namespace Nasser.SceneChunks
{
    /// <summary>
    /// Which transform the streaming window centres on when a separate view (camera)
    /// transform is supplied in addition to the target (player).
    /// </summary>
    public enum StreamingAnchor
    {
        /// <summary>Centre on the target (player). Default; identical to having no view transform.</summary>
        Target,

        /// <summary>Centre on the view (camera) transform.</summary>
        View,

        /// <summary>Centre on the point halfway between the target and the view.</summary>
        Midpoint
    }

    /// <summary>
    /// Pure, allocation-free grid maths for the streamer, kept separate from the MonoBehaviour
    /// so the load/unload decisions can be unit tested without a live scene.
    /// </summary>
    public static class ChunkWindow
    {
        /// <summary>World point the load window should centre on, before any directional bias.</summary>
        public static Vector3 AnchorPosition(StreamingAnchor anchor, Vector3 targetPos, Vector3 viewPos, bool hasView)
        {
            if (!hasView) return targetPos;

            switch (anchor)
            {
                case StreamingAnchor.View: return viewPos;
                case StreamingAnchor.Midpoint: return (targetPos + viewPos) * 0.5f;
                default: return targetPos;
            }
        }

        /// <summary>
        /// A world-space offset that pushes the load window along the view's forward vector.
        /// Flattened onto XZ so camera pitch never changes how far ahead we stream. Returns
        /// zero when bias is off or the forward vector has no horizontal component.
        /// </summary>
        public static Vector3 ForwardBias(Vector3 forward, float chunks, float chunkSize)
        {
            if (chunks == 0f) return Vector3.zero;

            forward.y = 0f;
            float sqr = forward.x * forward.x + forward.z * forward.z;
            if (sqr < 1e-6f) return Vector3.zero;

            float dist = chunks * chunkSize;
            float inv = dist / Mathf.Sqrt(sqr);
            return new Vector3(forward.x * inv, 0f, forward.z * inv);
        }

        /// <summary>True when a coord is inside the load radius of the given centre.</summary>
        public static bool IsWithinLoad(ChunkCoord coord, ChunkCoord center, int loadRadius)
        {
            return ChunkCoord.Distance(coord, center) <= loadRadius;
        }

        /// <summary>True when a coord is past the unload radius of the given centre.</summary>
        public static bool ShouldRelease(ChunkCoord coord, ChunkCoord center, int unloadRadius)
        {
            return ChunkCoord.Distance(coord, center) > unloadRadius;
        }

        /// <summary>Index of the least-recently pooled entry (smallest timestamp), or -1 when empty.</summary>
        public static int OldestIndex(IReadOnlyList<float> pooledAtSeconds)
        {
            if (pooledAtSeconds == null || pooledAtSeconds.Count == 0) return -1;

            int oldest = 0;
            for (int i = 1; i < pooledAtSeconds.Count; i++)
            {
                if (pooledAtSeconds[i] < pooledAtSeconds[oldest]) oldest = i;
            }
            return oldest;
        }
    }
}
