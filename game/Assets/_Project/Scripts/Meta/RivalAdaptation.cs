using System.Collections.Generic;
using Shitboxer.Race;
using UnityEngine;

namespace Shitboxer.Meta
{
    /// <summary>
    /// Turns persistent memory into racecraft: the ONE place a Meta type becomes a Race type.
    ///
    /// Everything crossing the boundary leaves as a bounded, confidence-gated
    /// <see cref="RivalMemoryProfile"/>. Nothing here can reach <c>BotDifficulty</c>, <c>BotModifiers</c>
    /// or the rubber-band — memory changes how a rival races you, never how fast it is. That rule is the
    /// difference between "this driver has your number" and transparent rubber-banding.
    ///
    /// Pure statics over their arguments, so the whole mapping is unit-testable with no scene.
    /// </summary>
    public static class RivalAdaptation
    {
        /// <summary>
        /// Personal encounters below which a rival has NO opinion at all, whatever the shared reputation
        /// says. A hard floor on top of the smooth confidence ramp, so a single fluke race can never change
        /// how the field races you.
        /// </summary>
        public const int MinEncountersToAdapt = 2;

        /// <summary>Signed preference inside this band reads as no preference. Kills noise-chasing.</summary>
        public const float SideDeadband = 0.25f;

        /// <summary>Most a bias may move in one race. The primary anti-oscillation defence.</summary>
        public const float MaxBiasSlewPerRace = 0.12f;

        /// <summary>
        /// Seconds of close racing that count as one observation of contact behaviour. Matches
        /// <see cref="RivalMemoryStore.FullWeightProximityS"/>, so "a race's worth of proximity" is one
        /// sample on this axis just as it is on the others.
        /// </summary>
        public const float ContactExposurePerSampleS = RivalMemoryStore.FullWeightProximityS;

        /// <summary>
        /// Maps a rival's memory to the bounded racecraft profile the brain consumes. Returns
        /// <c>default</c> — pure identity — for a rival that has not met the player enough times.
        /// </summary>
        public static RivalMemoryProfile ToProfile(PlayerStyleProfile style, in RivalMemory rival,
            in RivalLearningProfile learning)
        {
            if (style == null || rival.encounters < MinEncountersToAdapt) return RivalMemoryProfile.Unknown;

            // Confidence blends SHARED evidence with PERSONAL exposure. The personal gate is what stops the
            // whole field adapting in lockstep the moment the shared profile matures — a rival who knows
            // your reputation but has never diced with you does not act on it yet.
            float personal = rival.PersonalGate;
            float sideN = style.insidePreference.N * learning.LearnRate * personal;
            float diveN = style.divePropensity.N * learning.LearnRate * personal;
            // Contact exposure is measured in SECONDS, so it has to be converted to an equivalent sample
            // count before it can share the confidence model with the tallies above. Feeding raw seconds in
            // makes two minutes of close racing read as 120 observations, which saturates the gate on the
            // first race and drowns out every other signal — including the personality differences that are
            // supposed to make rivals feel distinct.
            float contactN = style.collisionRate.SampleEquivalent(ContactExposurePerSampleS)
                * learning.LearnRate * personal * learning.CollisionWeight;

            float sideGate = RivalMemoryMath.Gate(
                RivalMemoryMath.Confidence(sideN), learning.ConfLo, learning.ConfHi);
            float diveGate = RivalMemoryMath.Gate(
                RivalMemoryMath.Confidence(diveN, RivalMemoryMath.RareConfidenceK), learning.ConfLo, learning.ConfHi);
            float contactGate = RivalMemoryMath.Gate(
                RivalMemoryMath.Confidence(contactN), learning.ConfLo, learning.ConfHi);
            float overallGate = Mathf.Max(sideGate, Mathf.Max(diveGate, contactGate));

            float gain = learning.EffectiveGain(overallGate);

            // --- Derived relationship scalars ---
            // Respect, fear and trust are FUNCTIONS of the evidence rather than stored integrators: no path
            // dependence (learning you're fast and clean should earn respect regardless of the order it was
            // learned), one decay policy instead of four, and no way for a scalar to drift out of sync with
            // the metrics underneath it. Only rivalry keeps its own integrator — see RivalMemory.
            float respect = Mathf.Clamp01(
                0.45f * Mathf.Clamp01(style.paceScore.Mean)
                + 0.35f * style.CleanRacing
                + 0.20f * style.passSuccess.P);

            // Fear multiplies FREQUENCY by BLAME. One crash that was entirely your fault across a long
            // career is not a frightening driver, and a blame-only term would say it was.
            float collisionFreq = Mathf.Clamp01(style.collisionRate.Rate / PlayerStyleProfile.CollisionRateFullScale);
            float fear = Mathf.Clamp01(
                0.50f * collisionFreq * style.faultShare.P
                + 0.30f * style.divePropensity.P
                + 0.20f * Mathf.Clamp01(rival.personalFaultSeverity * 0.5f));

            float trust = Mathf.Clamp01(style.Predictability * (1f - 0.5f * style.faultShare.P));

            // --- Bias mapping, all clamped and gated ---
            var profile = new RivalMemoryProfile { Confidence01 = overallGate };

            // Threat: a rival that respects your pace picks you up from further back and defends earlier.
            profile.ThreatBias = RivalMemoryMath.SlewBias(rival.lastThreatBias,
                (respect - 0.5f) * 2f * RivalMemoryProfile.MaxThreatBias * gain, MaxBiasSlewPerRace);

            // Caution: a rival you've hurt gives you room. Scaled by how much this archetype expresses a
            // lesson as space at all — a Cautious driver backs off where an Aggressive one does not.
            profile.CautionBias = RivalMemoryMath.SlewBias(rival.lastCautionBias,
                (fear - 0.5f) * 2f * RivalMemoryProfile.MaxCautionBias * gain
                    * Mathf.Lerp(0.5f, 1.5f, learning.SpaceAffinity), MaxBiasSlewPerRace);

            // Contest: high rivalry AND low trust means it fights you specifically.
            profile.ContestBias = RivalMemoryMath.SlewBias(rival.lastContestBias,
                Mathf.Clamp((rival.rivalry01 - trust) * RivalMemoryProfile.MaxContestBias * gain
                    * learning.RivalryGain, -RivalMemoryProfile.MaxContestBias, RivalMemoryProfile.MaxContestBias),
                MaxBiasSlewPerRace);

            // Cover side: the headline behaviour. Blend of "attacks inside" and "dives when inside",
            // deadbanded so a 55/45 player buys no coverage at all.
            float threat = 0.65f * style.divePropensity.P + 0.35f * style.insidePreference.P;
            float sideSignal = RivalMemoryMath.Deadband(Mathf.Clamp(2f * (threat - 0.5f), -1f, 1f), SideDeadband);
            profile.CoverSideBias = RivalMemoryMath.SlewBias(rival.lastCoverSideBias,
                sideSignal * RivalMemoryProfile.MaxCoverSideBias * gain, MaxBiasSlewPerRace);

            // Bait: open the door on the side they like, then close it. Only for archetypes with the
            // patience for it, and only when their outside attempts actually work.
            float lure = Mathf.Clamp01(style.insidePreference.P * style.passSuccess.P);
            profile.BaitBias = Mathf.Clamp(lure * RivalMemoryProfile.MaxBaitBias * gain * learning.BaitAffinity,
                0f, RivalMemoryProfile.MaxBaitBias);

            // Space: an unpredictable player gets more room.
            profile.SpaceBias = Mathf.Clamp01((1f - style.Predictability) * gain * learning.SpaceAffinity);

            return profile;
        }

