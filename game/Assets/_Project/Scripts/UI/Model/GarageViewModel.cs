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

        // --- Shelf / crate -----------------------------------------------------------------------
        /// <summary>An open crate REPLACES the shelf — Offers is empty while a crate is open.</summary>
        public bool CrateOpen => Run.CrateOpen;
        public IReadOnlyList<OfferVm> Offers => _offers;
        public IReadOnlyList<OfferVm> CrateContents => _crate;
        public int CratePrice => _host.CratePrice;
        public int CrateDrawCount => _host.CrateDrawCount;
        public bool CanAffordCrate => Run.Money >= _host.CratePrice;
        public int RerollCost => _host.Shop.RerollCost;
        public bool CanAffordReroll => Run.Money >= _host.Shop.RerollCost;

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
        public bool Reroll() => Mutate(_host.RerollShop());
        public bool BuyCrate() => Mutate(_host.BuyCrate());
        public bool TakeFromCrate(PartDef part) => Mutate(_host.TakeFromCrate(part));
        public bool BuyUpgrade(TeamUpgrade upgrade) => Mutate(_host.BuyUpgrade(upgrade));
        public bool Repair() => Mutate(_host.RepairCar());
        public bool Equip(PartDef part) => Mutate(Run.Equip(part));
        public bool Unequip(PartDef part) => Mutate(Run.Unequip(part));
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
            VehicleSpec baseSpec = _host.BaseSpec;
            VehicleSpec current = baseSpec != null ? SpecModApplier.Apply(baseSpec, Run.EquippedParts) : null;
            Current = current != null ? StatSummary.Compute(current) : default;

            _offers.Clear();
            if (!Run.CrateOpen)
                foreach (PartDef part in _host.Shop.Offers)
                    _offers.Add(BuildOffer(part, current));

            _crate.Clear();
            if (Run.CrateOpen)
                foreach (PartDef part in Run.CrateContents)
                    _crate.Add(BuildOffer(part, current));

            _owned.Clear();
            foreach (PartDef part in Run.OwnedParts)
            {
                if (!part) continue;
                bool equipped = Run.IsEquipped(part);
                _owned.Add(new OwnedPartVm(part, part.DisplayName, part.Category, part.Edition,
                    PartDisplay.EditionTag(part.Edition), equipped, !equipped && Run.HasFreeSlot));
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
        private OfferVm BuildOffer(PartDef part, VehicleSpec current)
        {
            int price = _host.Shop.PriceOf(part);
            bool hasPreview = part && part.Category == PartCategory.Stat && current != null;

            StatDelta grip = default;
            StatDelta power = default;
            if (hasPreview)
            {
                StatSummary.Stats before = StatSummary.Compute(current);
                StatSummary.Stats after = StatSummary.Compute(SpecModApplier.Apply(current, new[] { part }));
                grip = new StatDelta(before.Grip, after.Grip);
                power = new StatDelta(before.Power, after.Power);
            }

            return new OfferVm(part, part ? part.DisplayName : "", part ? part.Category : default, price,
                part ? part.Edition : PartEdition.None, part ? PartDisplay.EditionTag(part.Edition) : "",
                part ? part.Description : "", Run.Money >= price, hasPreview, grip, power);
        }
    }
}
