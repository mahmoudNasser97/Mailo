using System.Collections.Generic;
using UnityEngine;

namespace ActiveRagdoll
{
    /// <summary>
    /// 2D convex-hull support-polygon maths on the XZ ground plane (spec §Phase 2).
    /// Pure helper — no state. BalanceController feeds it foot points and asks whether the
    /// capture point is inside, and how far from the edge.
    /// </summary>
    public static class SupportPolygon
    {
        /// <summary>Andrew's monotone-chain convex hull. Returns CCW hull (or the raw points if &lt; 3).</summary>
        public static List<Vector2> ConvexHull(List<Vector2> points)
        {
            if (points == null || points.Count < 3) return points != null ? new List<Vector2>(points) : new List<Vector2>();

            var p = new List<Vector2>(points);
            p.Sort((a, b) => Mathf.Approximately(a.x, b.x) ? a.y.CompareTo(b.y) : a.x.CompareTo(b.x));

            var hull = new List<Vector2>();
            // Lower chain.
            foreach (var pt in p)
            {
                while (hull.Count >= 2 && Cross(hull[hull.Count - 2], hull[hull.Count - 1], pt) <= 0f)
                    hull.RemoveAt(hull.Count - 1);
                hull.Add(pt);
            }
            // Upper chain.
            int lower = hull.Count + 1;
            for (int i = p.Count - 2; i >= 0; i--)
            {
                var pt = p[i];
                while (hull.Count >= lower && Cross(hull[hull.Count - 2], hull[hull.Count - 1], pt) <= 0f)
                    hull.RemoveAt(hull.Count - 1);
                hull.Add(pt);
            }
            hull.RemoveAt(hull.Count - 1); // last == first
            return hull;
        }

        /// <summary>True if the point is inside the convex hull.</summary>
        public static bool Contains(List<Vector2> hull, Vector2 pt)
        {
            if (hull == null || hull.Count < 3) return false;
            bool pos = false, neg = false;
            for (int i = 0; i < hull.Count; i++)
            {
                float c = Cross(hull[i], hull[(i + 1) % hull.Count], pt);
                if (c > 0f) pos = true;
                else if (c < 0f) neg = true;
                if (pos && neg) return false;
            }
            return true;
        }

        /// <summary>Distance to the nearest edge, negative inside, positive outside.</summary>
        public static float SignedDistance(List<Vector2> hull, Vector2 pt)
        {
            if (hull == null || hull.Count == 0) return float.PositiveInfinity;
            if (hull.Count < 3)
                return hull.Count == 1 ? Vector2.Distance(hull[0], pt)
                                       : DistToSegment(pt, hull[0], hull[1]);

            float min = float.MaxValue;
            for (int i = 0; i < hull.Count; i++)
                min = Mathf.Min(min, DistToSegment(pt, hull[i], hull[(i + 1) % hull.Count]));
            return Contains(hull, pt) ? -min : min;
        }

        public static Vector2 Centroid(List<Vector2> hull)
        {
            if (hull == null || hull.Count == 0) return Vector2.zero;
            Vector2 sum = Vector2.zero;
            foreach (var v in hull) sum += v;
            return sum / hull.Count;
        }

        static float Cross(Vector2 o, Vector2 a, Vector2 b) =>
            (a.x - o.x) * (b.y - o.y) - (a.y - o.y) * (b.x - o.x);

        static float DistToSegment(Vector2 p, Vector2 a, Vector2 b)
        {
            Vector2 ab = b - a;
            float len2 = ab.sqrMagnitude;
            if (len2 < 1e-8f) return Vector2.Distance(p, a);
            float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / len2);
            return Vector2.Distance(p, a + t * ab);
        }
    }
}
