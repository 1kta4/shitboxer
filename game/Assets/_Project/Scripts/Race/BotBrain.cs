using Shitboxer.Vehicle;
using UnityEngine;

namespace Shitboxer.Race
{
    /// <summary>What the brain can sense about its own car this step. Plain data so a server can fill it.</summary>
    public struct BotSensors
    {
        public Vector3 Position;
        public Vector3 Forward;
        public Vector3 Velocity;
        /// <summary>Max |slip ratio| across driven wheels — the bot's traction control input.</summary>
        public float DrivenWheelSlip;
    }

    /// <summary>Per-bot personality knobs so a field of 7 doesn't drive in lockstep.</summary>
    [System.Serializable]
    public struct BotSkill
    {
        [Tooltip("Scales cornering speed. ~0.8 timid, ~1.05 on the ragged edge.")]
        public float CornerSpeedMult;

        [Tooltip("Scales straight-line commitment and throttle sharpness. ~0.7 cruiser, ~1.1 sends it.")]
        public float Aggression;

        [Tooltip("Base pure-pursuit lookahead in metres (speed adds more). Lower = tighter, twitchier lines.")]
        public float LookaheadM;

        [Tooltip("Sideways offset from the centreline, metres (+ = right of travel). Spreads the field so bots race instead of forming a train.")]
        public float LateralOffsetM;

        public static BotSkill Default => new BotSkill { CornerSpeedMult = 1f, Aggression = 1f, LookaheadM = 12f };
    }

    /// <summary>
    /// Plain-C# driving policy: pure pursuit of a lookahead point on a RacingLine for
    /// steering, curvature-ahead speed planning for throttle/brake, and a timed
    /// reverse-out recovery when wedged against a wall. No engine-loop or scene
    /// dependency — step it from FixedUpdate today, from a headless server later.
    /// </summary>
    public sealed class BotBrain
    {
        // Tuning shared by all bots; per-bot flavour comes from BotSkill.
        private const float MaxLatAccel = 10f;         // m/s^2 assumed cornering grip
        private const float BrakeDecel = 8f;           // m/s^2 assumed braking
        private const float BaseStraightSpeed = 38f;   // m/s before aggression bonus
        private const float SteerSaturationDeg = 35f;  // heading error that means full lock
        private const float CurvatureHalfWindowM = 6f;
        private const float PlanHorizonStepM = 6f;
        private const float StuckSpeedMps = 1.2f;
        private const float StuckTriggerS = 2f;
        private const float ReverseDurationS = 2.0f;
        private const float SettleBeforeReverseS = 0.4f;
        private const float TractionSlipLimit = 0.2f;

        private readonly RacingLine _line;
        private readonly BotSkill _skill;

        private float _stuckTimer;
        private float _reverseTimer;

        public BotBrain(RacingLine line, BotSkill skill)
        {
            _line = line;
            _skill = skill;
        }

