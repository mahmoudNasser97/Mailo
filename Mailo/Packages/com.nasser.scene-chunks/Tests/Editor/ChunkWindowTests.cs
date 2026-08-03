using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace Nasser.SceneChunks.Tests
{
    public class ChunkWindowTests
    {
        // ---------------------------------------------------------------- hysteresis

        [Test]
        public void IsWithinLoad_AtExactlyLoadRadius_IsKept()
        {
            ChunkCoord center = new ChunkCoord(0, 0);
            ChunkCoord edge = new ChunkCoord(2, 0); // Chebyshev distance 2
            Assert.IsTrue(ChunkWindow.IsWithinLoad(edge, center, 2));
        }

        [Test]
        public void IsWithinLoad_OneBeyondLoadRadius_IsNotLoaded()
        {
            ChunkCoord center = new ChunkCoord(0, 0);
            ChunkCoord outside = new ChunkCoord(3, 0); // distance 3
            Assert.IsFalse(ChunkWindow.IsWithinLoad(outside, center, 2));
        }

        [Test]
        public void ShouldRelease_AtUnloadRadius_IsFalse()
        {
            // LoadRadius 2 + UnloadPadding 1 => UnloadRadius 3.
            ChunkCoord center = new ChunkCoord(0, 0);
            Assert.IsFalse(ChunkWindow.ShouldRelease(new ChunkCoord(3, 0), center, 3));
        }

        [Test]
        public void ShouldRelease_OneBeyondUnloadRadius_IsTrue()
        {
            ChunkCoord center = new ChunkCoord(0, 0);
            Assert.IsTrue(ChunkWindow.ShouldRelease(new ChunkCoord(4, 0), center, 3));
        }

        // ---------------------------------------------------------------- anchor

        [Test]
        public void AnchorPosition_Target_ReturnsTargetPosition()
        {
            Vector3 t = new Vector3(10f, 0f, 20f);
            Vector3 v = new Vector3(0f, 5f, 0f);
            Assert.AreEqual(t, ChunkWindow.AnchorPosition(StreamingAnchor.Target, t, v, true));
        }

        [Test]
        public void AnchorPosition_View_ReturnsViewPosition()
        {
            Vector3 t = new Vector3(10f, 0f, 20f);
            Vector3 v = new Vector3(0f, 5f, 40f);
            Assert.AreEqual(v, ChunkWindow.AnchorPosition(StreamingAnchor.View, t, v, true));
        }

        [Test]
        public void AnchorPosition_Midpoint_ReturnsAverage()
        {
            Vector3 t = new Vector3(0f, 0f, 0f);
            Vector3 v = new Vector3(10f, 0f, 20f);
            Assert.AreEqual(new Vector3(5f, 0f, 10f), ChunkWindow.AnchorPosition(StreamingAnchor.Midpoint, t, v, true));
        }

        [Test]
        public void AnchorPosition_ViewRequestedButNoView_FallsBackToTarget()
        {
            Vector3 t = new Vector3(10f, 0f, 20f);
            Assert.AreEqual(t, ChunkWindow.AnchorPosition(StreamingAnchor.View, t, Vector3.zero, false));
            Assert.AreEqual(t, ChunkWindow.AnchorPosition(StreamingAnchor.Midpoint, t, Vector3.zero, false));
        }

        // ---------------------------------------------------------------- forward bias

        [Test]
        public void ForwardBias_ZeroChunks_ReturnsZero()
        {
            Assert.AreEqual(Vector3.zero, ChunkWindow.ForwardBias(Vector3.forward, 0f, 125f));
        }

        [Test]
        public void ForwardBias_AlongForward_MagnitudeIsChunksTimesSize()
        {
            Vector3 bias = ChunkWindow.ForwardBias(Vector3.forward, 1.5f, 100f);
            Assert.AreEqual(150f, bias.magnitude, 0.001f);
            Assert.AreEqual(150f, bias.z, 0.001f);
        }

        [Test]
        public void ForwardBias_PurelyVerticalForward_ReturnsZero()
        {
            Vector3 bias = ChunkWindow.ForwardBias(new Vector3(0f, 1f, 0f), 1f, 100f);
            Assert.AreEqual(Vector3.zero, bias);
        }

        [Test]
        public void ForwardBias_IgnoresPitch_KeepsHorizontalMagnitude()
        {
            // Forward tilted 45 degrees down; the XZ part still drives a full-length horizontal bias.
            Vector3 forward = new Vector3(0f, -1f, 1f).normalized;
            Vector3 bias = ChunkWindow.ForwardBias(forward, 1f, 100f);
            Assert.AreEqual(0f, bias.y, 0.001f);
            Assert.AreEqual(100f, new Vector2(bias.x, bias.z).magnitude, 0.001f);
        }

        // ---------------------------------------------------------------- LRU eviction

        [Test]
        public void OldestIndex_PicksSmallestTimestamp()
        {
            List<float> times = new List<float> { 5f, 2f, 9f, 2.5f };
            Assert.AreEqual(1, ChunkWindow.OldestIndex(times));
        }

        [Test]
        public void OldestIndex_Empty_ReturnsMinusOne()
        {
            Assert.AreEqual(-1, ChunkWindow.OldestIndex(new List<float>()));
        }

        [Test]
        public void OldestIndex_Single_ReturnsZero()
        {
            Assert.AreEqual(0, ChunkWindow.OldestIndex(new List<float> { 42f }));
        }

        [Test]
        public void OldestIndex_TrimToCap_EvictsLeastRecentFirst()
        {
            // Trimming a pool down to MaxPooledChunks must drop the oldest and keep the most recent.
            List<float> working = new List<float> { 10f, 1f, 7f, 3f, 8f };
            const int cap = 3;
            List<float> evicted = new List<float>();

            while (working.Count > cap)
            {
                int oldest = ChunkWindow.OldestIndex(working);
                evicted.Add(working[oldest]);
                working.RemoveAt(oldest);
            }

            CollectionAssert.AreEquivalent(new List<float> { 1f, 3f }, evicted);
            CollectionAssert.AreEquivalent(new List<float> { 7f, 8f, 10f }, working);
        }
    }
}
