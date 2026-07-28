using System.Collections.Generic;
using NUnit.Framework;
using Shitboxer.Race;
using UnityEngine;

namespace Shitboxer.Tests
{
    /// <summary>
    /// Covers the pure-math closed-loop RacingLine, now a centripetal Catmull-Rom spline baked to a
    /// fine arc-length table. Asserts the spline's intended behaviour: it interpolates every waypoint,
    /// bows smoothly (so it is longer than the raw polyline and its heading/curvature vary
    /// continuously), and keeps arc-length projection + signed-delta wraparound consistent across the
    /// start/finish seam. All maths is XZ-planar, so waypoint height must not change any length.
    /// </summary>
    public class RacingLineTests : TestBase
    {
        // 100 m axis-aligned square in XZ. Waypoint 0 is the start/finish line (progress 0). The
        // Catmull-Rom spline through the four corners bows outward, so the loop is a bit over 400 m
        // and the corners sit past their 100 m polyline marks.
        private static RacingLine Square()
        {
            var pts = new List<Vector3>
            {
                new Vector3(0f, 0f, 0f),     // start/finish
                new Vector3(100f, 0f, 0f),
                new Vector3(100f, 0f, 100f),
                new Vector3(0f, 0f, 100f),
            };
            return new RacingLine(pts);
        }

        // Same XZ footprint, waypoints at wildly different heights: lengths/projection stay planar.
        private static RacingLine TiltedSquare()
        {
            var pts = new List<Vector3>
            {
                new Vector3(0f, 0f, 0f),
                new Vector3(100f, 50f, 0f),
                new Vector3(100f, -30f, 100f),
                new Vector3(0f, 12f, 100f),
            };
            return new RacingLine(pts);
        }

        // A loop with a genuine straight: four colinear waypoints along the bottom edge mean the
        // spline segment between the middle pair is dead straight (curvature 0), while the far
        // corner is a real bend. Used to prove curvature is ~0 on straights and positive at corners.
        private static RacingLine Stadium()
        {
            var pts = new List<Vector3>
            {
                new Vector3(0f, 0f, 0f),
                new Vector3(66f, 0f, 0f),
                new Vector3(133f, 0f, 0f),
                new Vector3(200f, 0f, 0f),   // colinear bottom straight
                new Vector3(200f, 0f, 100f),
                new Vector3(0f, 0f, 100f),
            };
            return new RacingLine(pts);
        }

        private static void AssertVecXZ(Vector3 expected, Vector3 actual, float tol, string label)
        {
            Assert.That(actual.x, Is.EqualTo(expected.x).Within(tol), label + " x");
            Assert.That(actual.z, Is.EqualTo(expected.z).Within(tol), label + " z");
        }

        [Test]
        public void TotalLength_IsPlanar_AndLongerThanThePolyline()
        {
            float len = Square().TotalLength;
            // The smooth spline bows outward past the 400 m corner-to-corner polyline, but only a little.
            Assert.Greater(len, 400f, "spline should bow past the polyline");
            Assert.Less(len, 440f, "but only modestly longer");
        }

        [Test]
        public void TotalLength_IsXZPlanar_IgnoringWaypointHeight()
        {
            // Identical XZ footprint at wild heights must give the identical planar loop length.
            Assert.That(TiltedSquare().TotalLength, Is.EqualTo(Square().TotalLength).Within(1e-2f));
        }

        [Test]
        public void SegmentCount_MatchesWaypointCount()
        {
            Assert.AreEqual(4, Square().SegmentCount);
        }

        [Test]
        public void Spline_PassesThroughEveryWaypoint()
        {
            var line = Square();
            var corners = new[]
            {
                new Vector3(0f, 0f, 0f),
                new Vector3(100f, 0f, 0f),
                new Vector3(100f, 0f, 100f),
                new Vector3(0f, 0f, 100f),
            };
            foreach (Vector3 wp in corners)
            {
                float d = line.ProjectPosition(wp);
                AssertVecXZ(wp, line.PointAt(d), 1e-2f, $"waypoint {wp}");
            }

            // Because the line bows out, a corner sits *past* its 100 m polyline distance.
            Assert.Greater(line.ProjectPosition(new Vector3(100f, 0f, 0f)), 100f, "corner past polyline mark");
        }

        [Test]
        public void PointAt_StartIsWaypointZero()
        {
            AssertVecXZ(new Vector3(0f, 0f, 0f), Square().PointAt(0f), 1e-3f, "start/finish");
        }

        [Test]
        public void PointAt_WrapsDistanceAroundTheLoop()
        {
            var line = Square();
            float tl = line.TotalLength;
            AssertVecXZ(line.PointAt(0f), line.PointAt(tl), 1e-2f, "one full lap");
            AssertVecXZ(line.PointAt(50f), line.PointAt(50f + tl), 1e-2f, "over one lap");
            AssertVecXZ(line.PointAt(tl - 50f), line.PointAt(-50f), 1e-2f, "negative wraps back");
        }

