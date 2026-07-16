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
        /// <summary>Base shelf size, before any Overstock upgrade — see <see cref="EffectiveOfferCount"/>.</summary>
        public const int OfferCount = 3;

        /// <summary>Base first-reroll cost, before any Reroll Surplus — see <see cref="EffectiveRerollBase"/>.</summary>
        public const int BaseRerollCost = 5;
        public const int RerollCostStep = 1;

        /// <summary>
        /// Parts on the shelf for this run: the <see cref="OfferCount"/> base plus any Overstock upgrade.
        /// A run owning no upgrades gets exactly the shipped 3.
        /// </summary>
        public static int EffectiveOfferCount(RunState run) => OfferCount + TeamUpgrades.ExtraShopOffers(run);

        /// <summary>
        /// This run's first-reroll cost: the <see cref="BaseRerollCost"/> base less any Reroll Surplus
        /// discount, floored at $1 so rerolling can never become free (a free reroll would let the player
        /// spin the shelf until it hands them whatever they want, which is the end of the shop as a choice).
        /// </summary>
        public static int EffectiveRerollBase(RunState run) =>
            Math.Max(1, BaseRerollCost - TeamUpgrades.RerollDiscount(run));

        /// <summary>
        /// Extra per-reroll escalation layered ON TOP of <see cref="RerollCostStep"/> (Balatro-style
        /// economy depth). 0 (default) reproduces the shipped +$1/reroll curve exactly; raise it to
        /// make rerolls bite harder each time within a visit. Resets nothing — it is a run-wide knob.
        /// </summary>
        public int RerollCostIncrement;

        /// <summary>
        /// Optional dynamic-pricing multiplier per <see cref="Rarity"/> tier, indexed by (int)Rarity.
        /// null (default) means every tier prices at 1x — i.e. the shipped sticker price. See
        /// <see cref="PriceOf"/> / <see cref="ShopEconomy.PartPrice(int, Rarity, PartCategory, float[], float[])"/>.
        /// </summary>
        public float[] RarityPriceMult;

        /// <summary>
        /// Optional dynamic-pricing multiplier per part <see cref="PartCategory"/> family, indexed by
        /// (int)PartCategory. null (default) means every family prices at 1x — the shipped price.
        /// </summary>
        public float[] FamilyPriceMult;

        private Random _rng;
        private readonly List<PartDef> _offers = new List<PartDef>();

        /// <summary>How many rerolls have been bought so far this visit (drives the escalating cost).</summary>
        private int _rerollsThisVisit;

        /// <summary>The parts currently on the shelf (bought ones are removed in place).</summary>
        public IReadOnlyList<PartDef> Offers => _offers;

        /// <summary>Cost of the next reroll this visit.</summary>
        public int RerollCost { get; private set; } = BaseRerollCost;

        /// <summary>
        /// The price this shop actually charges for a part, after any rarity/family pricing multipliers.
        /// With the default (null) multiplier tables this is exactly <c>part.Price</c>, so buying costs
        /// the shipped sticker price. Returns 0 for a null part.
        /// </summary>
        public int PriceOf(PartDef part) => part == null
            ? 0
            : ShopEconomy.PartPrice(part.Price, part.Rarity, part.Category, RarityPriceMult, FamilyPriceMult);

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
            _rerollsThisVisit = 0;
            RerollCost = EffectiveRerollBase(run);
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
            // Advance the per-visit counter and recompute the next cost through ShopEconomy. The
            // effective per-reroll step is the shipped RerollCostStep plus any extra depth tuning;
            // with RerollCostIncrement = 0 this is byte-for-byte today's +$1/reroll curve.
            _rerollsThisVisit++;
            RerollCost = ShopEconomy.RerollCost(
                EffectiveRerollBase(run), _rerollsThisVisit, RerollCostStep + RerollCostIncrement);
            Roll(pool, run);
            return true;
        }

        /// <summary>
        /// Buys an offered part: deducts the price, moves it into the run inventory, and
        /// auto-equips it when a slot is free (no-op otherwise — equip later in the garage).
        /// </summary>
        public bool TryBuy(PartDef part, RunState run)
        {
            int price = PriceOf(part);
            // The Owns guard is the backstop against duplicate ownership: parts are uniques, and a part can
            // now reach the inventory by two routes (shelf and crate). Without it, any path that leaves a
            // now-owned part on the shelf would let it be bought a second time.
            if (part == null || !_offers.Contains(part) || run.Owns(part) || run.Money < price) return false;
            run.Money -= price;
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
            DrawParts(pool, run, EffectiveOfferCount(run), _offers);
        }

        /// <summary>
        /// Buys a permanent team upgrade (doc 03's vouchers): pay once, own it for the rest of the run.
        /// Refuses — charging nothing — for a duplicate or when the money isn't there. There is no
        /// "equip": owning it IS the effect, resolved everywhere through <see cref="TeamUpgrades"/>.
        ///
        /// Re-derives <see cref="RerollCost"/> afterwards so Reroll Surplus bites on THIS visit's next
        /// reroll rather than making the player wait for the next garage. Overstock deliberately doesn't
        /// re-roll the shelf — the extra slot shows up on the next roll, so buying it can't be used to
        /// refresh the current stock for free.
        /// </summary>
        public bool TryBuyUpgrade(TeamUpgrade upgrade, RunState run)
        {
            if (run == null || run.HasUpgrade(upgrade)) return false;

            int price = TeamUpgrades.PriceOf(upgrade);
            if (run.Money < price) return false;

            run.Money -= price;
            run.OwnedUpgrades.Add(upgrade);
            RerollCost = ShopEconomy.RerollCost(
                EffectiveRerollBase(run), _rerollsThisVisit, RerollCostStep + RerollCostIncrement);
            return true;
        }

        /// <summary>
        /// Rarity-weighted, without-replacement draw of up to <paramref name="count"/> distinct parts the
        /// player doesn't own yet, appended to <paramref name="into"/>. Runs off the seeded RNG so a given
        /// seed reproduces the draw.
        ///
        /// Shared by the shelf (<see cref="Roll"/>) and by crates (<see cref="TryBuyCrate"/>) on purpose:
        /// a crate that drew from its own copy of this logic could drift from the shelf's rarity curve, and
        /// then "Rare is rare" would quietly mean two different things depending on where you looked.
        /// </summary>
        private void DrawParts(
            IReadOnlyList<PartDef> pool, RunState run, int count, List<PartDef> into,
            IReadOnlyList<PartDef> exclude = null)
        {
            if (pool == null || into == null || count <= 0) return;

            var candidates = new List<PartDef>(pool.Count);
            foreach (PartDef part in pool)
                if (part != null && !run.Owns(part) && !into.Contains(part) && !ListContains(exclude, part))
                    candidates.Add(part);

            int draws = Math.Min(count, candidates.Count);
            for (int i = 0; i < draws; i++)
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

                into.Add(candidates[pick]);
                candidates.RemoveAt(pick);
            }
        }

        /// <summary>
        /// Buys a booster-style part crate (doc 03): pay <paramref name="price"/>, draw
        /// <paramref name="drawCount"/> parts into <see cref="RunState.CrateContents"/> for the player to
        /// pick ONE from (<see cref="TryTakeFromCrate"/>). The draw runs at BUY time, not at visit open, so
        /// anything bought earlier this visit is correctly excluded.
        ///
        /// Refuses — charging nothing — when a crate is already open, when the money isn't there, or when
        /// the pool has no unowned candidates left. That last guard matters: selling an empty crate would
        /// take the money and hand back a pick screen with nothing in it.
        /// </summary>
        public bool TryBuyCrate(IReadOnlyList<PartDef> pool, RunState run, int price, int drawCount)
        {
            if (run == null || run.CrateOpen || run.Money < price) return false;

            // Exclude what's already on the shelf: the same part appearing in both places reads as a bug,
            // and it would open a duplicate-ownership path (take it from the crate, then buy the shelf copy).
            var drawn = new List<PartDef>();
            DrawParts(pool, run, drawCount, drawn, _offers);
            if (drawn.Count == 0) return false; // nothing left to draw — don't sell an empty box

            run.Money -= price;
            run.CrateContents.AddRange(drawn);
            return true;
        }

        /// <summary>
        /// Takes one part from the open crate: it joins the run inventory (auto-equipped when a slot is
        /// free, exactly like <see cref="TryBuy"/>) and the crate closes — the parts not chosen are gone,
        /// which is what makes the pick a decision. Already paid for at buy time, so this costs nothing.
        /// </summary>
        public bool TryTakeFromCrate(PartDef part, RunState run)
        {
            if (part == null || run == null || !run.CrateContents.Contains(part)) return false;

            run.CrateContents.Clear();
            run.OwnedParts.Add(part);
            run.Equip(part);
            _offers.Remove(part); // belt-and-braces: the shelf must never re-sell what you just took
            return true;
        }

        /// <summary>
        /// Membership test over an <see cref="IReadOnlyList{T}"/>, which — unlike List/ICollection — carries
        /// no Contains of its own. Hand-rolled rather than pulling in Linq: this runs inside the draw loop
        /// and the codebase keeps the shop allocation-free so a headless server can roll stock cheaply.
        /// </summary>
        private static bool ListContains(IReadOnlyList<PartDef> list, PartDef part)
        {
            if (list == null) return false;
            for (int i = 0; i < list.Count; i++)
                if (list[i] == part) return true;
            return false;
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
