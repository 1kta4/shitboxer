using System;

namespace Shitboxer.Meta
{
    /// <summary>
    /// Balatro-flavoured economy math for the between-race shop (doc 03): interest on banked
    /// money, reroll-cost escalation, and rarity/family part pricing. Pure, engine-free static
    /// helpers — no scene, Time, Input or RNG dependencies — so RunState/ShopLogic (and a headless
    /// server) can call them and unit tests can pin them exactly.
    ///
    /// Every tunable has a NO-OP default: zero interest rate, zero reroll increment, and unit
    /// (1x) price multipliers all reproduce today's shipped numbers byte-for-byte, so enabling the
    /// depth is opt-in and never drifts balance until a designer dials a knob.
    /// </summary>
    public static class ShopEconomy
    {
        /// <summary>Default block size for interest ($ of banked money that earns one payout block).</summary>
        public const int DefaultInterestBlockSize = 5;

        /// <summary>
        /// Interest paid on banked money at shop time (Balatro-style: a bonus per full $-block held,
        /// capped so hoarding rewards but doesn't run away). Returns the bonus to ADD to money — it
        /// never mutates anything.
        ///
        /// No-op default: <paramref name="perBlock"/> = 0 pays nothing, so an un-tuned run earns no
        /// interest exactly as shipped. Guards non-positive rate/block and non-positive balance.
        /// </summary>
        /// <param name="bankedMoney">Money currently banked (interest is computed on what you hold).</param>
        /// <param name="perBlock">$ paid per whole block of banked money. 0 (default) = no interest.</param>
        /// <param name="blockSize">$ of banked money per payout block (Balatro's "$1 per $5" → 5).</param>
        /// <param name="cap">Maximum interest paid this shop. int.MaxValue (default) = uncapped.</param>
        public static int Interest(int bankedMoney, int perBlock = 0,
                                   int blockSize = DefaultInterestBlockSize, int cap = int.MaxValue)
        {
            if (perBlock <= 0 || blockSize <= 0 || bankedMoney <= 0) return 0;

            long blocks = bankedMoney / blockSize;
            long bonus = (long)perBlock * blocks;
            if (cap < 0) cap = 0;
            if (bonus > cap) bonus = cap;
            if (bonus < 0) bonus = 0;
            return (int)bonus;
        }

        /// <summary>
        /// Cost of the next reroll given how many rerolls have already been bought this visit:
        /// <c>baseCost + increment * rerollsThisVisit</c>, floored at 0.
        ///
        /// The pure helper is flat when <paramref name="increment"/> = 0 (default). Today's live shop
        /// escalates by <see cref="ShopLogic.RerollCostStep"/> per reroll, so it passes that step as
        /// the increment; a NEW extra tunable (ShopLogic/RunState) layers on top of it, and stays 0 =
        /// shipped curve until raised.
        /// </summary>
        public static int RerollCost(int baseCost, int rerollsThisVisit, int increment = 0)
        {
            if (rerollsThisVisit < 0) rerollsThisVisit = 0;

            long cost = (long)baseCost + (long)increment * rerollsThisVisit;
            if (cost < 0) cost = 0;
            return (int)cost;
        }

        /// <summary>
        /// A part's shop price scaled by a single multiplier, rounded to the nearest whole $ and
        /// floored at 0. A multiplier of exactly 1 returns the base price untouched (no rounding
        /// artefacts), so identity pricing is a guaranteed no-op.
        /// </summary>
        public static int PartPrice(int basePrice, float multiplier = 1f)
        {
            if (multiplier == 1f) return basePrice < 0 ? 0 : basePrice;

            double scaled = Math.Round((double)basePrice * multiplier, MidpointRounding.AwayFromZero);
            if (scaled < 0) scaled = 0;
            return (int)scaled;
        }

        /// <summary>
        /// A part's shop price scaled by its rarity- and family-multiplier tables (dynamic pricing
        /// by rarity/family). Each table is indexed by the enum's integer value; a null table — or a
        /// tier/family beyond it — contributes a 1x (unit) multiplier, so passing null for both
        /// tables returns the base price exactly. This is the shipped default.
        /// </summary>
        /// <param name="rarityMults">Per-<see cref="Rarity"/> multipliers, indexed by (int)Rarity. null = all 1x.</param>
        /// <param name="familyMults">Per-<see cref="PartCategory"/> multipliers, indexed by (int)PartCategory. null = all 1x.</param>
        public static int PartPrice(int basePrice, Rarity rarity, PartCategory family,
                                    float[] rarityMults = null, float[] familyMults = null)
        {
            float rarityMult = MultiplierAt(rarityMults, (int)rarity);
            float familyMult = MultiplierAt(familyMults, (int)family);
            return PartPrice(basePrice, rarityMult * familyMult);
        }

        /// <summary>Reads a multiplier table at an index, defaulting to 1x when absent/out of range.</summary>
        private static float MultiplierAt(float[] table, int index)
        {
            if (table == null || index < 0 || index >= table.Length) return 1f;
            return table[index];
        }
    }
}
