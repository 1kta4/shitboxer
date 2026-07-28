using System.Collections.Generic;
using Shitboxer.Meta;
using Shitboxer.Vehicle;

namespace Shitboxer.UI.Model
{
    /// <summary>
    /// The garage screen's entire logical surface, as plain assertable state — no VisualElement, no
    /// OnGUI, no scene. It reads an <see cref="IRunHost"/> (the real RunDirector in game, a fake in
    /// tests) and exposes the shop shelf, crate, reroll, team upgrades, owned parts, repair and the
    /// live GRIP/POWER preview. Commands delegate to the host, then <see cref="Rebuild"/> re-reads and
    /// raises <see cref="Changed"/>: the view re-reads on that signal and never polls.
    ///
    /// This VM takes LIVE objects (RunState/ShopLogic), not a snapshot, because they are already plain
    /// C# the tests construct directly — snapshotting them would just fork the shop's rules. (The race
    /// HUD is the opposite case and takes a snapshot, because RaceCarStatus is internal-set.)
    /// </summary>
    public sealed class GarageViewModel
    {
        private readonly IRunHost _host;
        private readonly List<OfferVm> _offers = new List<OfferVm>();
        private readonly List<OfferVm> _crate = new List<OfferVm>();
        private readonly List<OwnedPartVm> _owned = new List<OwnedPartVm>();
        private readonly List<UpgradeVm> _availableUpgrades = new List<UpgradeVm>();
        private readonly List<UpgradeVm> _ownedUpgrades = new List<UpgradeVm>();
        private readonly List<PackVm> _packs = new List<PackVm>();
        private readonly List<ComponentVm> _components = new List<ComponentVm>();
        private readonly List<ComponentVm> _blueprints = new List<ComponentVm>();
        private readonly List<ComponentVm> _packComponents = new List<ComponentVm>();
        private readonly List<SpectralVm> _packSpectrals = new List<SpectralVm>();

        public GarageViewModel(IRunHost host)
        {
            _host = host;
            Rebuild();
        }

        /// <summary>Raised after any command mutates run state. The view re-reads; it never polls.</summary>
        public event System.Action Changed;

        private RunState Run => _host.Run;

        // --- Run status (titlebar) ---------------------------------------------------------------
        public int Money => Run.Money;
        public int Lives => Run.Lives;
        public bool IsBossRace => Run.IsBossRace;
        public string CircuitLine => $"CIRCUIT {Run.CircuitIndex + 1}/{Run.TotalCircuits}";

        /// <summary>The race about to be run, e.g. "race 3/5" or "BOSS race 5/5 (top 3 required)".</summary>
        public string NextRaceLine => Run.IsBossRace
            ? $"BOSS race {Run.RaceIndex + 1}/{Run.RacesPerCircuit} (top {Run.BossTopN} required)"
            : $"race {Run.RaceIndex + 1}/{Run.RacesPerCircuit}";

        public string LastRaceSummary => _host.LastRaceSummary;

        // --- Stat bars ---------------------------------------------------------------------------
        /// <summary>False only when there is no base spec to read (a bare race scene). Never false
        /// mid-run, since wave 23 captures BaseSpec unconditionally at scene bind.</summary>
        public bool HasStatPreview => _host.BaseSpec != null;

        /// <summary>Headline stats for the equipped set the player would take into the next race.</summary>
        public StatSummary.Stats Current { get; private set; }

        // --- Repair ------------------------------------------------------------------------------
        public bool RepairAvailable => Run.CarDurability < 1f;
        public int RepairCost => _host.RepairCost;
        public bool CanAffordRepair => Run.Money >= _host.RepairCost;
        public string RepairLabel => $"REPAIR CAR (${_host.RepairCost}) — durability {Run.CarDurability * 100f:0}%";

        // --- Shelf / packs -------------------------------------------------------------------------
        /// <summary>An open pack REPLACES the shelf — Offers is empty while any pack is open.</summary>
        public bool CrateOpen => Run.CrateOpen;

        /// <summary>True while an open COMPONENTS pack is waiting to be picked from.</summary>
        public bool ComponentPackOpen => Run.ComponentPackOpen;

