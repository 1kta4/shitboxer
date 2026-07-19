using System.Collections.Generic;
using Shitboxer.Race;
using UnityEngine;

namespace Shitboxer.Meta
{
    /// <summary>
    /// Reads and writes the persistent memory model. Mirrors <c>MetaProgress.RecordBestLap</c>/<c>BestLap</c>:
    /// a linear scan over a <c>List&lt;&gt;</c>, never a Dictionary, because <c>JsonUtility</c> cannot
    /// serialise one and the whole profile has to round-trip through it.
    ///
    /// All statics, all pure over their arguments — no Unity objects, no Time, no DateTime — so the fold
    /// is unit-testable and a headless server evolves the model identically.
    /// </summary>
    public static class RivalMemoryStore
    {
        /// <summary>Bounded like <c>MetaProgress.MaxRunHistory</c>, so the profile can't grow without limit.</summary>
        public const int MaxRivalMemories = 64;

        /// <summary>Proximity seconds at which a race teaches a full-weight lesson about a rival.</summary>
        public const float FullWeightProximityS = 30f;

        /// <summary>
        /// The rival's memory, DECAYED to the present. Decay is applied lazily on read rather than by
        /// sweeping the whole list each race, so a rival you haven't met in twenty races has genuinely
        /// faded without anyone having to walk 64 entries per race to make it so.
        /// An unknown id yields a fresh, neutral, zero-encounter memory.
        /// </summary>
        public static RivalMemory Get(List<RivalMemory> list, string rivalId, int careerRaces)
        {
            if (list == null || string.IsNullOrEmpty(rivalId)) return RivalMemory.Fresh(rivalId);

            for (int i = 0; i < list.Count; i++)
            {
                if (list[i].rivalId != rivalId) continue;

                RivalMemory m = list[i];
                int elapsed = Mathf.Max(0, careerRaces - m.lastSeenRaceOrdinal);
                if (elapsed <= 0) return m;

                // Races the player contested without this rival present decay it a little harder — a
                // 24-rival roster would otherwise have every member holding a maximal grudge forever.
                float absent = Mathf.Pow(RivalMemoryMath.AbsentRivalDecay, elapsed);

                m.paceVsPlayer = RivalMemoryMath.Decay(m.paceVsPlayer,
                    RivalMemoryMath.SuccessHalfLifeRaces, elapsed, RivalMemoryMath.SuccessCap);
                m.personalContactRate = RivalMemoryMath.Decay(m.personalContactRate,
                    RivalMemoryMath.ContactHalfLifeRaces, elapsed, RivalMemoryMath.ContactCap);
                m.personalFaultSeverity *= RivalMemoryMath.DecayFactor(
                    RivalMemoryMath.ContactHalfLifeRaces, elapsed);

                // Rivalry relaxes toward neutral rather than toward zero: forgetting a grudge means
                // indifference, not affection.
                float rivalryDecay = RivalMemoryMath.DecayFactor(RivalMemoryMath.RivalryHalfLifeRaces, elapsed) * absent;
                m.rivalry01 = RivalMemory.NeutralRivalry
                    + (m.rivalry01 - RivalMemory.NeutralRivalry) * rivalryDecay;

                return m;
            }
            return RivalMemory.Fresh(rivalId);
        }

