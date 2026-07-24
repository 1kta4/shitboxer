using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Shitboxer.Meta;
using Shitboxer.UI.Model;
using Shitboxer.Vehicle;
using UnityEngine;

namespace Shitboxer.Tests
{
    /// <summary>
    /// The garage's whole logical surface, now assertable with no scene and no OnGUI. Drives a
    /// <see cref="GarageViewModel"/> through a <see cref="FakeRunHost"/> whose shop commands run the
    /// shipped ShopLogic/RunState rules. This is what converts the garage from "verified manually in
    /// play" into state — affordability gates, crate-replaces-shelf, slot gating, the Changed signal,
    /// and the wave-23 regression (a run that STARTS with a stat part equipped must still preview).
    /// </summary>
    public class GarageViewModelTests : TestBase
    {
        private static PartDef Part(PartCategory category, string name, int price = 5)
        {
            var p = ScriptableObject.CreateInstance<PartDef>();
            p.DisplayName = name;
            p.Category = category;
            p.Price = price;
            return p;
        }

        private static PartDef GripPart(string name = "Sticky Compound")
        {
            var p = Part(PartCategory.Stat, name);
            p.SpecMods = new List<SpecMod>
            {
                new SpecMod { Target = SpecModTarget.GripFront, Multiplier = 1.3f, Op = SpecModOp.Multiply },
                new SpecMod { Target = SpecModTarget.GripRear, Multiplier = 1.3f, Op = SpecModOp.Multiply },
            };
            return p;
        }

        private static VehicleSpec CarSpec()
        {
            var spec = new VehicleSpec();
            spec.FrontTyre.PeakMu = 1.2f;
            spec.RearTyre.PeakMu = 1.2f;
            spec.Engine.PeakTorqueNm = 200f;
            return spec;
        }

        /// <summary>A host whose shelf is stocked from a pool via the real BeginVisit draw.</summary>
        private static FakeRunHost HostWithShelf(out List<PartDef> pool, int money = 1000)
        {
            var host = new FakeRunHost();
            host.Run.Money = money;
            pool = new List<PartDef> { Part(PartCategory.Economy, "Sponsor A", 4), Part(PartCategory.Economy, "Sponsor B", 7) };
            host.Pool = pool;
            host.Shop.BeginVisit(pool, host.Run, seed: 1);
            return host;
        }

        // --- Shelf ---------------------------------------------------------------------------------

        [Test]
        public void Offers_MirrorTheShopShelf_AtTheShopPrice()
        {
            FakeRunHost host = HostWithShelf(out _);
            var vm = new GarageViewModel(host);

            Assert.That(vm.Offers.Count, Is.EqualTo(host.Shop.Offers.Count));
            for (int i = 0; i < vm.Offers.Count; i++)
            {
                PartDef part = host.Shop.Offers[i];
                Assert.That(vm.Offers[i].Name, Is.EqualTo(part.DisplayName));
                // The price is the SHOP price, not part.Price — derived from the source so it holds
                // whether or not the rarity/family multipliers are active.
                Assert.That(vm.Offers[i].Price, Is.EqualTo(host.Shop.PriceOf(part)));
            }
        }

        [Test]
        public void Offer_Affordable_TracksMoney()
        {
            FakeRunHost host = HostWithShelf(out _);
            var vm = new GarageViewModel(host);
            int price = vm.Offers[0].Price;

            host.Run.Money = price - 1;
            vm.Rebuild();
            Assert.That(vm.Offers[0].Affordable, Is.False);

            host.Run.Money = price;
            vm.Rebuild();
            Assert.That(vm.Offers[0].Affordable, Is.True);
        }

        [Test]
        public void Buy_LeavesShelf_MovesToOwned_AndAutoEquips()
        {
            FakeRunHost host = HostWithShelf(out _);
            var vm = new GarageViewModel(host);
            PartDef bought = host.Shop.Offers[0];

            Assert.That(vm.Buy(bought), Is.True);
            Assert.That(host.Run.OwnedParts, Does.Contain(bought));
            Assert.That(host.Run.IsEquipped(bought), Is.True);
            Assert.That(vm.Offers.Any(o => o.Part == bought), Is.False, "a bought part leaves the shelf");
        }

        // --- Crate replaces the shelf --------------------------------------------------------------

