using UnityEngine;

namespace Shitboxer.Race
{
    /// <summary>
    /// Named on-track archetypes a host can hand a bot to give the field distinct CHARACTER — how a car
    /// races the others — as opposed to <see cref="BotDifficulty"/>, which sets how GOOD it is. Maps to a
    /// bounded <see cref="BotPersonality"/> bias set via <see cref="BotPersonality.FromKind"/>.
    /// <see cref="Neutral"/> (value 0, the serialized default) reproduces today's behaviour exactly.
    /// </summary>
    public enum BotPersonalityKind
    {
        /// <summary>Today's reference bot: no tactical bias.</summary>
        Neutral = 0,
        /// <summary>Defends the racing line and is reluctant to yield: covers drafters from further back, rarely dives.</summary>
        Blocker,
        /// <summary>Attempts riskier, earlier passes: commits on a slimmer speed edge, slices closer, tucks in behind a car.</summary>
        Diver,
        /// <summary>Clean and passive: cedes the line, doesn't go hunting for passes, leaves extra room.</summary>
        Cruiser,
    }

    /// <summary>
    /// Plain-C# personality/archetype bias for one bot: a small set of bounded scalars that nudge the
    /// EXISTING tactical knobs in <see cref="BotBrain"/> (how hard it covers the line for a drafter, how
    /// boldly it commits to a pass, how tight a gap it keeps behind a car) so a field of otherwise
    /// equally-skilled bots races with visibly different character. Orthogonal to <see cref="BotDifficulty"/>:
    /// personality = how it races others, difficulty = how good it is; the two layer without either
    /// overriding the other's bounds.
    ///
    /// Every knob is a signed BIAS whose neutral value is 0, so <c>default(BotPersonality)</c> — and
    /// <see cref="Neutral"/> — leaves every tactical expression bit-for-bit at its pre-personality value.
    /// Each bias is hard-clamped to a subtle band (the load-bearing guarantee below), so no archetype — nor
    /// any hand-built extreme struct — can make a bot cheat, cut the corridor, or drive into a rival.
    ///
    /// Engine-loop-independent by construction: only <see cref="Mathf"/> math, no Time / Input / scene access,
    /// so a headless server evaluates it identically.
    /// </summary>
    [System.Serializable]
    public struct BotPersonality
    {
        // --- Tunables (host-set). All three default to 0 (neutral), so default(BotPersonality) is identity. ---

        [Tooltip("Signed bias to how hard the bot covers the racing line for a drafting rival. 0 = neutral, + = defends harder (Blocker), - = cedes the line (Cruiser). Folded onto BotSkill.Defensiveness, then clamped.")]
        public float BlockBias;

        [Tooltip("Signed bias to overtake commitment. 0 = neutral, + = passes earlier and slices closer (Diver), - = hangs back (Cruiser). Folded onto BotSkill.OvertakeBoldness, then clamped.")]
        public float DiveAggression;

        [Tooltip("Signed bias to the gap kept behind a slower car. 0 = neutral, - = tucks in and closes harder (Diver), + = leaves more room (Cruiser). Applied as the FollowGapScale multiplier on the follow buffer.")]
        public float FollowGapBias;

        // --- Hard clamp band — the load-bearing guarantee that no bias, however extreme, escapes a subtle range. ---
        public const float MaxBlockBias = 0.5f;        // at most half the 0..1 defensiveness range
        public const float MaxDiveAggression = 0.5f;   // at most half the 0..1 boldness range
        public const float MaxFollowGapBias = 0.4f;    // follow buffer stays within [0.6x, 1.4x]

        /// <summary>Clamped defensiveness bias added onto <c>BotSkill.Defensiveness</c>. 0 at Neutral.</summary>
        public float BlockBiasClamped => Mathf.Clamp(BlockBias, -MaxBlockBias, MaxBlockBias);

        /// <summary>Clamped boldness bias added onto <c>BotSkill.OvertakeBoldness</c>. 0 at Neutral.</summary>
        public float DiveAggressionClamped => Mathf.Clamp(DiveAggression, -MaxDiveAggression, MaxDiveAggression);

        /// <summary>
        /// Multiplier on the follow buffer: &lt;1 tucks in (Diver), &gt;1 gives room (Cruiser). Bounded to
        /// [1 - <see cref="MaxFollowGapBias"/>, 1 + <see cref="MaxFollowGapBias"/>] and exactly 1 at Neutral,
        /// so the buffer stays positive (never rear-ends) and a neutral bot's gap is unchanged bit-for-bit.
        /// </summary>
        public float FollowGapScale => 1f + Mathf.Clamp(FollowGapBias, -MaxFollowGapBias, MaxFollowGapBias);

        /// <summary>
        /// The neutral baseline: every bias 0. Identical to <c>default(BotPersonality)</c>; the tactical sites
        /// in <see cref="BotBrain"/> collapse to their pre-personality expressions, so a bot configured with
        /// this drives exactly as it does today. Provided for readability.
        /// </summary>
        public static BotPersonality Neutral => default;

        /// <summary>Defends the line, reluctant to yield: covers drafters harder and from further back, rarely dives.</summary>
        public static BotPersonality Blocker => new BotPersonality
        {
            BlockBias = 0.45f,
            DiveAggression = -0.20f,
            FollowGapBias = 0.10f,
        };

        /// <summary>Riskier, earlier passes: commits on a slimmer speed edge, slices closer, tucks in behind a car.</summary>
        public static BotPersonality Diver => new BotPersonality
        {
            BlockBias = -0.15f,
            DiveAggression = 0.45f,
            FollowGapBias = -0.30f,
        };

        /// <summary>Clean and passive: cedes the line, doesn't hunt passes, leaves extra room.</summary>
        public static BotPersonality Cruiser => new BotPersonality
        {
            BlockBias = -0.30f,
            DiveAggression = -0.30f,
            FollowGapBias = 0.25f,
        };

        /// <summary>Maps a serialized <see cref="BotPersonalityKind"/> to its bias set. Unknown -> Neutral.</summary>
        public static BotPersonality FromKind(BotPersonalityKind kind)
        {
            switch (kind)
            {
                case BotPersonalityKind.Blocker: return Blocker;
                case BotPersonalityKind.Diver: return Diver;
                case BotPersonalityKind.Cruiser: return Cruiser;
                default: return Neutral;
            }
        }
    }
}
