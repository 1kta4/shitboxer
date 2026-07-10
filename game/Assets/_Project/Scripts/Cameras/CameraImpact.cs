using UnityEngine;

namespace Shitboxer.Cameras
{
    /// <summary>
    /// Trauma-based impact layer for <see cref="ChaseCamera"/>. Watches the followed car's motion
    /// for collision signatures — a sudden deceleration spike or a hard velocity-direction slam —
    /// and folds them into a normalised "trauma" value (0..1). Trauma drives a Perlin-noise camera
    /// shake (rotational + a little positional), a brief FOV punch on the hard hits, and a short
    /// directional recoil kicked opposite the impact. Everything is purely additive on top of the
    /// camera's follow pose and decays smoothly back to zero. Plain serializable class (no
    /// MonoBehaviour, no physics knowledge of its own) so the owning camera controls update order.
    /// </summary>
    [System.Serializable]
    public class CameraImpact
    {
        [Header("Detection")]
        [Tooltip("Deceleration (m/s^2) at which a hit starts registering — above hard braking, below a real collision.")]
        [SerializeField] private float decelTraumaStart = 30f;
        [Tooltip("Deceleration (m/s^2) that produces a full-strength hit.")]
        [SerializeField] private float decelTraumaFull = 200f;
        [Tooltip("Per-step velocity-direction change (deg) at which a sideways slam starts registering.")]
        [SerializeField] private float dirChangeStartDeg = 20f;
        [Tooltip("Per-step velocity-direction change (deg) that produces a full-strength hit.")]
        [SerializeField] private float dirChangeFullDeg = 80f;
        [Tooltip("Ignore direction-change hits below this speed (m/s) so slow manoeuvres stay calm.")]
        [SerializeField] private float minSpeedForDetection = 3f;

        [Header("Trauma / Shake")]
        [Tooltip("How fast trauma bleeds back to zero (per second).")]
        [SerializeField] private float traumaDecayPerSecond = 1.8f;
        [Tooltip("Peak rotational shake at full (trauma^2) shake, degrees.")]
        [SerializeField] private float maxShakeDegrees = 2.5f;
        [Tooltip("Peak positional shake at full shake, metres (view-relative).")]
        [SerializeField] private float maxShakeMeters = 0.05f;
        [Tooltip("Roll shake is scaled down relative to pitch/yaw so it never rolls the horizon hard.")]
        [Range(0f, 1f)] [SerializeField] private float rollScale = 0.6f;
        [Tooltip("Noise sampling rate — higher is buzzier, lower is a slower sway.")]
        [SerializeField] private float shakeFrequency = 24f;

        [Header("FOV Punch")]
        [Tooltip("Only hits above this severity (0..1) punch the FOV.")]
        [Range(0f, 1f)] [SerializeField] private float fovPunchThreshold = 0.35f;
        [Tooltip("Additive FOV kick on a full-strength hit, degrees.")]
        [SerializeField] private float fovPunchDegrees = 5f;
        [Tooltip("How fast the FOV punch decays (degrees per second).")]
        [SerializeField] private float fovPunchDecayPerSecond = 10f;

        [Header("Directional Recoil")]
        [Tooltip("Only hits above this severity (0..1) recoil the camera.")]
        [Range(0f, 1f)] [SerializeField] private float recoilThreshold = 0.2f;
        [Tooltip("Peak positional recoil kicked opposite the impact, metres.")]
        [SerializeField] private float recoilMeters = 0.2f;
        [Tooltip("SmoothDamp time for the recoil to settle back to zero (~150 ms).")]
        [SerializeField] private float recoilSettleTime = 0.12f;

        // Perlin sample offsets — fixed constants keep the noise channels decorrelated and fully
        // deterministic (no System.Random / Math.random anywhere; the clock is Time via Tick).
        private const float SeedPitch = 0f;
        private const float SeedYaw = 37.2f;
        private const float SeedRoll = 71.9f;
        private const float SeedShakeX = 113.4f;
        private const float SeedShakeY = 157.1f;

        private float _trauma;
        private float _fovPunch;
        private Vector3 _recoil;
        private Vector3 _recoilVel;
        private Vector3 _prevVelocity;
        private bool _hasPrev;

        // Cached results, refreshed by Tick and read by the camera in the same frame.
        private Quaternion _rotationOffset = Quaternion.identity;
        private Vector3 _localShake;

        /// <summary>Current trauma (0..1) — exposed so other juice (rumble/FX) can share the one number.</summary>
        public float Trauma => _trauma;
        /// <summary>Local-space rotational shake, applied as <c>rotation *= RotationOffset</c>.</summary>
        public Quaternion RotationOffset => _rotationOffset;
        /// <summary>View-relative positional shake (x = right, y = up); z stays zero to avoid dolly nausea.</summary>
        public Vector3 LocalShakeOffset => _localShake;
        /// <summary>World-space directional recoil, added to the camera position.</summary>
        public Vector3 WorldRecoil => _recoil;
        /// <summary>Additive FOV punch in degrees, layered on top of the speed-based FOV.</summary>
        public float FovPunch => _fovPunch;

