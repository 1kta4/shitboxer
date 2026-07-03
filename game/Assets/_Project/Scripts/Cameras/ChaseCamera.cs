using UnityEngine;

namespace Shitboxer.Cameras
{
    /// <summary>
    /// Third-person chase camera: damped follow behind the target's velocity direction,
    /// look-ahead into travel, and a speed-based FOV kick. Runs in LateUpdate off the
    /// rigidbody's interpolated transform, so it needs no physics knowledge.
    /// </summary>
    public class ChaseCamera : MonoBehaviour
    {
        [SerializeField] private Transform target;

        [Header("Follow")]
        [SerializeField] private float distance = 6.5f;
        [SerializeField] private float height = 2.4f;
        [SerializeField] private float positionSmoothTime = 0.09f;
        [Tooltip("Blend between chassis forward (0) and velocity direction (1) for the follow axis — velocity keeps drifts readable.")]
        [Range(0f, 1f)] [SerializeField] private float velocityAlign = 0.65f;

        [Header("Look")]
        [SerializeField] private float lookAheadSeconds = 0.4f;
        [SerializeField] private float lookHeight = 1.0f;
        [SerializeField] private float rotationLerp = 14f;

        [Header("FOV")]
        [SerializeField] private float baseFov = 62f;
        [SerializeField] private float fovPerKmh = 0.08f;
        [SerializeField] private float maxFov = 82f;

        private Camera _cam;
        private Vector3 _posVelocity;
        private Vector3 _lastTargetPos;
        private Vector3 _smoothedVelocity;

        public void SetTarget(Transform t)
        {
            target = t;
            if (t)
            {
                _lastTargetPos = t.position;
                _smoothedVelocity = Vector3.zero;
            }
        }

        private void Awake()
        {
            _cam = GetComponent<Camera>();
            if (target) _lastTargetPos = target.position;
        }

        private void LateUpdate()
        {
            if (!target) return;
            float dt = Time.deltaTime;
            if (dt <= 0f) return;

            // Estimate target velocity from movement — keeps the camera rigidbody-agnostic.
            Vector3 rawVel = (target.position - _lastTargetPos) / dt;
            _lastTargetPos = target.position;
            _smoothedVelocity = Vector3.Lerp(_smoothedVelocity, rawVel, 1f - Mathf.Exp(-6f * dt));

            float speed = _smoothedVelocity.magnitude;

            Vector3 flatFwd = Vector3.ProjectOnPlane(target.forward, Vector3.up).normalized;
            Vector3 flatVel = Vector3.ProjectOnPlane(_smoothedVelocity, Vector3.up);
            Vector3 followDir = flatFwd;
            if (flatVel.magnitude > 2f)
            {
                // Follow roughly behind the direction of travel; sign-flip handles reversing.
                Vector3 velDir = flatVel.normalized * (Vector3.Dot(flatVel, flatFwd) >= 0f ? 1f : -1f);
                followDir = Vector3.Slerp(flatFwd, velDir, velocityAlign).normalized;
            }

            Vector3 desiredPos = target.position - followDir * distance + Vector3.up * height;
            transform.position = Vector3.SmoothDamp(transform.position, desiredPos, ref _posVelocity, positionSmoothTime);

            Vector3 lookPoint = target.position + Vector3.up * lookHeight + flatVel * lookAheadSeconds;
            Quaternion desiredRot = Quaternion.LookRotation(lookPoint - transform.position, Vector3.up);
            transform.rotation = Quaternion.Slerp(transform.rotation, desiredRot, 1f - Mathf.Exp(-rotationLerp * dt));

            if (_cam)
                _cam.fieldOfView = Mathf.Min(maxFov, baseFov + speed * 3.6f * fovPerKmh);
        }
    }
}
