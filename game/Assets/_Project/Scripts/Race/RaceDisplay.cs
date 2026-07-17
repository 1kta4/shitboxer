namespace Shitboxer.Race
{
    /// <summary>
    /// Pure display formatting for the live race HUD — the pace delta, the projected survival-cutoff verdict,
    /// the inverted-payout preview, and the race clock. Lifted verbatim out of the throwaway IMGUI
    /// <c>RaceHud</c> so the formatting AND its tests survive the UI Toolkit rewrite: RaceHud is deleted in
    /// Phase 4 and the real HUD reuses these. None of it touches engine, scene or clock state, so it is
    /// unit-testable and a headless readout would format it identically.
    /// </summary>
    public static class RaceDisplay
    {
        /// <summary>
        /// Signed pace-delta text for the current lap: this lap's elapsed time (<paramref name="currentLapS"/>)
        /// minus the player's best lap (<paramref name="bestLapS"/>), e.g. "+1.2" or "-0.8". Empty when there
        /// is no best yet (sentinel &lt; 0) or the current time is invalid, so callers show nothing until a
        /// comparison is meaningful.
        /// </summary>
        public static string FormatPaceDelta(float currentLapS, float bestLapS)
        {
            if (bestLapS < 0f || currentLapS < 0f) return string.Empty;
            float delta = currentLapS - bestLapS;
            return delta.ToString("+0.0;-0.0");
        }

        /// <summary>
        /// How far the player is projected to finish BEHIND the winner, as a fraction of the winner's time
        /// (0.08 = projected to finish 8% slower — inside a 15% cutoff). -1 means "not meaningful yet, omit".
        ///
        /// Needs no clock: project each car's finish by holding its average pace; both projections extrapolate
        /// the SAME elapsed time over the SAME loop length, so the finish-time ratio (T·D/playerDist) /
        /// (T·D/leaderDist) cancels T and D exactly, leaving leaderDist/playerDist. A pure distance ratio IS
        /// the projected time ratio. Gated on <paramref name="minDistanceM"/> because the ~27 m grid spread is
        /// a fixed handicap in TotalDistanceM that swamps genuine pace over the opening metres. Returns 0 when
        /// the player IS the leader (identical distances).
        /// </summary>
        public static float ProjectedPaceExcess01(float leaderDistanceM, float playerDistanceM, float minDistanceM)
        {
            if (minDistanceM < 1f) minDistanceM = 1f;
            if (playerDistanceM < minDistanceM || leaderDistanceM < minDistanceM) return -1f;
            return (leaderDistanceM / playerDistanceM) - 1f;
        }

        /// <summary>
        /// The projected cutoff standing: the player's projected deficit against the gate they must stay
        /// inside, plus a blunt SAFE / AT RISK verdict. Empty string for the -1 "not yet meaningful" sentinel
        /// so the caller draws nothing. The gate is inclusive — sitting exactly on the line still reads SAFE.
        /// </summary>
        public static string FormatCutoffPace(float paceExcess01, float cutoffFraction)
        {
            if (paceExcess01 < 0f) return string.Empty;
            string verdict = paceExcess01 <= cutoffFraction ? "SAFE" : "AT RISK";
            return $"PACE +{paceExcess01 * 100f:0}%  /  CUT +{cutoffFraction * 100f:0}%   {verdict}";
        }

        /// <summary>
        /// Payout preview text: what the current position banks and — the point of the line — what winning
        /// would pay instead, so the inversion is legible at a glance ("BANKING $10 at P6 (WIN PAYS $7)").
        /// Leading, the comparison is redundant, so it collapses to the plain figure. Takes already-resolved
        /// figures rather than computing them (Race cannot reach the payout table).
        /// </summary>
        public static string FormatPayoutPreview(int position, int payoutHere, int payoutIfWon)
        {
            if (position <= 1) return $"BANKING ${payoutHere} — LEADING";
            return $"BANKING ${payoutHere} at P{position}   (WIN PAYS ${payoutIfWon})";
        }

        /// <summary>
        /// The live race clock as "M:SS.s" (one decimal), or "-:--.-" for the -1 "no time yet" sentinel. One
        /// decimal on purpose: this drives a number changing ~60x a second, where a second decimal would just
        /// shimmer. <see cref="Meta.PartDisplay.FormatLapRecord"/> uses two decimals for a static record — the
        /// divergence is deliberate, don't unify them.
        /// </summary>
        public static string FormatRaceClock(float seconds)
        {
            if (seconds < 0f) return "-:--.-";
            int minutes = (int)(seconds / 60f);
            return $"{minutes}:{seconds - minutes * 60f:00.0}";
        }
    }
}
