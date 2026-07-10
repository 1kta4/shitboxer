using System;
using System.Collections.Generic;
using UnityEngine;

namespace Shitboxer.Race
{
    /// <summary>
    /// Pure-math closed-loop racing line: waypoint positions in, arc-length queries out.
    /// The path is a <b>centripetal Catmull-Rom spline</b> through the waypoints (not the raw
    /// piecewise-linear polyline), baked once into a fine, evenly-stepped arc-length lookup
    /// table so distances, wraparound and projection stay consistent. Centripetal (alpha=0.5)
    /// parameterisation avoids the cusps/loops uniform Catmull-Rom produces when waypoint
    /// spacing is uneven (tight corner arcs next to long straights, exactly our tracks).
    /// No scene or engine-loop dependency so a headless server (or a bot brain running
    /// server-side) can use the identical track maths. All projection/curvature/length maths is
    /// done in the XZ plane; waypoint Y is interpolated only so returned points sit on the track.
    /// </summary>
    public sealed class RacingLine
    {
        // Spline samples per waypoint segment. The whole loop bakes to waypointCount*this samples;
        // dense enough that XZ-planar nearest-sample projection resolves corners cleanly and
        // curvature reads smoothly, cheap enough to linear-scan for projection each physics step.
        private const int SamplesPerSegment = 16;
        private const float CentripetalAlpha = 0.5f;

        private readonly int _waypointCount;
        private readonly Vector3[] _pos;       // baked spline sample positions (Y interpolated)
        private readonly Vector3[] _dir;        // normalized XZ-planar travel direction at each sample
        private readonly float[] _cum;          // XZ arc length at the start of sample-segment k

        /// <summary>Total loop length in metres (XZ-planar, along the spline).</summary>
        public float TotalLength { get; }

        /// <summary>Number of waypoints the loop was built from (one Catmull-Rom segment each).</summary>
        public int SegmentCount => _waypointCount;

        public RacingLine(IReadOnlyList<Vector3> loopPoints)
        {
            if (loopPoints == null || loopPoints.Count < 3)
                throw new ArgumentException("RacingLine needs at least 3 points forming a closed loop.");

            int n = loopPoints.Count;
            _waypointCount = n;
            var wp = new Vector3[n];
            for (int i = 0; i < n; i++) wp[i] = loopPoints[i];

            int m = n * SamplesPerSegment;
            _pos = new Vector3[m];
            _dir = new Vector3[m];
            _cum = new float[m];

            // Bake the spline: each waypoint i owns a Catmull-Rom segment wp[i] -> wp[i+1] using
            // wp[i-1] and wp[i+2] as tangent neighbours (indices wrap — it's a closed loop).
            int idx = 0;
            for (int i = 0; i < n; i++)
            {
                Vector3 p0 = wp[(i - 1 + n) % n];
                Vector3 p1 = wp[i];
                Vector3 p2 = wp[(i + 1) % n];
                Vector3 p3 = wp[(i + 2) % n];
                for (int j = 0; j < SamplesPerSegment; j++)
                {
                    float u = j / (float)SamplesPerSegment; // j==0 lands exactly on waypoint i
                    _pos[idx++] = CentripetalPoint(p0, p1, p2, p3, u);
                }
            }

            // Arc-length table over the closed chain of samples (planar; the last segment wraps).
            float cum = 0f;
            for (int k = 0; k < m; k++)
            {
                _cum[k] = cum;
                cum += PlanarDist(_pos[k], _pos[(k + 1) % m]);
            }
            TotalLength = cum;

            // Per-sample heading via central difference of neighbouring samples (XZ-planar, unit).
            for (int k = 0; k < m; k++)
            {
                Vector3 t = _pos[(k + 1) % m] - _pos[(k - 1 + m) % m];
                t.y = 0f;
                _dir[k] = t.sqrMagnitude > 1e-8f ? t.normalized : Vector3.forward;
            }
        }

        /// <summary>Wraps an arc-length distance into [0, TotalLength).</summary>
        public float Wrap(float distance)
        {
            float d = distance % TotalLength;
            if (d < 0f) d += TotalLength;
            return d;
        }

        /// <summary>
        /// Shortest signed arc-length step from one progress value to another, in
        /// [-TotalLength/2, TotalLength/2). Positive = forward along the loop.
        /// </summary>
        public float SignedDelta(float from, float to)
        {
            float d = Wrap(to) - Wrap(from);
            if (d >= TotalLength * 0.5f) d -= TotalLength;
            else if (d < -TotalLength * 0.5f) d += TotalLength;
            return d;
        }

