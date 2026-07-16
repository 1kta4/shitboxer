using System.Collections.Generic;
using NUnit.Framework;
using Shitboxer.Meta;
using UnityEngine;

namespace Shitboxer.Tests
{
    /// <summary>
    /// Covers the between-race shop: reroll escalation, buying, owned-part exclusion, and the booster-style
    /// part crates (buy → draw N → keep 1).
    /// </summary>
    public class ShopLogicTests : TestBase
    {
        private const int CratePrice = 6;
        private const int CrateDraw = 3;

        private static PartDef Part(int price)
        {
            var p = ScriptableObject.CreateInstance<PartDef>();
            p.Price = price;
            return p;
        }

        private static PartDef IdPart(string id)
        {
            var p = ScriptableObject.CreateInstance<PartDef>();
            p.Id = id;
            p.Price = 5;
            return p;
        }

        private static PartDef Part(int price, Rarity rarity)
        {
            var p = ScriptableObject.CreateInstance<PartDef>();
            p.Price = price;
            p.Rarity = rarity;
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

        [Test]
        public void Roll_RarityWeighted_StaysDistinctUnowned_WithinOfferCount_AndDeterministic()
        {
            // Shared instances so two seeded shops can be compared by reference for determinism.
            var pool = new List<PartDef>
            {
                Part(5, Rarity.Common), Part(5, Rarity.Common), Part(5, Rarity.Common),
                Part(7, Rarity.Uncommon), Part(7, Rarity.Uncommon),
                Part(12, Rarity.Rare), Part(12, Rarity.Rare),
            };

            var runA = new RunState { Money = 100 };
            var runB = new RunState { Money = 100 };
            runA.OwnedParts.Add(pool[5]); // own a Rare — must never be offered
            runB.OwnedParts.Add(pool[5]);

            var shopA = new ShopLogic(seed: 99);
            var shopB = new ShopLogic(seed: 99);
            shopA.BeginVisit(pool, runA);
            shopB.BeginVisit(pool, runB);

            // Never exceeds OfferCount, and 6 unowned candidates comfortably fill the shelf.
            Assert.LessOrEqual(shopA.Offers.Count, ShopLogic.OfferCount);
            Assert.AreEqual(ShopLogic.OfferCount, shopA.Offers.Count);

            // Distinct offers, none of them owned.
            Assert.AreEqual(shopA.Offers.Count, new HashSet<PartDef>(shopA.Offers).Count);
            foreach (PartDef owned in runA.OwnedParts)
                CollectionAssert.DoesNotContain(shopA.Offers, owned);

            // Seeded determinism preserved: same seed + pool -> identical shelf, same order.
            CollectionAssert.AreEqual(shopA.Offers, shopB.Offers);
        }

        // --- Part crates (wave 17) ------------------------------------------------------------------

        [Test]
        public void BuyCrate_ChargesOnceAndDrawsTheContents()
        {
            var shop = new ShopLogic(seed: 7);
            var run = new RunState { Money = 20 };
            shop.BeginVisit(Pool(12), run);

            Assert.IsTrue(shop.TryBuyCrate(Pool(12), run, CratePrice, CrateDraw));
            Assert.AreEqual(20 - CratePrice, run.Money);
            Assert.AreEqual(CrateDraw, run.CrateContents.Count);
            Assert.IsTrue(run.CrateOpen);
            // The contents are a pending PICK, not inventory — nothing is owned until KEEP.
            Assert.AreEqual(0, run.OwnedParts.Count);
        }

        [Test]
        public void BuyCrate_Unaffordable_ChargesNothingAndDrawsNothing()
        {
            var shop = new ShopLogic(seed: 7);
            var run = new RunState { Money = CratePrice - 1 };
            shop.BeginVisit(Pool(12), run);

            Assert.IsFalse(shop.TryBuyCrate(Pool(12), run, CratePrice, CrateDraw));
            Assert.AreEqual(CratePrice - 1, run.Money);
            Assert.IsFalse(run.CrateOpen);
        }

        [Test]
        public void BuyCrate_NothingLeftToDraw_DoesNotSellAnEmptyBox()
        {
            // Pool exhausted (every part owned). Charging here would take the money and hand back a pick
            // screen with nothing in it — the money would just be gone.
            var shop = new ShopLogic(seed: 7);
            var run = new RunState { Money = 50 };
            List<PartDef> pool = Pool(3);
            run.OwnedParts.AddRange(pool);

            Assert.IsFalse(shop.TryBuyCrate(pool, run, CratePrice, CrateDraw));
            Assert.AreEqual(50, run.Money, "an undrawable crate must not charge");
            Assert.IsFalse(run.CrateOpen);
        }

        [Test]
        public void BuyCrate_RefusesWhileOneIsAlreadyOpen()
        {
            var shop = new ShopLogic(seed: 7);
            var run = new RunState { Money = 50 };
            shop.BeginVisit(Pool(12), run);

            Assert.IsTrue(shop.TryBuyCrate(Pool(12), run, CratePrice, CrateDraw));
            int afterFirst = run.Money;
            Assert.IsFalse(shop.TryBuyCrate(Pool(12), run, CratePrice, CrateDraw), "one crate at a time");
            Assert.AreEqual(afterFirst, run.Money, "the refused second crate must not charge");
        }

        [Test]
        public void TakeFromCrate_KeepsOne_DiscardsTheRest_AndClosesTheCrate()
        {
            var shop = new ShopLogic(seed: 7);
            var run = new RunState { Money = 20 };
            shop.BeginVisit(Pool(12), run);
            shop.TryBuyCrate(Pool(12), run, CratePrice, CrateDraw);

            PartDef keep = run.CrateContents[1];
            int moneyBefore = run.Money;

            Assert.IsTrue(shop.TryTakeFromCrate(keep, run));
            Assert.AreEqual(moneyBefore, run.Money, "the pick is already paid for — it must cost nothing");
            CollectionAssert.Contains(run.OwnedParts, keep);
            Assert.AreEqual(1, run.OwnedParts.Count, "only the picked part is kept; the rest are scrapped");
            Assert.IsFalse(run.CrateOpen);
        }

        [Test]
        public void TakeFromCrate_AutoEquipsOnlyWhileASlotIsFree()
        {
            var shop = new ShopLogic(seed: 7);
            var run = new RunState { Money = 99, MaxEquipSlots = 1 };
            shop.BeginVisit(Pool(12), run);

            shop.TryBuyCrate(Pool(12), run, CratePrice, CrateDraw);
            PartDef first = run.CrateContents[0];
            shop.TryTakeFromCrate(first, run);
            Assert.IsTrue(run.IsEquipped(first), "a free slot auto-equips, matching TryBuy");

            shop.TryBuyCrate(Pool(12), run, CratePrice, CrateDraw);
            PartDef second = run.CrateContents[0];
            shop.TryTakeFromCrate(second, run);
            Assert.IsTrue(run.Owns(second), "slots full still OWNS the part...");
            Assert.IsFalse(run.IsEquipped(second), "...but cannot slot it — equip it manually in the garage");
        }

        [Test]
        public void TakeFromCrate_RejectsAPartThatIsNotInTheCrate()
        {
            var shop = new ShopLogic(seed: 7);
            var run = new RunState { Money = 20 };
            shop.BeginVisit(Pool(12), run);
            shop.TryBuyCrate(Pool(12), run, CratePrice, CrateDraw);

            Assert.IsFalse(shop.TryTakeFromCrate(Part(5), run), "can't conjure a part that was never drawn");
            Assert.IsTrue(run.CrateOpen, "a rejected take must leave the crate open");
        }

        [Test]
        public void CrateDraw_ExcludesOwnedPartsAndTheCurrentShelf()
        {
            // Overlap with the shelf reads as a bug and would open a duplicate-ownership path: keep the
            // part from the crate, then buy the shelf's copy of the same part.
            var shop = new ShopLogic(seed: 3);
            var run = new RunState { Money = 99 };
            List<PartDef> pool = Pool(10);
            shop.BeginVisit(pool, run);

            Assert.IsTrue(shop.TryBuyCrate(pool, run, CratePrice, CrateDraw));
            foreach (PartDef drawn in run.CrateContents)
            {
                CollectionAssert.DoesNotContain(shop.Offers, drawn, "crate must not duplicate the shelf");
                Assert.IsFalse(run.Owns(drawn), "crate must not draw an already-owned part");
            }
        }

        [Test]
        public void Buy_RefusesAPartAlreadyOwned()
        {
            // The duplicate-ownership backstop. Parts are uniques and can now arrive by two routes, so the
            // shelf must never sell one the run already holds.
            var shop = new ShopLogic(seed: 5);
            var run = new RunState { Money = 99 };
            shop.BeginVisit(Pool(8), run);

            PartDef offered = shop.Offers[0];
            run.OwnedParts.Add(offered); // reached the inventory by some other route (e.g. a crate)

            Assert.IsFalse(shop.TryBuy(offered, run));
            Assert.AreEqual(1, run.OwnedParts.Count, "must not be ownable twice");
            Assert.AreEqual(99, run.Money, "a refused buy must not charge");
        }

        [Test]
        public void CrateContents_SurviveASaveResumeRoundTrip()
        {
            // THE reason crate contents live on RunState: the crate is paid for at buy time and RunDirector
            // saves on every purchase, so if the pick didn't persist, quitting mid-crate would restore the
            // spend with nothing drawn and the money would simply evaporate.
            var pool = ScriptableObject.CreateInstance<PartPool>();
            pool.Parts = new List<PartDef> { IdPart("a"), IdPart("b"), IdPart("c") };

            var shop = new ShopLogic(seed: 11);
            var run = new RunState { Money = 20 };
            shop.BeginVisit(new List<PartDef>(), run); // empty shelf so the crate can draw the whole pool
            Assert.IsTrue(shop.TryBuyCrate(pool.Parts, run, CratePrice, CrateDraw));

            List<string> before = run.CrateContents.ConvertAll(p => p.Id);
            RunState resumed = RunSave.From(run).ToRunState(pool);

            Assert.IsTrue(resumed.CrateOpen, "a paid-for crate must still be waiting after a resume");
            CollectionAssert.AreEqual(before, resumed.CrateContents.ConvertAll(p => p.Id));
            Assert.AreEqual(20 - CratePrice, resumed.Money);
        }

        [Test]
        public void NoCrate_RoundTripsAsClosed()
        {
            // An older save has no crate ids at all; it must resume as "no crate open", not a stuck pick.
            var pool = ScriptableObject.CreateInstance<PartPool>();
            pool.Parts = new List<PartDef> { IdPart("a") };

            RunState resumed = RunSave.From(new RunState { Money = 9 }).ToRunState(pool);
            Assert.IsFalse(resumed.CrateOpen);
            Assert.AreEqual(0, resumed.CrateContents.Count);
        }
    }
}
