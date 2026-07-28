namespace Shitboxer.Meta
{
    /// <summary>
    /// Pure display formatting for the meta / garage surface — edition tags, lap-record times, run-history
    /// lines. Lifted verbatim out of the throwaway IMGUI <c>GarageScreen</c> so the formatting AND its tests
    /// survive the UI Toolkit rewrite: GarageScreen is deleted in Phase 4, and these strings are exactly what
    /// the real UI (and its ViewModel) reuse. No engine, scene or GUI state, so it is unit-testable and a
    /// headless tool would format it identically.
    /// </summary>
    public static class PartDisplay
    {
        /// <summary>
        /// A compact tag for a non-None edition, e.g. "[FOIL x1.25]"; empty for <see cref="PartEdition.None"/>
        /// so an un-editioned part shows nothing extra and today's look is unchanged. The magnitude is
        /// <see cref="PartEditionInfo.StatMult"/> — the same factor SpecModApplier scales the part's effect
        /// by — so the tag never advertises a different power than the part actually has.
        /// </summary>
        public static string EditionTag(PartEdition edition)
        {
            if (edition == PartEdition.None) return "";
            return $"[{edition.ToString().ToUpperInvariant()} x{PartEditionInfo.StatMult(edition):0.##}]";
        }

        /// <summary>
        /// A lap time in seconds as "M:SS.mm" (two decimals), or "--" for a non-positive / missing time
        /// (<see cref="MetaProgress.NoLapRecord"/> is 0). Two decimals on purpose: a RECORD is a static number
        /// a player compares precisely, so it is deliberately more precise than
        /// <see cref="Race.RaceDisplay.FormatRaceClock"/>, which shows one decimal for a clock changing ~60x a
        /// second. The two divergent formats are intentional — don't unify them.
        /// </summary>
        public static string FormatLapRecord(float seconds)
        {
            if (seconds <= 0f) return "--";
            int minutes = (int)(seconds / 60f);
            return $"{minutes}:{seconds - minutes * 60f:00.00}";
        }

        /// <summary>
        /// One compact line summarising a finished run for the end-screen history list, e.g.
        /// "License 1 - 2 circuits - $37". The stake is shown 1-based as a human "License N". Pure/static.
        /// </summary>
        public static string RunHistoryLine(RunHistoryEntry entry)
        {
            string circuits = entry.circuitsCleared == 1 ? "1 circuit" : $"{entry.circuitsCleared} circuits";
            return $"License {entry.stakeLevel + 1} - {circuits} - ${entry.finalMoney}";
        }
    }
}
