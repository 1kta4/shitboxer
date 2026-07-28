using UnityEngine;

namespace Shitboxer.Race
{
    /// <summary>
    /// F1 timing-screen colour for one completed sector. <see cref="None"/> is the un-set default so a
    /// car that has not yet completed a sector reads as "no colour" rather than accidentally reading as
    /// the slowest tier.
    /// </summary>
    public enum SectorColour : byte
    {
        /// <summary>No time recorded yet.</summary>
        None = 0,
        /// <summary>Slower than your own best for this sector.</summary>
        Yellow,
        /// <summary>Your own personal best for this sector, but not the session's fastest.</summary>
        Green,
        /// <summary>Fastest anyone has run this sector all session.</summary>
        Purple,
    }

    /// <summary>
    /// Pure sector-timing math shared by the referee and its tests — no engine, scene or clock state, so
    /// a headless server derives identical colours. Purely a readout concern: nothing here affects
    /// driving, lap validation, or the economy.
    ///
    /// Times are race-clock seconds and a NEGATIVE best means "none recorded yet", matching the
    /// convention <see cref="RaceCarStatus.BestLapTimeS"/> and <see cref="LapTiming.Fold"/> already use.
    /// </summary>
    public static class SectorTiming
    {
        /// <summary>
        /// Colour for a just-completed sector, compared against the bests as they stood BEFORE this time
        /// was folded in. Order matters: purple is tested first because beating the session also beats
        /// your own, and a car that took purple must not be reported as merely green.
        ///
        /// The first time anyone sets for a sector is purple by construction (session best is unset), which
        /// is correct — the first car through a sector genuinely holds the fastest time in it.
        /// A non-positive or non-finite time yields <see cref="SectorColour.None"/>.
        /// </summary>
        public static SectorColour Classify(float sectorTimeS, float personalBestS, float sessionBestS)
        {
            if (!(sectorTimeS > 0f) || float.IsInfinity(sectorTimeS)) return SectorColour.None;
            if (sessionBestS < 0f || sectorTimeS < sessionBestS) return SectorColour.Purple;
            if (personalBestS < 0f || sectorTimeS < personalBestS) return SectorColour.Green;
            return SectorColour.Yellow;
        }

        /// <summary>
        /// The new best given the prior best (negative = none yet) and a just-completed sector: keeps the
        /// minimum, and the first valid time always becomes the best. Delegates the actual comparison to
        /// <see cref="LapTiming.Fold"/> — sector bests and lap bests are the same fold over a different
        /// unit, and duplicating the rule would let the two drift.
        ///
        /// Unlike the lap fold this REJECTS a non-positive time. A zero-length sector is a degenerate
        /// reading — two boundaries credited inside one physics step — not a record, and folding one in
        /// would pin the best at ~0 for the rest of the session and make every genuine sector afterwards
        /// read yellow. Laps can't hit this (a lap is three sectors long), which is why the guard lives
        /// here rather than in the shared helper.
        /// </summary>
        public static float Fold(float bestSoFarS, float sectorTimeS) =>
            sectorTimeS > 0f ? LapTiming.Fold(bestSoFarS, sectorTimeS) : bestSoFarS;

        /// <summary>
        /// Elapsed seconds of a sector: the race clock now minus when this sector's timing began.
        /// Clamped non-negative. Same delegation rationale as <see cref="Fold"/>.
        /// </summary>
        public static float Elapsed(float nowS, float sectorStartS) => LapTiming.Elapsed(nowS, sectorStartS);

        /// <summary>
        /// Display colour for a sector colour, for the HUD. Kept beside the enum so the timing screen and
        /// any future telemetry overlay cannot disagree about what "purple" means.
        /// </summary>
        public static Color ToColor(SectorColour colour)
        {
            switch (colour)
            {
                case SectorColour.Purple: return new Color(0.72f, 0.35f, 0.95f); // timing-screen violet
                case SectorColour.Green: return new Color(0.30f, 0.85f, 0.40f);
                case SectorColour.Yellow: return new Color(0.95f, 0.82f, 0.25f);
                default: return new Color(0.60f, 0.62f, 0.66f);                  // no time yet — muted grey
            }
        }
    }
}
