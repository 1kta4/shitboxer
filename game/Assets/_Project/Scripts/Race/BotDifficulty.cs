using UnityEngine;

namespace Shitboxer.Race
{
    /// <summary>
    /// Bounded, identity-by-default multipliers a bot applies to its own driving this step. Plain data,
    /// produced by <see cref="BotDifficulty.Evaluate"/>. Every field is a scale where 1 = "drive exactly
    /// as the base plan"; the producer keeps each one inside a subtle clamped band so a difficulty/rubber-
    /// band nudge can never make a bot uncatchable, nor spin it out.
    /// </summary>
    public struct BotModifiers
    {
        /// <summary>Scales the free-flowing target speed the brain plans toward. Clamped ~[0.90, 1.10].</summary>
        public float TargetSpeedScale;
        /// <summary>Scales commanded throttle. Clamped ~[0.85, 1.12].</summary>
        public float ThrottleScale;
        /// <summary>Scales steering response (heading error -> lock). Clamped ~[0.85, 1.20].</summary>
        public float SteerSharpness;

        /// <summary>No-op modifiers: leaves the base plan untouched (bit-for-bit).</summary>
        public static BotModifiers Identity => new BotModifiers
        {
            TargetSpeedScale = 1f,
            ThrottleScale = 1f,
            SteerSharpness = 1f,
        };
    }

    /// <summary>
    /// Plain-C# difficulty model for one bot: a bounded rubber-band (a bot far AHEAD of the field eases
    /// off a touch, one far BEHIND pushes a touch) folded together with a skill tier (a rookie->pro
    /// competence scalar that rises with the run's license stake). It turns a signed gap-to-field into a
    /// small set of clamped <see cref="BotModifiers"/> the brain applies to its throttle/steer/target
    /// speed.
    ///
    /// Engine-loop-independent by construction: no UnityEngine.Time / Input / scene access, only
    /// <see cref="Mathf"/> math, so a headless server can evaluate it identically. The stake is taken as a
    /// plain <see cref="int"/> (mirrors <c>Meta.RunState.StakeLevel</c>) so the Race assembly needs no
    /// reference to Meta.
    ///
    /// Defaults are a true no-op: <see cref="Nominal"/> — and indeed <c>default(BotDifficulty)</c> — leave
    /// every modifier at exactly 1, so a bot with no difficulty configured drives precisely as it does
    /// today. A host enables the feature by handing a non-nominal instance to <c>BotBrain.SetDifficulty</c>.
    /// </summary>
    [System.Serializable]
    public struct BotDifficulty
    {
        // --- Tunables (host-set). All three default to the neutral value, so default(BotDifficulty) is identity. ---

        [Tooltip("Rubber-band authority: max +/- fraction the gap-to-field may nudge speed/throttle before the output clamp. 0 = OFF (today's behaviour). Kept small so the assist never reads as cheating.")]
        public float RubberBandStrength;

        [Tooltip("Signed skill bias from NOMINAL competence: 0 = nominal (identity), + = sharper (pro), - = softer (rookie). Prefer BotDifficulty.FromTier(baseSkill01) to set this from a 0..1 rookie->pro scalar.")]
        public float SkillBias01;

        [Tooltip("Run's license-stake level (plain scalar mirroring RunState.StakeLevel). 0 = shipped balance; higher lifts competence toward pro. Negative is treated as 0.")]
        public int StakeLevel;

        // --- Internal tuning (fixed shared feel; the output clamps below make every result provably bounded). ---
        public const float NominalSkill01 = 0.5f;          // the "nominal" competence that maps to identity
        private const float StakeSkillGain = 0.06f;        // competence gained per stake level (before clamp)
        private const float RubberBandFullGapM = 45f;      // gap (m) at which the rubber-band nudge saturates
        private const float RubberBandMaxStrength = 0.5f;  // sanity cap on host-set strength (output is clamped regardless)

        // Deviation-from-nominal competence -> per-channel modifier delta. A pro carries a little more speed,
        // commits a little more throttle and steers a touch sharper; a rookie does the reverse.
        private const float SkillSpeedSpan = 0.15f;
        private const float SkillThrottleSpan = 0.12f;
        private const float SkillSteerSpan = 0.20f;

