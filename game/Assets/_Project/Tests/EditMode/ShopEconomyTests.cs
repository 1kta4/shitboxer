using NUnit.Framework;
using Shitboxer.Meta;
using UnityEngine;

namespace Shitboxer.Tests
{
    /// <summary>
    /// Covers the Balatro-style economy depth: interest on banked money, reroll-cost escalation,
    /// and rarity/family part pricing. Pins the hard constraint that every zeroed/unit tunable
    /// reproduces the shipped numbers exactly (no balance drift), plus the RunState/ShopLogic wiring.
    /// </summary>
    public class ShopEconomyTests : TestBase
    {
        private static PartDef Part(int price, Rarity rarity = Rarity.Common,
                                    PartCategory category = PartCategory.Stat)
        {
            var p = ScriptableObject.CreateInstance<PartDef>();
            p.Price = price;
            p.Rarity = rarity;
            p.Category = category;
            return p;
        }

        // ---- Interest -------------------------------------------------------------------------

        [Test]
        public void Interest_IsZero_AtDefaultRate()
        {
            // No-op default: rate 0 pays nothing however much is banked — the shipped economy.
            Assert.AreEqual(0, ShopEconomy.Interest(100));
            Assert.AreEqual(0, ShopEconomy.Interest(100, perBlock: 0));
        }

        [Test]
        public void Interest_PaysPerBlock_OfBankedMoney()
        {
            // $1 per full $5 held → $25 banked earns $5, and a partial block ($4) earns nothing.
            Assert.AreEqual(5, ShopEconomy.Interest(25, perBlock: 1, blockSize: 5, cap: int.MaxValue));
            Assert.AreEqual(0, ShopEconomy.Interest(4, perBlock: 1, blockSize: 5, cap: int.MaxValue));
            Assert.AreEqual(2, ShopEconomy.Interest(12, perBlock: 1, blockSize: 5, cap: int.MaxValue)); // 12/5 = 2 whole blocks
            Assert.AreEqual(4, ShopEconomy.Interest(12, perBlock: 2, blockSize: 5, cap: int.MaxValue)); // rate scales: 2 blocks * $2
        }

        [Test]
        public void Interest_Caps()
        {
            // Hoarding rewards but plateaus: 100 banked would pay 20, but the cap holds it at 5.
            Assert.AreEqual(5, ShopEconomy.Interest(100, perBlock: 1, blockSize: 5, cap: 5));
            // Below the cap it still scales with what you hold.
            Assert.AreEqual(3, ShopEconomy.Interest(15, perBlock: 1, blockSize: 5, cap: 5));
        }

        [Test]
        public void Interest_GuardsNonPositiveInputs()
        {
            Assert.AreEqual(0, ShopEconomy.Interest(-50, perBlock: 1, blockSize: 5));
            Assert.AreEqual(0, ShopEconomy.Interest(50, perBlock: -1, blockSize: 5));
            Assert.AreEqual(0, ShopEconomy.Interest(50, perBlock: 1, blockSize: 0));
        }

        // ---- Reroll cost ----------------------------------------------------------------------

        [Test]
        public void RerollCost_IsConstant_AtIncrementZero()
        {
            // The pure helper is flat when its increment is 0 — the no-op default.
            Assert.AreEqual(5, ShopEconomy.RerollCost(5, 0));
            Assert.AreEqual(5, ShopEconomy.RerollCost(5, 3));
            Assert.AreEqual(5, ShopEconomy.RerollCost(5, 10, increment: 0));
        }

        [Test]
        public void RerollCost_Escalates_WithIncrement()
        {
            Assert.AreEqual(5, ShopEconomy.RerollCost(5, 0, increment: 1));
            Assert.AreEqual(6, ShopEconomy.RerollCost(5, 1, increment: 1));
            Assert.AreEqual(7, ShopEconomy.RerollCost(5, 2, increment: 1));
            Assert.AreEqual(11, ShopEconomy.RerollCost(5, 2, increment: 3)); // 5 + 3*2
        }

        [Test]
        public void RerollCost_FloorsAtZero()
        {
            Assert.AreEqual(0, ShopEconomy.RerollCost(5, 10, increment: -1)); // 5 - 10 clamps to 0
            Assert.AreEqual(5, ShopEconomy.RerollCost(5, -3, increment: 1));  // negative count clamps to 0
        }

