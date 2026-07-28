using System;
using System.Collections.Generic;
using Shitboxer.Vehicle;
using UnityEngine;

namespace Shitboxer.Meta
{
    /// <summary>
    /// All persistent state of one roguelike run. Plain C# and [Serializable] — no
    /// UnityEngine.Object references except the PartDef lists — so a headless server could
    /// own the authoritative copy later. RunDirector holds the single live instance.
    /// </summary>
    [Serializable]
    public class RunState
    {
        public int Money;
        public int Lives = 3;

        /// <summary>
        /// Deterministic run seed, set once when a run starts (RunDirector rolls it for a fresh
        /// run and persists it in the save). RunDirector derives a per-garage-visit shop seed
        /// from Seed + CircuitIndex + RaceIndex, so a resumed or shared run reproduces the exact
        /// same shop stock and reroll chain. 0 on a state that was never explicitly seeded.
        /// </summary>
        public int Seed;

        /// <summary>0-based index of the current/upcoming race within the circuit.</summary>
        public int RaceIndex;
        public int RacesPerCircuit = 3;

        /// <summary>0-based index of the current circuit within the season.</summary>
        public int CircuitIndex;

        /// <summary>
        /// How many circuits make up a full season. 8 per doc 08 decision 12: the 24-race, ~75-minute
        /// full season (decision 12 explicitly overrides doc 05's "start with 1 circuit" — the long
        /// horizon is what makes team upgrades and slow-burn parts viable). A run-start constant that
        /// RunSave deliberately does not persist; RunDirector re-stamps it from its inspector field on
        /// every run it adopts (see RunDirector.ApplySeasonShape), so this default only applies to a
        /// RunState built outside the director.
        /// </summary>
        public int TotalCircuits = 8;

        /// <summary>The circuit's last race is the Boss/Feature race: must finish top-N to advance.</summary>
        public int BossTopN = 3;

        public int MaxEquipSlots = 6;

        /// <summary>
        /// License-stake level (0 = base license, i.e. exactly today's balance). Higher stakes are
        /// UNLOCKED across runs by clearing the season one stake below them (recorded in MetaProgress),
        /// and they scale BOTH difficulty and reward: StakeLevel folds into <see cref="DifficultyMult"/>
        /// via <see cref="StakeMult"/> (so the season ramp AND RunDirector's bot/cutoff scaling pick it
        /// up automatically), and RunDirector applies the same <see cref="StakeMult"/> as a modest payout
        /// bump on a clean finish. Defaults to 0 so an un-staked run plays and pays exactly as shipped.
        /// </summary>
        public int StakeLevel;

        /// <summary>Which chassis this run drives (index into RunDirector.chassisSpecs; 0 = Grip). Chosen
        /// on the car-select screen and persisted, so a resumed run keeps its car.</summary>
        public int ChassisId;

        /// <summary>Per-stake difficulty/reward gain. Stake 0 is a no-op (factor 1.0), gentle above.</summary>
        public const float StakeGainPerLevel = 0.15f;

        /// <summary>
        /// Difficulty/reward scalar contributed purely by the license stake: 1.0 at stake 0 (shipped
        /// balance), climbing gently above. Multiplies the per-circuit ramp in <see cref="DifficultyMult"/>
        /// and doubles as RunDirector's clean-finish reward multiplier.
        /// </summary>
        public float StakeMult => 1f + StakeGainPerLevel * Math.Max(0, StakeLevel);

        /// <summary>
        /// Persistent 0..1 structural integrity of the run's car (1 = pristine). Unlike the sim's
        /// per-race Durability this carries ACROSS races within a run: RunDirector re-applies it onto
        /// each freshly-rebuilt sim, captures the sim's ending value back after every race, and resets
        /// it to 1 when the player pays to repair in the garage. A fresh run starts pristine.
        /// </summary>
        public float CarDurability = 1f;

        /// <summary>
        /// Credits earned DURING the current race by sector-scoring parts, banked into
        /// <see cref="Money"/> at payout and then cleared. Separate from Money on purpose: the race is
        /// still in progress while this accrues, and the HUD wants to show "earned so far" distinctly
        /// from the wallet.
        ///
        /// Per doc 08 decision 9 this stays at 0 unless the player equips a part with sector rules, so
        /// the base position-only inverted economy is untouched for a run that buys none. Deliberately
        /// NOT persisted by RunSave — a run resumed mid-race restarts the race anyway.
        /// </summary>
        [System.NonSerialized] public int InRaceEarnings;

