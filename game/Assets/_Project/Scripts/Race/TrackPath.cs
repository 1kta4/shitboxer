using System.Collections.Generic;
using UnityEngine;

namespace Shitboxer.Race
{
    /// <summary>
    /// Scene-side definition of a closed racing loop: an ordered list of waypoint
    /// transforms (falls back to direct children in hierarchy order). Thin wrapper —
    /// all queries delegate to the plain-C# RacingLine so headless code can share the
    /// maths. Waypoint 0 is the start/finish line (progress 0).
    /// </summary>
    public class TrackPath : MonoBehaviour
    {
        [Tooltip("Ordered waypoints forming a closed loop; loop closes last -> first. Leave empty to use direct children in order.")]
        [SerializeField] private List<Transform> waypoints = new List<Transform>();

        private RacingLine _line;
        private bool _buildFailed;

        /// <summary>Baked math core. Built lazily from waypoint positions; null if under 3 waypoints.</summary>
        public RacingLine Line
        {
            get
            {
                if (_line == null && !_buildFailed) _line = BuildLine();
                return _line;
            }
        }

        public float TotalLength => Line?.TotalLength ?? 0f;

        /// <summary>Replaces the waypoint list (used by editor builders) and invalidates the baked line.</summary>
        public void SetWaypoints(List<Transform> ordered)
        {
            waypoints = ordered ?? new List<Transform>();
            _line = null;
            _buildFailed = false;
        }

        /// <summary>Arc-length progress (m) of the nearest point on the loop to a world position.</summary>
        public float ProjectPosition(Vector3 worldPos) => Line.ProjectPosition(worldPos);

        /// <summary>World point aheadM metres further along the loop from a progress value.</summary>
        public Vector3 LookaheadPoint(float progressM, float aheadM) => Line.PointAt(progressM + aheadM);

        private RacingLine BuildLine()
        {
            List<Transform> source = ResolveWaypoints();
            if (source.Count < 3)
            {
                Debug.LogError($"[TrackPath] '{name}' needs at least 3 waypoints (has {source.Count}).", this);
                _buildFailed = true;
                return null;
            }

            var points = new Vector3[source.Count];
            for (int i = 0; i < source.Count; i++) points[i] = source[i].position;
            return new RacingLine(points);
        }

        private List<Transform> ResolveWaypoints()
        {
            if (waypoints != null && waypoints.Count >= 3)
            {
                var valid = new List<Transform>(waypoints.Count);
                foreach (Transform t in waypoints)
                    if (t) valid.Add(t);
                if (valid.Count >= 3) return valid;
            }

            var children = new List<Transform>(transform.childCount);
            foreach (Transform child in transform) children.Add(child);
            return children;
        }

#if UNITY_EDITOR
        private void OnDrawGizmos()
        {
            List<Transform> wps = ResolveWaypoints();
            if (wps.Count < 2) return;

            Gizmos.color = Color.yellow;
            for (int i = 0; i < wps.Count; i++)
            {
                Transform a = wps[i];
                Transform b = wps[(i + 1) % wps.Count];
                if (!a || !b) continue;
                Gizmos.DrawLine(a.position, b.position);
                Gizmos.DrawWireSphere(a.position, 0.6f);
            }

            // Start/finish marker.
            if (wps[0])
            {
                Gizmos.color = Color.green;
                Gizmos.DrawWireSphere(wps[0].position, 1.2f);
            }
        }
#endif
    }
}