        // ---- Part pricing ---------------------------------------------------------------------

        [Test]
        public void PartPrice_EqualsBase_AtUnitMultiplier()
        {
            // Identity pricing is a guaranteed no-op — the shipped sticker price, no rounding drift.
            Assert.AreEqual(10, ShopEconomy.PartPrice(10, 1f));
            Assert.AreEqual(13, ShopEconomy.PartPrice(13));
        }

        [Test]
        public void PartPrice_ScalesAndRounds()
        {
            Assert.AreEqual(20, ShopEconomy.PartPrice(10, 2f));
            Assert.AreEqual(5, ShopEconomy.PartPrice(10, 0.5f));
            Assert.AreEqual(6, ShopEconomy.PartPrice(5, 1.1f)); // 5.5 rounds away from zero
            Assert.AreEqual(0, ShopEconomy.PartPrice(-5, 2f));  // floored at 0
        }

        [Test]
        public void PartPrice_ByRarityAndFamily_IsIdentityWithNullTables()
        {
            // The shipped default: null multiplier tables leave every part at its base price.
            Assert.AreEqual(12, ShopEconomy.PartPrice(12, Rarity.Rare, PartCategory.Attack, null, null));
        }

        [Test]
        public void PartPrice_RespectsRarity()
        {
            // Rare tier priced at 2x, others at 1x (index by (int)Rarity: Common0, Uncommon1, Rare2).
            var rarityMults = new[] { 1f, 1f, 2f };
            Assert.AreEqual(10, ShopEconomy.PartPrice(10, Rarity.Common, PartCategory.Stat, rarityMults));
            Assert.AreEqual(20, ShopEconomy.PartPrice(10, Rarity.Rare, PartCategory.Stat, rarityMults));
        }

        [Test]
        public void PartPrice_RespectsFamily_AndCombinesWithRarity()
        {
            var rarityMults = new[] { 1f, 1f, 2f };            // Rare 2x
            var familyMults = new[] { 1f, 1f, 3f };            // Attack 3x
            // Attack family alone: 10 * 3 = 30.
            Assert.AreEqual(30, ShopEconomy.PartPrice(10, Rarity.Common, PartCategory.Attack, rarityMults, familyMults));
            // Rare Attack: 10 * 2 * 3 = 60.
            Assert.AreEqual(60, ShopEconomy.PartPrice(10, Rarity.Rare, PartCategory.Attack, rarityMults, familyMults));
        }

        // ---- RunState wiring ------------------------------------------------------------------

        [Test]
        public void RunState_ShopInterest_IsZeroByDefault_AndOptIn()
        {
            var run = new RunState { Money = 100 };
            Assert.AreEqual(0, run.ShopInterest());          // default rate 0
            Assert.AreEqual(0, run.ApplyShopInterest());     // opt-in step grants nothing...
            Assert.AreEqual(100, run.Money);                 // ...and leaves money untouched (no drift)

            run.InterestPerBlock = 1;                        // designer turns the knob on
            run.InterestBlockSize = 5;
            run.InterestCap = 5;
            Assert.AreEqual(5, run.ShopInterest());
            Assert.AreEqual(5, run.ApplyShopInterest());     // now it adds, and reports the bonus
            Assert.AreEqual(105, run.Money);
        }

        [Test]
        public void RunState_NextRerollCost_MatchesShippedCurve_AndResets()
        {
            var run = new RunState { Money = 1000 };
            // Default increment 0 → shipped curve: base, base+step, base+2*step.
            Assert.AreEqual(ShopLogic.BaseRerollCost, run.NextRerollCost());
            Assert.AreEqual(ShopLogic.BaseRerollCost, run.ChargeReroll());
            Assert.AreEqual(ShopLogic.BaseRerollCost + ShopLogic.RerollCostStep, run.NextRerollCost());
            run.ChargeReroll();
            Assert.AreEqual(ShopLogic.BaseRerollCost + 2 * ShopLogic.RerollCostStep, run.NextRerollCost());

            run.ResetRerollCounter();
            Assert.AreEqual(0, run.RerollsThisVisit);
            Assert.AreEqual(ShopLogic.BaseRerollCost, run.NextRerollCost()); // back to base next visit
        }

