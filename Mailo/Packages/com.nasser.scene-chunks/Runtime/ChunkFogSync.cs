using UnityEngine;

namespace Nasser.SceneChunks
{
    /// <summary>
    /// Keeps <see cref="RenderSettings"/> linear fog matched to the streamed region, so the
    /// player can never see past the loaded world. Fog end is set to LoadRadius x ChunkSize
    /// (or a manual override) and fog start to a fraction of that.
    ///
    /// This is the one place the "you can never see past the loaded region" invariant lives,
    /// rather than being tribal knowledge spread across the lighting settings.
    /// </summary>
    [ExecuteAlways]
    [AddComponentMenu("Scene Chunks/Chunk Fog Sync")]
    public class ChunkFogSync : MonoBehaviour
    {
        [Tooltip("Settings whose LoadRadius x ChunkSize drives the fog end distance.")]
        [SerializeField] private ChunkStreamingSettings settings;

        [Tooltip("Master toggle. When off, this component leaves RenderSettings untouched.")]
        [SerializeField] private bool apply = true;

        [Tooltip("Fog start as a fraction of fog end. 0.6 means fog begins at 60% of the loaded distance.")]
        [Range(0f, 1f)]
        [SerializeField] private float startFraction = 0.6f;

        [Tooltip("Ignore the settings and use the manual end distance below instead.")]
        [SerializeField] private bool overrideDistance = false;

        [Tooltip("Fog end distance in metres, used when Override Distance is on.")]
        [Min(1f)]
        [SerializeField] private float manualEndDistance = 250f;

        [Tooltip("Force fog on and switch it to Linear mode. Off = only rewrite the distances and leave the mode/enabled flag alone.")]
        [SerializeField] private bool forceLinearFog = true;

        /// <summary>Settings whose LoadRadius x ChunkSize drives the fog end distance.</summary>
        public ChunkStreamingSettings Settings { get { return settings; } set { settings = value; } }

        /// <summary>The fog end distance this component would apply right now.</summary>
        public float ResolvedEndDistance
        {
            get
            {
                if (overrideDistance || settings == null) return manualEndDistance;
                return settings.LoadRadius * settings.ChunkSize;
            }
        }

        private void OnEnable()
        {
            Apply();
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            Apply();
        }
#endif

        /// <summary>Write the resolved fog distances into <see cref="RenderSettings"/>.</summary>
        [ContextMenu("Apply Now")]
        public void Apply()
        {
            if (!apply) return;

            float end = Mathf.Max(1f, ResolvedEndDistance);
            float start = Mathf.Clamp01(startFraction) * end;

            if (forceLinearFog)
            {
                RenderSettings.fog = true;
                RenderSettings.fogMode = FogMode.Linear;
            }

            RenderSettings.fogStartDistance = start;
            RenderSettings.fogEndDistance = end;
        }
    }
}