        /// <summary>
        /// Level of each car component, indexed by <see cref="CarComponent"/> ordinal (doc 08
        /// decision 4). Every component is always installed; a Blueprint bought in the garage raises
        /// one by a level. All start at <see cref="CarComponentCatalog.MinLevel"/>, which contributes
        /// nothing, so a fresh run drives the authored chassis exactly.
        /// </summary>
        public int[] ComponentLevels = NewComponentLevels();

        /// <summary>A fresh, all-baseline component set.</summary>
        public static int[] NewComponentLevels()
        {
            var levels = new int[CarComponentCatalog.Count];
            for (int i = 0; i < levels.Length; i++) levels[i] = CarComponentCatalog.MinLevel;
            return levels;
        }

        /// <summary>Current level of a component; the baseline for anything out of range.</summary>
        public int LevelOf(CarComponent component)
        {
            int index = (int)component;
            return ComponentLevels != null && index >= 0 && index < ComponentLevels.Length
                ? CarComponentCatalog.ClampLevel(ComponentLevels[index])
                : CarComponentCatalog.MinLevel;
        }

        /// <summary>Cost of the next Blueprint for a component.</summary>
        public int BlueprintPriceFor(CarComponent component) =>
            CarComponentCatalog.BlueprintPrice(LevelOf(component));

        /// <summary>
        /// Buys one level of a component, charging <see cref="Money"/>. False — and nothing charged —
        /// if the component is already maxed or the money isn't there.
        ///
        /// The money-and-level PRIMITIVE, deliberately with no notion of what is for sale. The garage
        /// must go through <see cref="ShopLogic.TryBuyBlueprint"/>, which adds the shelf check — buying
        /// straight through here would restore the old "pick any of the ten, any time" menu.
        /// </summary>
        public bool BuyBlueprint(CarComponent component)
        {
            int index = (int)component;
            if (ComponentLevels == null || index < 0 || index >= ComponentLevels.Length) return false;

            int level = LevelOf(component);
            if (!CarComponentCatalog.CanLevel(level)) return false;

            int price = CarComponentCatalog.BlueprintPrice(level);
            if (Money < price) return false;

            Money -= price;
            ComponentLevels[index] = level + 1;
            return true;
        }

        /// <summary>The stat points this run's components contribute — fed straight into the ledger.</summary>
        public BuildLedger ComponentLedger() => CarComponentCatalog.Accumulate(ComponentLevels);

        /// <summary>Everything bought this run.</summary>
        public List<PartDef> OwnedParts = new List<PartDef>();

        /// <summary>The subset currently slotted onto the car (max MaxEquipSlots).</summary>
        public List<PartDef> EquippedParts = new List<PartDef>();

        /// <summary>
        /// Parts drawn by a bought-but-unresolved crate, awaiting the player's pick (doc 03's
        /// booster-style part crates). Empty whenever no crate is open.
        ///
        /// This lives on the RUN, not on ShopLogic, precisely because it must survive a save: the crate is
        /// paid for at buy time and RunDirector saves immediately on every purchase, so parking the contents
        /// in transient shop state would let a quit-then-resume restore the spend with nothing drawn — the
        /// player's money would simply evaporate. RunSave persists these by Id like every other part list.
        /// </summary>
        public List<PartDef> CrateContents = new List<PartDef>();

        /// <summary>True while a paid-for parts pack is waiting to be picked from.</summary>
        public bool CrateOpen => CrateContents.Count > 0;

        /// <summary>
        /// Components drawn by a bought-but-unresolved COMPONENT pack, awaiting the player's pick — the
        /// component-side twin of <see cref="CrateContents"/>. Stored as <see cref="CarComponent"/>
        /// ordinals so the whole run stays plainly serializable. Empty whenever no such pack is open.
        ///
        /// Lives on the run for exactly the reason the parts crate does: the pack is paid for at buy
        /// time and the director saves immediately, so parking it in transient shop state would let a
        /// quit-then-resume keep the spend and lose the draw.
        /// </summary>
        public List<int> PackComponents = new List<int>();

        /// <summary>True while a paid-for component pack is waiting to be picked from.</summary>
        public bool ComponentPackOpen => PackComponents.Count > 0;