        [Test]
        public void RunState_RerollCostIncrement_SteepensCurve()
        {
            var run = new RunState { Money = 1000, RerollCostIncrement = 2 };
            // Effective step = shipped step (1) + extra (2) = 3.
            int step = ShopLogic.RerollCostStep + 2;
            Assert.AreEqual(ShopLogic.BaseRerollCost, run.NextRerollCost());
            run.ChargeReroll();
            Assert.AreEqual(ShopLogic.BaseRerollCost + step, run.NextRerollCost());
        }

        [Test]
        public void RunState_ChargeReroll_FailsWhenBroke_LeavingCounterAndMoney()
        {
            var run = new RunState { Money = 0 };
            Assert.AreEqual(-1, run.ChargeReroll());
            Assert.AreEqual(0, run.Money);
            Assert.AreEqual(0, run.RerollsThisVisit); // nothing charged, counter untouched
        }

        // ---- ShopLogic wiring -----------------------------------------------------------------

        [Test]
        public void ShopLogic_RerollCurve_MatchesShipped_AtIncrementZero()
        {
            var shop = new ShopLogic(seed: 1);
            var run = new RunState { Money = 1000 };
            var pool = new System.Collections.Generic.List<PartDef>();
            for (int i = 0; i < 12; i++) pool.Add(Part(5));

            shop.BeginVisit(pool, run);
            Assert.AreEqual(ShopLogic.BaseRerollCost, shop.RerollCost);
            Assert.IsTrue(shop.TryReroll(pool, run));
            Assert.AreEqual(ShopLogic.BaseRerollCost + ShopLogic.RerollCostStep, shop.RerollCost);
            Assert.IsTrue(shop.TryReroll(pool, run));
            Assert.AreEqual(ShopLogic.BaseRerollCost + 2 * ShopLogic.RerollCostStep, shop.RerollCost);
        }

        [Test]
        public void ShopLogic_RerollCostIncrement_SteepensCurve_AndResetsNextVisit()
        {
            var shop = new ShopLogic(seed: 2) { RerollCostIncrement = 4 };
            var run = new RunState { Money = 1000 };
            var pool = new System.Collections.Generic.List<PartDef>();
            for (int i = 0; i < 12; i++) pool.Add(Part(5));
            int step = ShopLogic.RerollCostStep + 4;

            shop.BeginVisit(pool, run);
            Assert.AreEqual(ShopLogic.BaseRerollCost, shop.RerollCost);
            Assert.IsTrue(shop.TryReroll(pool, run));
            Assert.AreEqual(ShopLogic.BaseRerollCost + step, shop.RerollCost);

            // A fresh visit resets the escalation back to base.
            shop.BeginVisit(pool, run);
            Assert.AreEqual(ShopLogic.BaseRerollCost, shop.RerollCost);
        }

        [Test]
        public void ShopLogic_PriceOf_IsStickerPrice_ByDefault()
        {
            var shop = new ShopLogic(seed: 3);
            var part = Part(7, Rarity.Rare, PartCategory.Attack);
            Assert.AreEqual(7, shop.PriceOf(part)); // null multiplier tables → base price
            Assert.AreEqual(0, shop.PriceOf(null));
        }

        [Test]
        public void ShopLogic_DynamicPricing_ChargesAdjustedPriceOnBuy()
        {
            var shop = new ShopLogic(seed: 4) { RarityPriceMult = new[] { 1f, 1f, 2f } }; // Rare 2x
            var run = new RunState { Money = 100 };
            var common = Part(6, Rarity.Common);
            var rare = Part(6, Rarity.Rare);
            var pool = new System.Collections.Generic.List<PartDef> { common, rare };

            Assert.AreEqual(6, shop.PriceOf(common));
            Assert.AreEqual(12, shop.PriceOf(rare));

            shop.BeginVisit(pool, run);
            // Buy the rare that is on the shelf — it should cost the doubled price.
            PartDef rareOffer = null;
            foreach (PartDef o in shop.Offers)
                if (o.Rarity == Rarity.Rare) { rareOffer = o; break; }
            Assert.IsNotNull(rareOffer);
            Assert.IsTrue(shop.TryBuy(rareOffer, run));
            Assert.AreEqual(100 - 12, run.Money);
        }
    }
}