        [Test]
        public void OpenCrate_ReplacesTheShelf()
        {
            FakeRunHost host = HostWithShelf(out _);
            host.Run.CrateContents.Add(Part(PartCategory.Stat, "Crate Turbo"));
            var vm = new GarageViewModel(host);

            Assert.That(vm.CrateOpen, Is.True);
            Assert.That(vm.Offers, Is.Empty, "an open crate must hide the shelf");
            Assert.That(vm.CrateContents.Count, Is.EqualTo(1));
            Assert.That(vm.CrateContents[0].Name, Is.EqualTo("Crate Turbo"));
        }

        // --- Owned parts + equip gating ------------------------------------------------------------

        [Test]
        public void FittedPartsOfferASellValue()
        {
            // There is no EQUIP action any more — a bought part is always fitted, so the only thing the
            // list can do to a part is sell it, which is also the only way to free a slot.
            var host = new FakeRunHost();
            host.Run.MaxEquipSlots = 1;
            PartDef a = Part(PartCategory.Economy, "A");
            host.Run.OwnedParts.Add(a);
            host.Run.Equip(a);

            var vm = new GarageViewModel(host);
            OwnedPartVm row = Find(vm.OwnedParts, a);

            Assert.That(row.Equipped, Is.True);
            Assert.That(row.SellValue, Is.EqualTo(host.Shop.SellValueOf(a)));
            Assert.That(row.SellValue, Is.GreaterThan(0), "nothing is ever worthless");
        }

        [Test]
        public void SellingFreesTheSlotAndRefunds()
        {
            var host = new FakeRunHost();
            host.Run.MaxEquipSlots = 1;
            host.Run.Money = 0;
            PartDef a = Part(PartCategory.Economy, "A");
            host.Run.OwnedParts.Add(a);
            host.Run.Equip(a);

            var vm = new GarageViewModel(host);
            int refund = Find(vm.OwnedParts, a).SellValue;

            Assert.That(vm.Sell(a), Is.True);
            Assert.That(vm.Money, Is.EqualTo(refund));
            Assert.That(vm.SlotsUsed, Is.EqualTo(0));
            Assert.That(vm.CarIsFull, Is.False);
        }

        [Test]
        public void AFullCarBlocksBuyingAndSaysSo()
        {
            // "I have money and it won't let me buy" reads as a bug unless the screen says which of the
            // two gates is closed. Stocks a real shelf first — asserting over an empty offer list would
            // pass vacuously and prove nothing.
            FakeRunHost host = HostWithShelf(out _, money: 999);
            host.Run.MaxEquipSlots = 1;
            PartDef a = Part(PartCategory.Economy, "A");
            host.Run.OwnedParts.Add(a);
            host.Run.Equip(a);

            var vm = new GarageViewModel(host);
            Assert.That(vm.Offers.Count, Is.GreaterThan(0), "the shelf must actually be stocked");
            Assert.That(vm.CarIsFull, Is.True);
            foreach (OfferVm offer in vm.Offers)
                Assert.That(offer.Affordable, Is.False, "a full car blocks every buy, however rich you are");
        }

        [Test]
        public void ComponentsAreListedWithLevelAndBlueprintPrice()
        {
            var host = new FakeRunHost();
            host.Run.Money = 999;
            var vm = new GarageViewModel(host);

            Assert.That(vm.Components.Count, Is.EqualTo(CarComponentCatalog.Count));
            foreach (ComponentVm c in vm.Components)
            {
                Assert.That(c.Level, Is.EqualTo(CarComponentCatalog.MinLevel));
                Assert.That(c.MaxLevel, Is.EqualTo(CarComponentCatalog.MaxLevel));
                Assert.That(c.Price, Is.GreaterThan(0));
                Assert.That(c.CanLevel, Is.True);
                Assert.That(c.LevelLabel, Is.EqualTo($"L{c.Level}/{c.MaxLevel}"));
            }
        }

        [Test]
        public void BlueprintsAreStockedAndPricedAtTheNextLevel()
        {
            FakeRunHost host = HostWithShelf(out _);
            var vm = new GarageViewModel(host);

            Assert.That(vm.Blueprints.Count, Is.EqualTo(ShopLogic.BlueprintOfferCount));
            foreach (ComponentVm b in vm.Blueprints)
            {
                Assert.That(b.Price, Is.EqualTo(host.Run.BlueprintPriceFor(b.Component)));
                Assert.That(b.CanLevel, Is.True, "a maxed component must never be stocked");
            }
        }

