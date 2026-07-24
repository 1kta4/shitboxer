using System;
using System.Collections.Generic;

namespace Shitboxer.Meta
{
    /// <summary>
    /// Between-race shop rules, engine-free so it unit-tests without a scene. One instance
    /// lives for the whole run; BeginVisit when the garage opens resets the reroll cost to
    /// Balatro's curve shape (doc 03: $5 base, +$1 per reroll, resets next shop). Every part
    /// is treated as a unique for now — owned parts never reappear in offers.
    ///
    /// Everything buyable here is ROLLED, never browsed: parts, packs and Blueprints alike have to
    /// turn up on the shelf before they can be bought. That is why <see cref="TryBuyBlueprint"/>
    /// exists rather than the garage calling <see cref="RunState.BuyBlueprint"/> directly.
    /// </summary>
    public class ShopLogic
    {
        /// <summary>Base shelf size, before any Overstock upgrade — see <see cref="EffectiveOfferCount"/>.</summary>
        public const int OfferCount = 3;

        /// <summary>
        /// Blueprints stocked per visit. Component levels are NOT an always-open menu of all ten
        /// (re-litigated 2026-07-22): a Blueprint has to SHOW UP — on the shelf here, or as a pick out
        /// of a components pack. Two at a time keeps the shelf a choice between what turned up rather
        /// than a checklist you work down, which is the whole reason the shop is rolled and not browsed.
        /// </summary>
        public const int BlueprintOfferCount = 2;

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

        /// <summary>Fraction of a part's price refunded when sold. Balatro's half, floored at $1.</summary>
        public const float SellFraction = 0.5f;

        /// <summary>Packs offered every visit, drawn from <see cref="ShopPackKind"/>.</summary>
        public const int PacksPerVisit = 2;

        private Random _rng;
        private readonly List<PartDef> _offers = new List<PartDef>();
        private readonly List<ShopPack> _packs = new List<ShopPack>(PacksPerVisit);
        private readonly List<CarComponent> _blueprints = new List<CarComponent>(BlueprintOfferCount);

        /// <summary>Reused by <see cref="RollBlueprints"/> so rolling the shelf allocates nothing.</summary>
        private readonly List<int> _drawScratch = new List<int>(BlueprintOfferCount);

        /// <summary>
        /// How many rerolls have been bought BACK TO BACK, with no purchase in between — the escalating
        /// cost keys off this rather than a visit total. Buying anything resets it, so a player who
        /// engages with the shelf gets a cheap reroll again while one who only spins pays more each
        /// time. That is the point: the escalation punishes fishing, not shopping.
        /// </summary>
        private int _consecutiveRerolls;

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

        /// <summary>The packs on offer this visit. Bought packs are removed in place.</summary>
        public IReadOnlyList<ShopPack> Packs => _packs;

        /// <summary>
        /// The Blueprints that turned up this visit — the ONLY way to buy a component level outright.
        /// Bought ones are removed in place; a reroll replaces the whole row.
        /// </summary>
        public IReadOnlyList<CarComponent> Blueprints => _blueprints;

        /// <summary>Opens a shop visit: resets the reroll cost and rolls fresh stock, Blueprints and packs.</summary>
        public void BeginVisit(IReadOnlyList<PartDef> pool, RunState run)
        {
            _consecutiveRerolls = 0;
            RerollCost = EffectiveRerollBase(run);
            Roll(pool, run);
            RollBlueprints(run);
            RollPacks();
        }

