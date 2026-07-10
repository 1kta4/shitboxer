using Shitboxer.Vehicle;
using UnityEngine;

namespace Shitboxer.Race
{
    /// <summary>One rival as the brain sees it this step: world-space position/velocity so the host
    /// stays dumb and the brain does all track-frame reasoning. Plain data so a server can fill it.</summary>
    public struct BotNeighbor
    {
        /// <summary>Rival position minus ours (world; the brain zeroes Y and projects onto the track).</summary>
        public Vector3 RelativePosition;
        /// <summary>Rival world velocity.</summary>
        public Vector3 Velocity;
    }

    /// <summary>What the brain can sense about its own car this step. Plain data so a server can fill it.</summary>
    public struct BotSensors
    {
        public Vector3 Position;
        public Vector3 Forward;
        public Vector3 Velocity;
        /// <summary>Max |slip ratio| across driven wheels — the bot's traction control input.</summary>
        public float DrivenWheelSlip;

        /// <summary>Nearby rivals this step (host-filled buffer; only the first <see cref="NeighborCount"/> are valid).</summary>
        public BotNeighbor[] Neighbors;
        /// <summary>Valid entries in <see cref="Neighbors"/>. 0 (and/or a null array) means "race blind".</summary>
        public int NeighborCount;
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

        // --- Rubber-band (bounded, engine-loop-independent): the host hands us a commitment factor
        // derived from our gap to the field. A trailing bot commits a touch harder, a runaway leader
        // eases off — but always clamped to this subtle band so the assist never reads as cheating.
        // 1 = neutral (the default) reproduces the base plan exactly.
        private const float RubberbandMin = 0.90f; // furthest a leader may ease off (-10%)
        private const float RubberbandMax = 1.10f; // hardest a trailing bot may push (+10%)

        // --- Opponent awareness (all engine-loop-independent; fed by BotSensors.Neighbors) ---
        private const float LaneHalfWidthM = 2.6f;         // lateral band that counts as "in my path"
        private const float CorridorHalfWidthM = 14f;      // keep the pursuit target this far off-centre at most (walls sit ~20 m out)
        private const float FollowRangeM = 24f;            // look this far ahead for a car we could rear-end
        private const float FollowBufferM = 6f;            // gap we try to keep to the car ahead (m)
        private const float FollowClosingGainMps = 0.7f;   // extra approach speed allowed per metre of clear gap
        private const float MinFollowSpeed = 3f;           // never queue slower than this while a gap remains
        private const float OvertakeSpeedMargin = 1.5f;    // must want to go this much faster to bother passing (m/s)
        private const float PassClearanceM = 3f;           // sideways room we aim to leave beside the car we pass
        private const float DraftRangeM = 12f;             // a follower this close behind triggers a light block
        private const float DefendMaxOffsetM = 2.5f;       // cap on the defensive line-cover nudge
        private const float TacticalSlewMps = 5f;          // how fast the tactical offset may move (offset-m per s)
        private const float OvertakeMinSpeed = 5f;         // don't weave for a pass while crawling/spun

        private readonly RacingLine _line;
        private readonly BotSkill _skill;

        private float _stuckTimer;
        private float _reverseTimer;
        private float _tacticalOffsetM; // smoothed lateral tactic (right-of-travel m), added onto the base line offset

        public BotBrain(RacingLine line, BotSkill skill)
        {
            _line = line;
            _skill = skill;
        }

        /// <summary>
        /// Clamps a raw commitment/rubber-band factor to the subtle bounded band. Kept pure and
        /// static so the catch-up boost / leader ease-off is unit-testable without a scene, and so
        /// no gap value the host feeds in — however large — can ever push a bot past +/-10%.
        /// 1 = neutral.
        /// </summary>
        public static float ClampRubberband(float factor) => Mathf.Clamp(factor, RubberbandMin, RubberbandMax);