        /// <summary>
        /// Offers drawn by a bought-but-unresolved SPECTRAL pack (doc 08 slice 13 — editions as
        /// materials), each encoded by <see cref="SpectralOffer"/> as "Edition:partId". Strings so
        /// the whole run stays plainly serializable, same rationale as <see cref="PackComponents"/>.
        /// Lives on the run for exactly the reason the parts crate does: paid at buy time, saved
        /// immediately, so a quit-then-resume must not keep the spend and lose the draw.
        /// </summary>
        public List<string> PackSpectrals = new List<string>();

        /// <summary>True while a paid-for Spectral pack is waiting to be picked from.</summary>
        public bool SpectralPackOpen => PackSpectrals.Count > 0;

        /// <summary>True while ANY paid-for pack is unresolved. Blocks the rest of the shop.</summary>
        public bool PackOpen => CrateOpen || ComponentPackOpen || SpectralPackOpen;

        /// <summary>
        /// Editions applied THIS RUN, keyed by <see cref="PartDef.Id"/> (doc 08 slice 13). Editions
        /// must live here and never on the PartDef: parts are shared ScriptableObject assets, so
        /// stamping one at runtime would mutate the asset on disk and leak the upgrade into every
        /// future run. Read through <see cref="EditionOf"/>, which merges in anything the asset was
        /// authored with; persisted by RunSave as "id:Edition" pairs.
        /// </summary>
        public readonly Dictionary<string, PartEdition> PartEditions = new Dictionary<string, PartEdition>();

        /// <summary>
        /// A part's EFFECTIVE edition: the run's applied material when it beats the authored one,
        /// else whatever the asset shipped with (all shipped assets are None today). This is the
        /// value the bake, the sell price and the garage rows all read — one lookup, no disagreement.
        /// </summary>
        public PartEdition EditionOf(PartDef part)
        {
            if (part == null) return PartEdition.None;
            if (!string.IsNullOrEmpty(part.Id)
                && PartEditions.TryGetValue(part.Id, out PartEdition applied)
                && applied > part.Edition)
                return applied;
            return part.Edition;
        }

        /// <summary>
        /// Applies an edition material to an owned part. Materials only ever UPGRADE — applying a
        /// tier at or below the current effective edition is refused, so a Foil can never overwrite
        /// a Polychrome and a duplicate material is never silently swallowed.
        /// </summary>
        public bool TryUpgradeEdition(PartDef part, PartEdition edition)
        {
            if (part == null || string.IsNullOrEmpty(part.Id) || !Owns(part)) return false;
            if (edition <= EditionOf(part)) return false;
            PartEditions[part.Id] = edition;
            return true;
        }

        /// <summary>
        /// Permanent team upgrades bought this run (doc 03's vouchers). Unlike parts these are never
        /// equipped or broken — owning one IS the effect, for the rest of the run. Persisted by name in
        /// RunSave; every effect is computed from this list by <see cref="TeamUpgrades"/>.
        /// </summary>
        public List<TeamUpgrade> OwnedUpgrades = new List<TeamUpgrade>();

        public bool HasUpgrade(TeamUpgrade upgrade) => OwnedUpgrades.Contains(upgrade);

        public bool IsBossRace => RaceIndex >= RacesPerCircuit - 1;

        /// <summary>True once the run reaches the season's last circuit.</summary>
        public bool IsFinalCircuit => CircuitIndex >= TotalCircuits - 1;

        /// <summary>
        /// Races completed so far this run, counting across circuits — 0 on the very first race.
        /// The bot-strength ramp keys off this rather than <see cref="CircuitIndex"/>: bots never buy
        /// parts, so a field that only stepped up per circuit would sit showroom-stock for a whole
        /// season while the player's build pulled away at every garage.
        /// </summary>
        public int RaceNumber => CircuitIndex * RacesPerCircuit + RaceIndex;

        /// <summary>
        /// Per-circuit difficulty scalar and tuning hook: 1.0 on the first circuit, ramping
        /// gently at first then steeper as the season wears on (convex in CircuitIndex).
        /// RunDirector — or a headless server — can multiply payouts / survival expectations by
        /// this without hard-coding a per-circuit table. Wave-1 default (stake 0): 1.0, 1.35, 1.70, ...
        /// The license stake multiplies the whole ramp (<see cref="StakeMult"/>): stake 0 leaves the
        /// sequence untouched, higher stakes lift every circuit uniformly so the existing ramp — and
        /// RunDirector's bot/cutoff scaling that reads this — picks the stake up with no extra wiring.
        /// </summary>
        public float DifficultyMult =>
            (1f + 0.3f * CircuitIndex + 0.05f * CircuitIndex * CircuitIndex) * StakeMult;