        /// <summary>
        /// Ends a consecutive-reroll streak. Called by every purchase, so the escalating cost measures
        /// back-to-back spinning rather than a visit total, and re-derives the next reroll price
        /// immediately so the shop shows the reset rather than making the player guess.
        /// </summary>
        private void BreakRerollStreak(RunState run)
        {
            _consecutiveRerolls = 0;
            RerollCost = EffectiveRerollBase(run);
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

        /// <summary>
        /// Pays the escalating reroll cost and rerolls the shelf — parts AND Blueprints, since one
        /// reroll buys a whole new shelf. Packs deliberately stay: they are the visit's fixed offer,
        /// and rerolling into a better pack table would make the reroll the only button worth pressing.
        /// False if unaffordable.
        /// </summary>
        public bool TryReroll(IReadOnlyList<PartDef> pool, RunState run)
        {
            if (run.Money < RerollCost) return false;
            run.Money -= RerollCost;
            // Advance the CONSECUTIVE counter and recompute the next cost. Any purchase resets this
            // (see BreakRerollStreak), so the +$1 curve escalates only across back-to-back rerolls.
            _consecutiveRerolls++;
            RerollCost = ShopEconomy.RerollCost(
                EffectiveRerollBase(run), _consecutiveRerolls, RerollCostStep + RerollCostIncrement);
            Roll(pool, run);
            RollBlueprints(run);
            return true;
        }

        /// <summary>What selling a part refunds: half its shop price, floored at $1 so nothing is worthless.</summary>
        public int SellValueOf(PartDef part) =>
            part == null ? 0 : Math.Max(1, (int)(PriceOf(part) * SellFraction));

        /// <summary>
        /// Run-aware sell value: an editioned part (doc 08 slice 13) refunds against its
        /// edition-multiplied price — Foil ×1.5, Holo ×2, Polychrome ×3 — so a material applied is
        /// value banked, not value burned. A plain part (or a null run) refunds exactly the shipped
        /// number above.
        /// </summary>
        public int SellValueOf(PartDef part, RunState run)
        {
            if (part == null) return 0;
            PartEdition edition = run != null ? run.EditionOf(part) : part.Edition;
            return Math.Max(1, (int)(PriceOf(part) * PartEditionInfo.PriceMult(edition) * SellFraction));
        }

        /// <summary>
        /// Buys an offered part. A bought part is ALWAYS equipped — there is no owned-but-benched state
        /// for shelf purchases — which is why this refuses when the car is full rather than quietly
        /// selling something the player can't use. Sell a part to make room.
        /// </summary>
        public bool TryBuy(PartDef part, RunState run)
        {
            int price = PriceOf(part);
            // The Owns guard is the backstop against duplicate ownership: parts are uniques, and a part can
            // now reach the inventory by two routes (shelf and pack). Without it, any path that leaves a
            // now-owned part on the shelf would let it be bought a second time.
            if (part == null || !_offers.Contains(part) || run.Owns(part) || run.Money < price) return false;
            if (!run.HasFreeSlot) return false; // full car — sell something first

            run.Money -= price;
            _offers.Remove(part);
            run.OwnedParts.Add(part);
            run.Equip(part);
            BreakRerollStreak(run);
            return true;
        }

        /// <summary>
        /// Sells an owned part back for <see cref="SellValueOf"/>, freeing its slot. The part leaves the
        /// run entirely — it is not benched — matching the "bought means equipped" rule above.
        ///
        /// Deliberately allowed even while a pack is open: with buying gated on a free slot, a player who
        /// filled their last slot and then opened a pack would otherwise be stuck with nothing to do.
        /// </summary>
        public bool TrySell(PartDef part, RunState run)
        {
            if (part == null || run == null || !run.Owns(part)) return false;
            int refund = SellValueOf(part, run); // edition-aware: the applied material is priced into the refund
            // Any open Spectral offer AIMED at this part dies with it — an offer targeting a part
            // that has left the run could never be resolved, and leaving it behind would wedge the
            // pack open forever. If that empties the pack, the pack is resolved: selling every
            // target was the player's choice, and the shelf must come back.
            if (run.PackSpectrals.Count > 0 && !string.IsNullOrEmpty(part.Id))
                run.PackSpectrals.RemoveAll(o =>
                    SpectralOffer.TryDecode(o, out _, out string targetId) && targetId == part.Id);
            run.RemovePart(part);
            run.Money += refund;
            BreakRerollStreak(run);
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
            // Also ends the reroll streak, like every other purchase. Reroll Surplus therefore bites on
            // THIS visit's next reroll rather than making the player wait for the next garage.
            BreakRerollStreak(run);
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
        /// <summary>
        /// Rolls this visit's two packs from the weighted kind table. Duplicates are allowed — two parts
        /// packs is a legitimate (if unlucky) shelf, and forcing variety would make the second slot
        /// predictable once you'd seen the first.
        /// </summary>
        private void RollPacks()
        {
            _packs.Clear();

            int totalWeight = 0;
            foreach (ShopPackKind kind in ShopPackCatalog.All) totalWeight += ShopPackCatalog.Weight(kind);
            if (totalWeight <= 0) return; // nothing implemented to stock

            for (int i = 0; i < PacksPerVisit; i++)
            {
                int roll = _rng.Next(totalWeight);
                ShopPackKind picked = ShopPackCatalog.All[0];
                foreach (ShopPackKind kind in ShopPackCatalog.All)
                {
                    roll -= ShopPackCatalog.Weight(kind);
                    if (roll < 0) { picked = kind; break; }
                }
                _packs.Add(ShopPackCatalog.Make(picked));
            }
        }

        /// <summary>
        /// Buys one of this visit's packs and opens its pick-one draw. Refuses — charging nothing —
        /// when another pack is already open, the money isn't there, or the pack would open onto
        /// nothing to choose from.
        /// </summary>
        public bool TryBuyPack(int packIndex, IReadOnlyList<PartDef> pool, RunState run)
        {
            if (run == null || run.PackOpen) return false;
            if (packIndex < 0 || packIndex >= _packs.Count) return false;

            ShopPack pack = _packs[packIndex];
            if (run.Money < pack.Price) return false;

            switch (pack.Kind)
            {
                case ShopPackKind.Parts:
                {
                    // A parts pack hands over a part, and a bought part is always equipped — so refuse
                    // rather than sell a pack whose prize cannot be taken.
                    if (!run.HasFreeSlot) return false;

                    var drawn = new List<PartDef>();
                    DrawParts(pool, run, pack.DrawCount, drawn, _offers);
                    if (drawn.Count == 0) return false; // don't sell an empty box

                    run.Money -= pack.Price;
                    run.CrateContents.AddRange(drawn);
                    break;
                }

                case ShopPackKind.Components:
                {
                    var drawn = new List<int>();
                    DrawComponents(run, pack.DrawCount, drawn);
                    if (drawn.Count == 0) return false; // every component already maxed

                    run.Money -= pack.Price;
                    run.PackComponents.AddRange(drawn);
                    break;
                }

                case ShopPackKind.Spectral:
                {
                    // Editions as materials (doc 08 slice 13). Each offer is pre-aimed — "FOIL onto
                    // the Junkyard Turbo" — so the pick stays the same one-tap decision as every
                    // other pack, no second target-choosing step. Refused when nothing fitted can
                    // take an edition: never sell a pack whose prize can't be taken.
                    var drawn = new List<string>();
                    DrawSpectrals(run, pack.DrawCount, drawn);
                    if (drawn.Count == 0) return false;

                    run.Money -= pack.Price;
                    run.PackSpectrals.AddRange(drawn);
                    break;
                }

                default:
                    return false; // unknown future kind — never stocked, never sold
            }

            _packs.RemoveAt(packIndex);
            BreakRerollStreak(run);
            return true;
        }

        /// <summary>
        /// Draws up to <paramref name="count"/> Spectral offers: DISTINCT fitted parts that can
        /// actually take an edition — they carry SpecMods (editions amplify stat effect; on a
        /// stat-less part a material would be a lie) and sit below Polychrome — each paired with a
        /// weighted-rolled tier STRICTLY ABOVE its current one. Fitted parts only: the material is
        /// applied on pick and a bought part is always fitted, so an un-fitted target can't exist.
        /// </summary>
        private void DrawSpectrals(RunState run, int count, List<string> into)
        {
            if (run == null || into == null || count <= 0) return;

            var candidates = new List<PartDef>();
            foreach (PartDef part in run.EquippedParts)
                if (part != null && !string.IsNullOrEmpty(part.Id)
                    && part.SpecMods != null && part.SpecMods.Count > 0
                    && run.EditionOf(part) < PartEdition.Polychrome)
                    candidates.Add(part);

            int draws = Math.Min(count, candidates.Count);
            for (int i = 0; i < draws; i++)
            {
                int pick = _rng.Next(candidates.Count);
                PartDef part = candidates[pick];
                candidates.RemoveAt(pick);
                into.Add(SpectralOffer.Encode(RollEditionAbove(run.EditionOf(part)), part.Id));
            }
        }

        /// <summary>A weighted tier roll restricted to editions above <paramref name="current"/> —
        /// Foil-heavy from None, and from Holo the only legal pull is the Polychrome jackpot.</summary>
        private PartEdition RollEditionAbove(PartEdition current)
        {
            int total = 0;
            foreach (PartEdition tier in SpectralOffer.Tiers)
                if (tier > current) total += SpectralOffer.Weight(tier);
            if (total <= 0) return PartEdition.Polychrome; // unreachable for a valid candidate

            int roll = _rng.Next(total);
            foreach (PartEdition tier in SpectralOffer.Tiers)
            {
                if (tier <= current) continue;
                roll -= SpectralOffer.Weight(tier);
                if (roll < 0) return tier;
            }
            return PartEdition.Polychrome;
        }

        /// <summary>
        /// Takes one offer from an open Spectral pack, stamping the edition onto that part for the
        /// rest of the run. Already paid at buy time; the options not chosen are gone. Refuses an
        /// offer that is not in the open pack, or whose target has been sold out from under it —
        /// though TrySell already purges those, so that path is a belt-and-braces guard.
        /// </summary>
        public bool TryTakeSpectral(PartDef part, PartEdition edition, RunState run)
        {
            if (part == null || run == null) return false;
            string encoded = SpectralOffer.Encode(edition, part.Id);
            if (!run.PackSpectrals.Contains(encoded)) return false;
            if (!run.TryUpgradeEdition(part, edition)) return false;

            run.PackSpectrals.Clear();
            return true;
        }

        /// <summary>
        /// Draws distinct, still-levellable components — for a components pack and for the shelf's
        /// Blueprint row alike. A component already at the ceiling is excluded, so a late-run draw
        /// never offers a pick that would do nothing.
        /// Unweighted: every component is equally worth offering, and rarity is a parts concept.
        /// </summary>
        private void DrawComponents(RunState run, int count, List<int> into)
        {
            if (run == null || into == null || count <= 0) return;

            var candidates = new List<int>(CarComponentCatalog.Count);
            for (int i = 0; i < CarComponentCatalog.Count; i++)
                if (CarComponentCatalog.CanLevel(run.LevelOf((CarComponent)i)))
                    candidates.Add(i);

            int draws = Math.Min(count, candidates.Count);
            for (int i = 0; i < draws; i++)
            {
                int pick = _rng.Next(candidates.Count);
                into.Add(candidates[pick]);
                candidates.RemoveAt(pick);
            }
        }

        /// <summary>
        /// Rolls this visit's Blueprint row: <see cref="BlueprintOfferCount"/> distinct, still-levellable
        /// components, drawn by the same rules a components pack uses — so "what can turn up" means one
        /// thing everywhere, and a maxed component is never offered a level it cannot take.
        ///
        /// Rolls off the same seeded stream as the parts shelf, so a resumed run reproduces the whole
        /// visit. Late in a run this can legitimately come back SHORT (or empty) once most components
        /// are maxed — the garage just shows fewer, which is the honest read of a nearly-finished car.
        /// </summary>
        private void RollBlueprints(RunState run)
        {
            _blueprints.Clear();
            _drawScratch.Clear();
            DrawComponents(run, BlueprintOfferCount, _drawScratch);
            for (int i = 0; i < _drawScratch.Count; i++)
                _blueprints.Add((CarComponent)_drawScratch[i]);
        }

        /// <summary>
        /// Buys a Blueprint OFF THE SHELF, raising that component one level. The shelf check is the
        /// whole point of this method: <see cref="RunState.BuyBlueprint"/> will happily level anything
        /// it can afford, and routing every caller through here is what makes a component level
        /// something that has to turn up rather than something you pick off a list of all ten.
        ///
        /// Refuses — charging nothing — for a component that isn't stocked, is maxed, or costs more
        /// than the player has. Consumes the offer on success, so the same Blueprint can't be bought
        /// twice off one shelf; the next level of that component has to turn up again.
        /// </summary>
        public bool TryBuyBlueprint(CarComponent component, RunState run)
        {
            if (run == null || !_blueprints.Contains(component)) return false;
            if (!run.BuyBlueprint(component)) return false;   // charges, levels, or refuses outright

            _blueprints.Remove(component);
            BreakRerollStreak(run);
            return true;
        }

        /// <summary>
        /// Takes one component from an open components pack, raising it a level. Already paid for at
        /// buy time, so this costs nothing; the options not chosen are gone, which is what makes the
        /// pick a decision.
        /// </summary>
        public bool TryTakeComponent(CarComponent component, RunState run)
        {
            if (run == null || !run.PackComponents.Contains((int)component)) return false;

            run.PackComponents.Clear();
            int index = (int)component;
            run.ComponentLevels[index] =
                CarComponentCatalog.ClampLevel(run.LevelOf(component) + 1);
            return true;
        }

        public bool TryBuyCrate(IReadOnlyList<PartDef> pool, RunState run, int price, int drawCount)
        {
            if (run == null || run.PackOpen || run.Money < price) return false;
            if (!run.HasFreeSlot) return false; // its prize is always equipped — see TryBuy

            // Exclude what's already on the shelf: the same part appearing in both places reads as a bug,
            // and it would open a duplicate-ownership path (take it from the crate, then buy the shelf copy).
            var drawn = new List<PartDef>();
            DrawParts(pool, run, drawCount, drawn, _offers);
            if (drawn.Count == 0) return false; // nothing left to draw — don't sell an empty box

            run.Money -= price;
            run.CrateContents.AddRange(drawn);
            BreakRerollStreak(run);
            return true;
        }

        /// <summary>
        /// Takes one part from the open pack: it joins the run inventory and is equipped, exactly like
        /// <see cref="TryBuy"/>, and the pack closes — the parts not chosen are gone, which is what
        /// makes the pick a decision. Already paid for at buy time, so this costs nothing.
        ///
        /// Refuses if the car has filled up since the pack was bought (the player sold nothing and
        /// bought elsewhere). The pack stays open rather than consuming the pick, so selling a part
        /// then taking again works.
        /// </summary>
        public bool TryTakeFromCrate(PartDef part, RunState run)
        {
            if (part == null || run == null || !run.CrateContents.Contains(part)) return false;
            if (!run.HasFreeSlot) return false; // pack stays open — sell something, then take

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
