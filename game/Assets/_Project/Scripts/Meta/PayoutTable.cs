using System;

namespace Shitboxer.Meta
{
    /// <summary>
    /// The inverted payout economy (doc 03): finish position → cash, and the back of the pack
    /// pays MORE than the front — the catch-up rubber band that starves winners and funds
    /// strugglers. The curve has DIMINISHING RETURNS: the base cash climbs as you drop places,
    /// then PLATEAUS at the very back, so the last slot or two pay no marginal money for tanking
    /// (the NBA "flatten the bottom" anti-tanking shape). A small podium bonus is layered on top
    /// so a win is never a strictly dominated line — it merely trades the backmarker's hazard pay
    /// for guaranteed cash. Eliminated cars get a flat consolation instead of their position
    /// payout: elimination's real price is the life AND the wallet. Plain C# and [Serializable]
    /// so it can sit as a tunable field on RunDirector (or be stepped by a headless server later).
    ///
    /// ⚠ These defaults are ALSO serialized per-scene onto RunDirector.payoutTable (RaceTest,
    /// RaceGauntlet, RaceSpeedway). Editing this file alone changes nothing in a built scene —
    /// the scene's copy wins. Retune both, or the game and the tests disagree.
    /// </summary>
    [Serializable]
    public class PayoutTable
    {
        /// <summary>
        /// Base cash per finish position, index 0 = P1. Inverted but concave: it rises toward the
        /// back then flattens (P7–P8 equal here), so tanking to the very back earns nothing extra.
        ///
        /// These numbers ARE the signature tension, so they have to be big enough to be a decision.
        /// The previous curve ({4,6,7,8,9,10,10,10}) made winning vs. finishing dead last worth $3 —
        /// against a $5 start, a $5 reroll and 47 parts priced $3–18 (median $7). The whole of
        /// "push to win or hang back to farm" was less than one reroll, so it wasn't a choice, and
        /// a first-race player met no inverted economy at all: the gradient only appeared once you
        /// bought an economy part. That is a build exploiting a rule, with the rule missing.
        ///
        /// Now P1=$5 … P8=$13 — a $8 spread, ≈ one median part per race. Economy parts still
        /// amplify it to $15–25 on top (PayoutTable.EconomyBonusFor), which is the intended shape:
        /// a base rule the whole field feels, and a build that leans into it.
        ///
        /// The anti-sandbag leash is deliberately NOT this curve — it's the tight CutoffFraction
        /// plus wave 20's hard bots, because a skill tightrope is a better leash than a flattened
        /// payout (doc 07 risk 4d). Flattening the curve too was a redundant second leash, and it
        /// was the one that cost the signature.
        /// </summary>
        public int[] PayoutByPosition = { 2, 4, 6, 8, 10, 12, 13, 13 };

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