        /// <summary>True while an open SPECTRAL pack is waiting to be picked from (doc 08 slice 13).</summary>
        public bool SpectralPackOpen => Run.SpectralPackOpen;

        /// <summary>True while any paid-for pack is unresolved. The shelf is hidden until it is.</summary>
        public bool PackOpen => Run.PackOpen;

        public IReadOnlyList<OfferVm> Offers => _offers;
        public IReadOnlyList<OfferVm> CrateContents => _crate;

        /// <summary>This visit's two booster packs.</summary>
        public IReadOnlyList<PackVm> Packs => _packs;

        /// <summary>The components an open components pack is offering.</summary>
        public IReadOnlyList<ComponentVm> PackComponents => _packComponents;

        /// <summary>The edition materials an open Spectral pack is offering, each pre-aimed at a fitted part.</summary>
        public IReadOnlyList<SpectralVm> PackSpectrals => _packSpectrals;

        public int CratePrice => _host.CratePrice;
        public int CrateDrawCount => _host.CrateDrawCount;
        public bool CanAffordCrate => Run.Money >= _host.CratePrice;
        public int RerollCost => _host.Shop.RerollCost;
        public bool CanAffordReroll => Run.Money >= _host.Shop.RerollCost;

        /// <summary>
        /// Why the shelf's BUY buttons are off, when they are — a full car rather than empty pockets.
        /// The screen has to say which, or "I have money and it won't let me buy" reads as a bug.
        /// </summary>
        public bool CarIsFull => !Run.HasFreeSlot;

        /// <summary>Carried durability as a 0-100 readout for the rail's HULL line — the companion of
        /// the RepairAvailable/RepairCost/CanAffordRepair trio above.</summary>
        public int DurabilityPercent
        {
            get
            {
                float d = Run.CarDurability;
                if (d < 0f) d = 0f; else if (d > 1f) d = 1f;
                return (int)System.Math.Round(d * 100f);
            }
        }

        // --- Components ----------------------------------------------------------------------------
        /// <summary>
        /// All ten components with their current level, in family order — a STATUS list, not a shop.
        /// Nothing here is buyable: to raise a component you buy one of the <see cref="Blueprints"/>
        /// that turned up, or pick one out of a components pack.
        /// </summary>
        public IReadOnlyList<ComponentVm> Components => _components;

        /// <summary>
        /// The Blueprints on this visit's shelf, priced at each component's next level. Empty while a
        /// pack is open (the pick replaces the shelf) and late in a run once components are maxed out.
        /// </summary>
        public IReadOnlyList<ComponentVm> Blueprints => _blueprints;

        // --- Team upgrades -----------------------------------------------------------------------
        public IReadOnlyList<UpgradeVm> AvailableUpgrades => _availableUpgrades;
        public IReadOnlyList<UpgradeVm> OwnedUpgrades => _ownedUpgrades;

        // --- Owned parts -------------------------------------------------------------------------
        public IReadOnlyList<OwnedPartVm> OwnedParts => _owned;
        public int SlotsUsed => Run.EquippedParts.Count;
        public int SlotsTotal => Run.EffectiveEquipSlots;
        public string SlotLine => $"{SlotsUsed}/{SlotsTotal} slots used";

        // --- Commands: delegate, Rebuild, raise Changed only on a real mutation -------------------
        public bool Buy(PartDef part) => Mutate(_host.BuyOffer(part));
        public bool Sell(PartDef part) => Mutate(_host.SellPart(part));
        public bool Reroll() => Mutate(_host.RerollShop());
        public bool BuyCrate() => Mutate(_host.BuyCrate());
        public bool BuyPack(int packIndex) => Mutate(_host.BuyPack(packIndex));
        public bool TakeFromCrate(PartDef part) => Mutate(_host.TakeFromCrate(part));
        public bool TakeComponent(CarComponent component) => Mutate(_host.TakeComponent(component));
        public bool TakeSpectral(SpectralVm pick) => Mutate(_host.TakeSpectral(pick.Part, pick.Edition));
        public bool BuyBlueprint(CarComponent component) => Mutate(_host.BuyBlueprint(component));
        public bool BuyUpgrade(TeamUpgrade upgrade) => Mutate(_host.BuyUpgrade(upgrade));
        public bool Repair() => Mutate(_host.RepairCar());
        public void NextRace() => _host.StartNextRace();