        /// <summary>
        /// Applies the NEMESIS BUDGET across a field: at most a third of the grid may run a strong cover
        /// bias, ranked by how much history they actually have with the player.
        ///
        /// This is a feature, not merely a safeguard. Without it, a shared reputation eventually opens
        /// every rival's gate at once and the whole field becomes one cautious defensive blob — which makes
        /// the game EASIER, the exact opposite of the intent, and reads as the AI going strange rather than
        /// personal. One or two cars having your number while the rest are traffic is legible; seven is mush.
        ///
        /// Deterministic: ranks by rivalry-weighted personal gate, breaking ties on roster id, so it never
        /// needs a random draw and reproduces on a headless server.
        /// </summary>
        public static void ApplyNemesisBudget(IList<string> rivalIds, IList<RivalMemory> memories,
            IList<RivalMemoryProfile> profiles)
        {
            if (profiles == null || memories == null || rivalIds == null) return;
            int n = Mathf.Min(profiles.Count, Mathf.Min(memories.Count, rivalIds.Count));
            if (n == 0) return;

            int allowed = Mathf.Max(1, Mathf.CeilToInt(n / 3f));

            var order = new List<int>(n);
            for (int i = 0; i < n; i++) order.Add(i);
            order.Sort((x, y) =>
            {
                float sx = memories[x].rivalry01 * memories[x].PersonalGate;
                float sy = memories[y].rivalry01 * memories[y].PersonalGate;
                int cmp = sy.CompareTo(sx); // descending
                return cmp != 0 ? cmp : string.CompareOrdinal(rivalIds[x], rivalIds[y]);
            });

            for (int rank = allowed; rank < order.Count; rank++)
            {
                int i = order[rank];
                RivalMemoryProfile p = profiles[i];
                p.CoverSideBias = Mathf.Clamp(p.CoverSideBias, -NonNemesisCoverCap, NonNemesisCoverCap);
                p.BaitBias = 0f;
                profiles[i] = p;
            }
        }

        /// <summary>Cover bias a rival outside the nemesis budget may still carry.</summary>
        public const float NonNemesisCoverCap = 0.30f;

        /// <summary>
        /// Writes this race's emitted biases back onto the memory so the next race slews from them rather
        /// than recomputing from scratch. Without this the slew limit would only smooth WITHIN a race and a
        /// bias could still flip sign between races — which is exactly the oscillation it exists to prevent.
        /// </summary>
        public static RivalMemory RememberBiases(RivalMemory m, in RivalMemoryProfile p)
        {
            m.lastCoverSideBias = RivalMemoryMath.Quantize(p.CoverSideBias);
            m.lastThreatBias = RivalMemoryMath.Quantize(p.ThreatBias);
            m.lastCautionBias = RivalMemoryMath.Quantize(p.CautionBias);
            m.lastContestBias = RivalMemoryMath.Quantize(p.ContestBias);
            return m;
        }
    }
}
