using System;
using System.Collections.Generic;
using Shitboxer.Meta;
using Shitboxer.Race;
using Shitboxer.Vehicle;

namespace Shitboxer.Tests
{
    /// <summary>
    /// In-memory <see cref="IRunHost"/> for ViewModel fixtures — no scene, no MonoBehaviour. The shop
    /// commands delegate to the SAME plain <see cref="ShopLogic"/> / <see cref="RunState"/> the real
    /// RunDirector uses, so a test drives the shelf, crate, reroll and upgrades through exactly the
    /// shipped rules. The non-shop commands (repair, start-race) get a minimal fake or a call count,
    /// since those live on the director rather than the shop.
    /// </summary>
    internal sealed class FakeRunHost : IRunHost
    {
        public RunState Run { get; set; } = new RunState();
        public ShopLogic Shop { get; set; } = new ShopLogic(seed: 1);
        public MetaProgress Meta { get; set; } = new MetaProgress();
        public RunPhase Phase { get; private set; } = RunPhase.Garage;
        public string LastRaceSummary { get; set; } = "";
        public VehicleSpec BaseSpec { get; set; }

        public int RepairCost { get; set; } = 6;
        public int CratePrice { get; set; } = 6;
        public int CrateDrawCount { get; set; } = 3;

        /// <summary>Pool the fake reroll / crate draw from. Populate it before exercising those commands.</summary>
        public List<PartDef> Pool { get; set; } = new List<PartDef>();

        public Func<int, int> PayoutPreview { get; set; }
        public int PayoutPreviewFor(int position) => PayoutPreview?.Invoke(position) ?? 0;

        public RaceManager CurrentRace => null;
        public VehicleController PlayerCar => null;
        public SectorPartRunner SectorParts => null;   // no race, so nothing scores sectors
        public ActiveReadout ActiveItem => default;    // no race, so no live charge meter either

        public event Action<RunPhase> PhaseChanged;

        public void SetPhase(RunPhase phase)
        {
            if (Phase == phase) return;
            Phase = phase;
            PhaseChanged?.Invoke(phase);
        }

        public int StartNextRaceCalls { get; private set; }
        public int StartNewRunCalls { get; private set; }
        public int QuitToMenuCalls { get; private set; }

        public bool BuyOffer(PartDef part) => Shop.TryBuy(part, Run);
        public bool RerollShop() => Shop.TryReroll(Pool, Run);
        public bool SellPart(PartDef part) => Shop.TrySell(part, Run);
        public bool BuyCrate() => Shop.TryBuyCrate(Pool, Run, CratePrice, CrateDrawCount);
        public bool TakeFromCrate(PartDef part) => Shop.TryTakeFromCrate(part, Run);
        public bool BuyPack(int packIndex) => Shop.TryBuyPack(packIndex, Pool, Run);
        public bool TakeComponent(CarComponent component) => Shop.TryTakeComponent(component, Run);
        public bool TakeSpectral(PartDef part, PartEdition edition) => Shop.TryTakeSpectral(part, edition, Run);
        public bool BuyBlueprint(CarComponent component) => Shop.TryBuyBlueprint(component, Run);
        public bool BuyUpgrade(TeamUpgrade upgrade) => Shop.TryBuyUpgrade(upgrade, Run);

        public bool RepairCar()
        {
            if (Run.CarDurability >= 1f || Run.Money < RepairCost) return false;
            Run.Money -= RepairCost;
            Run.CarDurability = 1f;
            return true;
        }

        public void StartNextRace() => StartNextRaceCalls++;
        public void StartNewRun() => StartNewRunCalls++;
        public void QuitToMenu() => QuitToMenuCalls++;
    }
}