        /// <summary>The run is only won after clearing the FINAL race of the FINAL circuit.</summary>
        public bool RunComplete => IsFinalCircuit && RaceIndex >= RacesPerCircuit;
        /// <summary>
        /// Equip slots actually available: the authored <see cref="MaxEquipSlots"/> base plus anything the
        /// Toolbox upgrade has added. Layered on top rather than folded into the field because
        /// MaxEquipSlots is the AUTHORED tuning value — fixtures and a future car/chassis set it directly,
        /// and turning it into a computed property would take that away.
        /// </summary>
        public int EffectiveEquipSlots => MaxEquipSlots + TeamUpgrades.ExtraEquipSlots(this);

        public bool HasFreeSlot => EquippedParts.Count < EffectiveEquipSlots;

        public bool Owns(PartDef part) => OwnedParts.Contains(part);
        public bool IsEquipped(PartDef part) => EquippedParts.Contains(part);

        /// <summary>Slots an owned part if a slot is free. False if unowned, duplicate, or full.</summary>
        public bool Equip(PartDef part)
        {
            if (part == null || !Owns(part) || IsEquipped(part) || !HasFreeSlot) return false;
            EquippedParts.Add(part);
            return true;
        }

        public bool Unequip(PartDef part) => EquippedParts.Remove(part);

        /// <summary>
        /// Removes a part from the run entirely — both the equipped slot (if slotted) and the owned
        /// pool. Parts are unique instances in the pool, so dropping the PartDef reference is a clean
        /// delete. Returns true if the part was owned (and thus removed). Used when a Fragile part
        /// breaks under heavy race damage (RunDirector); safe on null or an unowned part.
        /// </summary>
        public bool RemovePart(PartDef part)
        {
            if (part == null) return false;
            EquippedParts.Remove(part);
            // The material leaves with the part: selling (or breaking) an editioned part forfeits
            // its edition — the sale already priced it in — and a later re-acquisition of the same
            // unique must come back plain rather than remembering a bonus that was paid out.
            if (!string.IsNullOrEmpty(part.Id)) PartEditions.Remove(part.Id);
            return OwnedParts.Remove(part);
        }

        /// <summary>
        /// Total end-of-run refund from owned Cashout parts: the Price of every owned part tagged
        /// PartCondition.Cashout, whether equipped or not (refund-if-KEPT — you get the money back
        /// for holding onto them to the end). RunDirector folds this into final Money when the run
        /// terminates. 0 when no Cashout parts are held.
        /// </summary>
        public int CashoutRefundTotal()
        {
            int total = 0;
            foreach (PartDef part in OwnedParts)
                if (part != null && part.Condition == PartCondition.Cashout)
                    total += part.Price;
            return total;
        }

        /// <summary>
        /// Pure cash cost to fully repair a car sitting at <paramref name="carDurability"/>, given the
        /// full-repair price and an optional damage-curve exponent — the formula behind RunDirector's
        /// garage REPAIR button, lifted here as a static, engine-loop-free helper so the economy is
        /// unit-testable without a scene. 0 when pristine, at least $1 for any wear at all, up to
        /// <paramref name="fullRepairCost"/> when battered all the way to the durability floor.
        ///
        /// The shipped default <paramref name="damageExponent"/> = 1 keeps the cost LINEAR in normalized
        /// wear and skips the exponent entirely, so it reproduces the number the garage has always
        /// charged bit-for-bit. An exponent above 1 makes deep damage cost proportionally more (a convex
        /// money sink); below 1 front-loads light damage. The exponent only reshapes the curve between
        /// the endpoints — pristine still costs $0 and the durability floor still costs fullRepairCost.
        /// </summary>
        public static int RepairCostFor(float carDurability, int fullRepairCost, float damageExponent = 1f)
        {
            float wear = 1f - carDurability;                     // 0 (pristine) .. (1 - MinDurability) at the floor
            if (wear <= 0f) return 0;
            float span = 1f - VehicleSim.MinDurability;          // total wear span from pristine to the floor
            float t = span > 0f ? Mathf.Clamp01(wear / span) : 1f;
            // Default exponent 1 short-circuits Pow so the priced value is the exact shipped expression.
            float shaped = damageExponent == 1f ? t : Mathf.Pow(t, Mathf.Max(0f, damageExponent));
            return Mathf.Max(1, Mathf.CeilToInt(fullRepairCost * shaped)); // any wear costs at least $1
        }

