using System.Collections.Generic;
using NUnit.Framework;
using Shitboxer.Meta;
using UnityEngine;

namespace Shitboxer.Tests
{
    /// <summary>Covers the between-race shop: reroll escalation, buying, and owned-part exclusion.</summary>
    public class ShopLogicTests : TestBase
    {
        private static PartDef Part(int price)
        {
            var p = ScriptableObject.CreateInstance<PartDef>();
            p.Price = price;
            return p;
        }

        private static List<PartDef> Pool(int n)
        {
            var list = new List<PartDef>(n);
            for (int i = 0; i < n; i++) list.Add(Part(5));
            return list;
        }

        [Test]
        public void BeginVisit_ResetsRerollCost_AndRollsOffers()
        {
            var shop = new ShopLogic(seed: 1);
            var run = new RunState { Money = 100 };
            shop.BeginVisit(Pool(5), run);
            Assert.AreEqual(ShopLogic.BaseRerollCost, shop.RerollCost);
            Assert.AreEqual(ShopLogic.OfferCount, shop.Offers.Count);
        }

        [Test]
        public void Reroll_EscalatesCost_AndDeductsMoney()
        {
            var shop = new ShopLogic(seed: 2);
            var run = new RunState { Money = 100 };
            shop.BeginVisit(Pool(10), run);
            int before = run.Money;
            Assert.IsTrue(shop.TryReroll(Pool(10), run));
            Assert.AreEqual(before - ShopLogic.BaseRerollCost, run.Money);
            Assert.AreEqual(ShopLogic.BaseRerollCost + ShopLogic.RerollCostStep, shop.RerollCost);
        }

        [Test]
        public void Reroll_FailsWhenBroke()
        {
            var shop = new ShopLogic(seed: 3);
            var run = new RunState { Money = 0 };
            shop.BeginVisit(Pool(5), run);
            Assert.IsFalse(shop.TryReroll(Pool(5), run));
            Assert.AreEqual(0, run.Money);
        }

        [Test]
        public void Buy_DeductsPrice_MovesToOwned_AndAutoEquips()
        {
            var shop = new ShopLogic(seed: 4);
            var run = new RunState { Money = 100 };
            shop.BeginVisit(Pool(6), run);
            PartDef offer = shop.Offers[0];
            Assert.IsTrue(shop.TryBuy(offer, run));
            Assert.AreEqual(100 - offer.Price, run.Money);
            Assert.IsTrue(run.Owns(offer));
            Assert.IsTrue(run.IsEquipped(offer));
            CollectionAssert.DoesNotContain(shop.Offers, offer);
        }

        [Test]
        public void Buy_FailsWhenUnaffordable()
        {
            var shop = new ShopLogic(seed: 5);
            var run = new RunState { Money = 1 }; // parts cost 5
            shop.BeginVisit(Pool(6), run);
            PartDef offer = shop.Offers[0];
            Assert.IsFalse(shop.TryBuy(offer, run));
            Assert.AreEqual(1, run.Money);
        }

        [Test]
        public void Roll_NeverOffersOwnedParts()
        {
            var shop = new ShopLogic(seed: 6);
            var pool = Pool(4);
            var run = new RunState { Money = 100 };
            for (int i = 0; i < 3; i++) run.OwnedParts.Add(pool[i]); // own all but one
            shop.BeginVisit(pool, run);
            Assert.AreEqual(1, shop.Offers.Count);
            foreach (PartDef owned in run.OwnedParts)
                CollectionAssert.DoesNotContain(shop.Offers, owned);
        }

        [Test]
        public void Offers_AreDistinct()
        {
            var shop = new ShopLogic(seed: 7);
            var run = new RunState { Money = 100 };
            shop.BeginVisit(Pool(20), run);
            var seen = new HashSet<PartDef>(shop.Offers);
            Assert.AreEqual(shop.Offers.Count, seen.Count);
        }
    }
}
