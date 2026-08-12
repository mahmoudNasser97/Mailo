using UnityEngine;

namespace Nasser.SceneChunks
{
    /// <summary>
    /// Conversions between world space and chunk space. The grid lies on XZ; Y is never chunked.
    /// </summary>
    public static class ChunkGrid
    {
        public static ChunkCoord WorldToCoord(Vector3 world, float chunkSize)
        {
            if (chunkSize <= 0.01f) chunkSize = 1f;
            return new ChunkCoord(
                Mathf.FloorToInt(world.x / chunkSize),
                Mathf.FloorToInt(world.z / chunkSize));
        }

        /// <summary>Corner of the chunk. Chunk prefabs are instantiated here.</summary>
        public static Vector3 CoordToOrigin(ChunkCoord coord, float chunkSize)
        {
            return new Vector3(coord.X * chunkSize, 0f, coord.Z * chunkSize);
        }

        public static Vector3 CoordToCenter(ChunkCoord coord, float chunkSize)
        {
            return new Vector3((coord.X + 0.5f) * chunkSize, 0f, (coord.Z + 0.5f) * chunkSize);
        }

        public static Bounds CoordToBounds(ChunkCoord coord, float chunkSize, float height)
        {
            Vector3 center = CoordToCenter(coord, chunkSize);
            center.y = height * 0.5f;
            return new Bounds(center, new Vector3(chunkSize, height, chunkSize));
        }
    }
}