        [Test]
        public void BuyingABlueprintLevelsTheComponentAndCharges()
        {
            FakeRunHost host = HostWithShelf(out _, money: 50);
            var vm = new GarageViewModel(host);

            ComponentVm offer = vm.Blueprints[0];
            int levelBefore = Find(vm.Components, offer.Component).Level;
            Assert.That(vm.BuyBlueprint(offer.Component), Is.True);

            Assert.That(vm.Money, Is.EqualTo(50 - offer.Price));
            Assert.That(Find(vm.Components, offer.Component).Level, Is.EqualTo(levelBefore + 1));
            foreach (ComponentVm b in vm.Blueprints)
                Assert.That(b.Component, Is.Not.EqualTo(offer.Component), "a bought Blueprint leaves the shelf");
        }

        [Test]
        public void AComponentNotOnTheShelfCannotBeBought()
        {
            // The point of the whole rework: components are ROLLED, not browsed. A component absent from
            // this visit's Blueprint row must be unbuyable however much money is on the table — otherwise
            // the ten-row list is still a menu, just an undrawn one.
            FakeRunHost host = HostWithShelf(out _);
            var vm = new GarageViewModel(host);

            CarComponent unstocked = Unstocked(vm);
            int level = Find(vm.Components, unstocked).Level;
            int money = vm.Money;

            Assert.That(vm.BuyBlueprint(unstocked), Is.False);
            Assert.That(vm.Money, Is.EqualTo(money), "a refused buy must charge nothing");
            Assert.That(Find(vm.Components, unstocked).Level, Is.EqualTo(level));
        }

        [Test]
        public void TheComponentListStaysAReadOutOfAllTen()
        {
            // The status list is not the shop: it still shows every component, stocked or not, so the
            // player can read what the car IS while only the rolled Blueprints are buyable.
            FakeRunHost host = HostWithShelf(out _);
            var vm = new GarageViewModel(host);

            Assert.That(vm.Components.Count, Is.EqualTo(CarComponentCatalog.Count));
            Assert.That(vm.Blueprints.Count, Is.LessThan(vm.Components.Count));
        }

        /// <summary>A component this visit did NOT stock — there are always some, with 10 in the catalogue
        /// and <see cref="ShopLogic.BlueprintOfferCount"/> on the shelf.</summary>
        private static CarComponent Unstocked(GarageViewModel vm)
        {
            foreach (CarComponentInfo info in CarComponentCatalog.All)
            {
                bool stocked = false;
                foreach (ComponentVm b in vm.Blueprints)
                    if (b.Component == info.Component) { stocked = true; break; }
                if (!stocked) return info.Component;
            }
            Assert.Fail("the shelf stocked every component — this test can no longer say anything");
            return default;
        }

        [Test]
        public void EveryVisitListsTwoPacks()
        {
            // Packs are rolled by BeginVisit, so this needs a host that has actually opened a visit —
            // a bare FakeRunHost has an unopened shop and would list none.
            FakeRunHost host = HostWithShelf(out _);
            var vm = new GarageViewModel(host);
            Assert.That(vm.Packs.Count, Is.EqualTo(ShopLogic.PacksPerVisit));
        }

        [Test]
        public void AnUnopenedShopListsNoPacks()
        {
            // The complement, pinned deliberately: the VM reads live shop state rather than inventing a
            // shelf, so a host that never opened a visit shows an empty garage instead of phantom stock.
            var vm = new GarageViewModel(new FakeRunHost());
            Assert.That(vm.Packs, Is.Empty);
            Assert.That(vm.Offers, Is.Empty);
        }

        private static ComponentVm Find(System.Collections.Generic.IReadOnlyList<ComponentVm> list,
            CarComponent component)
        {
            foreach (ComponentVm c in list)
                if (c.Component == component) return c;
            Assert.Fail($"{component} missing from the component list");
            return default;
        }

        [Test]
        public void SlotLine_ShowsUsedOverTotal()
        {
            var host = new FakeRunHost();
            host.Run.MaxEquipSlots = 6;
            PartDef a = Part(PartCategory.Economy, "A");
            host.Run.OwnedParts.Add(a);
            host.Run.Equip(a);

            var vm = new GarageViewModel(host);
            Assert.That(vm.SlotsUsed, Is.EqualTo(1));
            Assert.That(vm.SlotsTotal, Is.EqualTo(6));
            Assert.That(vm.SlotLine, Is.EqualTo("1/6 slots used"));
        }

        // --- Team upgrades -------------------------------------------------------------------------

        [Test]
        public void Upgrades_OwnedAreExcludedFromAvailable()
        {
            var host = new FakeRunHost();
            TeamUpgrade owned = TeamUpgrades.All[0];
            host.Run.OwnedUpgrades.Add(owned);

            var vm = new GarageViewModel(host);
            Assert.That(vm.OwnedUpgrades.Any(u => u.Upgrade == owned), Is.True);
            Assert.That(vm.AvailableUpgrades.Any(u => u.Upgrade == owned), Is.False);
            Assert.That(vm.AvailableUpgrades.Count + vm.OwnedUpgrades.Count, Is.EqualTo(TeamUpgrades.All.Count));
        }

