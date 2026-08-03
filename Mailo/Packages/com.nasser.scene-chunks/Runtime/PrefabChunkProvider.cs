using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Nasser.SceneChunks
{
    /// <summary>
    /// Streams chunks from a coord-to-prefab table. This is what the Chunk Slicer window fills in
    /// when it bakes an existing scene, and it is the fastest option for authored worlds.
    /// </summary>
    [AddComponentMenu("Scene Chunks/Prefab Chunk Provider")]
    public class PrefabChunkProvider : ChunkProvider
    {
        [Serializable]
        public class Entry
        {
            public ChunkCoord Coord;
            public GameObject Prefab;
        }

        [Tooltip("One entry per authored chunk. Populated automatically by the Chunk Slicer.")]
        [SerializeField] private List<Entry> entries = new List<Entry>();

        [Tooltip("Must match the chunk size the prefabs were baked with.")]
        [SerializeField] private float chunkSize = 50f;

        [Tooltip("Spread instantiation over a frame boundary so a big chunk does not spike the current frame.")]
        [SerializeField] private bool yieldBeforeInstantiate = true;

        private Dictionary<ChunkCoord, GameObject> _lookup;

        public List<Entry> Entries { get { return entries; } }
        public float ChunkSize { get { return chunkSize; } set { chunkSize = value; } }

        private void Awake()
        {
            RebuildLookup();
        }

        public void RebuildLookup()
        {
            _lookup = new Dictionary<ChunkCoord, GameObject>(entries.Count);
            for (int i = 0; i < entries.Count; i++)
            {
                Entry e = entries[i];
                if (e == null || e.Prefab == null) continue;
                _lookup[e.Coord] = e.Prefab;
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

            GameObject prefab;
            if (!_lookup.TryGetValue(coord, out prefab) || prefab == null)
            {
                onLoaded(null);
                yield break;
            }

            if (yieldBeforeInstantiate) yield return null;

            GameObject instance = Instantiate(prefab, ChunkGrid.CoordToOrigin(coord, chunkSize), Quaternion.identity, parent);
            instance.name = "Chunk_" + coord;
            onLoaded(instance);
        }

        public override void Unload(ChunkCoord coord, GameObject root)
        {
            if (root != null) Destroy(root);
        }

        public override bool TryGetWorldExtents(out ChunkCoord min, out ChunkCoord max)
        {
            min = default(ChunkCoord);
            max = default(ChunkCoord);
            if (entries.Count == 0) return false;

            int minX = int.MaxValue, minZ = int.MaxValue, maxX = int.MinValue, maxZ = int.MinValue;
            for (int i = 0; i < entries.Count; i++)
            {
                ChunkCoord c = entries[i].Coord;
                if (c.X < minX) minX = c.X;
                if (c.Z < minZ) minZ = c.Z;
                if (c.X > maxX) maxX = c.X;
                if (c.Z > maxZ) maxZ = c.Z;
            }
            min = new ChunkCoord(minX, minZ);
            max = new ChunkCoord(maxX, maxZ);
            return true;
        }
    }
}
