using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Nasser.SceneChunks
{
    /// <summary>
    /// Streams chunks as additive scenes. Heavier than prefabs and it cannot pool
    /// (a scene is either loaded or not), but it lets level designers work on
    /// one chunk at a time without touching a shared prefab.
    ///
    /// Every scene listed here must also be added to Build Settings.
    /// </summary>
    [AddComponentMenu("Scene Chunks/Additive Scene Chunk Provider")]
    public class AdditiveSceneChunkProvider : ChunkProvider
    {
        [Serializable]
        public class Entry
        {
            public ChunkCoord Coord;
            public string SceneName;
        }

        [SerializeField] private List<Entry> entries = new List<Entry>();

        private Dictionary<ChunkCoord, string> _lookup;
        private readonly Dictionary<ChunkCoord, Scene> _loaded = new Dictionary<ChunkCoord, Scene>();

        public List<Entry> Entries { get { return entries; } }
        public override bool SupportsPooling { get { return false; } }

        private void Awake()
        {
            RebuildLookup();
        }

        public void RebuildLookup()
        {
            _lookup = new Dictionary<ChunkCoord, string>(entries.Count);
            for (int i = 0; i < entries.Count; i++)
            {
                Entry e = entries[i];
                if (e == null || string.IsNullOrEmpty(e.SceneName)) continue;
                _lookup[e.Coord] = e.SceneName;
            }
        }

        public override bool Exists(ChunkCoord coord)
        {
            if (_lookup == null) RebuildLookup();
            return _lookup.ContainsKey(coord);
        }

        public override IEnumerator Load(ChunkCoord coord, Transform parent, Action<GameObject> onLoaded)
        {
            if (_lookup == null) RebuildLookup();

            string sceneName;
            if (!_lookup.TryGetValue(coord, out sceneName))
            {
                onLoaded(null);
                yield break;
            }

            AsyncOperation op = SceneManager.LoadSceneAsync(sceneName, LoadSceneMode.Additive);
            if (op == null)
            {
                Debug.LogError("[SceneChunks] Scene '" + sceneName + "' is missing from Build Settings.", this);
                onLoaded(null);
                yield break;
            }

            while (!op.isDone) yield return null;

            _loaded[coord] = SceneManager.GetSceneByName(sceneName);
            onLoaded(null);
        }

        public override void Unload(ChunkCoord coord, GameObject root)
        {
            Scene scene;
            if (_loaded.TryGetValue(coord, out scene))
            {
                _loaded.Remove(coord);
                if (scene.isLoaded) SceneManager.UnloadSceneAsync(scene);
            }
        }
    }
}
