using System;
using System.Collections.Generic;
using UnityEngine;

namespace Shitboxer.Race
{
    /// <summary>
    /// Pure-math closed-loop polyline: waypoint positions in, arc-length queries out.
    /// No scene or engine-loop dependency so a headless server (or a bot brain running
    /// server-side) can use the identical track maths. All projection/curvature maths is
    /// done in the XZ plane; waypoint Y is kept only so returned points sit on the track.
    /// </summary>
    public sealed class RacingLine
    {
        private readonly Vector3[] _points;
        private readonly Vector3[] _segDir;    // normalized XZ-planar direction of segment i
        private readonly float[] _segLength;   // planar length of segment i
        private readonly float[] _cumLength;   // arc length at the start of segment i

        /// <summary>Total loop length in metres (XZ-planar).</summary>
        public float TotalLength { get; }

        public int SegmentCount => _points.Length;

        public RacingLine(IReadOnlyList<Vector3> loopPoints)
        {
            if (loopPoints == null || loopPoints.Count < 3)
                throw new ArgumentException("RacingLine needs at least 3 points forming a closed loop.");

            int n = loopPoints.Count;
            _points = new Vector3[n];
            _segDir = new Vector3[n];
            _segLength = new float[n];
            _cumLength = new float[n];
            for (int i = 0; i < n; i++) _points[i] = loopPoints[i];

            float cum = 0f;
            for (int i = 0; i < n; i++)
            {
                Vector3 delta = _points[(i + 1) % n] - _points[i];
                delta.y = 0f;
                float len = Mathf.Max(delta.magnitude, 0.001f);
                _segDir[i] = delta / len;
                _segLength[i] = len;
                _cumLength[i] = cum;
                cum += len;
            }
            TotalLength = cum;
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
        /// Arc-length progress of the nearest point on the loop to a world position
        /// (XZ-planar nearest over all segments — fine for tracks whose opposite
        /// corridors are farther apart than the corridor width).
        /// </summary>
        public float ProjectPosition(Vector3 worldPos)
        {
            float bestSqr = float.MaxValue;
            float bestProgress = 0f;

            for (int i = 0; i < _points.Length; i++)
            {
                Vector3 toPos = worldPos - _points[i];
                toPos.y = 0f;
                float t = Mathf.Clamp(Vector3.Dot(toPos, _segDir[i]), 0f, _segLength[i]);
                Vector3 offset = toPos - _segDir[i] * t;
                float sqr = offset.sqrMagnitude;
                if (sqr < bestSqr)
                {
                    bestSqr = sqr;
                    bestProgress = _cumLength[i] + t;
                }
            }
            return bestProgress;
        }

        /// <summary>World point at an arc-length distance along the loop (wraps).</summary>
        public Vector3 PointAt(float distance)
        {
            int i = SegmentIndexAt(distance, out float local);
            return _points[i] + _segDir[i] * local;
        }

        /// <summary>Normalized XZ-planar travel direction at an arc-length distance (wraps).</summary>
        public Vector3 DirectionAt(float distance)
        {
            int i = SegmentIndexAt(distance, out _);
            return _segDir[i];
        }

        /// <summary>
        /// Curvature estimate (rad/m) at a distance along the loop, from the heading
        /// change across a +/- halfWindow metres span. ~1/radius on constant arcs.
        /// </summary>
        public float CurvatureAt(float distance, float halfWindowM)
        {
            Vector3 before = DirectionAt(distance - halfWindowM);
            Vector3 after = DirectionAt(distance + halfWindowM);
            float angleRad = Vector3.Angle(before, after) * Mathf.Deg2Rad;
            return angleRad / Mathf.Max(2f * halfWindowM, 0.01f);
        }

        private int SegmentIndexAt(float distance, out float localDistance)
        {
            float d = Wrap(distance);
            // Linear scan is plenty for the ~24-segment loops we use.
            for (int i = _points.Length - 1; i >= 0; i--)
            {
                if (d >= _cumLength[i])
                {
                    localDistance = Mathf.Min(d - _cumLength[i], _segLength[i]);
                    return i;
                }
            }
            localDistance = 0f;
            return 0;
        }
    }
}
