using NUnit.Framework;
using UnityEngine;

namespace Nasser.SceneChunks.Tests
{
    public class ChunkGridTests
    {
        private const float Size = 50f;

        [Test]
        public void WorldToCoord_PositionInsideOriginChunk_ReturnsZero()
        {
            ChunkCoord c = ChunkGrid.WorldToCoord(new Vector3(10f, 0f, 10f), Size);
            Assert.AreEqual(0, c.X);
            Assert.AreEqual(0, c.Z);
        }

        [Test]
        public void WorldToCoord_ExactBoundary_BelongsToHigherChunk()
        {
            ChunkCoord c = ChunkGrid.WorldToCoord(new Vector3(Size, 0f, Size), Size);
            Assert.AreEqual(1, c.X);
            Assert.AreEqual(1, c.Z);
        }

        [Test]
        public void WorldToCoord_JustBelowBoundary_StaysInLowerChunk()
        {
            ChunkCoord c = ChunkGrid.WorldToCoord(new Vector3(Size - 0.001f, 0f, Size - 0.001f), Size);
            Assert.AreEqual(0, c.X);
            Assert.AreEqual(0, c.Z);
        }

        [Test]
        public void WorldToCoord_NegativePosition_FloorsDown()
        {
            ChunkCoord c = ChunkGrid.WorldToCoord(new Vector3(-1f, 0f, -1f), Size);
            Assert.AreEqual(-1, c.X);
            Assert.AreEqual(-1, c.Z);
        }

        [Test]
        public void WorldToCoord_NegativeBoundary_BelongsToLowerChunk()
        {
            // floor(-50 / 50) == floor(-1) == -1
            ChunkCoord c = ChunkGrid.WorldToCoord(new Vector3(-Size, 0f, -Size), Size);
            Assert.AreEqual(-1, c.X);
            Assert.AreEqual(-1, c.Z);
        }

        [Test]
        public void WorldToCoord_OriginAndCentre_RoundTripForAllQuadrants()
        {
            ChunkCoord[] coords =
            {
                new ChunkCoord(0, 0),
                new ChunkCoord(3, -2),
                new ChunkCoord(-5, -7),
                new ChunkCoord(-1, 4)
            };

            for (int i = 0; i < coords.Length; i++)
            {
                Vector3 origin = ChunkGrid.CoordToOrigin(coords[i], Size);
                Assert.AreEqual(coords[i], ChunkGrid.WorldToCoord(origin, Size), "origin round-trip " + coords[i]);

                Vector3 centre = ChunkGrid.CoordToCenter(coords[i], Size);
                Assert.AreEqual(coords[i], ChunkGrid.WorldToCoord(centre, Size), "centre round-trip " + coords[i]);
            }
        }
    }
}
