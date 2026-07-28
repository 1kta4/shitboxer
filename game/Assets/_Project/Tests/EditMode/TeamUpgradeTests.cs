using System.Collections.Generic;
using NUnit.Framework;
using Shitboxer.Meta;
using UnityEngine;

namespace Shitboxer.Tests
{
    /// <summary>
    /// doc 03's permanent team upgrades (Balatro's vouchers): buying, the four effects, and the by-name
    /// save round-trip. The load-bearing contract is the same one every other opt-in system here carries —
    /// a run owning NO upgrades must reproduce the shipped shop byte-for-byte — so each effect is pinned
    /// both off and on.
    /// </summary>
    public class TeamUpgradeTests : TestBase
    {
        private static PartDef Part(string id)
        {
            var p = ScriptableObject.CreateInstance<PartDef>();
            p.Id = id;
            p.Price = 5;
            return p;
        }

        private static List<PartDef> Pool(int n)
        {
            var list = new List<PartDef>(n);
            for (int i = 0; i < n; i++) list.Add(Part($"p{i}"));
            return list;
        }

        private static PartPool AssetPool(params PartDef[] parts)
        {
            var pool = ScriptableObject.CreateInstance<PartPool>();
            pool.Parts = new List<PartDef>(parts);
            return pool;
        }

        // --- No upgrades == the shipped shop ---------------------------------------------------------

        [Test]
        public void NoUpgrades_ReproduceTheShippedShopExactly()
        {
            var run = new RunState();
            Assert.AreEqual(ShopLogic.OfferCount, ShopLogic.EffectiveOfferCount(run));
            Assert.AreEqual(ShopLogic.BaseRerollCost, ShopLogic.EffectiveRerollBase(run));
            Assert.AreEqual(run.MaxEquipSlots, run.EffectiveEquipSlots);
            Assert.AreEqual(0, TeamUpgrades.ExtraCrateDraws(run));
        }

        [Test]
        public void EveryEffect_IsZeroForANullRun()
        {
            // The effects are read from UI and director paths that can run before a run exists.
            Assert.AreEqual(0, TeamUpgrades.ExtraShopOffers(null));
            Assert.AreEqual(0, TeamUpgrades.RerollDiscount(null));
            Assert.AreEqual(0, TeamUpgrades.ExtraEquipSlots(null));
            Assert.AreEqual(0, TeamUpgrades.ExtraCrateDraws(null));
        }

        // --- The four effects ------------------------------------------------------------------------

        [Test]
        public void Overstock_AddsAShelfSlot_AndTheShelfActuallyDrawsIt()
        {
            var run = new RunState { Money = 99 };
            run.OwnedUpgrades.Add(TeamUpgrade.Overstock);

            Assert.AreEqual(ShopLogic.OfferCount + TeamUpgrades.OverstockExtraOffers,
                ShopLogic.EffectiveOfferCount(run));

            // Not just arithmetic — the roll must really put the extra part on the shelf.
            var shop = new ShopLogic(seed: 4);
            shop.BeginVisit(Pool(12), run);
            Assert.AreEqual(ShopLogic.OfferCount + 1, shop.Offers.Count);
        }

        [Test]
        public void RerollSurplus_MakesEveryVisitStartCheaper()
        {
            var run = new RunState { Money = 99 };
            run.OwnedUpgrades.Add(TeamUpgrade.RerollSurplus);

            var shop = new ShopLogic(seed: 4);
            shop.BeginVisit(Pool(12), run);
            Assert.AreEqual(ShopLogic.BaseRerollCost - TeamUpgrades.RerollSurplusDiscount, shop.RerollCost);
        }

        [Test]
        public void RerollSurplus_StillEscalatesFromTheDiscountedBase()
        {
            // The discount shifts the curve down; it must not flatten it, or rerolling stops being greedy.
            var run = new RunState { Money = 99 };
            run.OwnedUpgrades.Add(TeamUpgrade.RerollSurplus);

            var shop = new ShopLogic(seed: 4);
            List<PartDef> pool = Pool(12);
            shop.BeginVisit(pool, run);

            int expectedBase = ShopLogic.BaseRerollCost - TeamUpgrades.RerollSurplusDiscount;
            Assert.IsTrue(shop.TryReroll(pool, run));
            Assert.AreEqual(expectedBase + ShopLogic.RerollCostStep, shop.RerollCost);
        }