        /// <summary>
        /// Drives one step. <paramref name="rubberband"/> is the host's commitment factor
        /// (gap-to-field * difficulty); it is clamped internally to the subtle band and scales the
        /// free-flowing speed plan, so 1 (the default) reproduces the base behaviour exactly.
        /// </summary>
        public VehicleInput Step(float dt, in BotSensors sensors, float rubberband = 1f)
        {
            Vector3 fwd = sensors.Forward;
            fwd.y = 0f;
            fwd = fwd.sqrMagnitude > 0.001f ? fwd.normalized : Vector3.forward;

            Vector3 vel = sensors.Velocity;
            vel.y = 0f;
            float speed = vel.magnitude;

            float progress = _line.ProjectPosition(sensors.Position);

            // Local track frame at our position — used to classify where rivals sit (ahead/behind, which side).
            Vector3 trackDir = _line.DirectionAt(progress);
            trackDir.y = 0f;
            trackDir = trackDir.sqrMagnitude > 1e-4f ? trackDir.normalized : fwd;
            Vector3 trackRight = Vector3.Cross(Vector3.up, trackDir); // unit, +right of travel
            float ourAlongSpeed = Vector3.Dot(vel, trackDir);

            // Free-flowing speed plan (before rivals / off-line penalties). Also tells the opponent
            // logic whether we're quick enough to want a pass. The bounded rubber-band nudges the whole
            // plan up/down so a trailing bot commits a little harder and a runaway leader eases off; it
            // rides on top of, and never overrides, the corner-safety and following caps below.
            float freeTargetSpeed = PlanTargetSpeed(progress, speed) * ClampRubberband(rubberband);

            // Opponent awareness: a speed cap so we settle in behind a slower car instead of rear-ending it,
            // plus a lateral tactic (overtake to the clearer side, or a light cover of a drafting follower).
            float baseLateral = -_skill.LateralOffsetM; // existing sign convention (see offset application below)
            PlanVsOpponents(sensors, trackDir, trackRight, ourAlongSpeed, freeTargetSpeed, baseLateral,
                out float speedCap, out float desiredTactical);
            _tacticalOffsetM = Mathf.MoveTowards(_tacticalOffsetM, desiredTactical, TacticalSlewMps * dt);

            // Where are we going? (needed both for driving and for reverse-out steering)
            float lookahead = _skill.LookaheadM + speed * 0.5f;
            Vector3 target = _line.PointAt(progress + lookahead);
            Vector3 offsetDir = Vector3.Cross(Vector3.up, _line.DirectionAt(progress + lookahead)).normalized;
            // Base spread + tactic, clamped to the drivable corridor so a pass/block never steers into a wall.
            float finalLateral = Mathf.Clamp(baseLateral + _tacticalOffsetM, -CorridorHalfWidthM, CorridorHalfWidthM);
            target += offsetDir * finalLateral;
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
            float targetSpeed = freeTargetSpeed;

            // Facing badly off-line (spun, post-crash): creep and pivot instead of powering into a wall.
            float absErr = Mathf.Abs(headingErrDeg);
            if (absErr > 60f)
                targetSpeed = Mathf.Min(targetSpeed, Mathf.Lerp(8f, 3f, Mathf.InverseLerp(60f, 150f, absErr)));

            // Don't rear-end the car ahead: hold at the following cap until we've moved off its bumper.
            targetSpeed = Mathf.Min(targetSpeed, speedCap);

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

        /// <summary>
        /// Opponent awareness, all in the local track frame. Produces a speed cap so we ease in
        /// behind a slower car rather than rear-ending it, and a desired lateral tactic (right-of-
        /// travel metres, additive to the base line offset): swing to the clearer side to overtake
        /// when we're faster, otherwise a light cover of a drafting follower's passing line.
        /// </summary>
        private void PlanVsOpponents(in BotSensors sensors, Vector3 trackDir, Vector3 trackRight,
            float ourAlongSpeed, float freeTargetSpeed, float baseLateral,
            out float speedCap, out float desiredTactical)
        {
            speedCap = float.MaxValue;
            desiredTactical = 0f;

            int count = sensors.Neighbors != null ? Mathf.Min(sensors.NeighborCount, sensors.Neighbors.Length) : 0;
            if (count == 0) return;

            // Nearest rival in our lane ahead (rear-end + overtake trigger) and behind (defence).
            float aheadDist = float.MaxValue, aheadLateral = 0f, aheadAlongSpeed = 0f;
            float behindDist = float.MaxValue, behindLateral = 0f;
            bool hasAhead = false, hasBehind = false;

            for (int i = 0; i < count; i++)
            {
                Vector3 rel = sensors.Neighbors[i].RelativePosition;
                rel.y = 0f;
                float along = Vector3.Dot(rel, trackDir);      // + = ahead of us
                float lateral = Vector3.Dot(rel, trackRight);  // + = to our right

                Vector3 nvel = sensors.Neighbors[i].Velocity;
                nvel.y = 0f;

                if (along > 1f && along < FollowRangeM && Mathf.Abs(lateral) < LaneHalfWidthM && along < aheadDist)
                {
                    aheadDist = along;
                    aheadLateral = lateral;
                    aheadAlongSpeed = Vector3.Dot(nvel, trackDir);
                    hasAhead = true;
                }
                else if (along < -1f && along > -DraftRangeM && Mathf.Abs(lateral) < LaneHalfWidthM * 1.5f && -along < behindDist)
                {
                    behindDist = -along;
                    behindLateral = lateral;
                    hasBehind = true;
                }
            }

            bool wantToPass = false;
            if (hasAhead)
            {
                // Following model: more clear gap buys more approach speed; back right off inside the buffer
                // so we never nose into a stopped/slow car.
                float effGap = aheadDist - FollowBufferM;
                float theirSpeed = Mathf.Max(0f, aheadAlongSpeed);
                float cap = theirSpeed + effGap * FollowClosingGainMps;
                speedCap = Mathf.Max(effGap > 0f ? MinFollowSpeed : 0f, cap);

                wantToPass = ourAlongSpeed > OvertakeMinSpeed
                    && freeTargetSpeed > theirSpeed + OvertakeSpeedMargin;
            }

            if (wantToPass)
                desiredTactical = PickOvertakeOffset(sensors, count, trackDir, trackRight, aheadLateral, baseLateral);
            else if (hasBehind)
                // Light block: lean toward the side the follower is drifting to; small, and corridor-clamped later.
                desiredTactical = Mathf.Clamp(behindLateral, -DefendMaxOffsetM, DefendMaxOffsetM);
        }

        /// <summary>
        /// Picks the pass side: aim to sit PassClearanceM beside the blocker on whichever side keeps us
        /// furthest from a wall and clearest of other traffic. Returns an additive lateral offset
        /// (right-of-travel metres) relative to the base line.
        /// </summary>
        private float PickOvertakeOffset(in BotSensors sensors, int count, Vector3 trackDir,
            Vector3 trackRight, float blockerLateral, float baseLateral)
        {
            float leftTarget = blockerLateral - PassClearanceM;
            float rightTarget = blockerLateral + PassClearanceM;
            float chosen = ScoreLane(sensors, count, trackDir, trackRight, leftTarget)
                         >= ScoreLane(sensors, count, trackDir, trackRight, rightTarget)
                ? leftTarget : rightTarget;
            chosen = Mathf.Clamp(chosen, -CorridorHalfWidthM, CorridorHalfWidthM);
            return chosen - baseLateral;
        }

        /// <summary>Higher = better lane to aim for: penalises wall proximity and traffic already using it.</summary>
        private float ScoreLane(in BotSensors sensors, int count, Vector3 trackDir, Vector3 trackRight, float laneLateral)
        {
            float score = CorridorHalfWidthM - Mathf.Abs(laneLateral);
            if (Mathf.Abs(laneLateral) > CorridorHalfWidthM) score -= 100f; // off the corridor: hard no

            for (int i = 0; i < count; i++)
            {
                Vector3 rel = sensors.Neighbors[i].RelativePosition;
                rel.y = 0f;
                float along = Vector3.Dot(rel, trackDir);
                if (along < -2f || along > FollowRangeM) continue; // only cars alongside or ahead can block a pass
                if (Mathf.Abs(Vector3.Dot(rel, trackRight) - laneLateral) < LaneHalfWidthM * 2f)
                    score -= 30f;
            }
            return score;
        }
    }
}
