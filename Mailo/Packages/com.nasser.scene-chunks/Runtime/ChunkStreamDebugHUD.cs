using UnityEngine;

namespace Nasser.SceneChunks
{
    /// <summary>Tiny on-screen readout for tuning radius and budget on device.</summary>
    [AddComponentMenu("Scene Chunks/Chunk Stream Debug HUD")]
    [RequireComponent(typeof(ChunkStreamer))]
    public class ChunkStreamDebugHUD : MonoBehaviour
    {
        [SerializeField] private bool show = true;
        [SerializeField] private int fontSize = 14;

        private ChunkStreamer _streamer;
        private GUIStyle _style;

        private void Awake()
        {
            _streamer = GetComponent<ChunkStreamer>();
        }

        private void OnGUI()
        {
            if (!show || _streamer == null || !_streamer.IsReady) return;

            if (_style == null)
            {
                _style = new GUIStyle(GUI.skin.label);
                _style.fontSize = fontSize;
            }

            string text =
                "chunk      " + _streamer.CurrentCoord + "\n" +
                "active     " + _streamer.ActiveCount + "\n" +
                "loading    " + _streamer.LoadingCount + "\n" +
                "pooled     " + _streamer.PooledCount + "\n" +
                "resident   " + _streamer.ResidentCount;

            GUI.Box(new Rect(10, 10, 170, 108), GUIContent.none);
            GUI.Label(new Rect(20, 16, 160, 100), text, _style);
        }
    }
}
