using UnityEngine;

namespace Nasser.SceneChunks
{
    /// <summary>
    /// Tuning for the streamer. Kept as an asset so you can ship different profiles
    /// per platform (mobile / desktop) and swap them at runtime.
    /// </summary>
    [CreateAssetMenu(menuName = "Scene Chunks/Streaming Settings", fileName = "ChunkStreamingSettings")]
    public class ChunkStreamingSettings : ScriptableObject
    {
        [Header("Grid")]
        [Tooltip("World-space size of one chunk on X and Z.")]
        [Min(1f)] public float ChunkSize = 50f;

        [Header("Radii")]
        [Tooltip("Chunks within this Chebyshev distance of the target are kept active. 0 = only the chunk you stand on.")]
        [Min(0)] public int LoadRadius = 2;

        [Tooltip("Extra rings kept resident before a chunk is released. This gap is the hysteresis band that stops chunks thrashing when the player walks back and forth over a boundary. 0 is almost always a mistake.")]
        [Min(0)] public int UnloadPadding = 1;

        [Header("Budget")]
        [Tooltip("How many chunks may be loading at the same time. Low values keep frame time flat but need a bigger LoadRadius to stay ahead of the player.")]
        [Min(1)] public int MaxConcurrentLoads = 2;

        [Tooltip("How many chunks may be released per refresh tick.")]
        [Min(1)] public int MaxReleasesPerTick = 4;

        [Tooltip("Seconds between grid evaluations. The streamer does not need to run every frame.")]
        [Min(0f)] public float RefreshInterval = 0.15f;

        [Header("Pooling")]
        [Tooltip("Deactivate released chunks instead of destroying them, so returning to an area costs nothing.")]
        public bool EnablePooling = true;

        [Tooltip("How many released chunks to keep dormant. Oldest is destroyed first once the cap is hit.")]
        [Min(0)] public int MaxPooledChunks = 12;

        [Header("Editor")]
        public bool DrawGizmos = true;
        [Min(0f)] public float GizmoHeight = 10f;

        /// <summary>Distance past which a chunk is released.</summary>
        public int UnloadRadius
        {
            get { return LoadRadius + Mathf.Max(0, UnloadPadding); }
        }

        /// <summary>Count of chunks that will be active at rest.</summary>
        public int ActiveChunkCount
        {
            get { int side = LoadRadius * 2 + 1; return side * side; }
        }
    }
}