        [Test]
        public void RerollBase_IsNeverFree_WhateverTheConstantsBecome()
        {
            // Honest about what this pins: at today's numbers ($5 base, $2 discount → $3) the floor is NOT
            // reached, so this exercises no live branch. It's a guard for the retune — if someone pushes
            // RerollSurplusDiscount past the base, rerolls must clamp to $1 rather than going free. A free
            // reroll lets the player spin the shelf until it yields whatever they want, which ends the shop
            // as a decision.
            Assert.GreaterOrEqual(ShopLogic.EffectiveRerollBase(new RunState()), 1);

            var run = new RunState();
            run.OwnedUpgrades.Add(TeamUpgrade.RerollSurplus);
            Assert.GreaterOrEqual(ShopLogic.EffectiveRerollBase(run), 1);
        }

        [Test]
        public void Toolbox_AddsAnEquipSlot_OverTheAuthoredBase()
        {
            var run = new RunState { MaxEquipSlots = 2 };
            Assert.AreEqual(2, run.EffectiveEquipSlots);

            run.OwnedUpgrades.Add(TeamUpgrade.Toolbox);
            Assert.AreEqual(2 + TeamUpgrades.ToolboxExtraSlots, run.EffectiveEquipSlots,
                "the upgrade layers ON TOP of the authored base, it doesn't replace it");
        }

        [Test]
        public void Toolbox_LetsOneMorePartActuallyEquip()
        {
            var run = new RunState { MaxEquipSlots = 1 };
            PartDef a = Part("a"), b = Part("b");
            run.OwnedParts.Add(a);
            run.OwnedParts.Add(b);

            Assert.IsTrue(run.Equip(a));
            Assert.IsFalse(run.Equip(b), "slots full at the authored base");

            run.OwnedUpgrades.Add(TeamUpgrade.Toolbox);
            Assert.IsTrue(run.Equip(b), "Toolbox opens the extra slot for real, not just on the counter");
        }

        [Test]
        public void BulkBuyer_AddsACrateDraw()
        {
            var run = new RunState();
            Assert.AreEqual(0, TeamUpgrades.ExtraCrateDraws(run));

            run.OwnedUpgrades.Add(TeamUpgrade.BulkBuyer);
            Assert.AreEqual(TeamUpgrades.BulkBuyerExtraDraws, TeamUpgrades.ExtraCrateDraws(run));
        }

        // --- Buying ----------------------------------------------------------------------------------

        [Test]
        public void BuyUpgrade_ChargesOnceAndGrantsIt()
        {
            var shop = new ShopLogic(seed: 4);
            var run = new RunState { Money = 50 };
            int price = TeamUpgrades.PriceOf(TeamUpgrade.Toolbox);

            Assert.IsTrue(shop.TryBuyUpgrade(TeamUpgrade.Toolbox, run));
            Assert.AreEqual(50 - price, run.Money);
            Assert.IsTrue(run.HasUpgrade(TeamUpgrade.Toolbox));
        }

        [Test]
        public void BuyUpgrade_RefusesDuplicates_AndChargesNothing()
        {
            // Owning it IS the effect — buying twice would just be money for nothing.
            var shop = new ShopLogic(seed: 4);
            var run = new RunState { Money = 50 };
            shop.TryBuyUpgrade(TeamUpgrade.Toolbox, run);
            int afterFirst = run.Money;

            Assert.IsFalse(shop.TryBuyUpgrade(TeamUpgrade.Toolbox, run));
            Assert.AreEqual(afterFirst, run.Money);
            Assert.AreEqual(1, run.OwnedUpgrades.Count);
        }