        // --- The Changed signal --------------------------------------------------------------------

        [Test]
        public void Changed_FiresOnceOnASuccessfulMutation()
        {
            FakeRunHost host = HostWithShelf(out _);
            var vm = new GarageViewModel(host);
            int fired = 0;
            vm.Changed += () => fired++;

            Assert.That(vm.Buy(host.Shop.Offers[0]), Is.True);
            Assert.That(fired, Is.EqualTo(1));
        }

        [Test]
        public void Changed_DoesNotFireWhenTheCommandIsRefused()
        {
            FakeRunHost host = HostWithShelf(out _, money: 0); // can't afford anything
            var vm = new GarageViewModel(host);
            int fired = 0;
            vm.Changed += () => fired++;

            Assert.That(vm.Buy(host.Shop.Offers[0]), Is.False);
            Assert.That(fired, Is.EqualTo(0));
        }

        // --- Repair --------------------------------------------------------------------------------

        [Test]
        public void Repair_AvailabilityAndAffordabilityGate()
        {
            var host = new FakeRunHost { RepairCost = 6 };
            host.Run.Money = 10;
            host.Run.CarDurability = 0.62f;
            var vm = new GarageViewModel(host);

            Assert.That(vm.RepairAvailable, Is.True);
            Assert.That(vm.CanAffordRepair, Is.True);
            Assert.That(vm.RepairLabel, Does.StartWith("REPAIR CAR ($6)"));
            Assert.That(vm.RepairLabel, Does.Contain("62"));

            host.Run.CarDurability = 1f;
            vm.Rebuild();
            Assert.That(vm.RepairAvailable, Is.False, "a pristine car offers no repair");
        }

        // --- Header lines --------------------------------------------------------------------------

        [Test]
        public void NextRaceLine_DistinguishesBossFromHeat()
        {
            var host = new FakeRunHost();
            host.Run.RacesPerCircuit = 5;

            host.Run.RaceIndex = 2; // a heat
            Assert.That(new GarageViewModel(host).NextRaceLine, Is.EqualTo("race 3/5"));

            host.Run.RaceIndex = 4; // the final race = boss
            host.Run.BossTopN = 3;
            Assert.That(new GarageViewModel(host).NextRaceLine, Is.EqualTo("BOSS race 5/5 (top 3 required)"));
        }

        [Test]
        public void CircuitLine_IsOneBased()
        {
            var host = new FakeRunHost();
            host.Run.CircuitIndex = 0;
            host.Run.TotalCircuits = 1;
            Assert.That(new GarageViewModel(host).CircuitLine, Is.EqualTo("CIRCUIT 1/1"));
        }

        // --- The wave-23 regression ----------------------------------------------------------------

        [Test]
        public void StatPreview_SurvivesRunThatStartsWithStatPartEquipped()
        {
            // The exact shape that blacked out the old GarageScreen: a run whose EquippedParts already
            // contains a stat part on the very first draw (a RESUMED run). The old code gated its base-spec
            // capture on "no stat part equipped" and so never captured, killing the bars for the whole run.
            var host = new FakeRunHost();
            host.BaseSpec = CarSpec();
            PartDef grip = GripPart();
            host.Run.OwnedParts.Add(grip);
            host.Run.EquippedParts.Add(grip);

            var vm = new GarageViewModel(host);

            Assert.That(vm.HasStatPreview, Is.True);
            float baseGrip = StatSummary.Compute(host.BaseSpec).Grip;
            Assert.That(vm.Current.Grip, Is.GreaterThan(baseGrip),
                "the equipped grip part must lift the previewed Grip above the bare car");
        }

        [Test]
        public void StatDelta_Sign_HasAHalfPointDeadband()
        {
            Assert.That(new StatDelta(50f, 50.4f).Sign, Is.EqualTo(0), "under half a point reads as no change");
            Assert.That(new StatDelta(50f, 51f).Sign, Is.EqualTo(1));
            Assert.That(new StatDelta(50f, 49f).Sign, Is.EqualTo(-1));
        }

        private static OwnedPartVm Find(IReadOnlyList<OwnedPartVm> rows, PartDef part)
        {
            foreach (OwnedPartVm row in rows)
                if (row.Part == part) return row;
            Assert.Fail($"owned part {part.DisplayName} not found in the VM");
            return default;
        }
    }
}