        /// <summary>The shared style profile, decayed to the present. Never null.</summary>
        public static PlayerStyleProfile GetStyle(PlayerStyleProfile style, int careerRaces, int lastFoldedRace)
        {
            if (style == null) return new PlayerStyleProfile();
            int elapsed = Mathf.Max(0, careerRaces - lastFoldedRace);
            if (elapsed <= 0) return style;

            var s = style;
            s.insidePreference = RivalMemoryMath.Decay(s.insidePreference, RivalMemoryMath.StyleHalfLifeRaces, elapsed, RivalMemoryMath.StyleCap);
            s.defendsInside = RivalMemoryMath.Decay(s.defendsInside, RivalMemoryMath.StyleHalfLifeRaces, elapsed, RivalMemoryMath.StyleCap);
            s.divePropensity = RivalMemoryMath.Decay(s.divePropensity, RivalMemoryMath.StyleHalfLifeRaces, elapsed, RivalMemoryMath.StyleCap);
            s.passSuccess = RivalMemoryMath.Decay(s.passSuccess, RivalMemoryMath.SuccessHalfLifeRaces, elapsed, RivalMemoryMath.SuccessCap);
            s.yieldiness = RivalMemoryMath.Decay(s.yieldiness, RivalMemoryMath.StyleHalfLifeRaces, elapsed, RivalMemoryMath.StyleCap);
            s.bluffPropensity = RivalMemoryMath.Decay(s.bluffPropensity, RivalMemoryMath.BluffHalfLifeRaces, elapsed, RivalMemoryMath.BluffCap);
            s.diveSeverity = RivalMemoryMath.Decay(s.diveSeverity, RivalMemoryMath.StyleHalfLifeRaces, elapsed, RivalMemoryMath.StyleCap);
            s.defendShiftM = RivalMemoryMath.Decay(s.defendShiftM, RivalMemoryMath.StyleHalfLifeRaces, elapsed, RivalMemoryMath.StyleCap);
            s.paceScore = RivalMemoryMath.Decay(s.paceScore, RivalMemoryMath.SuccessHalfLifeRaces, elapsed, RivalMemoryMath.SuccessCap);
            s.collisionRate = RivalMemoryMath.Decay(s.collisionRate, RivalMemoryMath.ContactHalfLifeRaces, elapsed, RivalMemoryMath.ContactCap);
            s.faultShare = RivalMemoryMath.Decay(s.faultShare, RivalMemoryMath.ContactHalfLifeRaces, elapsed, RivalMemoryMath.ContactCap);
            return s;
        }

        /// <summary>
        /// Folds one race's observations into the SHARED style profile (tier 1). Every contribution is
        /// weighted by how much of the race the two actually spent near each other, so a rival the player
        /// barely saw teaches almost nothing.
        /// </summary>
        public static void FoldStyle(PlayerStyleProfile style, in RaceObservationSummary race)
        {
            if (style == null || race.Rivals == null) return;

            foreach (RivalEncounterSummary e in race.Rivals)
            {
                float w = Mathf.Clamp01(e.ProximitySeconds / FullWeightProximityS);
                if (w <= 0f) continue;

                // Inside vs outside. STRAIGHT-LINE passes are excluded outright: on three tracks with long
                // straights the slipstream pass happens on whichever side the layout dictates, so folding
                // those in would teach rivals the shape of the circuit rather than the habits of the driver.
                style.insidePreference.Add(e.PlayerPassesInside * w, e.PlayerPassesOutside * w);

                int attempts = e.PlayerPassesOnRival + e.PlayerAttemptsAborted + e.PlayerAttemptsRanWide;
                style.passSuccess.Add(e.PlayerPassesCompletedClean * w,
                    Mathf.Max(0, attempts - e.PlayerPassesCompletedClean) * w);

                style.divePropensity.Add(e.PlayerDiveAttempts * w,
                    Mathf.Max(0, e.PlayerPassesOnRival - e.PlayerDiveAttempts) * w);
                if (e.PlayerDiveAttempts > 0)
                    style.diveSeverity.Add(e.PlayerDiveScoreTotal / e.PlayerDiveAttempts, w * e.PlayerDiveAttempts);

                if (e.PlayerDefensiveMoves > 0)
                {
                    float meanShift = e.PlayerDefendShiftTotal / e.PlayerDefensiveMoves;
                    style.defendShiftM.Add(meanShift, w * e.PlayerDefensiveMoves);
                    // + = covered the inside (already corner-relative from the observer).
                    if (meanShift > 0f) style.defendsInside.Add(e.PlayerDefensiveMoves * w, 0f);
                    else style.defendsInside.Add(0f, e.PlayerDefensiveMoves * w);
                }

                style.yieldiness.Add(e.PlayerYields * w, e.RivalYields * w);
                style.bluffPropensity.Add(e.PlayerBluffs * w, Mathf.Max(0, e.Engagements - e.PlayerBluffs) * w);

                // Contact as a RATE over proximity, plus a separate blame share. Keeping frequency and
                // fault apart is what lets CleanRacing multiply them rather than confuse them.
                style.collisionRate.Add(e.ContactsPlayerFault * w, e.ProximitySeconds * w);
                float blamed = e.PlayerFaultSeverityTotal;
                float unblamed = Mathf.Max(0f, e.ContactSeverityTotal - blamed);
                style.faultShare.Add(blamed * w, unblamed * w);
            }

            if (race.FieldSize > 1)
            {
                float pace = Mathf.Clamp01((race.FieldSize - race.PlayerFinishPosition) / (float)(race.FieldSize - 1));
                style.paceScore.Add(pace);
            }
            style.racesObserved++;
        }