        [Test]
        public void DirectionAt_IsUnit_AndTangentToThePath()
        {
            var line = Square();
            const float h = 0.5f;
            foreach (float d in new[] { 30f, 130f, 250f, 380f })
            {
                Vector3 dir = line.DirectionAt(d);
                Assert.That(dir.magnitude, Is.EqualTo(1f).Within(1e-4f), $"unit @ {d}");

                // The reported heading must point along the path — parallel to a finite-difference tangent.
                Vector3 fd = line.PointAt(d + h) - line.PointAt(d - h);
                fd.y = 0f;
                fd.Normalize();
                Assert.Greater(Vector3.Dot(dir, fd), 0.99f, $"tangent @ {d}");
            }
        }

        [Test]
        public void CurvatureAt_IsZeroOnAStraight_AndPositiveAtACorner()
        {
            var line = Stadium();
            float dStraight = line.ProjectPosition(new Vector3(100f, 0f, 0f)); // mid the colinear bottom run
            float dCorner = line.ProjectPosition(new Vector3(200f, 0f, 0f));   // the SE bend

            Assert.That(line.CurvatureAt(dStraight, 5f), Is.EqualTo(0f).Within(1e-3f), "straight is flat");
            Assert.Greater(line.CurvatureAt(dCorner, 5f), 0.01f, "corner bends");
            Assert.Greater(line.CurvatureAt(dCorner, 5f), line.CurvatureAt(dStraight, 5f), "corner > straight");
        }

        [Test]
        public void ProjectPosition_RoundTripsAPointOnTheLine()
        {
            var line = Square();
            foreach (float d in new[] { 20f, 60f, 140f, 300f })
            {
                float back = line.ProjectPosition(line.PointAt(d));
                Assert.Less(Mathf.Abs(line.SignedDelta(d, back)), 0.5f, $"round-trip @ {d}");
            }
        }

        [Test]
        public void ProjectPosition_IgnoresLateralOffsetAndHeight()
        {
            var line = Square();
            foreach (float d in new[] { 20f, 140f, 300f })
            {
                Vector3 dir = line.DirectionAt(d);
                Vector3 left = new Vector3(-dir.z, 0f, dir.x); // unit, left of travel
                Vector3 off = line.PointAt(d) + Vector3.up * 500f + left * 3f;
                float back = line.ProjectPosition(off);
                Assert.Less(Mathf.Abs(line.SignedDelta(d, back)), 1f, $"offset+height ignored @ {d}");
            }

            // The start/finish point projects to progress 0, not the wrapped TotalLength.
            Assert.That(line.ProjectPosition(new Vector3(0f, 0f, 0f)), Is.EqualTo(0f).Within(1e-2f));
        }

        [Test]
        public void SignedDelta_IsSimpleDifferenceAwayFromTheSeam()
        {
            var line = Square();
            Assert.That(line.SignedDelta(10f, 30f), Is.EqualTo(20f).Within(1e-3f));   // forward
            Assert.That(line.SignedDelta(30f, 10f), Is.EqualTo(-20f).Within(1e-3f));  // backward
            Assert.That(line.SignedDelta(120f, 120f), Is.EqualTo(0f).Within(1e-3f));  // no movement
        }

        [Test]
        public void SignedDelta_TakesTheShortWayAcrossTheStartLine()
        {
            var line = Square();
            float tl = line.TotalLength;
            // (tl-10) -> 10 is 20 m forward across the start/finish seam, not (tl-20) m backward.
            Assert.That(line.SignedDelta(tl - 10f, 10f), Is.EqualTo(20f).Within(1e-3f));
            // 10 -> (tl-10) is 20 m backward across the seam, not (tl-20) m forward.
            Assert.That(line.SignedDelta(10f, tl - 10f), Is.EqualTo(-20f).Within(1e-3f));
        }

        [Test]
        public void SignedDelta_SplitsForwardVsBackwardAtTheHalfLoop()
        {
            var line = Square();
            float half = line.TotalLength * 0.5f;
            // Just under half a loop reads as forward; just over reads as the shorter way back.
            Assert.That(line.SignedDelta(0f, half - 1f), Is.EqualTo(half - 1f).Within(1e-2f));
            Assert.That(line.SignedDelta(0f, half + 1f), Is.EqualTo(-(half - 1f)).Within(1e-2f));
        }

        [Test]
        public void SignedDelta_HandlesUnwrappedInputs()
        {
            var line = Square();
            float tl = line.TotalLength;
            // Inputs outside [0, TotalLength) are wrapped first: (tl+10) -> 30 forward is +20.
            Assert.That(line.SignedDelta(tl + 10f, 30f), Is.EqualTo(20f).Within(1e-3f));
        }
    }
}
