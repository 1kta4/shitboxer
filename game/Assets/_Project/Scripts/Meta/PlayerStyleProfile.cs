using UnityEngine;

namespace Shitboxer.Meta
{
    /// <summary>
    /// TIER 1 of the memory model: what the paddock as a whole knows about how this player races. One
    /// shared profile that every rival reads.
    ///
    /// WHY SHARED AT ALL, given the AI is supposed to learn only from what it observes. Taken strictly,
    /// a rival 300 m back cannot witness a divebomb at the front — and with seven rivals and a realistic
    /// witness radius each one personally sees maybe a quarter of what happens. For a rare event like a
    /// dive that is one to three samples across an entire season, far below any threshold at which an
    /// estimator says anything. A strictly-witnessed-only model would be built, shipped, and never once
    /// activate. So aggregate STYLE is shared and justified diegetically as reputation: timing screens,
    /// paddock talk, and having watched the same driver all season.
    ///
    /// What is NOT shared is the personal half — respect, fear, rivalry — which lives in
    /// <see cref="RivalMemory"/> and only moves from contests a rival was actually part of. That split is
    /// what keeps the system statistically viable AND fair-feeling: everyone knows your reputation, but
    /// only the driver you actually wheel-banged holds a grudge about it.
    ///
    /// Flat and [Serializable] so <c>JsonUtility</c> round-trips it inline with the rest of the profile.
    /// </summary>
    [System.Serializable]
    public class PlayerStyleProfile
    {
        // --- Racecraft preferences ---
        /// <summary>Inside vs outside on passes. Straight-line passes are excluded entirely — see below.</summary>
        public BetaStat insidePreference;
        /// <summary>Covers the inside when defending, vs cedes it.</summary>
        public BetaStat defendsInside;
        /// <summary>How often an attack is a lunge rather than a clean pass.</summary>
        public BetaStat divePropensity;
        /// <summary>Passes that stick, cleanly.</summary>
        public BetaStat passSuccess;
        /// <summary>Concedes when defended, vs holds the line.</summary>
        public BetaStat yieldiness;
        /// <summary>Feints that never became attempts. Expect this to stay near-empty; see RaceObserver.</summary>
        public BetaStat bluffPropensity;

        // --- Continuous ---
        /// <summary>Mean 0..1 divebomb score; the variance is the unpredictability signal.</summary>
        public MeanStat diveSeverity;
        /// <summary>Signed corner-relative defensive shift (m); + = covers the inside.</summary>
        public MeanStat defendShiftM;
        /// <summary>Finish position normalised 0..1, 1 = won. The pace signal behind respect.</summary>
        public MeanStat paceScore;

        // --- Rates over exposure ---
        /// <summary>Player-fault contacts per second of proximity — frequency, not blame.</summary>
        public RateStat collisionRate;
        /// <summary>Share of contact severity that was the player's doing — blame, not frequency.</summary>
        public BetaStat faultShare;

        /// <summary>Races folded into this profile. Drives the shared-evidence side of confidence.</summary>
        public int racesObserved;

        /// <summary>
        /// How predictable this player is, 0..1. Combines one-sidedness of their pass preference, the
        /// steadiness of their dive severity, and how much they feint. Consumed as (1 - this) to decide how
        /// much room a rival leaves — you give an erratic driver space.
        /// </summary>
        public float Predictability
        {
            get
            {
                float sided = Mathf.Abs(insidePreference.Signed);                   // strongly one-sided = readable
                float steady = 1f - Mathf.Clamp01(diveSeverity.Variance * 4f);       // consistent commitment
                float straight = 1f - Mathf.Clamp01(bluffPropensity.P * 2f);          // few games
                return Mathf.Clamp01(0.40f * sided + 0.35f * steady + 0.25f * straight);
            }
        }

        /// <summary>
        /// 0..1 "how clean". The first term is a PRODUCT of frequency and blame, deliberately: a driver
        /// with one crash in forty races that happened to be entirely their fault is not a dirty driver,
        /// and a fault-share-only score would call them one.
        /// </summary>
        public float CleanRacing
        {
            get
            {
                float freq = Mathf.Clamp01(collisionRate.Rate / CollisionRateFullScale);
                return Mathf.Clamp01(1f - (0.70f * freq * faultShare.P + 0.30f * freq));
            }
        }

        /// <summary>0..1 aggression, blending contact, blame, and how much they attack down the inside.</summary>
        public float Aggression
        {
            get
            {
                float freq = Mathf.Clamp01(collisionRate.Rate / CollisionRateFullScale);
                return Mathf.Clamp01(0.35f * freq + 0.25f * faultShare.P
                    + 0.20f * Mathf.Clamp01(insidePreference.P) + 0.20f * divePropensity.P);
            }
        }

        /// <summary>Player-fault contacts per second of proximity at which the frequency term saturates.</summary>
        public const float CollisionRateFullScale = 1f / 60f; // one per minute of close racing = flat out
    }
}