        private bool Mutate(bool changed)
        {
            if (changed)
            {
                Rebuild();
                Changed?.Invoke();
            }
            return changed;
        }

        /// <summary>Recompute every list and the stat preview from the host. Ctor + every command.</summary>
        public void Rebuild()
        {
            // Components set the baseline, parts bolt on over the top — the same order the director
            // bakes the real car in, so the preview cannot disagree with what you drive.
            VehicleSpec baseSpec = _host.BaseSpec;
            VehicleSpec current = baseSpec != null
                ? SpecModApplier.Apply(StatLedger.Bake(baseSpec, Run.ComponentLedger()), Run.EquippedParts, Run.EditionOf)
                : null;
            Current = current != null ? StatSummary.Compute(current) : default;

            _offers.Clear();
            if (!Run.PackOpen)
                foreach (PartDef part in _host.Shop.Offers)
                    _offers.Add(BuildOffer(part, current));

            _crate.Clear();
            if (Run.CrateOpen)
                foreach (PartDef part in Run.CrateContents)
                    _crate.Add(BuildOffer(part, current, alreadyPaid: true));

            _packs.Clear();
            if (!Run.PackOpen)
                for (int i = 0; i < _host.Shop.Packs.Count; i++)
                {
                    ShopPack pack = _host.Shop.Packs[i];
                    bool affordable = Run.Money >= pack.Price;
                    // Mirror every refusal TryBuyPack itself would make, so a blocked card can SAY the
                    // rule instead of silently eating the click: a parts pack needs a free slot (a
                    // bought part is always fitted), a components pack needs a levellable component,
                    // a Spectral pack needs a fitted part that can still take a material.
                    bool eligible;
                    string blocked;
                    switch (pack.Kind)
                    {
                        case ShopPackKind.Parts:
                            eligible = Run.HasFreeSlot;
                            blocked = "CAR FULL — SELL ONE";
                            break;
                        case ShopPackKind.Components:
                            eligible = ShopLogic.AnyComponentLevellable(Run);
                            blocked = "ALL COMPONENTS MAXED";
                            break;
                        case ShopPackKind.Spectral:
                            eligible = ShopLogic.AnySpectralTarget(Run);
                            blocked = "NO PART CAN TAKE ONE";
                            break;
                        default:
                            eligible = false;
                            blocked = "NOT FOR SALE";
                            break;
                    }
                    bool buyable = affordable && eligible;
                    string reason = buyable ? null : !eligible ? blocked : "NO FUNDS";
                    _packs.Add(new PackVm(i, pack.Kind, pack.DisplayName, pack.Price, pack.DrawCount,
                        affordable, buyable, reason));
                }

            _packComponents.Clear();
            if (Run.ComponentPackOpen)
                foreach (int ordinal in Run.PackComponents)
                    _packComponents.Add(BuildComponent((CarComponent)ordinal, free: true));

            // Spectral picks (doc 08 slice 13): resolve each stored "Edition:partId" offer against
            // the owned list. An offer whose target has vanished mid-pack is skipped here as a
            // display guard, but TrySell already purges those, so normally all resolve.
            _packSpectrals.Clear();
            if (Run.SpectralPackOpen)
                foreach (string encoded in Run.PackSpectrals)
                {
                    if (!SpectralOffer.TryDecode(encoded, out PartEdition edition, out string partId)) continue;
                    PartDef target = Run.OwnedParts.Find(p => p != null && p.Id == partId);
                    if (target == null) continue;
                    _packSpectrals.Add(new SpectralVm(target, edition,
                        $"{PartDisplay.EditionTag(edition)} → {target.DisplayName.ToUpperInvariant()}"));
                }

            // The ten-row list is a read-out of the car, so it stays visible even mid-pack — unlike the
            // Blueprint shelf, which is stock and hides behind an open pack like every other offer.
            _components.Clear();
            foreach (CarComponentInfo info in CarComponentCatalog.All)
                _components.Add(BuildComponent(info.Component, free: false));

            _blueprints.Clear();
            if (!Run.PackOpen)
                foreach (CarComponent component in _host.Shop.Blueprints)
                    _blueprints.Add(BuildComponent(component, free: false));

            _owned.Clear();
            foreach (PartDef part in Run.OwnedParts)
            {
                if (!part) continue;
                // EFFECTIVE edition (run material beats the authored one) drives both the row tag
                // and the refund — the fitted list must show the same number the sale pays.
                PartEdition edition = Run.EditionOf(part);
                _owned.Add(new OwnedPartVm(part, part.DisplayName, part.Category, edition,
                    PartDisplay.EditionTag(edition), Run.IsEquipped(part),
                    _host.Shop.SellValueOf(part, Run)));
            }

            _availableUpgrades.Clear();
            _ownedUpgrades.Clear();
            foreach (TeamUpgrade upgrade in TeamUpgrades.All)
            {
                TeamUpgradeInfo info = TeamUpgrades.Info(upgrade);
                var vm = new UpgradeVm(upgrade, info.DisplayName, info.Description, info.Price,
                    Run.Money >= info.Price);
                if (Run.HasUpgrade(upgrade)) _ownedUpgrades.Add(vm);
                else _availableUpgrades.Add(vm);
            }
        }

