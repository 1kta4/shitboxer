using UnityEngine;

namespace Shitboxer.Meta
{
    /// <summary>
    /// How one <see cref="RivalPersonality"/> LEARNS, as opposed to how it drives.
    ///
    /// The six career personalities are deliberately orthogonal to the four on-track archetypes
    /// (<c>BotPersonalityKind</c>). The archetype answers "how does this car race?" and is evaluated every
    /// physics step; this answers "how does this driver form and act on an opinion of you?" and is read
    /// once per race. A Cautious driver can be a Blocker — defends hard, but cleanly and with room.
    ///
    /// Two mechanics carry most of the character:
    ///
    /// <see cref="Overreact"/> multiplies the gain while confidence is still LOW, tapering to 1 as the
    /// rival becomes sure. That single lerp is what makes a Rookie visibly jump to conclusions off two
    /// samples while a Veteran waits — and makes everyone converge once the evidence is in.
    ///
    /// <see cref="LearnRate"/> is applied at READ time as a multiplier on the shared evidence count, never
    /// at write time. The shared style profile is written once and cannot carry seven different learning
    /// rates; reading it seven different ways costs nothing and keeps the save single-writer.
    /// </summary>
    public readonly struct RivalLearningProfile
    {
        /// <summary>Scales the shared evidence count at read time. &gt;1 = forms opinions on less.</summary>
        public readonly float LearnRate;
        /// <summary>Confidence at which this rival starts acting at all.</summary>
        public readonly float ConfLo;
        /// <summary>Confidence at which it is acting at full strength.</summary>
        public readonly float ConfHi;
        /// <summary>Overall strength of the biases it emits once confident.</summary>
        public readonly float AdaptGain;
        /// <summary>Weight on contact evidence — a hot-head lets one punt colour everything.</summary>
        public readonly float CollisionWeight;
        /// <summary>How fast a personal grudge builds.</summary>
        public readonly float RivalryGain;
        /// <summary>Gain multiplier while UNSURE, lerping to 1 as the gate opens.</summary>
        public readonly float Overreact;
        /// <summary>Willingness to bait. Baiting is a skill — a Rookie should not be doing it.</summary>
        public readonly float BaitAffinity;
        /// <summary>Willingness to express a lesson as space rather than escalation.</summary>
        public readonly float SpaceAffinity;

        public RivalLearningProfile(float learnRate, float confLo, float confHi, float adaptGain,
            float collisionWeight, float rivalryGain, float overreact, float baitAffinity, float spaceAffinity)
        {
            LearnRate = learnRate;
            ConfLo = confLo;
            ConfHi = confHi;
            AdaptGain = adaptGain;
            CollisionWeight = collisionWeight;
            RivalryGain = rivalryGain;
            Overreact = overreact;
            BaitAffinity = baitAffinity;
            SpaceAffinity = spaceAffinity;
        }

        /// <summary>
        /// The per-personality table. Each row is a character sketch expressed as learning parameters:
        ///
        ///  Rookie      — jumps to conclusions off two samples, ACTS on them, forgets fast. Reads as flustered.
        ///  Aggressive  — learns fast and converts it into attack rather than defence.
        ///  HotHeaded   — one punt colours everything; heavy contact weight, fast-building grudge.
        ///  Calculating — demands evidence, then surgical. The one that baits.
        ///  Veteran     — the highest evidence bar and the sharpest response once met: "he's got your number".
        ///  Cautious    — learns readily but expresses every lesson as space, never escalation.
        /// </summary>
        public static RivalLearningProfile For(RivalPersonality personality)
        {
            switch (personality)
            {
                //                                    learn  lo     hi     gain   coll   rival  over   bait   space
                case RivalPersonality.Rookie:
                    return new RivalLearningProfile(1.60f, 0.10f, 0.40f, 0.75f, 1.0f, 0.9f, 1.80f, 0.15f, 0.50f);
                case RivalPersonality.Aggressive:
                    return new RivalLearningProfile(1.20f, 0.20f, 0.55f, 1.15f, 1.3f, 1.3f, 1.20f, 0.60f, 0.25f);
                case RivalPersonality.HotHeaded:
                    return new RivalLearningProfile(1.00f, 0.25f, 0.60f, 1.10f, 3.0f, 1.8f, 1.40f, 0.30f, 0.20f);
                case RivalPersonality.Calculating:
                    return new RivalLearningProfile(1.00f, 0.40f, 0.85f, 1.25f, 0.8f, 0.7f, 0.90f, 1.00f, 0.60f);
                case RivalPersonality.Veteran:
                    return new RivalLearningProfile(0.80f, 0.45f, 0.90f, 1.30f, 0.9f, 0.8f, 0.85f, 0.90f, 0.70f);
                case RivalPersonality.Cautious:
                    return new RivalLearningProfile(1.10f, 0.25f, 0.65f, 0.85f, 1.5f, 0.6f, 1.10f, 0.00f, 1.00f);
                default:
                    return new RivalLearningProfile(1.00f, 0.30f, 0.75f, 1.00f, 1.0f, 1.0f, 1.00f, 0.50f, 0.50f);
            }
        }

        /// <summary>
        /// Effective gain for a given gate: <see cref="Overreact"/> while unsure, tapering to
        /// <see cref="AdaptGain"/> once confident. "Overreacts early, normalises late" in one lerp.
        /// </summary>
        public float EffectiveGain(float gate) => AdaptGain * Mathf.Lerp(Overreact, 1f, Mathf.Clamp01(gate));
    }
}
