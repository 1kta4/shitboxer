using System;
using System.Collections.Generic;
using Shitboxer.Meta;
using Shitboxer.Race;
using Shitboxer.Vehicle;
using UnityEngine;

namespace Shitboxer.UI
{
    /// <summary>
    /// A canned <see cref="IRunHost"/> for the standalone UI gallery — no run, no scene, no RunDirector.
    /// Lets the garage be looked at in isolation with representative data, and driven (buy/equip/reroll)
    /// through the real ShopLogic/RunState rules. Runtime-only (creates its own PartDef instances), so it
    /// does not touch the project's authored assets.
    /// </summary>
    public sealed class GalleryHost : IRunHost
    {
        private readonly List<PartDef> _pool;

        public RunState Run { get; } = new RunState();
        public ShopLogic Shop { get; } = new ShopLogic(1);
        public MetaProgress Meta { get; } = new MetaProgress();
        public RunPhase Phase => RunPhase.Garage;
        public string LastRaceSummary => "";
        public VehicleSpec BaseSpec { get; }
        public int RepairCost => 6;
        public int CratePrice => 6;
        public int CrateDrawCount => 3;
        public int PayoutPreviewFor(int position) => 0;
        public RaceManager CurrentRace => null;
        public VehicleController PlayerCar => null;
        public SectorPartRunner SectorParts => null;   // gallery preview, never a live race
        public ActiveReadout ActiveItem => default;    // no live race, so no charge meter either
        public event Action<RunPhase> PhaseChanged { add { } remove { } }

        public GalleryHost()
        {
            Run.Money = 15;
            Run.Lives = 3;
            Run.CarDurability = 0.72f;

            BaseSpec = new VehicleSpec();
            BaseSpec.FrontTyre.PeakMu = 1.2f;
            BaseSpec.RearTyre.PeakMu = 1.2f;
            BaseSpec.Engine.PeakTorqueNm = 205f;

            PartDef owned = StatPart("Sticky Compound", "Softer rubber, more bite in the corners.");
            Run.OwnedParts.Add(owned);
            Run.Equip(owned);
            Run.OwnedParts.Add(EconomyPart("Pizza Sponsor", "A takeaway slaps a sticker on the door. +$1 per finishing position."));

            _pool = new List<PartDef>
            {
                StatPart("Junkyard Turbo", "Boost from a scrapyard. More Power, maybe less reliability."),
                EconomyPart("Broadcast Deal", "The network pays per second you're on screen scrapping in the pack."),
                StatPart("Race Slicks", "Grip you can feel. Wears fast."),
            };
            Shop.BeginVisit(_pool, Run, 1);
        }

        private static PartDef StatPart(string name, string desc)
        {
            var p = ScriptableObject.CreateInstance<PartDef>();
            p.DisplayName = name;
            p.Description = desc;
            p.Category = PartCategory.Stat;
            p.Price = 5;
            p.SpecMods = new List<SpecMod>
            {
                new SpecMod { Target = SpecModTarget.GripFront, Multiplier = 1.15f, Op = SpecModOp.Multiply },
                new SpecMod { Target = SpecModTarget.GripRear, Multiplier = 1.15f, Op = SpecModOp.Multiply },
            };
            return p;
        }

        private static PartDef EconomyPart(string name, string desc)
        {
            var p = ScriptableObject.CreateInstance<PartDef>();
            p.DisplayName = name;
            p.Description = desc;
            p.Category = PartCategory.Economy;
            p.Price = 3;
            return p;
        }

        public bool BuyOffer(PartDef part) => Shop.TryBuy(part, Run);
        public bool RerollShop() => Shop.TryReroll(_pool, Run);
        public bool SellPart(PartDef part) => Shop.TrySell(part, Run);
        public bool BuyCrate() => Shop.TryBuyCrate(_pool, Run, CratePrice, CrateDrawCount);
        public bool TakeFromCrate(PartDef part) => Shop.TryTakeFromCrate(part, Run);
        public bool BuyPack(int packIndex) => Shop.TryBuyPack(packIndex, _pool, Run);
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

        public void StartNextRace() { }
        public void StartNewRun() { }
        public void QuitToMenu() { }
    }
}
