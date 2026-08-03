using Nasser.SceneChunks;
using UnityEditor;
using UnityEngine;

namespace Nasser.SceneChunks.EditorTools
{
    [CustomEditor(typeof(ChunkStreamer))]
    public class ChunkStreamerEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            ChunkStreamer streamer = (ChunkStreamer)target;
            ChunkStreamingSettings settings = streamer.Settings;

            EditorGUILayout.Space(8f);

            if (settings == null)
            {
                EditorGUILayout.HelpBox("No settings assigned. Tools > Scene Chunks > Streaming Setup can create one.", MessageType.Warning);
            }
            else
            {
                if (settings.UnloadPadding == 0)
                {
                    EditorGUILayout.HelpBox(
                        "Unload padding is 0, so chunks load and unload at the same boundary. " +
                        "A player pacing across an edge will thrash the same chunk. Use at least 1.",
                        MessageType.Warning);
                }

                if (settings.EnablePooling && settings.MaxPooledChunks == 0)
                {
                    EditorGUILayout.HelpBox("Pooling is on but the pool size is 0, so nothing is ever reused.", MessageType.Warning);
                }

                if (streamer.Provider != null && !streamer.Provider.SupportsPooling && settings.EnablePooling)
                {
                    EditorGUILayout.HelpBox("This provider cannot pool. The pooling settings will be ignored.", MessageType.Info);
                }

                int side = settings.LoadRadius * 2 + 1;
                EditorGUILayout.HelpBox(
                    side + " x " + side + " active (" + settings.ActiveChunkCount + " chunks), " +
                    "released past ring " + settings.UnloadRadius + ".\n" +
                    "Active footprint: " + (side * settings.ChunkSize).ToString("0") + "m across.",
                    MessageType.None);

                WarnIfViewOutrunsLoadedWorld(settings);
            }

            if (Application.isPlaying && streamer.IsReady)
            {
                EditorGUILayout.Space(6f);
                EditorGUILayout.LabelField("Live", EditorStyles.boldLabel);
                EditorGUILayout.LabelField("Chunk", streamer.CurrentCoord.ToString());
                EditorGUILayout.LabelField("Active / Loading / Pooled",
                    streamer.ActiveCount + " / " + streamer.LoadingCount + " / " + streamer.PooledCount);
                Repaint();
            }

            EditorGUILayout.Space(6f);
            if (GUILayout.Button("Open Scene Chunks Window"))
            {
                SceneChunksWindow.Open();
            }
        }

        // The mistake that actually gets made: a load radius smaller than how far the player can
        // see, so they watch chunks pop in (or stare into a void) at the edge of the loaded world.
        private static void WarnIfViewOutrunsLoadedWorld(ChunkStreamingSettings settings)
        {
            float reach = settings.LoadRadius * settings.ChunkSize;

            bool fogOn = RenderSettings.fog;
            float limit;
            string limitLabel;
            if (fogOn)
            {
                limit = RenderSettings.fogEndDistance;
                limitLabel = "fog end distance";
            }
            else
            {
                limit = LargestCameraFarClip();
                limitLabel = "camera far clip";
            }

            if (limit <= 0f || reach >= limit) return;

            int minRadius = Mathf.CeilToInt(limit / Mathf.Max(1f, settings.ChunkSize));
            EditorGUILayout.HelpBox(
                "Load reach is " + reach.ToString("0") + "m (LoadRadius " + settings.LoadRadius +
                " x ChunkSize " + settings.ChunkSize.ToString("0") + "m) but the " + limitLabel +
                " is " + limit.ToString("0") + "m, so the player can see past the loaded world - " +
                "expect pop-in or a visible edge.\n" +
                "Raise LoadRadius to at least " + minRadius +
                (fogOn ? ", or pull fog in to match with a ChunkFogSync component."
                       : ", enable fog, or add a ChunkFogSync component to pull the view in."),
                MessageType.Warning);
        }

        private static float LargestCameraFarClip()
        {
            float far = 0f;
#if UNITY_2023_1_OR_NEWER
            Camera[] cameras = Object.FindObjectsByType<Camera>(FindObjectsSortMode.None);
#else
            Camera[] cameras = Object.FindObjectsOfType<Camera>();
#endif
            for (int i = 0; i < cameras.Length; i++)
            {
                if (cameras[i].farClipPlane > far) far = cameras[i].farClipPlane;
            }
            return far;
        }
    }
}