        // Hard output clamps — the load-bearing guarantee that no input can push a bot past a subtle band.
        private const float SpeedScaleMin = 0.90f, SpeedScaleMax = 1.10f;
        private const float ThrottleScaleMin = 0.85f, ThrottleScaleMax = 1.12f;
        private const float SteerSharpnessMin = 0.85f, SteerSharpnessMax = 1.20f;

        /// <summary>
        /// The neutral baseline: rubber-band off, nominal skill, stake 0. <see cref="Evaluate"/> returns
        /// <see cref="BotModifiers.Identity"/> for any gap, so a bot configured with this drives exactly as
        /// it does today. Identical to <c>default(BotDifficulty)</c>; provided for readability.
        /// </summary>
        public static BotDifficulty Nominal => new BotDifficulty
        {
            RubberBandStrength = 0f,
            SkillBias01 = 0f,
            StakeLevel = 0,
        };

        /// <summary>
        /// Builds a difficulty from a rookie->pro base skill (0 = rookie, 0.5 = nominal, 1 = pro), the run's
        /// stake level, and an optional rubber-band strength. <c>FromTier(0.5)</c> is exactly
        /// <see cref="Nominal"/> (identity).
        /// </summary>
        public static BotDifficulty FromTier(float baseSkill01, int stakeLevel = 0, float rubberBandStrength = 0f)
        {
            return new BotDifficulty
            {
                // Store as a bias off nominal so the neutral point is 0 -> default(struct) stays identity.
                SkillBias01 = Mathf.Clamp01(baseSkill01) - NominalSkill01,
                StakeLevel = stakeLevel,
                RubberBandStrength = rubberBandStrength,
            };
        }

        /// <summary>
        /// Final competence in [0,1]: nominal base skill, biased per bot and lifted by stake. Higher stake
        /// (never negative) gives higher competence, up to the pro ceiling. Nominal + stake 0 == 0.5.
        /// </summary>
        public float Competence01 =>
            Mathf.Clamp01(NominalSkill01 + SkillBias01 + StakeSkillGain * Mathf.Max(0, StakeLevel));

        /// <summary>
        /// Turns a signed gap-to-field into the step's bounded modifiers.
        /// <paramref name="signedGapM"/> is metres of track distance relative to the field: POSITIVE = this
        /// bot is AHEAD (eases off), NEGATIVE = BEHIND (pushes). Zero gap contributes no rubber-band.
        /// Every returned modifier is hard-clamped, so no gap/strength/skill/stake — however extreme — can
        /// carry a bot outside the subtle band. Pure and deterministic: unit-testable without a scene.
        /// </summary>
        public BotModifiers Evaluate(float signedGapM)
        {
            // Skill: how far this bot's competence sits from nominal drives a small, steady per-channel bias.
            float skillDev = Competence01 - NominalSkill01; // 0 at nominal -> identity

            // Rubber-band: ahead -> negative (ease), behind -> positive (push). Tapered by gap, scaled by
            // strength (which is itself sanity-capped). Strength 0 collapses this term to exactly 0.
            float strength = Mathf.Clamp(RubberBandStrength, 0f, RubberBandMaxStrength);
            float gapT = Mathf.Clamp(signedGapM / RubberBandFullGapM, -1f, 1f);
            float rubber = -gapT * strength;

            return new BotModifiers
            {
                TargetSpeedScale = Mathf.Clamp(1f + skillDev * SkillSpeedSpan + rubber, SpeedScaleMin, SpeedScaleMax),
                ThrottleScale = Mathf.Clamp(1f + skillDev * SkillThrottleSpan + rubber, ThrottleScaleMin, ThrottleScaleMax),
                // Steering sharpness follows skill only — a rubber-band assist must never make a car steer
                // harder (that destabilises), only carry more/less speed.
                SteerSharpness = Mathf.Clamp(1f + skillDev * SkillSteerSpan, SteerSharpnessMin, SteerSharpnessMax),
            };
        }
    }
}