        /// <summary>
        /// Arc-length progress of the nearest point on the spline to a world position
        /// (XZ-planar nearest over all baked sample-segments — the fine sampling keeps this
        /// accurate as long as opposite corridors sit farther apart than the corridor width).
        /// </summary>
        public float ProjectPosition(Vector3 worldPos)
        {
            float bestSqr = float.MaxValue;
            float bestProgress = 0f;

            int m = _pos.Length;
            for (int k = 0; k < m; k++)
            {
                Vector3 seg = _pos[(k + 1) % m] - _pos[k];
                seg.y = 0f;
                float segLen = seg.magnitude;
                if (segLen < 1e-6f) continue;
                Vector3 dir = seg / segLen;

                Vector3 toPos = worldPos - _pos[k];
                toPos.y = 0f;
                float t = Mathf.Clamp(Vector3.Dot(toPos, dir), 0f, segLen);
                Vector3 offset = toPos - dir * t;
                float sqr = offset.sqrMagnitude;
                if (sqr < bestSqr)
                {
                    bestSqr = sqr;
                    bestProgress = _cum[k] + t;
                }
            }
            return bestProgress;
        }

        /// <summary>World point at an arc-length distance along the spline (wraps).</summary>
        public Vector3 PointAt(float distance)
        {
            int k = SampleIndexAt(distance, out float frac);
            return Vector3.LerpUnclamped(_pos[k], _pos[(k + 1) % _pos.Length], frac);
        }

        /// <summary>Normalized XZ-planar travel direction at an arc-length distance (wraps).</summary>
        public Vector3 DirectionAt(float distance)
        {
            int k = SampleIndexAt(distance, out float frac);
            Vector3 d = Vector3.LerpUnclamped(_dir[k], _dir[(k + 1) % _dir.Length], frac);
            d.y = 0f;
            return d.sqrMagnitude > 1e-8f ? d.normalized : _dir[k];
        }

        /// <summary>
        /// Curvature estimate (rad/m) at a distance along the loop, from the heading
        /// change across a +/- halfWindow metres span. ~1/radius on constant arcs, and now
        /// smooth (the spline heading varies continuously instead of jumping at waypoints).
        /// </summary>
        public float CurvatureAt(float distance, float halfWindowM)
        {
            Vector3 before = DirectionAt(distance - halfWindowM);
            Vector3 after = DirectionAt(distance + halfWindowM);
            float angleRad = Vector3.Angle(before, after) * Mathf.Deg2Rad;
            return angleRad / Mathf.Max(2f * halfWindowM, 0.01f);
        }

        /// <summary>Index of the sample-segment containing a (wrapped) distance, plus the 0..1 fraction into it.</summary>
        private int SampleIndexAt(float distance, out float frac)
        {
            float d = Wrap(distance);
            int m = _pos.Length;

            // _cum is sorted ascending — binary search for the last sample with _cum[k] <= d.
            int lo = 0, hi = m - 1;
            while (lo < hi)
            {
                int mid = (lo + hi + 1) >> 1;
                if (_cum[mid] <= d) lo = mid;
                else hi = mid - 1;
            }

            int k = lo;
            float nextCum = k + 1 < m ? _cum[k + 1] : TotalLength;
            float segLen = nextCum - _cum[k];
            frac = segLen > 1e-6f ? Mathf.Clamp01((d - _cum[k]) / segLen) : 0f;
            return k;
        }

        /// <summary>
        /// Centripetal Catmull-Rom evaluation of the segment p1 -> p2 (u in [0,1]) via the
        /// Barry-Goldman pyramid. Knot spacing uses XZ-planar distances so the XZ curve — and
        /// therefore every length/projection query — is independent of waypoint height.
        /// </summary>
        private static Vector3 CentripetalPoint(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float u)
        {
            float t0 = 0f;
            float t1 = t0 + KnotDelta(p0, p1);
            float t2 = t1 + KnotDelta(p1, p2);
            float t3 = t2 + KnotDelta(p2, p3);
            float t = Mathf.Lerp(t1, t2, u);

            Vector3 a1 = Vector3.LerpUnclamped(p0, p1, (t - t0) / (t1 - t0));
            Vector3 a2 = Vector3.LerpUnclamped(p1, p2, (t - t1) / (t2 - t1));
            Vector3 a3 = Vector3.LerpUnclamped(p2, p3, (t - t2) / (t3 - t2));
            Vector3 b1 = Vector3.LerpUnclamped(a1, a2, (t - t0) / (t2 - t0));
            Vector3 b2 = Vector3.LerpUnclamped(a2, a3, (t - t1) / (t3 - t1));
            return Vector3.LerpUnclamped(b1, b2, (t - t1) / (t2 - t1));
        }

        private static float KnotDelta(Vector3 a, Vector3 b) =>
            Mathf.Max(Mathf.Pow(PlanarDist(a, b), CentripetalAlpha), 1e-4f);

        private static float PlanarDist(Vector3 a, Vector3 b)
        {
            float dx = a.x - b.x;
            float dz = a.z - b.z;
            return Mathf.Sqrt(dx * dx + dz * dz);
        }
    }
}
