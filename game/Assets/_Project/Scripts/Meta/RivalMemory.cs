using UnityEngine;

namespace Shitboxer.Meta
{
    /// <summary>
    /// TIER 2 of the memory model: what ONE named rival personally holds against this player. Keyed by
    /// the roster's permanent string id — never a slot index, or reshuffling the roster would transplant
    /// one driver's entire history onto another.
    ///
    /// Only <see cref="rivalry01"/> is a stored integrator. Respect, trust and fear are DERIVED from the
    /// shared style profile at read time (see <c>RivalAdaptation</c>) because they are genuinely functions
    /// of what the player does: learning that someone is fast and clean should earn respect regardless of
    /// the order you learned it in, and deriving them means one decay policy instead of four and no way
    /// for a scalar to drift out of sync with the evidence underneath it.
    ///
    /// Rivalry is the exception and earns its integrator. It is episodic and personal — it should spike
    /// from a specific incident (you punted me on the last lap of race three) and fade slowly. No function
    /// of aggregate style can produce "this particular green car has a problem with you", and that is
    /// precisely the part a player can perceive.
    /// </summary>
    [System.Serializable]
    public struct RivalMemory
    {
        /// <summary>Permanent roster id. The primary key for everything below.</summary>
        public string rivalId;

        /// <summary>Races this rival has personally contested against the player.</summary>
        public int encounters;

        /// <summary>Seconds spent racing the player closely. The personal-evidence denominator.</summary>
        public float proximitySeconds;

        /// <summary>0..1 grudge. 0.5 is neutral; rises with incident, falls when raced cleanly.</summary>
        public float rivalry01;

        /// <summary>Mean signed gap to the player across encounters (+ = player ahead). Feeds respect.</summary>
        public MeanStat paceVsPlayer;

        /// <summary>Contacts between these two specifically, and the player's share of the damage.</summary>
        public RateStat personalContactRate;
        public float personalFaultSeverity;

        /// <summary>Career race ordinal when this memory was last folded — the decay clock.</summary>
        public int lastSeenRaceOrdinal;

        /// <summary>Caller-supplied unix seconds. Pure logic never calls DateTime.Now.</summary>
        public long lastSeenTimestamp;

        /// <summary>
        /// Previous frame's emitted biases, so <c>RivalAdaptation</c> can slew rather than jump. Persisting
        /// them is what makes the slew limit meaningful ACROSS races — recomputing from scratch each race
        /// would let a bias flip sign the instant the estimate did, which is the oscillation this design
        /// most needs to avoid.
        /// </summary>
        public float lastCoverSideBias;
        public float lastThreatBias;
        public float lastCautionBias;
        public float lastContestBias;

        public const float NeutralRivalry = 0.5f;

        /// <summary>A rival who has never raced this player.</summary>
        public static RivalMemory Fresh(string id) => new RivalMemory
        {
            rivalId = id,
            rivalry01 = NeutralRivalry,
        };

        /// <summary>
        /// 0..1 personal-evidence gate. THE anti-blob mechanism, and load-bearing.
        ///
        /// Tier 1 is shared, so without this every rival's confidence would cross its threshold on the same
        /// race and the entire field would start adapting in lockstep — seven identical cautious blockers,
        /// which makes the game EASIER and is the exact opposite of the intent. Gating on personal
        /// encounters means a rival who has never diced with you knows your reputation but does not act on
        /// it yet. The field diverges naturally, and it is diegetic besides.
        /// </summary>
        public float PersonalGate => encounters / (encounters + PersonalGateK);
        public const float PersonalGateK = 4f;

        /// <summary>True once this rival has enough personal history to hold any opinion at all.</summary>
        public bool HasMetPlayer => encounters > 0;
    }
}
