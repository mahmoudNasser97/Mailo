using System;
using UnityEngine;

namespace Nasser.SceneChunks
{
    /// <summary>
    /// Integer address of a chunk on the XZ grid.
    /// </summary>
    [Serializable]
    public struct ChunkCoord : IEquatable<ChunkCoord>
    {
        public int X;
        public int Z;

        public ChunkCoord(int x, int z)
        {
            X = x;
            Z = z;
        }

        /// <summary>
        /// Chebyshev (square ring) distance. A radius of N means an (2N+1)x(2N+1) block of chunks.
        /// </summary>
        public static int Distance(ChunkCoord a, ChunkCoord b)
        {
            return Mathf.Max(Mathf.Abs(a.X - b.X), Mathf.Abs(a.Z - b.Z));
        }

        public bool Equals(ChunkCoord other)
        {
            return X == other.X && Z == other.Z;
        }

        public override bool Equals(object obj)
        {
            return obj is ChunkCoord other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked { return (X * 397) ^ Z; }
        }

        public override string ToString()
        {
            return X + "_" + Z;
        }

        public static bool operator ==(ChunkCoord a, ChunkCoord b) { return a.Equals(b); }
        public static bool operator !=(ChunkCoord a, ChunkCoord b) { return !a.Equals(b); }
    }
}