        public VehicleInput Step(float dt, in BotSensors sensors)
        {
            Vector3 fwd = sensors.Forward;
            fwd.y = 0f;
            fwd = fwd.sqrMagnitude > 0.001f ? fwd.normalized : Vector3.forward;

            Vector3 vel = sensors.Velocity;
            vel.y = 0f;
            float speed = vel.magnitude;

            float progress = _line.ProjectPosition(sensors.Position);

            // Where are we going? (needed both for driving and for reverse-out steering)
            float lookahead = _skill.LookaheadM + speed * 0.5f;
            Vector3 target = _line.PointAt(progress + lookahead);
            if (Mathf.Abs(_skill.LateralOffsetM) > 0.01f)
            {
                Vector3 lineDir = _line.DirectionAt(progress + lookahead);
                target += Vector3.Cross(Vector3.up, lineDir).normalized * -_skill.LateralOffsetM;
            }
            Vector3 toTarget = target - sensors.Position;
            toTarget.y = 0f;
            float headingErrDeg = Vector3.SignedAngle(fwd, toTarget, Vector3.up);

            // --- Stuck recovery: settle the wheels first (reverse only engages once the
            // driven wheels stop spinning), then back away with the nose cocked at the target.
            if (_reverseTimer > 0f)
            {
                _reverseTimer -= dt;
                if (_reverseTimer <= 0f) _stuckTimer = 0f;
                bool settling = _reverseTimer > ReverseDurationS - SettleBeforeReverseS;
                return new VehicleInput
                {
                    Steer = settling ? 0f : -Mathf.Sign(headingErrDeg),
                    Throttle = 0f,
                    Brake = 1f, // pure brake while wheels spin down, then reverse throttle
                    Handbrake = settling ? 1f : 0f,
                };
            }

            // --- Steering: pure pursuit.
            float steer = Mathf.Clamp(headingErrDeg / SteerSaturationDeg, -1f, 1f);

            // --- Speed plan: slowest corner within braking range wins.
            float targetSpeed = PlanTargetSpeed(progress, speed);

            // Facing badly off-line (spun, post-crash): creep and pivot instead of powering into a wall.
            float absErr = Mathf.Abs(headingErrDeg);
            if (absErr > 60f)
                targetSpeed = Mathf.Min(targetSpeed, Mathf.Lerp(8f, 3f, Mathf.InverseLerp(60f, 150f, absErr)));

            float speedError = targetSpeed - speed;
            float throttle = 0f, brake = 0f;
            if (speedError > 0.5f)
                throttle = Mathf.Clamp01(speedError * (0.2f + 0.2f * _skill.Aggression));
            else if (speedError < -1.5f)
                brake = Mathf.Clamp01(-speedError * 0.25f);
            else
                throttle = 0.15f; // hold speed against drag

            // Traction control: past ~1.5x peak slip the tyre is burning grip it could
            // spend cornering — the Power bots' spin-then-stall cycle starts exactly here.
            if (sensors.DrivenWheelSlip > TractionSlipLimit)
                throttle = Mathf.Min(throttle, Mathf.Lerp(0.5f, 0.1f,
                    Mathf.InverseLerp(TractionSlipLimit, TractionSlipLimit * 3f, sensors.DrivenWheelSlip)));

            // --- Stuck detection: commanded forward but not moving (nosed into a wall).
            if (speed < StuckSpeedMps && throttle > 0.3f)
                _stuckTimer += dt;
            else
                _stuckTimer = Mathf.Max(0f, _stuckTimer - dt * 2f);

            if (_stuckTimer >= StuckTriggerS)
            {
                _stuckTimer = 0f;
                _reverseTimer = ReverseDurationS;
            }

            return new VehicleInput
            {
                Steer = steer,
                Throttle = throttle,
                Brake = brake,
                Handbrake = 0f,
            };
        }

        /// <summary>
        /// Samples curvature ahead and returns the highest speed that still allows braking
        /// down to each upcoming corner's speed at BrakeDecel.
        /// </summary>
        private float PlanTargetSpeed(float progress, float speed)
        {
            float target = BaseStraightSpeed + 14f * _skill.Aggression;
            float horizon = Mathf.Max(40f, speed * speed / (2f * BrakeDecel) + 20f);

            for (float d = 4f; d <= horizon; d += PlanHorizonStepM)
            {
                float curvature = _line.CurvatureAt(progress + d, CurvatureHalfWindowM);
                if (curvature < 1e-3f) continue;

                float cornerSpeed = Mathf.Sqrt(MaxLatAccel / curvature) * _skill.CornerSpeedMult;
                float allowedNow = Mathf.Sqrt(cornerSpeed * cornerSpeed
                    + 2f * BrakeDecel * Mathf.Max(0f, d - CurvatureHalfWindowM));
                target = Mathf.Min(target, allowedNow);
            }
            return target;
        }
    }
}