        [Test]
        public void BuyUpgrade_Unaffordable_ChargesNothing()
        {
            var shop = new ShopLogic(seed: 4);
            var run = new RunState { Money = TeamUpgrades.PriceOf(TeamUpgrade.Toolbox) - 1 };

            Assert.IsFalse(shop.TryBuyUpgrade(TeamUpgrade.Toolbox, run));
            Assert.AreEqual(TeamUpgrades.PriceOf(TeamUpgrade.Toolbox) - 1, run.Money);
            Assert.IsFalse(run.HasUpgrade(TeamUpgrade.Toolbox));
        }

        [Test]
        public void BuyingRerollSurplus_CutsTheCostOnThisVisitNotTheNextOne()
        {
            // Bought mid-visit it must bite immediately, or its first garage is a dead purchase.
            var shop = new ShopLogic(seed: 4);
            var run = new RunState { Money = 99 };
            shop.BeginVisit(Pool(12), run);
            Assert.AreEqual(ShopLogic.BaseRerollCost, shop.RerollCost);

            Assert.IsTrue(shop.TryBuyUpgrade(TeamUpgrade.RerollSurplus, run));
            Assert.AreEqual(ShopLogic.BaseRerollCost - TeamUpgrades.RerollSurplusDiscount, shop.RerollCost);
        }

        [Test]
        public void EveryUpgrade_HasNameAndPrice()
        {
            // The garage renders straight off this table; a blank or free entry would be a shop bug.
            foreach (TeamUpgrade upgrade in TeamUpgrades.All)
            {
                TeamUpgradeInfo info = TeamUpgrades.Info(upgrade);
                Assert.IsNotEmpty(info.DisplayName, $"{upgrade} needs a name");
                Assert.IsNotEmpty(info.Description, $"{upgrade} needs a description");
                Assert.Greater(info.Price, 0, $"{upgrade} must cost something");
            }
        }

        // --- Persistence ------------------------------------------------------------------------------

        [Test]
        public void OwnedUpgrades_SurviveASaveResumeRoundTrip()
        {
            PartPool pool = AssetPool(Part("a"));
            var run = new RunState { Money = 7 };
            run.OwnedUpgrades.Add(TeamUpgrade.Overstock);
            run.OwnedUpgrades.Add(TeamUpgrade.Toolbox);

            RunState resumed = RunSave.From(run).ToRunState(pool);

            Assert.IsTrue(resumed.HasUpgrade(TeamUpgrade.Overstock));
            Assert.IsTrue(resumed.HasUpgrade(TeamUpgrade.Toolbox));
            Assert.IsFalse(resumed.HasUpgrade(TeamUpgrade.BulkBuyer), "must not invent one that wasn't bought");
        }

        [Test]
        public void UpgradesArePersistedByName_NotOrdinal()
        {
            // Stored by name so inserting a new enum member can't silently reinterpret an old save's
            // upgrades as different ones.
            var run = new RunState();
            run.OwnedUpgrades.Add(TeamUpgrade.BulkBuyer);

            RunSave dto = RunSave.From(run);
            CollectionAssert.Contains(dto.teamUpgradeIds, "BulkBuyer");
        }

        [Test]
        public void UnknownUpgradeName_IsDiscardedNotThrown()
        {
            // A save written before an upgrade was renamed/removed must degrade quietly, exactly like an
            // unresolvable part Id.
            PartPool pool = AssetPool(Part("a"));
            var dto = new RunSave { money = 5 };
            dto.teamUpgradeIds.Add("Overstock");
            dto.teamUpgradeIds.Add("ThisUpgradeNoLongerExists");

            RunState resumed = dto.ToRunState(pool);

            Assert.AreEqual(1, resumed.OwnedUpgrades.Count);
            Assert.IsTrue(resumed.HasUpgrade(TeamUpgrade.Overstock));
        }

        [Test]
        public void NoUpgrades_RoundTripsEmpty()
        {
            PartPool pool = AssetPool(Part("a"));
            RunState resumed = RunSave.From(new RunState { Money = 5 }).ToRunState(pool);
            Assert.AreEqual(0, resumed.OwnedUpgrades.Count);
        }
    }
}
