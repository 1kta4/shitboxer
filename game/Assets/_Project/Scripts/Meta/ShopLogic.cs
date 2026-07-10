using System;
using System.Collections.Generic;

namespace Shitboxer.Meta
{
    /// <summary>
    /// Between-race shop rules, engine-free so it unit-tests without a scene. One instance
    /// lives for the whole run; BeginVisit when the garage opens resets the reroll cost to
    /// Balatro's curve shape (doc 03: $5 base, +$1 per reroll, resets next shop). Every part
    /// is treated as a unique for now — owned parts never reappear in offers.
    /// </summary>
    public class ShopLogic
    {
        public const int OfferCount = 3;
        public const int BaseRerollCost = 5;
        public const int RerollCostStep = 1;

        private Random _rng;
        private readonly List<PartDef> _offers = new List<PartDef>();

        /// <summary>The parts currently on the shelf (bought ones are removed in place).</summary>
        public IReadOnlyList<PartDef> Offers => _offers;

        /// <summary>Cost of the next reroll this visit.</summary>
        public int RerollCost { get; private set; } = BaseRerollCost;

        public ShopLogic() : this(Environment.TickCount) { }
        public ShopLogic(int seed) => _rng = new Random(seed);

        /// <summary>
        /// Reseeds the draw RNG so a visit can be made byte-for-byte reproducible from a run seed.
        /// RunDirector calls this (via the seeded BeginVisit overload) with a per-visit seed
        /// derived from the run seed plus the circuit/race indices, so a resumed run reproduces
        /// the same stock and reroll chain. Existing callers are unaffected.
        /// </summary>
        public void Reseed(int seed) => _rng = new Random(seed);

        /// <summary>Opens a shop visit: resets the reroll cost and rolls fresh stock.</summary>
        public void BeginVisit(IReadOnlyList<PartDef> pool, RunState run)
        {
            RerollCost = BaseRerollCost;
            Roll(pool, run);
        }

        /// <summary>
        /// Deterministic BeginVisit: reseeds the RNG from <paramref name="seed"/> first, then rolls
        /// exactly as the seedless overload — same rarity-weighted, without-replacement draw. Later
        /// rerolls continue off this seeded stream, so the whole visit reproduces from the seed.
        /// </summary>
        public void BeginVisit(IReadOnlyList<PartDef> pool, RunState run, int seed)
        {
            Reseed(seed);
            BeginVisit(pool, run);
        }

        /// <summary>Pays the escalating reroll cost and rerolls the shelf. False if unaffordable.</summary>
        public bool TryReroll(IReadOnlyList<PartDef> pool, RunState run)
        {
            if (run.Money < RerollCost) return false;
            run.Money -= RerollCost;
            RerollCost += RerollCostStep;
            Roll(pool, run);
            return true;
        }

        /// <summary>
        /// Buys an offered part: deducts the price, moves it into the run inventory, and
        /// auto-equips it when a slot is free (no-op otherwise — equip later in the garage).
        /// </summary>
        public bool TryBuy(PartDef part, RunState run)
        {
            if (part == null || !_offers.Contains(part) || run.Money < part.Price) return false;
            run.Money -= part.Price;
            _offers.Remove(part);
            run.OwnedParts.Add(part);
            run.Equip(part);
            return true;
        }

        /// <summary>
        /// Rarity-weighted draw of up to OfferCount distinct parts the player doesn't own yet:
        /// Common is common, Rare is rare (doc 03's Balatro shelf). Weighted picks are made
        /// without replacement so offers stay distinct, and the whole draw runs off the seeded
        /// RNG so a given seed is reproducible.
        /// </summary>
        private void Roll(IReadOnlyList<PartDef> pool, RunState run)
        {
            _offers.Clear();
            if (pool == null) return;

            var candidates = new List<PartDef>(pool.Count);
            foreach (PartDef part in pool)
                if (part != null && !run.Owns(part))
                    candidates.Add(part);

            int count = Math.Min(OfferCount, candidates.Count);
            for (int i = 0; i < count; i++)
            {
                int totalWeight = 0;
                foreach (PartDef part in candidates)
                    totalWeight += RarityWeight(part.Rarity);

                int roll = _rng.Next(totalWeight);
                int pick = candidates.Count - 1; // last standing if rounding leaves a sliver
                for (int c = 0; c < candidates.Count; c++)
                {
                    roll -= RarityWeight(candidates[c].Rarity);
                    if (roll < 0) { pick = c; break; }
                }

                _offers.Add(candidates[pick]);
                candidates.RemoveAt(pick);
            }
        }

        /// <summary>Relative shelf frequency per tier — Common shows up most, Rare least.</summary>
        private static int RarityWeight(Rarity rarity)
        {
            switch (rarity)
            {
                case Rarity.Uncommon: return 30;
                case Rarity.Rare:     return 8;
                default:              return 100; // Common
            }
        }
    }
}
