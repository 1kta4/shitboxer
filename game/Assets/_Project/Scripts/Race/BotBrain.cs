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

        /// <summary>
        /// True when this neighbour is the human player — the only car a bot holds a memory of.
        /// <c>false</c> (the struct default) makes every neighbour anonymous, which is exactly today's
        /// behaviour, so a host that never sets it drives unchanged.
        ///
        /// A bool rather than an identity key because only the player is ever modelled: a key plus a
        /// key-to-profile lookup inside the brain would cost allocation and complexity for a capability
        /// nothing needs. NOTE this assumes exactly one human — split-screen or netcode would need this to
        /// become a key. Cheap to change; nothing else depends on the shape.
        /// </summary>
        public bool IsPlayer;
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

        // --- Personality/racecraft (all 0..1; 0 = today's neutral behaviour, so old presets and
        // already-saved scenes that leave these fields at their struct default drive exactly as before). ---

        [Tooltip("0..1. How hard the bot covers the racing line when a rival is drafting it. 0 = today's light cover, 1 = blocks harder and picks up drafters from further back.")]
        public float Defensiveness;

        [Tooltip("0..1. Commitment when passing: bolder bots attack on a slimmer speed advantage and slice past leaving less room. 0 = today's cautious pass.")]
        public float OvertakeBoldness;

        [Tooltip("0..1. Chance of an occasional brief, bounded throttle-lift bobble so the bot isn't robotic. Deterministic per bot (seeded off its own track position). 0 = flawless (today's behaviour).")]
        public float MistakeRate;

        [Tooltip("0..1. Steadiness: higher damps the size of any bobble. Only matters when MistakeRate > 0.")]
        public float Consistency;

        public static BotSkill Default => new BotSkill
        {
            CornerSpeedMult = 1f,
            Aggression = 1f,
            LookaheadM = 12f,
            // Neutral racecraft: the reference bot defends/passes exactly as the pre-personality code did
            // and never bobbles, so BotSkill.Default reproduces prior behaviour bit-for-bit.
            Defensiveness = 0f,
            OvertakeBoldness = 0f,
            MistakeRate = 0f,
            Consistency = 1f,
        };
    }

    /// <summary>
    /// What the brain believes its car can do. Fed by the host from the car's ACTUAL VehicleSpec,
    /// so a bot handed better tyres drives to them.
    ///
    /// This used to be two hardcoded constants (10 / 8), which had two costs. It left the bots
    /// under-driving even their stock car — a GripBox is PeakMu 1.32, so ~13 m/s^2 of grip planned
    /// as if it were 10 — and it meant any attempt to scale a bot's car was invisible to the brain:
    /// you could hand a rival race slicks and it would still brake for the corner as though it were
    /// on the old ones. Cornering speed is sqrt(MaxLatAccel / curvature), so this struct is what
    /// decides bot pace.
    /// </summary>
    public struct BotLimits
    {
        /// <summary>Cornering grip the speed plan trusts (m/s^2).</summary>
        public float MaxLatAccel;

        /// <summary>Braking the speed plan trusts (m/s^2).</summary>
        public float BrakeDecel;

        /// <summary>Braking as a fraction of cornering grip — same contact patch, minus a margin
        /// for weight transfer and the lack of ABS, so bots don't plan on locking up.</summary>
        private const float BrakeGripFraction = 0.9f;

        /// <summary>The historical hardcoded pair. Reproduces the pre-BotLimits plan bit-for-bit.</summary>
        public static BotLimits Default => new BotLimits { MaxLatAccel = 10f, BrakeDecel = 8f };

        /// <summary>
        /// Limits implied by a tyre's peak grip coefficient: mu*g of lateral, a shade less braking.
        /// Pure so the mapping is unit-testable without a scene.
        /// </summary>
        public static BotLimits FromGrip(float peakMu)
        {
            float lat = Mathf.Max(1f, peakMu * 9.81f);
            return new BotLimits { MaxLatAccel = lat, BrakeDecel = Mathf.Max(1f, lat * BrakeGripFraction) };
        }
    }

    /// <summary>
    /// Plain-C# driving policy: pure pursuit of a lookahead point on a RacingLine for
    /// steering, curvature-ahead speed planning for throttle/brake, and a timed
    /// reverse-out recovery when wedged against a wall. No engine-loop or scene
    /// dependency — step it from FixedUpdate today, from a headless server later.
    /// </summary>
    public sealed class BotBrain
    {
        // Tuning shared by all bots; per-bot flavour comes from BotSkill, per-car from BotLimits.
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

        // --- Personality/racecraft (all bounded so a field of distinct characters still completes clean laps).
        // Every knob is a 0..1 BotSkill value; at 0 each expression below collapses to the pre-personality
        // constant, so the neutral bot is unchanged. ---
        private const float DraftRangeDefenseGain = 0.5f;         // full defensiveness picks a drafter up 50% further back
        private const float DefendOffsetDefenseGain = 1f;         // ...and covers up to 2x the base line (still corridor-clamped)
        private const float OvertakeMarginBoldnessCut = 0.6f;     // full boldness commits on 40% of the base speed advantage
        private const float OvertakeClearanceBoldnessCut = 0.55f; // ...and slices past leaving 45% of the base gap (never negative)

        // --- Persistent-memory gains. Every one multiplies a *Effective bias that is 0 for an unknown
        // player, so all of these are inert until the memory layer supplies a confident profile. Sized so a
        // maximally-remembered player shifts racecraft noticeably but never past the bounds a maximally
        // defensive/bold bot already reaches today. ---
        private const float FollowGapCautionGain = 0.5f;   // max caution widens the follow buffer ~17.5%
        private const float FollowGapSpaceGain = 0.25f;    // ...and an erratic player buys a bit more again
        private const float MaxMemoryFollowGapScale = 1.7f; // ceiling on the combined widening (base band tops out at 1.4)
        private const float ClearanceCautionGain = 0.35f;  // and more room when slicing past them
        private const float ClearanceSpaceGain = 0.20f;
        private const float SideCoverM = 1.2f;             // how far to pre-cover the side a player favours (m)
        private const float LanePreferenceScoreGain = 8f;  // SAFETY-CRITICAL: must stay well under ScoreLane's 30/car penalty
        private const float AlongsideHalfLengthM = 3.5f;   // longitudinal overlap that counts as "beside us"
        private const float AlongsideHalfWidthM = 3.2f;    // ...within this lateral gap. Door-slam guard only.
        private const float MistakeBinLengthM = 16f;              // each ~16 m of track gets one stable mistake draw (~0.5 s bobble at pace)
        private const float MistakeMaxChance = 0.35f;             // at MistakeRate 1, ~35% of bins bobble
        private const float MistakeMaxLift = 0.5f;                // a bobble eases at most half the throttle — a lift, never a stop
        private const float MistakeConsistencyDamp = 0.6f;        // a rock-steady (Consistency 1) bot only ever twitches
        private const float MistakeBrakeSoften = 0.5f;            // the late-brake half is bounded to half the lift so it stays on the corridor

        private readonly RacingLine _line;
        private readonly BotSkill _skill;
        private readonly BotLimits _limits;
        private readonly int _mistakeSeed; // stable per-bot seed so no two cars bobble in lockstep at the same corner

        private float _stuckTimer;
        private float _reverseTimer;
        private float _tacticalOffsetM; // smoothed lateral tactic (right-of-travel m), added onto the base line offset

        // Difficulty/skill-tier layer. Nominal by default -> Evaluate() yields identity modifiers, so a bot
        // with no difficulty set drives exactly as before. The host (BotDriver / a server) opts in via
        // SetDifficulty; nothing here reads the scene, so the core stays engine-loop-independent.
        private BotDifficulty _difficulty = BotDifficulty.Nominal;

        // Personality/archetype layer (orthogonal to difficulty: personality = how it races others, difficulty
        // = how good it is). Neutral by default -> every tactical bias is identity, so a bot with no personality
        // set defends/passes/follows exactly as before. The host opts in via SetPersonality; nothing here reads
        // the scene, so the core stays engine-loop-independent.
        private BotPersonality _personality = BotPersonality.Neutral;

        // Persistent-memory layer (orthogonal to both: memory = what this rival has learned about THIS player,
        // personality = how it races anyone, difficulty = how good it is). Unknown by default -> every bias is
        // gated to zero by Confidence01 == 0, so a bot with no memory set races exactly as before. The host
        // opts in via SetPlayerMemory; nothing here reads the scene, so the core stays engine-loop-independent.
        // Only ever consulted for a neighbour flagged BotNeighbor.IsPlayer.
        private RivalMemoryProfile _playerMemory = RivalMemoryProfile.Unknown;

        /// <summary>Brain on the historical hardcoded limits (10 / 8) — unchanged behaviour.</summary>
        public BotBrain(RacingLine line, BotSkill skill) : this(line, skill, BotLimits.Default) { }

        /// <summary>
        /// Brain that plans against <paramref name="limits"/> — the host derives these from the
        /// car's real spec, so scaling a bot's tyres actually shows up in its lap time.
        /// </summary>
        public BotBrain(RacingLine line, BotSkill skill, BotLimits limits)
        {
            _line = line;
            _skill = skill;
            _limits = limits;
            _mistakeSeed = ComputeSeed(skill);
        }

        /// <summary>
        /// Sets the bounded difficulty/skill-tier model consulted each <see cref="Step"/>. Fed by the host
        /// (kept out of the core so a headless server can drive it too). <see cref="BotDifficulty.Nominal"/>
        /// — the default — reproduces today's behaviour exactly.
        /// </summary>
        public void SetDifficulty(in BotDifficulty difficulty) => _difficulty = difficulty;

        /// <summary>
        /// Sets the bounded on-track personality/archetype consulted each <see cref="Step"/>, biasing the
        /// EXISTING tactical knobs (line cover, pass commitment, follow gap). Fed by the host and kept out of
        /// the core so a headless server can set it too. <see cref="BotPersonality.Neutral"/> — the default —
        /// leaves every bias at identity, reproducing today's behaviour exactly.
        /// </summary>
        public void SetPersonality(in BotPersonality personality) => _personality = personality;

        /// <summary>
        /// Sets what this rival remembers about the human player, biasing the SAME tactical knobs the
        /// personality layer touches (line cover, pass commitment, follow gap, lane choice) — never pace.
        /// Fed by the host from the persistent career memory and kept out of the core so a headless server
        /// can set it too. <see cref="RivalMemoryProfile.Unknown"/> — the default — leaves every bias gated
        /// to zero, reproducing today's behaviour exactly.
        /// </summary>
        public void SetPlayerMemory(in RivalMemoryProfile memory) => _playerMemory = memory;

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
        /// <paramref name="signedGapM"/> is this bot's track-distance gap to the field (+ ahead, - behind),
        /// supplied by the host; it feeds the difficulty/skill-tier model set via <see cref="SetDifficulty"/>.
        /// With the default (nominal) difficulty the model returns identity modifiers, so the gap has no
        /// effect and the output is bit-for-bit the base behaviour.
        /// </summary>
        public VehicleInput Step(float dt, in BotSensors sensors, float rubberband = 1f, float signedGapM = 0f)
        {
            // Bounded difficulty/skill-tier modifiers for this step (identity under the default nominal config).
            BotModifiers mods = _difficulty.Evaluate(signedGapM);

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
            // plan up/down so a trailing bot commits a little harder and a runaway leader eases off; the
            // difficulty/skill tier layers a second bounded scale on top (pro carries a touch more speed,
            // rookie a touch less). Both ride on top of, and never override, the corner-safety and following
            // caps below. At nominal difficulty mods.TargetSpeedScale == 1, so this is unchanged.
            float freeTargetSpeed = PlanTargetSpeed(progress, speed) * ClampRubberband(rubberband) * mods.TargetSpeedScale;

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

            // --- Steering: pure pursuit. Skill sharpens/softens the response (mods.SteerSharpness == 1 at
            // nominal, so `headingErrDeg * 1f / SteerSaturationDeg` is the base expression bit-for-bit).
            float steer = Mathf.Clamp(headingErrDeg * mods.SteerSharpness / SteerSaturationDeg, -1f, 1f);

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

            // Difficulty/skill throttle bias, bounded: a pro commits a touch more, a rookie a touch less, and
            // a trailing bot pushes via the rubber-band. Clamped back into [0,1]; the traction-control and
            // stuck-detection checks below still see the result. mods.ThrottleScale == 1 at nominal, and
            // throttle is already in [0,1] here, so Clamp01(throttle * 1) leaves it untouched bit-for-bit.
            throttle = Mathf.Clamp01(throttle * mods.ThrottleScale);

            // Traction control: past ~1.5x peak slip the tyre is burning grip it could
            // spend cornering — the Power bots' spin-then-stall cycle starts exactly here.
            if (sensors.DrivenWheelSlip > TractionSlipLimit)
                throttle = Mathf.Min(throttle, Mathf.Lerp(0.5f, 0.1f,
                    Mathf.InverseLerp(TractionSlipLimit, TractionSlipLimit * 3f, sensors.DrivenWheelSlip)));

            // --- Personality: an occasional brief, bounded, per-bot-DETERMINISTIC bobble. It is seeded off our
            // own quantised progress (a hash — never Math/Unity Random or Time), so it reproduces bit-for-bit on
            // a headless server and repeats lap-to-lap for a given bot. It only ever eases the throttle (and
            // softens the brake a touch), so a mistake costs time and strings the field out without ever
            // carrying enough speed to leave the corridor. MistakeRate 0 leaves throttle/brake untouched.
            float mistake = MistakeFactor(progress, _skill.MistakeRate, _skill.Consistency, _mistakeSeed);
            if (mistake > 0f)
            {
                throttle *= 1f - mistake;
                brake *= 1f - mistake * MistakeBrakeSoften;
            }

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
            float horizon = Mathf.Max(40f, speed * speed / (2f * _limits.BrakeDecel) + 20f);

            for (float d = 4f; d <= horizon; d += PlanHorizonStepM)
            {
                float curvature = _line.CurvatureAt(progress + d, CurvatureHalfWindowM);
                if (curvature < 1e-3f) continue;

                float cornerSpeed = Mathf.Sqrt(_limits.MaxLatAccel / curvature) * _skill.CornerSpeedMult;
                float allowedNow = Mathf.Sqrt(cornerSpeed * cornerSpeed
                    + 2f * _limits.BrakeDecel * Mathf.Max(0f, d - CurvatureHalfWindowM));
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

            // Racecraft (per-bot skill) folded with the archetype personality bias (orthogonal character): a
            // more defensive bot / a Blocker archetype picks a drafter up from further back and covers more of
            // the line; a bolder bot / a Diver archetype commits to passes on a slimmer speed advantage (and
            // squeezes closer, see PickOvertakeOffset). Each archetype bias is 0 at Neutral, so for a neutral
            // personality every term below is bit-for-bit the pre-archetype value.
            float defensiveness = Mathf.Clamp01(_skill.Defensiveness + _personality.BlockBiasClamped);
            float boldness = Mathf.Clamp01(_skill.OvertakeBoldness + _personality.DiveAggressionClamped);
            float draftRange = DraftRangeM * (1f + DraftRangeDefenseGain * defensiveness);

            // Memory: a rival that has learned this player is a threat starts watching for them from further
            // back than it would any other car. ThreatEffective is 0 for an unknown player, so this collapses
            // to the identical float expression as `draftRange` above — bit-for-bit, not merely close.
            float defensivenessVsPlayer = Mathf.Clamp01(defensiveness + _playerMemory.ThreatEffective);
            float draftRangePlayer = DraftRangeM * (1f + DraftRangeDefenseGain * defensivenessVsPlayer);

            int count = sensors.Neighbors != null ? Mathf.Min(sensors.NeighborCount, sensors.Neighbors.Length) : 0;
            if (count == 0) return;

            // Nearest rival in our lane ahead (rear-end + overtake trigger) and behind (defence).
            float aheadDist = float.MaxValue, aheadLateral = 0f, aheadAlongSpeed = 0f;
            float behindDist = float.MaxValue, behindLateral = 0f;
            bool hasAhead = false, hasBehind = false;
            bool aheadIsPlayer = false, behindIsPlayer = false;

            for (int i = 0; i < count; i++)
            {
                Vector3 rel = sensors.Neighbors[i].RelativePosition;
                rel.y = 0f;
                float along = Vector3.Dot(rel, trackDir);      // + = ahead of us
                float lateral = Vector3.Dot(rel, trackRight);  // + = to our right

                Vector3 nvel = sensors.Neighbors[i].Velocity;
                nvel.y = 0f;

                bool isPlayer = sensors.Neighbors[i].IsPlayer;
                float rearRange = isPlayer ? draftRangePlayer : draftRange;

                if (along > 1f && along < FollowRangeM && Mathf.Abs(lateral) < LaneHalfWidthM && along < aheadDist)
                {
                    aheadDist = along;
                    aheadLateral = lateral;
                    aheadAlongSpeed = Vector3.Dot(nvel, trackDir);
                    hasAhead = true;
                    aheadIsPlayer = isPlayer;
                }
                else if (along < -1f && along > -rearRange && Mathf.Abs(lateral) < LaneHalfWidthM * 1.5f && -along < behindDist)
                {
                    behindDist = -along;
                    behindLateral = lateral;
                    hasBehind = true;
                    behindIsPlayer = isPlayer;
                }
            }

            bool wantToPass = false;
            if (hasAhead)
            {
                // Following model: more clear gap buys more approach speed; back right off inside the buffer
                // so we never nose into a stopped/slow car. A Diver archetype trims the buffer (FollowGapScale
                // < 1) so it tucks in and closes harder; a Cruiser keeps more room (> 1). Neutral == 1, so this
                // is bit-for-bit the base follow gap for a neutral bot, and the scale is bounded so the buffer
                // stays positive (never rear-ends).
                // Memory: back off a player this rival has learned to be wary of (or that it has seen drive
                // erratically) by widening the buffer it keeps. Both terms are 0 for an unknown player, so
                // gapScale is bit-for-bit _personality.FollowGapScale and effGap is unchanged. Re-clamped to
                // the same band FollowGapScale guarantees, so the buffer can never go non-positive and the
                // "never rear-ends" property survives.
                float gapScale = _personality.FollowGapScale;
                if (aheadIsPlayer)
                {
                    gapScale *= 1f + FollowGapCautionGain * _playerMemory.CautionEffective
                                   + FollowGapSpaceGain * _playerMemory.SpaceEffective;
                    gapScale = Mathf.Clamp(gapScale,
                        1f - BotPersonality.MaxFollowGapBias, MaxMemoryFollowGapScale);
                }

                float effGap = aheadDist - FollowBufferM * gapScale;
                float theirSpeed = Mathf.Max(0f, aheadAlongSpeed);
                float cap = theirSpeed + effGap * FollowClosingGainMps;
                speedCap = Mathf.Max(effGap > 0f ? MinFollowSpeed : 0f, cap);

                // Bolder bots need less of a speed advantage before they commit to the move. Memory adds a
                // grudge term: a rival with a score to settle attacks THIS player on a slimmer edge than it
                // would anyone else. 0 for an unknown player.
                float boldnessVsAhead = aheadIsPlayer
                    ? Mathf.Clamp01(boldness + _playerMemory.ContestEffective)
                    : boldness;
                float overtakeMargin = OvertakeSpeedMargin * (1f - OvertakeMarginBoldnessCut * boldnessVsAhead);
                wantToPass = ourAlongSpeed > OvertakeMinSpeed
                    && freeTargetSpeed > theirSpeed + overtakeMargin;
            }

            if (wantToPass)
                desiredTactical = PickOvertakeOffset(sensors, count, trackDir, trackRight, aheadLateral,
                    baseLateral, aheadIsPlayer);
            else if (hasBehind)
            {
                // Light block: lean toward the side the follower is drifting to; a more defensive bot covers
                // a little more of the line. Still small, and corridor-clamped later.
                float defenseVsBehind = behindIsPlayer ? defensivenessVsPlayer : defensiveness;
                float defendMax = DefendMaxOffsetM * (1f + DefendOffsetDefenseGain * defenseVsBehind);

                // Memory, and the most legible behaviour in the whole system: PRE-COVER the side this player
                // habitually attacks, instead of only reacting to where they have already moved. A rival that
                // has watched you go down the inside nine times starts edging there before you commit.
                //
                // The anticipation is added INSIDE the existing clamp, deliberately. It can shift WHERE the
                // cover sits but never widen it beyond what defensiveness already authorises, so no amount of
                // memory can make a bot cover more of the track than a maximally defensive bot does today.
                float anticipate = behindIsPlayer ? SideCoverM * _playerMemory.CoverSideEffective : 0f;
                desiredTactical = Mathf.Clamp(behindLateral + anticipate, -defendMax, defendMax);

                // Safety clamp that should exist regardless of memory: never steer the tactical offset INTO a
                // car that is already alongside. Without this, a well-timed anticipation could close the door
                // on a player whose nose is level — which is a door-slam, and reads as griefing rather than
                // racecraft however defensible the intent.
                desiredTactical = AvoidClosingOnAlongside(sensors, count, trackDir, trackRight, desiredTactical);
            }
        }

        /// <summary>
        /// Picks the pass side: aim to sit PassClearanceM beside the blocker on whichever side keeps us
        /// furthest from a wall and clearest of other traffic. Returns an additive lateral offset
        /// (right-of-travel metres) relative to the base line.
        /// </summary>
        private float PickOvertakeOffset(in BotSensors sensors, int count, Vector3 trackDir,
            Vector3 trackRight, float blockerLateral, float baseLateral, bool blockerIsPlayer = false)
        {
            // Bolder bots leave less room as they slice past — bounded so there's always a positive gap. The
            // Diver/Cruiser archetype bias folds into boldness here too, so a bolder character squeezes closer;
            // the bias is 0 at Neutral, so this is bit-for-bit the pre-archetype clearance for a neutral bot.
            // Memory adds the same grudge term used for the commitment threshold above, so a rival that wants
            // to beat THIS player specifically also slices closer to them. 0 for an unknown player.
            float boldness = Mathf.Clamp01(_skill.OvertakeBoldness + _personality.DiveAggressionClamped
                + (blockerIsPlayer ? _playerMemory.ContestEffective : 0f));
            float clearance = PassClearanceM * (1f - OvertakeClearanceBoldnessCut * boldness);

            // ...but a player this rival is wary of, or has found erratic, gets MORE room, not less. Applied
            // after the boldness cut so caution can win back what a grudge takes away.
            if (blockerIsPlayer)
                clearance *= 1f + ClearanceCautionGain * _playerMemory.CautionEffective
                                + ClearanceSpaceGain * _playerMemory.SpaceEffective;

            // Memory: prefer the lane this player does NOT habitually use. Signed and corner-relative, so
            // +CoverSideBias (a player who lives down the inside) pushes this rival to attack around the
            // outside instead of trading paint for the same piece of road. 0 for an unknown player, in which
            // case both ScoreLane calls receive 0 and score exactly as they do today.
            float side = blockerIsPlayer ? _playerMemory.CoverSideEffective : 0f;
            float leftTarget = blockerLateral - clearance;
            float rightTarget = blockerLateral + clearance;
            float chosen = ScoreLane(sensors, count, trackDir, trackRight, leftTarget, +side)
                         >= ScoreLane(sensors, count, trackDir, trackRight, rightTarget, -side)
                ? leftTarget : rightTarget;
            chosen = Mathf.Clamp(chosen, -CorridorHalfWidthM, CorridorHalfWidthM);
            return chosen - baseLateral;
        }

        /// <summary>
        /// Higher = better lane to aim for: penalises wall proximity and traffic already using it.
        /// <paramref name="lanePreference"/> is a signed memory bias for THIS lane; 0 (the default, and the
        /// value every non-player blocker gets) reproduces today's scoring exactly.
        /// </summary>
        private float ScoreLane(in BotSensors sensors, int count, Vector3 trackDir, Vector3 trackRight,
            float laneLateral, float lanePreference = 0f)
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

            // Memory is a TIEBREAK, never an override. The gain is sized deliberately small against the two
            // penalties above (30 per occupying car, 100 off-corridor): at 8 it can pick between two clear
            // lanes but can never talk a bot into a lane that already has a car in it, let alone off the
            // corridor into a wall. LanePreference_NeverBeatsTraffic pins that relationship — if these three
            // constants ever drift, that test fails rather than the bot quietly learning to crash.
            return score + lanePreference * LanePreferenceScoreGain;
        }

        /// <summary>
        /// Refuses to move the tactical offset TOWARD a car that is currently alongside. Pure geometry, no
        /// memory involved: whatever produced the desired offset — reactive block, archetype bias, or learned
        /// anticipation — closing the door on a car whose nose is already level is a door-slam. Returns the
        /// desired offset unchanged whenever nothing is alongside, which is the overwhelming common case.
        /// </summary>
        private float AvoidClosingOnAlongside(in BotSensors sensors, int count, Vector3 trackDir,
            Vector3 trackRight, float desiredTactical)
        {
            for (int i = 0; i < count; i++)
            {
                Vector3 rel = sensors.Neighbors[i].RelativePosition;
                rel.y = 0f;
                float along = Vector3.Dot(rel, trackDir);
                if (Mathf.Abs(along) > AlongsideHalfLengthM) continue; // not overlapping us longitudinally

                float lateral = Vector3.Dot(rel, trackRight);
                if (Mathf.Abs(lateral) > AlongsideHalfWidthM) continue; // far enough sideways to be no one's problem

                // They're beside us. Don't let the offset travel any further toward their side than we
                // already are; clamping at 0 keeps us straight rather than jerking the other way, which
                // would be its own hazard with a third car about.
                if (lateral > 0f) desiredTactical = Mathf.Min(desiredTactical, 0f);
                else desiredTactical = Mathf.Max(desiredTactical, 0f);
            }
            return desiredTactical;
        }

        /// <summary>
        /// A brief, bounded, deterministic "mistake" signal in [0, <see cref="MistakeMaxLift"/>]: 0 nearly
        /// always (a clean stretch of track), and occasionally a small throttle-ease so a field of bots strings
        /// out instead of driving in lockstep. Seeded purely off the bot's quantised track progress (plus a
        /// per-bot seed) — no Math/Unity Random, no Time — so it reproduces bit-for-bit on a headless server and
        /// repeats lap-to-lap. Frequency scales with <paramref name="mistakeRate"/>; the bobble's size is damped
        /// by <paramref name="consistency"/>. Rate 0 returns 0 (today's flawless behaviour). Pure and static so
        /// the boundedness is unit-testable without a scene.
        /// </summary>
        public static float MistakeFactor(float progress, float mistakeRate, float consistency, int seed = 0)
        {
            mistakeRate = Mathf.Clamp01(mistakeRate);
            if (mistakeRate <= 0f) return 0f;
            consistency = Mathf.Clamp01(consistency);

            // One stable pseudo-random draw per short progress bin decides whether this stretch bobbles.
            long bin = (long)Mathf.Floor(progress / MistakeBinLengthM);
            unchecked
            {
                if (Hash01(bin * 0x2545F4914F6CDD1DL + seed) >= mistakeRate * MistakeMaxChance)
                    return 0f;

                // A second independent draw sets the bobble's size; steady bots barely wobble. Clamped to
                // MistakeMaxLift no matter the inputs, so laps always complete cleanly and stay on the corridor.
                float mag = Hash01(bin * 0x5851F42D4C957F2DL - seed);
                float lift = mag * (1f - MistakeConsistencyDamp * consistency);
                return Mathf.Clamp01(lift) * MistakeMaxLift;
            }
        }

        /// <summary>SplitMix64 bit-avalanche → [0,1). Pure and deterministic; the bot's only source of "noise".</summary>
        private static float Hash01(long x)
        {
            unchecked
            {
                ulong z = (ulong)x + 0x9E3779B97F4A7C15UL;
                z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
                z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
                z ^= z >> 31;
                return (float)((z >> 11) * (1.0 / 9007199254740992.0));
            }
        }

        /// <summary>Stable per-bot seed from its skill knobs so two cars never bobble at the same corner.</summary>
        private static int ComputeSeed(in BotSkill s)
        {
            unchecked
            {
                int h = 17;
                h = h * 31 + Mathf.RoundToInt(s.LateralOffsetM * 8f);
                h = h * 31 + Mathf.RoundToInt(s.Aggression * 128f);
                h = h * 31 + Mathf.RoundToInt(s.CornerSpeedMult * 128f);
                h = h * 31 + Mathf.RoundToInt(s.LookaheadM * 4f);
                return h;
            }
        }
    }
}
