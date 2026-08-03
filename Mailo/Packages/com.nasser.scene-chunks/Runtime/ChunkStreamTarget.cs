using UnityEngine;

namespace Nasser.SceneChunks
{
    /// <summary>
    /// Put this on the local player. It binds itself to the streamer on spawn, which is
    /// what you want in multiplayer where the player object does not exist at scene load
    /// and remote players must NOT drive streaming.
    /// </summary>
    [AddComponentMenu("Scene Chunks/Chunk Stream Target")]
    public class ChunkStreamTarget : MonoBehaviour
    {
        [Tooltip("Leave on for single player. In multiplayer, set this from code only for the locally owned player.")]
        [SerializeField] private bool bindOnEnable = true;

        [Tooltip("Streamer to drive. Falls back to ChunkStreamer.Instance.")]
        [SerializeField] private ChunkStreamer streamer;

        private void OnEnable()
        {
            if (bindOnEnable) Bind();
        }

        /// <summary>Call this manually for the local player in a networked spawn callback.</summary>
        public void Bind()
        {
            ChunkStreamer s = streamer != null ? streamer : ChunkStreamer.Instance;
            if (s == null)
            {
                Debug.LogWarning("[SceneChunks] No ChunkStreamer found to bind to.", this);
                return;
            }
            s.SetTarget(transform);
        }
    }
}
