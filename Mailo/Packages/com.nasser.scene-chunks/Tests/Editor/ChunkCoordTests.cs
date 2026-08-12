using NUnit.Framework;

namespace Nasser.SceneChunks.Tests
{
    public class ChunkCoordTests
    {
        [Test]
        public void Distance_AxisAligned_ReturnsLargestAxisDelta()
        {
            Assert.AreEqual(3, ChunkCoord.Distance(new ChunkCoord(0, 0), new ChunkCoord(3, 1)));
        }

        [Test]
        public void Distance_Diagonal_IsChebyshevNotEuclidean()
        {
            // Euclidean distance here is ~2.83; Chebyshev is 2.
            Assert.AreEqual(2, ChunkCoord.Distance(new ChunkCoord(0, 0), new ChunkCoord(2, 2)));
        }

        [Test]
        public void Distance_NegativeCoords_UsesAbsoluteDeltas()
        {
            Assert.AreEqual(5, ChunkCoord.Distance(new ChunkCoord(-2, -5), new ChunkCoord(0, 0)));
        }

        [Test]
        public void Distance_IsSymmetric()
        {
            ChunkCoord a = new ChunkCoord(-3, 4);
            ChunkCoord b = new ChunkCoord(6, -1);
            Assert.AreEqual(ChunkCoord.Distance(a, b), ChunkCoord.Distance(b, a));
        }

        [Test]
        public void Equality_SameCoords_AreEqual()
        {
            Assert.IsTrue(new ChunkCoord(2, -3) == new ChunkCoord(2, -3));
            Assert.IsTrue(new ChunkCoord(2, -3).Equals(new ChunkCoord(2, -3)));
        }

        [Test]
        public void Equality_DifferentCoords_AreNotEqual()
        {
            Assert.IsTrue(new ChunkCoord(2, -3) != new ChunkCoord(2, 3));
        }
    }
}
