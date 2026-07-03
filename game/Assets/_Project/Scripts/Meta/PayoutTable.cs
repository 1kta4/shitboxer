using System;

namespace Shitboxer.Meta
{
    /// <summary>
    /// The inverted payout economy (doc 03): finish position → cash, and LAST pays the MOST —
    /// the catch-up rubber band that starves winners and funds strugglers. Eliminated cars get
    /// a flat consolation instead of their position payout: elimination's real price is the
    /// life, not the wallet. Plain C# and [Serializable] so it can sit as a tunable field on
    /// RunDirector (or be stepped by a headless server later).
    /// </summary>
    [Serializable]
    public class PayoutTable
    {
        /// <summary>Cash per finish position, index 0 = P1. Ascending: P1 earns least, P8 most.</summary>
        public int[] PayoutByPosition = { 4, 5, 6, 7, 8, 9, 10, 11 };

        /// <summary>Flat payout for eliminated cars — small, because they also just lost a life.</summary>
        public int EliminationConsolation = 2;

        /// <summary>
        /// Payout for a 1-based finish position. Positions beyond the table clamp to the last
        /// (richest) entry so oversized fields still resolve.
        /// </summary>
        public int PayoutFor(int position, bool eliminated)
        {
            if (eliminated) return EliminationConsolation;
            if (PayoutByPosition == null || PayoutByPosition.Length == 0) return 0;

            int index = position - 1;
            if (index < 0) index = 0;
            if (index >= PayoutByPosition.Length) index = PayoutByPosition.Length - 1;
            return PayoutByPosition[index];
        }
    }
}
