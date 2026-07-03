using Shitboxer.Vehicle;
using UnityEngine;

namespace Shitboxer.Race
{
    /// <summary>
    /// Host adapter for BotBrain: gathers sensors from the rigidbody each physics step,
    /// runs the brain, and writes VehicleController.Input. Sits where VehicleInputProvider
    /// would on a human car — never put both on the same vehicle.
    /// </summary>
    [RequireComponent(typeof(VehicleController))]
    public class BotDriver : MonoBehaviour
    {
        private const float FlippedRecoverS = 3f;
        private const float ResetToTrackAfterS = 8f;
        private const float ProgressAnchorWindowM = 6f;

        [SerializeField] private TrackPath trackPath;
        [SerializeField] private BotSkill skill = BotSkill.Default;

        private VehicleController _controller;
        private BotBrain _brain;
        private float _flippedTimer;
        private float _noProgressTimer;
        private float _lastProgress;

        /// <summary>Wires the bot up (used by editor builders — sets serialized fields only).</summary>
        public void Configure(TrackPath path, float cornerSpeedMult, float aggression, float lookaheadM, float lateralOffsetM = 0f)
        {
            trackPath = path;
            skill = new BotSkill
            {
                CornerSpeedMult = cornerSpeedMult,
                Aggression = aggression,
                LookaheadM = lookaheadM,
                LateralOffsetM = lateralOffsetM,
            };
            _brain = null;
        }

        private void Awake()
        {
            _controller = GetComponent<VehicleController>();
        }

        private void OnDisable()
        {
            // Coast when switched off (e.g. by RaceManager after finishing) — a lingering
            // brake input would read as reverse once the car stops.
            if (_controller) _controller.Input = default;
        }

        private void FixedUpdate()
        {
            if (!trackPath || trackPath.Line == null) return;
            _brain ??= new BotBrain(trackPath.Line, skill);

            Vector3 velocity = _controller.Body ? _controller.Body.linearVelocity : Vector3.zero;
            UpdateFlipRecovery(velocity);
            UpdateResetToTrack();

            var sensors = new BotSensors
            {
                Position = transform.position,
                Forward = transform.forward,
                Velocity = velocity,
                DrivenWheelSlip = MaxDrivenWheelSlip(),
            };

            _controller.Input = _brain.Step(Time.fixedDeltaTime, sensors);
        }

        private float MaxDrivenWheelSlip()
        {
            var sim = _controller.Sim;
            if (sim == null) return 0f;
            float max = 0f;
            for (int i = 0; i < VehicleSim.WheelCount; i++)
                if (sim.IsDriven(i))
                    max = Mathf.Max(max, Mathf.Abs(sim.SlipRatio[i]));
            return max;
        }

        /// <summary>
        /// Bots have no "press R to flip" — right them after a few seconds resting well off
        /// their wheels. Threshold is deliberately loose (leaning on a wall on two wheels
        /// counts): with wheels unloaded the brain's reverse-out recovery is powerless.
        /// </summary>
        private void UpdateFlipRecovery(Vector3 velocity)
        {
            bool flipped = transform.up.y < 0.6f && velocity.magnitude < 3f;
            _flippedTimer = flipped ? _flippedTimer + Time.fixedDeltaTime : 0f;
            if (_flippedTimer < FlippedRecoverS || !_controller.Body) return;

            _flippedTimer = 0f;
            Rigidbody body = _controller.Body;
            Vector3 flatFwd = Vector3.ProjectOnPlane(transform.forward, Vector3.up).normalized;
            if (flatFwd.sqrMagnitude < 0.01f) flatFwd = Vector3.forward;
            body.position += Vector3.up * 1.5f;
            body.rotation = Quaternion.LookRotation(flatFwd, Vector3.up);
            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
        }

        /// <summary>
        /// Last-resort watchdog: whatever the failure mode (wedged between crates, balanced
        /// on a wall lip, recovery loop that never escapes), a bot that makes no track
        /// progress for ResetToTrackAfterS gets teleported onto the centreline facing the
        /// racing direction. Guarantees no permanent stalls.
        /// </summary>
        private void UpdateResetToTrack()
        {
            float progress = trackPath.ProjectPosition(transform.position);
            // _lastProgress is an anchor: gaining ProgressAnchorWindowM of track distance
            // from it counts as progress and re-anchors; failing to for ResetToTrackAfterS
            // (slowest legit crawl still re-anchors well inside the window) triggers reset.
            if (Mathf.Abs(trackPath.Line.SignedDelta(_lastProgress, progress)) > ProgressAnchorWindowM)
            {
                _lastProgress = progress;
                _noProgressTimer = 0f;
                return;
            }

            _noProgressTimer += Time.fixedDeltaTime;
            if (_noProgressTimer < ResetToTrackAfterS || !_controller.Body) return;

            _noProgressTimer = 0f;
            _lastProgress = progress;
            Rigidbody body = _controller.Body;
            body.position = trackPath.Line.PointAt(progress) + Vector3.up * 1.2f;
            body.rotation = Quaternion.LookRotation(trackPath.Line.DirectionAt(progress), Vector3.up);
            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
        }
    }
}
