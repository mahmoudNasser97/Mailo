using System.Collections.Generic;
using System.IO;
using Nasser.SceneChunks;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Nasser.SceneChunks.EditorTools
{
    /// <summary>
    /// One window to get chunk streaming into a project:
    ///   Setup       - creates the settings asset and the streamer rig in one click.
    ///   Slice       - takes an existing hand-built scene and bakes it into chunk prefabs.
    ///   Diagnostics - live view of what is resident while you playtest.
    /// </summary>
    public class SceneChunksWindow : EditorWindow
    {
        private enum Tab { Setup, Slice, Diagnostics }

        private Tab _tab = Tab.Setup;

        // Setup
        private ChunkStreamingSettings _settings;
        private float _newChunkSize = 50f;
        private int _newLoadRadius = 2;

        // Slice
        private Transform _sliceRoot;
        private float _sliceChunkSize = 50f;
        private string _outputFolder = "Assets/StreamedChunks";
        private bool _disableOriginals = true;
        private bool _skipEmptyChunks = true;
        private Vector2 _scroll;
        private string _status;
        private MessageType _statusType = MessageType.Info;
        private readonly List<string> _preview = new List<string>();

        [MenuItem("Tools/Scene Chunks/Streaming Setup %#k")]
        public static void Open()
        {
            SceneChunksWindow window = GetWindow<SceneChunksWindow>("Scene Chunks");
            window.minSize = new Vector2(400f, 460f);
            window.Show();
        }

        private void OnGUI()
        {
            EditorGUILayout.Space(6f);
            _tab = (Tab)GUILayout.Toolbar((int)_tab, new[] { "Setup", "Slice Scene", "Diagnostics" });
            EditorGUILayout.Space(8f);

            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            switch (_tab)
            {
                case Tab.Setup: DrawSetup(); break;
                case Tab.Slice: DrawSlice(); break;
                case Tab.Diagnostics: DrawDiagnostics(); break;
            }
            EditorGUILayout.EndScrollView();

            if (!string.IsNullOrEmpty(_status))
            {
                EditorGUILayout.Space(4f);
                EditorGUILayout.HelpBox(_status, _statusType);
            }
        }

        // ------------------------------------------------------------------ setup

        private void DrawSetup()
        {
            EditorGUILayout.LabelField("1. Settings asset", EditorStyles.boldLabel);
            _settings = (ChunkStreamingSettings)EditorGUILayout.ObjectField("Settings", _settings, typeof(ChunkStreamingSettings), false);

            using (new EditorGUI.DisabledScope(_settings != null))
            {
                _newChunkSize = EditorGUILayout.FloatField("Chunk Size", _newChunkSize);
                _newLoadRadius = EditorGUILayout.IntSlider("Load Radius", _newLoadRadius, 0, 6);

                int side = _newLoadRadius * 2 + 1;
                EditorGUILayout.HelpBox(
                    side + " x " + side + " = " + (side * side) + " chunks active, covering " +
                    (side * _newChunkSize).ToString("0") + "m across.\n" +
                    "Rule of thumb: the radius must cover more ground than the player crosses in the time a chunk takes to load.",
                    MessageType.None);

                if (GUILayout.Button("Create Settings Asset"))
                {
                    CreateSettingsAsset();
                }
            }

            EditorGUILayout.Space(12f);
            EditorGUILayout.LabelField("2. Scene rig", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("Adds a Chunk Streaming object with a streamer and a prefab provider.", EditorStyles.wordWrappedMiniLabel);

            using (new EditorGUI.DisabledScope(_settings == null))
            {
                if (GUILayout.Button("Create Streaming Rig In Scene"))
                {
                    CreateRig();
                }
            }

            EditorGUILayout.Space(12f);
            EditorGUILayout.LabelField("3. Player", EditorStyles.boldLabel);
            EditorGUILayout.LabelField(
                "Add a Chunk Stream Target component to your local player prefab. " +
                "In multiplayer, call Bind() from your spawn callback for the locally owned player only.",
                EditorStyles.wordWrappedMiniLabel);
        }

        private void CreateSettingsAsset()
        {
            string path = EditorUtility.SaveFilePanelInProject(
                "Create Streaming Settings", "ChunkStreamingSettings", "asset",
                "Where should the settings asset live?");
            if (string.IsNullOrEmpty(path)) return;

            ChunkStreamingSettings asset = CreateInstance<ChunkStreamingSettings>();
            asset.ChunkSize = _newChunkSize;
            asset.LoadRadius = _newLoadRadius;
            AssetDatabase.CreateAsset(asset, path);
            AssetDatabase.SaveAssets();

            _settings = asset;
            _sliceChunkSize = _newChunkSize;
            Report("Created " + path, MessageType.Info);
        }

        private void CreateRig()
        {
            GameObject root = new GameObject("Chunk Streaming");
            Undo.RegisterCreatedObjectUndo(root, "Create Streaming Rig");

            PrefabChunkProvider provider = root.AddComponent<PrefabChunkProvider>();
            provider.ChunkSize = _settings.ChunkSize;

            ChunkStreamer streamer = root.AddComponent<ChunkStreamer>();
            streamer.Settings = _settings;
            streamer.Provider = provider;

            root.AddComponent<ChunkStreamDebugHUD>();

            // Keep the fog end matched to the loaded region so you can never see past it.
            ChunkFogSync fog = root.AddComponent<ChunkFogSync>();
            fog.Settings = _settings;
            fog.Apply();

            Selection.activeGameObject = root;
            EditorSceneManager.MarkSceneDirty(root.scene);
            Report("Streaming rig added (streamer + provider + HUD + fog sync). Fog end is now matched " +
                   "to LoadRadius x ChunkSize. Slice a scene next to fill the provider.", MessageType.Info);
        }

        // ------------------------------------------------------------------ slice

        private void DrawSlice()
        {
            EditorGUILayout.LabelField("Bake an existing scene into chunk prefabs", EditorStyles.boldLabel);
            EditorGUILayout.LabelField(
                "Every direct child of the root is assigned to a chunk by its renderer bounds, " +
                "then each group is saved as a prefab positioned at its chunk origin.",
                EditorStyles.wordWrappedMiniLabel);

            EditorGUILayout.Space(6f);
            _sliceRoot = (Transform)EditorGUILayout.ObjectField("Source Root", _sliceRoot, typeof(Transform), true);
            _sliceChunkSize = EditorGUILayout.FloatField("Chunk Size", _sliceChunkSize);
            _outputFolder = EditorGUILayout.TextField("Output Folder", _outputFolder);
            _disableOriginals = EditorGUILayout.Toggle(
                new GUIContent("Disable Originals", "Deactivate the source objects after baking instead of deleting them, so you can undo by hand."),
                _disableOriginals);
            _skipEmptyChunks = EditorGUILayout.Toggle("Skip Empty Chunks", _skipEmptyChunks);

            EditorGUILayout.Space(8f);

            using (new EditorGUI.DisabledScope(_sliceRoot == null))
            {
                if (GUILayout.Button("Preview Distribution"))
                {
                    Preview();
                }

                EditorGUILayout.Space(4f);
                GUI.backgroundColor = new Color(0.36f, 0.79f, 0.65f);
                if (GUILayout.Button("Slice And Bake Prefabs", GUILayout.Height(28f)))
                {
                    Slice();
                }
                GUI.backgroundColor = Color.white;
            }

            if (_preview.Count > 0)
            {
                EditorGUILayout.Space(8f);
                EditorGUILayout.LabelField("Distribution", EditorStyles.boldLabel);
                for (int i = 0; i < _preview.Count && i < 40; i++)
                {
                    EditorGUILayout.LabelField(_preview[i], EditorStyles.miniLabel);
                }
                if (_preview.Count > 40)
                {
                    EditorGUILayout.LabelField("... and " + (_preview.Count - 40) + " more", EditorStyles.miniLabel);
                }
            }
        }

        private Dictionary<ChunkCoord, List<Transform>> GroupChildren()
        {
            Dictionary<ChunkCoord, List<Transform>> groups = new Dictionary<ChunkCoord, List<Transform>>();
            List<Transform> children = new List<Transform>();
            foreach (Transform child in _sliceRoot) children.Add(child);

            for (int i = 0; i < children.Count; i++)
            {
                Transform child = children[i];
                Vector3 anchor = child.position;

                Renderer[] renderers = child.GetComponentsInChildren<Renderer>();
                if (renderers.Length > 0)
                {
                    Bounds b = renderers[0].bounds;
                    for (int r = 1; r < renderers.Length; r++) b.Encapsulate(renderers[r].bounds);
                    anchor = b.center;
                }

                ChunkCoord coord = ChunkGrid.WorldToCoord(anchor, _sliceChunkSize);
                List<Transform> list;
                if (!groups.TryGetValue(coord, out list))
                {
                    list = new List<Transform>();
                    groups[coord] = list;
                }
                list.Add(child);
            }
            return groups;
        }

        private void Preview()
        {
            _preview.Clear();
            Dictionary<ChunkCoord, List<Transform>> groups = GroupChildren();

            int total = 0;
            foreach (KeyValuePair<ChunkCoord, List<Transform>> kv in groups)
            {
                _preview.Add("Chunk " + kv.Key + "  -  " + kv.Value.Count + " objects");
                total += kv.Value.Count;
            }
            _preview.Sort();

            Report(groups.Count + " chunks would be created from " + total + " objects.", MessageType.Info);
        }

        private void Slice()
        {
            if (_sliceRoot == null) return;
            if (_sliceChunkSize <= 0.01f)
            {
                Report("Chunk size must be greater than zero.", MessageType.Error);
                return;
            }

            Dictionary<ChunkCoord, List<Transform>> groups = GroupChildren();
            if (groups.Count == 0)
            {
                Report("No child objects found under the source root.", MessageType.Warning);
                return;
            }

            if (!EnsureFolder(_outputFolder))
            {
                Report("Could not create output folder: " + _outputFolder, MessageType.Error);
                return;
            }

            PrefabChunkProvider provider = FindProvider();
            if (provider != null)
            {
                Undo.RecordObject(provider, "Slice Scene Into Chunks");
                provider.Entries.Clear();
                provider.ChunkSize = _sliceChunkSize;
            }

            int index = 0;
            List<GameObject> containers = new List<GameObject>();

            try
            {
                foreach (KeyValuePair<ChunkCoord, List<Transform>> kv in groups)
                {
                    index++;
                    EditorUtility.DisplayProgressBar("Slicing", "Chunk " + kv.Key, index / (float)groups.Count);

                    if (_skipEmptyChunks && kv.Value.Count == 0) continue;

                    // Container sits at the chunk origin BEFORE reparenting, so every
                    // baked prefab stores offsets relative to its own chunk.
                    GameObject container = new GameObject("Chunk_" + kv.Key);
                    container.transform.position = ChunkGrid.CoordToOrigin(kv.Key, _sliceChunkSize);
                    containers.Add(container);

                    for (int i = 0; i < kv.Value.Count; i++)
                    {
                        Transform source = kv.Value[i];
                        GameObject copy = Instantiate(source.gameObject);
                        copy.name = source.name;
                        copy.transform.SetPositionAndRotation(source.position, source.rotation);
                        copy.transform.localScale = source.lossyScale;
                        copy.transform.SetParent(container.transform, true);
                    }

                    string path = _outputFolder + "/Chunk_" + kv.Key + ".prefab";
                    bool success;
                    GameObject prefab = PrefabUtility.SaveAsPrefabAsset(container, path, out success);

                    if (success && provider != null)
                    {
                        provider.Entries.Add(new PrefabChunkProvider.Entry { Coord = kv.Key, Prefab = prefab });
                    }
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
                for (int i = 0; i < containers.Count; i++) DestroyImmediate(containers[i]);
            }

            if (_disableOriginals)
            {
                Undo.RecordObject(_sliceRoot.gameObject, "Disable Sliced Originals");
                _sliceRoot.gameObject.SetActive(false);
            }

            if (provider != null)
            {
                provider.RebuildLookup();
                EditorUtility.SetDirty(provider);
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            EditorSceneManager.MarkSceneDirty(_sliceRoot.gameObject.scene);

            string tail = provider != null
                ? " and wired into the provider."
                : ". No PrefabChunkProvider in the scene, so assign the prefabs by hand.";
            Report("Baked " + groups.Count + " chunk prefabs into " + _outputFolder + tail, MessageType.Info);
            Preview();
        }

        private static PrefabChunkProvider FindProvider()
        {
#if UNITY_2023_1_OR_NEWER
            return Object.FindFirstObjectByType<PrefabChunkProvider>();
#else
            return Object.FindObjectOfType<PrefabChunkProvider>();
#endif
        }

        private static bool EnsureFolder(string folder)
        {
            if (AssetDatabase.IsValidFolder(folder)) return true;
            if (!folder.StartsWith("Assets")) return false;

            string[] parts = folder.Split('/');
            string current = parts[0];
            for (int i = 1; i < parts.Length; i++)
            {
                string next = current + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next)) AssetDatabase.CreateFolder(current, parts[i]);
                current = next;
            }
            return AssetDatabase.IsValidFolder(folder);
        }

        // ------------------------------------------------------------ diagnostics

        private void DrawDiagnostics()
        {
            ChunkStreamer streamer = ChunkStreamer.Instance;

            if (!Application.isPlaying)
            {
                EditorGUILayout.HelpBox("Enter play mode to see live streaming stats.", MessageType.Info);
                return;
            }

            if (streamer == null || !streamer.IsReady)
            {
                EditorGUILayout.HelpBox("No active streamer, or it has no target bound yet.", MessageType.Warning);
                return;
            }

            EditorGUILayout.LabelField("Current chunk", streamer.CurrentCoord.ToString());
            EditorGUILayout.LabelField("Active", streamer.ActiveCount.ToString());
            EditorGUILayout.LabelField("Loading", streamer.LoadingCount.ToString());
            EditorGUILayout.LabelField("Pooled", streamer.PooledCount.ToString());
            EditorGUILayout.LabelField("Resident total", streamer.ResidentCount.ToString());

            EditorGUILayout.Space(8f);
            DrawMiniGrid(streamer);

            EditorGUILayout.Space(8f);
            if (GUILayout.Button("Flush All Chunks")) streamer.FlushAll();

            Repaint();
        }

        private void DrawMiniGrid(ChunkStreamer streamer)
        {
            int r = streamer.Settings.UnloadRadius;
            int side = r * 2 + 1;
            float cell = Mathf.Min(18f, (position.width - 40f) / side);

            Rect area = GUILayoutUtility.GetRect(side * cell, side * cell);
            ChunkCoord center = streamer.CurrentCoord;

            for (int dz = -r; dz <= r; dz++)
            {
                for (int dx = -r; dx <= r; dx++)
                {
                    ChunkCoord coord = new ChunkCoord(center.X + dx, center.Z + dz);
                    Rect cellRect = new Rect(
                        area.x + (dx + r) * cell,
                        area.y + (r - dz) * cell,
                        cell - 2f, cell - 2f);

                    Color color;
                    switch (streamer.GetState(coord))
                    {
                        case ChunkState.Active: color = new Color(0.36f, 0.79f, 0.65f); break;
                        case ChunkState.Loading:
                        case ChunkState.Queued: color = new Color(0.94f, 0.62f, 0.15f); break;
                        case ChunkState.Pooled: color = new Color(0.45f, 0.45f, 0.5f); break;
                        default: color = new Color(0.25f, 0.25f, 0.27f); break;
                    }

                    EditorGUI.DrawRect(cellRect, color);
                    if (dx == 0 && dz == 0)
                    {
                        EditorGUI.DrawRect(new Rect(cellRect.center.x - 3f, cellRect.center.y - 3f, 6f, 6f), new Color(0.85f, 0.35f, 0.19f));
                    }
                }
            }
        }

        private void Report(string message, MessageType type)
        {
            _status = message;
            _statusType = type;
        }
    }
}