        /// <summary>
        /// Folds one rival's encounter into their PERSONAL memory (tier 2), then stamps it. Decays first,
        /// so folding is always onto an up-to-date model.
        /// </summary>
        public static void Fold(List<RivalMemory> list, string rivalId, in RivalEncounterSummary e,
            int careerRaces, long timestamp)
        {
            if (list == null || string.IsNullOrEmpty(rivalId)) return;

            RivalMemory m = Get(list, rivalId, careerRaces);
            m.rivalId = rivalId;

            if (e.ProximitySeconds > 0f)
            {
                m.encounters++;
                m.proximitySeconds += e.ProximitySeconds;
                m.paceVsPlayer.Add(Mathf.Clamp(e.MeanSignedGapM / 50f, -1f, 1f));
                m.personalContactRate.Add(e.ContactsPlayerFault, e.ProximitySeconds);
                m.personalFaultSeverity += e.PlayerFaultSeverityTotal;
                m.rivalry01 = Mathf.Clamp01(m.rivalry01 + RivalryDelta(e));
            }

            m.lastSeenRaceOrdinal = careerRaces;
            m.lastSeenTimestamp = timestamp;

            m.rivalry01 = RivalMemoryMath.Quantize(m.rivalry01);
            m.personalFaultSeverity = RivalMemoryMath.Quantize(m.personalFaultSeverity);

            Store(list, m, careerRaces);
        }

        /// <summary>
        /// How much this race moved the grudge.
        ///
        /// The NEGATIVE term matters as much as the positive ones: a player who races a rival hard but
        /// cleanly, and gives room when beaten, can actually cool them down. Without it every rival on a
        /// 24-driver roster saturates at maximum hostility over a long career and the whole scalar stops
        /// discriminating — and it gives the player a lever they can feel.
        /// </summary>
        public static float RivalryDelta(in RivalEncounterSummary e)
        {
            float delta = 0f;
            delta += 0.12f * e.ContactsPlayerFault * Mathf.Clamp01(e.PlayerFaultSeverityTotal);
            delta += 0.05f * e.PlayerPassesOnRival;      // beating me is a reason to remember you
            delta += 0.05f * e.RivalPassesOnPlayer;      // so is fighting me for it
            delta += 0.03f * Mathf.Clamp01(e.ProximitySeconds / 60f);
            delta -= 0.06f * e.PlayerYields;             // ...and yielding cools it
            delta -= 0.04f * e.ContactsRivalFault;       // my own fault is not your crime
            return Mathf.Clamp(delta, -MaxRivalryStep, MaxRivalryStep);
        }

        /// <summary>Cap on how far one race can move a grudge, so no single incident maxes it out.</summary>
        public const float MaxRivalryStep = 0.25f;

        /// <summary>Inserts or replaces, evicting the least-recently-seen entry when full.</summary>
        private static void Store(List<RivalMemory> list, in RivalMemory m, int careerRaces)
        {
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i].rivalId == m.rivalId) { list[i] = m; return; }
            }

            if (list.Count >= MaxRivalMemories)
            {
                int oldest = 0;
                for (int i = 1; i < list.Count; i++)
                    if (list[i].lastSeenRaceOrdinal < list[oldest].lastSeenRaceOrdinal) oldest = i;
                list[oldest] = m;
                return;
            }
            list.Add(m);
        }
    }
}
