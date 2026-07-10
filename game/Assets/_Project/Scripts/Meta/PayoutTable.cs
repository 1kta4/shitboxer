using System;

namespace Shitboxer.Meta
{
    /// <summary>
    /// The inverted payout economy (doc 03): finish position → cash, and the back of the pack
    /// still pays MORE than the front — the catch-up rubber band that starves winners and funds
    /// strugglers. But the curve now has DIMINISHING RETURNS: the base cash climbs as you drop
    /// places, then PLATEAUS past mid-pack, so there is no marginal money for cruising dead-last
    /// (the NBA "flatten the bottom" anti-tanking shape). A small podium bonus is layered on top
    /// so a win is never a strictly dominated line — it merely trades the backmarker's hazard pay
    /// for guaranteed cash. Eliminated cars get a flat consolation instead of their position
    /// payout: elimination's real price is the life AND the wallet. Plain C# and [Serializable]
    /// so it can sit as a tunable field on RunDirector (or be stepped by a headless server later).
    /// </summary>
    [Serializable]
    public class PayoutTable
    {
        /// <summary>
        /// Base cash per finish position, index 0 = P1. Inverted but concave: it rises toward the
        /// back then flattens (P6–P8 equal here), so dropping below mid-pack earns nothing extra.
        /// </summary>
        public int[] PayoutByPosition = { 4, 6, 7, 8, 9, 10, 10, 10 };

        /// <summary>
        /// Podium cash added on top of the base for the top finishers, index 0 = P1. Keeps a win
        /// economically worthwhile so winning is never strictly worse than a cruise.
        /// </summary>
        public int[] PodiumBonus = { 3, 2, 1 };

        /// <summary>Flat payout for eliminated cars — small, because they also just lost a life.</summary>
        public int EliminationConsolation = 2;

        /// <summary>
        /// Economy-part income is paid per finishing position but CAPPED at this position, so a
        /// backmarker build can't compound money by finishing ever-lower — positions past mid-pack
        /// (P4 in an 8-car field) all pay the same. This is what leashes the last-place sandbag.
        /// </summary>
        public int EconomyBonusPositionCap = 4;

        /// <summary>
        /// Net payout for a 1-based finish position: base cash plus any podium bonus. Positions
        /// beyond the table clamp to the last (richest) base entry so oversized fields still
        /// resolve; the podium bonus only applies to the actual podium positions.
        /// </summary>
        public int PayoutFor(int position, bool eliminated)
        {
            if (eliminated) return EliminationConsolation;
            return BasePayoutFor(position) + PodiumBonusFor(position);
        }

        /// <summary>
        /// Sponsor money an economy part pays a finisher: <paramref name="perPositionRate"/> per
        /// finishing position, clamped to <see cref="EconomyBonusPositionCap"/>. Holding a place
        /// worse than the cap never pays more, which removes the last-place compounding exploit.
        /// </summary>
        public int EconomyBonusFor(int perPositionRate, int position)
        {
            if (perPositionRate <= 0) return 0;

            int effective = position;
            if (effective < 1) effective = 1;
            if (effective > EconomyBonusPositionCap) effective = EconomyBonusPositionCap;
            return perPositionRate * effective;
        }

        /// <summary>Base (inverted, diminishing-returns) cash for a clamped 1-based position.</summary>
        private int BasePayoutFor(int position)
        {
            if (PayoutByPosition == null || PayoutByPosition.Length == 0) return 0;

            int index = position - 1;
            if (index < 0) index = 0;
            if (index >= PayoutByPosition.Length) index = PayoutByPosition.Length - 1;
            return PayoutByPosition[index];
        }

        /// <summary>Podium bonus for a 1-based position; zero once off the podium.</summary>
        private int PodiumBonusFor(int position)
        {
            if (PodiumBonus == null || PodiumBonus.Length == 0) return 0;

            int index = position - 1;
            if (index < 0) index = 0;                     // below P1 clamps to the podium winner
            if (index >= PodiumBonus.Length) return 0;    // off the podium — no bonus
            return PodiumBonus[index];
        }
    }
}
