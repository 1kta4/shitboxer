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
        public void OwnedPart_EquipGate_RespectsFreeSlots()
        {
            var host = new FakeRunHost();
            host.Run.MaxEquipSlots = 1;
            PartDef a = Part(PartCategory.Economy, "A");
            PartDef b = Part(PartCategory.Economy, "B");
            host.Run.OwnedParts.Add(a);
            host.Run.OwnedParts.Add(b);
            host.Run.Equip(a); // fills the single slot

            var vm = new GarageViewModel(host);
            OwnedPartVm rowA = Find(vm.OwnedParts, a);
            OwnedPartVm rowB = Find(vm.OwnedParts, b);

            Assert.That(rowA.Equipped, Is.True);
            Assert.That(rowB.Equipped, Is.False);
            Assert.That(rowB.CanEquip, Is.False, "no free slot, so the un-equipped part can't be equipped");
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