        // Both the shelf and the crate are built the same way — a priced row with a stat preview for
        // Stat parts, computed by applying the single part ON TOP of the current equipped set (Apply
        // clones internally, so run state is never touched). Identical logic to the old
        // GarageScreen.DrawOffer/DrawStatPreview, minus the drawing.
        /// <summary>
        /// One component row. <paramref name="free"/> marks a pick from an already-paid-for pack, where
        /// the price is 0 and affordability is irrelevant — otherwise it prices the next Blueprint.
        /// </summary>
        private ComponentVm BuildComponent(CarComponent component, bool free)
        {
            CarComponentInfo info = CarComponentCatalog.Info(component);
            int level = Run.LevelOf(component);
            bool canLevel = CarComponentCatalog.CanLevel(level);
            int price = free ? 0 : CarComponentCatalog.BlueprintPrice(level);

            return new ComponentVm(component, info.DisplayName, info.Description, info.Family,
                level, CarComponentCatalog.MaxLevel, price,
                free || Run.Money >= price, canLevel);
        }

        /// <param name="alreadyPaid">
        /// True for a pack pick, which was paid for when the pack was bought — so only the free-slot
        /// half of the gate applies. Charging the part's price again would grey out a pick the player
        /// has already bought.
        /// </param>
        private OfferVm BuildOffer(PartDef part, VehicleSpec current, bool alreadyPaid = false)
        {
            int price = _host.Shop.PriceOf(part);
            bool hasPreview = part && part.Category == PartCategory.Stat && current != null;

            StatDelta grip = default, power = default, weight = default, durability = default;
            if (hasPreview)
            {
                StatSummary.Stats before = StatSummary.Compute(current);
                StatSummary.Stats after = StatSummary.Compute(SpecModApplier.Apply(current, new[] { part }));
                grip = new StatDelta(before.Grip, after.Grip);
                power = new StatDelta(before.Power, after.Power);
                weight = new StatDelta(before.Weight, after.Weight);
                durability = new StatDelta(before.Durability, after.Durability);
            }

            // Affordable means BUYABLE: a bought part is always fitted, so a full car blocks the
            // purchase just as surely as an empty wallet.
            bool buyable = Run.HasFreeSlot && (alreadyPaid || Run.Money >= price);

            return new OfferVm(part, part ? part.DisplayName : "", part ? part.Category : default, price,
                part ? part.Edition : PartEdition.None, part ? PartDisplay.EditionTag(part.Edition) : "",
                part ? part.Description : "", buyable, hasPreview, grip, power, weight, durability);
        }
    }
}
