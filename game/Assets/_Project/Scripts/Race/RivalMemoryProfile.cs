using UnityEngine;

namespace Shitboxer.Race
{
    /// <summary>
    /// What one rival has LEARNED about the player, reduced to a handful of bounded racecraft biases.
    /// This is the single type the persistent-memory layer pushes across the assembly boundary: Meta owns
    /// the estimators, the decay and the career save; Race receives only this flat, clamped result. Same
    /// one-way push as <c>RaceManager.SetRuleset</c> / <c>SetDifficultyScalar</c>, and the reason
    /// <c>Shitboxer.Race</c> still needs no reference to <c>Shitboxer.Meta</c>.
    ///
    /// THE LOAD-BEARING RULE: memory modulates RACECRAFT ONLY — never pace. Nothing here reaches
    /// <see cref="BotDifficulty"/>, <see cref="BotModifiers"/> or the rubber-band. A rival that remembers you
    /// defends earlier, covers the side you favour and leaves you more room; it never gets faster. The moment
    /// memory touched raw speed the system would read as rubber-banding and the "learns only from observed
    /// behaviour, never cheats" premise would be dead. <c>RivalMemoryTests</c> pins this.
    ///
    /// Mirrors <see cref="BotPersonality"/>'s contract deliberately — signed biases, neutral at 0, hard clamps
    /// as the guarantee — and adds a second gate on top: every bias is scaled by <see cref="Confidence01"/>,
    /// so a rival with no evidence behaves exactly like today's bot. <c>default</c> is therefore a true no-op
    /// at every consumption site, which is what lets the whole adaptation surface ship inert.
    ///
    /// Engine-loop-independent: only <see cref="Mathf"/>, no Time / Input / scene access, so a headless
    /// server evaluates it identically.
    /// </summary>
    [System.Serializable]
    public struct RivalMemoryProfile
    {
        // --- Tunables (host-set). All default to 0, so default(RivalMemoryProfile) is identity. ---

        [Tooltip("0..1 certainty in the learned model. 0 (the default) scales EVERY bias below to exactly zero, so an unknown player is raced exactly as today. Grows with evidence and shrinks as memories decay.")]
        public float Confidence01;

        [Tooltip("Signed. + = this player is a fast threat: pick them up from further back and start defending earlier. Derived from respect.")]
        public float ThreatBias;

        [Tooltip("Signed. + = this player hurts me: leave a wider follow buffer and more room when passing them. Derived from fear.")]
        public float CautionBias;

        [Tooltip("Signed. + = I have a score to settle: commit to passes on this player on a slimmer edge. Derived from rivalry minus trust.")]
        public float ContestBias;

        [Tooltip("Signed, CORNER-RELATIVE: +1 = this player habitually attacks down the inside, -1 = around the outside. Drives pre-covering the side they favour and picking the lane they don't.")]
        public float CoverSideBias;

        [Tooltip("0..1. How much to open the door on the side this player likes before closing it. A skill — only patient archetypes are given any.")]
        public float BaitBias;

        [Tooltip("0..1. How unpredictable this player is. Widens pass clearance and follow gap — you give an erratic driver room.")]
        public float SpaceBias;

        // --- Hard clamp band — the load-bearing guarantee that no bias, however extreme, escapes a
        // subtle range even before the confidence gate is applied. ---

        public const float MaxThreatBias = 0.40f;
        public const float MaxCautionBias = 0.35f;
        public const float MaxContestBias = 0.40f;
        /// <summary>
        /// Deliberately partial: a rival covers at most ~60% of the side you favour. This is a DESIGN bound,
        /// not merely a safety one — a defender that covers perfectly has no stable equilibrium and pushes the
        /// player into pure oscillation, while an imperfect one leaves a real (if narrower) opening and the
        /// duel stays a duel.
        /// </summary>
        public const float MaxCoverSideBias = 0.60f;
        public const float MaxBaitBias = 0.45f;
        public const float MaxSpaceBias = 1.00f;

        /// <summary>The confidence gate every effective bias is scaled by. 0 at default = total no-op.</summary>
        private float Gate => Mathf.Clamp01(Confidence01);

        public float ThreatEffective => Mathf.Clamp(ThreatBias, -MaxThreatBias, MaxThreatBias) * Gate;
        public float CautionEffective => Mathf.Clamp(CautionBias, -MaxCautionBias, MaxCautionBias) * Gate;
        public float ContestEffective => Mathf.Clamp(ContestBias, -MaxContestBias, MaxContestBias) * Gate;
        public float CoverSideEffective => Mathf.Clamp(CoverSideBias, -MaxCoverSideBias, MaxCoverSideBias) * Gate;
        public float BaitEffective => Mathf.Clamp(BaitBias, 0f, MaxBaitBias) * Gate;
        public float SpaceEffective => Mathf.Clamp(SpaceBias, 0f, MaxSpaceBias) * Gate;

        /// <summary>
        /// A rival who has never raced this player. Identical to <c>default(RivalMemoryProfile)</c>; every
        /// effective bias is exactly 0, so the tactical sites in <see cref="BotBrain"/> collapse to their
        /// pre-memory expressions. Provided for readability at call sites.
        /// </summary>
        public static RivalMemoryProfile Unknown => default;
    }
}