        /// <summary>Reset the detection baseline and clear any in-flight shake (call when the target changes).</summary>
        public void Reset(Vector3 velocity)
        {
            _prevVelocity = velocity;
            _hasPrev = true;
            _trauma = 0f;
            _fovPunch = 0f;
            _recoil = Vector3.zero;
            _recoilVel = Vector3.zero;
            _rotationOffset = Quaternion.identity;
            _localShake = Vector3.zero;
        }

        /// <summary>
        /// Feed the followed car's velocity each step. A deceleration spike or a hard velocity-
        /// direction change adds trauma, an FOV punch, and a recoil kicked opposite the impact.
        /// </summary>
        public void Detect(Vector3 velocity, float dt)
        {
            if (dt <= 0f || !_hasPrev)
            {
                _prevVelocity = velocity;
                _hasPrev = true;
                return;
            }

            Vector3 deltaV = velocity - _prevVelocity;
            float speed = velocity.magnitude;
            float prevSpeed = _prevVelocity.magnitude;

            // Deceleration spike — a wall or a ram bleeds a lot of speed in a single step.
            float decel = (prevSpeed - speed) / dt;
            float decelSeverity = Mathf.InverseLerp(decelTraumaStart, decelTraumaFull, decel);

            // Direction slam — velocity snapped sideways (shunt / spin); only while actually moving.
            float dirSeverity = 0f;
            if (prevSpeed > minSpeedForDetection && speed > 0.01f)
            {
                float angle = Vector3.Angle(_prevVelocity, velocity);
                dirSeverity = Mathf.InverseLerp(dirChangeStartDeg, dirChangeFullDeg, angle);
            }

            float severity = Mathf.Max(decelSeverity, dirSeverity);
            if (severity > 0f)
                AddImpact(severity, -deltaV);   // recoil opposite the impact (along the lost momentum)

            _prevVelocity = velocity;
        }

        /// <summary>
        /// Inject an impact directly: <paramref name="severity"/> is 0..1, <paramref name="worldRecoilDir"/>
        /// is the world-space direction to recoil toward (need not be normalised). Detection routes
        /// through here, and it is public so a future collision responder could add impacts the car's
        /// own motion can't infer (e.g. a tap while nearly stationary).
        /// </summary>
        public void AddImpact(float severity, Vector3 worldRecoilDir)
        {
            severity = Mathf.Clamp01(severity);
            if (severity <= 0f) return;

            _trauma = Mathf.Clamp01(_trauma + severity);

            float punch = Mathf.InverseLerp(fovPunchThreshold, 1f, severity) * fovPunchDegrees;
            if (punch > _fovPunch) _fovPunch = punch;

            if (severity >= recoilThreshold && worldRecoilDir.sqrMagnitude > 1e-6f)
                _recoil = Vector3.ClampMagnitude(
                    _recoil + worldRecoilDir.normalized * (severity * recoilMeters), recoilMeters);
        }

        /// <summary>
        /// Decay trauma / punch / recoil and refresh the cached shake offsets. <paramref name="time"/>
        /// is the noise clock (pass <c>Time.time</c>); <paramref name="dt"/> is the frame delta.
        /// </summary>
        public void Tick(float dt, float time)
        {
            if (dt > 0f)
            {
                _trauma = Mathf.Max(0f, _trauma - traumaDecayPerSecond * dt);
                _fovPunch = Mathf.Max(0f, _fovPunch - fovPunchDecayPerSecond * dt);
                _recoil = Vector3.SmoothDamp(_recoil, Vector3.zero, ref _recoilVel, recoilSettleTime, Mathf.Infinity, dt);
            }

            // Square trauma so taps barely register and only hard hits shake hard.
            float shake = _trauma * _trauma;
            float ang = maxShakeDegrees * shake;
            float t = time * shakeFrequency;
            _rotationOffset = Quaternion.Euler(
                ang * SignedNoise(SeedPitch, t),
                ang * SignedNoise(SeedYaw, t),
                ang * rollScale * SignedNoise(SeedRoll, t));

            float posShake = maxShakeMeters * shake;
            _localShake = new Vector3(
                posShake * SignedNoise(SeedShakeX, t),
                posShake * SignedNoise(SeedShakeY, t),
                0f);
        }

        // Perlin noise remapped from 0..1 to -1..1 — smooth, deterministic, no random calls.
        private static float SignedNoise(float seed, float t) => Mathf.PerlinNoise(seed, t) * 2f - 1f;
    }
}