        // ---- Economy depth (Balatro-style): shop interest + reroll escalation --------------------
        // All tunables default so the run plays and pays exactly as shipped: interest rate 0 pays
        // nothing, and the reroll increment 0 leaves ShopLogic's +$1/reroll curve untouched. These
        // are additive, opt-in hooks — no existing money path is altered except ApplyShopInterest,
        // which is an explicit step a caller must invoke to grant interest.

        /// <summary>$ interest paid per whole block of banked money at shop time. 0 (default) = no interest.</summary>
        public int InterestPerBlock;

        /// <summary>$ of banked money per interest block (Balatro's "per $5"). Shipped default block size.</summary>
        public int InterestBlockSize = ShopEconomy.DefaultInterestBlockSize;

        /// <summary>Cap on interest paid in a single shop. int.MaxValue (default) = uncapped.</summary>
        public int InterestCap = int.MaxValue;

        /// <summary>Base cost of the first reroll of a visit (mirrors ShopLogic's shipped base).</summary>
        public int RerollBaseCost = ShopLogic.BaseRerollCost;

        /// <summary>
        /// Extra per-reroll escalation layered on top of <see cref="ShopLogic.RerollCostStep"/>.
        /// 0 (default) reproduces the shipped +$1/reroll curve. Mirrors ShopLogic.RerollCostIncrement
        /// so a headless economy driver can compute reroll costs off RunState alone.
        /// </summary>
        public int RerollCostIncrement;

        /// <summary>Rerolls bought so far this garage visit (transient; not persisted).</summary>
        private int _rerollsThisVisit;

        /// <summary>Rerolls bought so far this garage visit.</summary>
        public int RerollsThisVisit => _rerollsThisVisit;

        /// <summary>
        /// Interest the current banked <see cref="Money"/> would earn at shop time, per the interest
        /// tunables. Pure — computes without mutating. 0 unless InterestPerBlock is raised.
        /// </summary>
        public int ShopInterest() =>
            ShopEconomy.Interest(Money, InterestPerBlock, InterestBlockSize, InterestCap);

        /// <summary>
        /// Grants <see cref="ShopInterest"/> by ADDING it to <see cref="Money"/> and returning the
        /// amount granted. An explicit, opt-in step: nothing calls it automatically, so leaving the
        /// interest rate at 0 (or never invoking it) keeps the shipped economy exactly. Call it once
        /// when a garage opens to pay Balatro-style interest on hoarded cash.
        /// </summary>
        public int ApplyShopInterest()
        {
            int bonus = ShopInterest();
            Money += bonus;
            return bonus;
        }

        /// <summary>
        /// Cost of the next reroll given how many have been bought this visit, routed through
        /// <see cref="ShopEconomy.RerollCost"/>. The effective per-reroll step is the shipped
        /// <see cref="ShopLogic.RerollCostStep"/> plus <see cref="RerollCostIncrement"/>, so with the
        /// default increment 0 it matches the live shop's curve (base, base+step, base+2*step, ...).
        /// </summary>
        public int NextRerollCost() => ShopEconomy.RerollCost(
            RerollBaseCost, _rerollsThisVisit, ShopLogic.RerollCostStep + RerollCostIncrement);

        /// <summary>Resets the per-visit reroll counter — call when a fresh garage visit opens.</summary>
        public void ResetRerollCounter() => _rerollsThisVisit = 0;

        /// <summary>
        /// Charges <see cref="NextRerollCost"/> against <see cref="Money"/> and advances the per-visit
        /// counter (so the next reroll costs more). Returns the amount charged, or -1 if unaffordable
        /// (in which case nothing is charged and the counter is untouched). A standalone economy API;
        /// the live garage reroll runs through <see cref="ShopLogic"/> instead.
        /// </summary>
        public int ChargeReroll()
        {
            int cost = NextRerollCost();
            if (Money < cost) return -1;
            Money -= cost;
            _rerollsThisVisit++;
            return cost;
        }
    }
}
