using System;
using System.Collections;
using UnityEngine;

namespace Nasser.SceneChunks
{
    /// <summary>
    /// Where chunk content comes from. The streamer owns *when* a chunk lives or dies;
    /// the provider owns *how* it is produced. Subclass this to stream from Addressables,
    /// an asset bundle, a procedural generator, or anything else.
    /// </summary>
    public abstract class ChunkProvider : MonoBehaviour
    {
        /// <summary>Return false for coords outside the authored world so the streamer skips them.</summary>
        public abstract bool Exists(ChunkCoord coord);

        /// <summary>
        /// Produce the chunk. Yield across frames for anything expensive.
        /// Call onLoaded exactly once, with the root object (may be null for scene-based providers).
        /// </summary>
        public abstract IEnumerator Load(ChunkCoord coord, Transform parent, Action<GameObject> onLoaded);

        /// <summary>Tear the chunk down for good. Only called when pooling is off or the pool evicts it.</summary>
        public abstract void Unload(ChunkCoord coord, GameObject root);

        /// <summary>
        /// False for providers whose chunks cannot simply be deactivated and reused
        /// (additive scenes, for example).
        /// </summary>
        public virtual bool SupportsPooling { get { return true; } }

        /// <summary>Optional: bounds of the authored world, used by editor gizmos.</summary>
        public virtual bool TryGetWorldExtents(out ChunkCoord min, out ChunkCoord max)
        {
            min = default(ChunkCoord);
            max = default(ChunkCoord);
            return false;
        }
    }
}
