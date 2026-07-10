using Shitboxer.Race;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Shitboxer.Cameras
{
    /// <summary>
    /// Third-person chase camera: damped follow behind the target's velocity direction,
    /// look-ahead into travel, and a speed-based FOV kick. Runs in LateUpdate off the
    /// rigidbody's interpolated transform, so it needs no physics knowledge.
    ///
    /// Collision feel is a purely additive <see cref="CameraImpact"/> layer. When the target car
    /// carries a <see cref="Shitboxer.Race.VehicleCombat"/> we drive that trauma from its real
    /// <see cref="Shitboxer.Race.VehicleCombat.OnImpact"/> events — the SAME 0..1 severity the sim
    /// used for the physics response — and keep the velocity-inferred detection running underneath
    /// as a fallback for sustained wall scrapes. Genuine hits also kick the player's gamepad.
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

        [Header("Impact")]
        [Tooltip("Rigidbody whose motion drives the collision shake. Auto-resolved from the target if left empty.")]
        [SerializeField] private Rigidbody targetBody;
        [SerializeField] private CameraImpact impact = new CameraImpact();

        [Header("Rumble")]
        [Tooltip("Turn on for the player's camera to drive the current gamepad's motors from genuine impact severity. Off by default so existing scenes are unchanged; null-safe when no pad is connected.")]
        [SerializeField] private bool enableGamepadRumble = false;
        [Tooltip("Low-frequency (heavy) motor speed at a full-severity impact, 0..1.")]
        [Range(0f, 1f)] [SerializeField] private float rumbleLowFreqMax = 0.7f;
        [Tooltip("High-frequency (buzzy) motor speed at a full-severity impact, 0..1.")]
        [Range(0f, 1f)] [SerializeField] private float rumbleHighFreqMax = 0.45f;
        [Tooltip("Seconds the motors run after an impact before cutting back to zero.")]
        [SerializeField] private float rumbleDurationSeconds = 0.18f;

        private Camera _cam;
        private Vector3 _posVelocity;
        private Vector3 _lastTargetPos;
        private Vector3 _smoothedVelocity;
        // Follow pose kept separate from the transform so the impact shake stays a pure additive
        // layer and never feeds back into the SmoothDamp / Slerp smoothing.
        private Vector3 _basePosition;
        private Quaternion _baseRotation = Quaternion.identity;

        // Real collision severity comes from the followed car's combat component (a different assembly);
        // the velocity-inferred Detect below stays on underneath it as a scrape fallback.
        private VehicleCombat _combat;
        private bool _subscribed;

        // Gamepad rumble state — the pad we last kicked and how long it has left to run.
        private Gamepad _rumbleGamepad;
        private float _rumbleTimer;

        public void SetTarget(Transform t)
        {
            target = t;
            if (t)
            {
                _lastTargetPos = t.position;
                _smoothedVelocity = Vector3.zero;
                targetBody = t.GetComponentInParent<Rigidbody>();
                impact.Reset(targetBody ? targetBody.linearVelocity : Vector3.zero);
            }
            RebindCombat();   // move the impact subscription to the new car (clears it when t is null)
        }

        private void Awake()
        {
            _cam = GetComponent<Camera>();
            _basePosition = transform.position;
            _baseRotation = transform.rotation;
            if (target)
            {
                _lastTargetPos = target.position;
                if (!targetBody) targetBody = target.GetComponentInParent<Rigidbody>();
                impact.Reset(targetBody ? targetBody.linearVelocity : Vector3.zero);
            }
            RebindCombat();
        }

        private void OnEnable() => SubscribeImpacts();

        private void OnDisable()
        {
            UnsubscribeImpacts();
            StopRumble();   // never leave the motors running once the camera is off
        }

        private void FixedUpdate()
        {
            // Sample the authoritative physics velocity so impacts read cleanly regardless of frame rate.
            if (targetBody)
                impact.Detect(targetBody.linearVelocity, Time.fixedDeltaTime);
        }

        private void LateUpdate()
        {
            UpdateRumble(Time.deltaTime);   // tick the motor timeout even when there's no target to follow
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

            // --- base follow pose (kept clean of shake so the smoothing never feeds back on itself) ---
            Vector3 desiredPos = target.position - followDir * distance + Vector3.up * height;
            _basePosition = Vector3.SmoothDamp(_basePosition, desiredPos, ref _posVelocity, positionSmoothTime);

            Vector3 lookPoint = target.position + Vector3.up * lookHeight + flatVel * lookAheadSeconds;
            Quaternion desiredRot = Quaternion.LookRotation(lookPoint - _basePosition, Vector3.up);
            _baseRotation = Quaternion.Slerp(_baseRotation, desiredRot, 1f - Mathf.Exp(-rotationLerp * dt));

            // --- impact layer: purely additive trauma shake / recoil / FOV punch that decays to zero ---
            if (!targetBody) impact.Detect(rawVel, dt);   // fallback when the target has no rigidbody
            impact.Tick(dt, Time.time);

            transform.rotation = _baseRotation * impact.RotationOffset;
            transform.position = _basePosition
                + _baseRotation * impact.LocalShakeOffset
                + impact.WorldRecoil;

            if (_cam)
                _cam.fieldOfView = Mathf.Min(maxFov, baseFov + speed * 3.6f * fovPerKmh) + impact.FovPunch;
        }

        // ----------------------------------------------------------------- real-impact wiring (VehicleCombat)

        /// <summary>Point at the target car's <see cref="VehicleCombat"/> (its own or a parent's), carrying any
        /// live impact subscription across with it. Safe to call repeatedly and with a null target.</summary>
        private void RebindCombat()
        {
            VehicleCombat next = target ? target.GetComponentInParent<VehicleCombat>() : null;
            if (next == _combat) return;
            UnsubscribeImpacts();               // drop the old car before we lose the reference to it
            _combat = next;
            if (isActiveAndEnabled) SubscribeImpacts();
        }

        private void SubscribeImpacts()
        {
            if (_combat != null && !_subscribed)
            {
                _combat.OnImpact += HandleImpact;
                _subscribed = true;
            }
        }

        private void UnsubscribeImpacts()
        {
            if (_combat != null && _subscribed)
            {
                _combat.OnImpact -= HandleImpact;
                _subscribed = false;
            }
        }

        /// <summary>Feed a genuine collision into the trauma layer off the SAME 0..1 severity the sim used, and
        /// kick the player's gamepad. Recoil is opposite the shove on the car (contact → car), matching the
        /// velocity fallback's <c>-deltaV</c> so inferred and real hits throw the view the same way.</summary>
        private void HandleImpact(VehicleCombat.ImpactEvent e)
        {
            impact.AddImpact(e.Severity, -e.Direction);
            TriggerRumble(e.Severity);
        }

        // ----------------------------------------------------------------- gamepad rumble (player only)

        /// <summary>Kick the current gamepad's motors, amplitude scaled by severity. Null-safe: a missing pad or
        /// a disabled toggle is a no-op. A fresh hit refreshes the full duration; <see cref="StopRumble"/> cuts it.</summary>
        private void TriggerRumble(float severity)
        {
            if (!enableGamepadRumble) return;
            Gamepad pad = Gamepad.current;
            if (pad == null) return;

            severity = Mathf.Clamp01(severity);
            pad.SetMotorSpeeds(rumbleLowFreqMax * severity, rumbleHighFreqMax * severity);
            _rumbleGamepad = pad;
            _rumbleTimer = rumbleDurationSeconds;
        }

        private void UpdateRumble(float dt)
        {
            if (_rumbleTimer <= 0f) return;
            _rumbleTimer -= dt;
            if (_rumbleTimer <= 0f) StopRumble();
        }

        private void StopRumble()
        {
            _rumbleTimer = 0f;
            if (_rumbleGamepad != null && _rumbleGamepad.added)   // skip a pad that's since been unplugged
                _rumbleGamepad.SetMotorSpeeds(0f, 0f);
            _rumbleGamepad = null;
        }
    }
}
