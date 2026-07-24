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
        public void APackIsNotSoldWhenTheCarIsAlreadyFull()
        {
            // A pack's prize is always equipped, so selling one whose contents can't be taken would be
            // taking money for nothing. Refuse at buy time instead.
            var shop = new ShopLogic(seed: 7);
            var run = new RunState { Money = 99, MaxEquipSlots = 1 };
            shop.BeginVisit(Pool(12), run);

            Assert.IsTrue(shop.TryBuyCrate(Pool(12), run, CratePrice, CrateDraw));
            PartDef first = run.CrateContents[0];
            Assert.IsTrue(shop.TryTakeFromCrate(first, run));
            Assert.IsTrue(run.IsEquipped(first), "a bought part is always equipped");

            int money = run.Money;
            Assert.IsFalse(shop.TryBuyCrate(Pool(12), run, CratePrice, CrateDraw),
                "car is full — the pack must not be sold");
            Assert.AreEqual(money, run.Money, "a refused pack charges nothing");
        }

        [Test]
        public void SellingFreesASlotAndRefundsHalf()
        {
            var shop = new ShopLogic(seed: 21);
            var run = new RunState { Money = 100, MaxEquipSlots = 1 };
            shop.BeginVisit(Pool(6), run);

            PartDef bought = shop.Offers[0];
            shop.TryBuy(bought, run);
            int afterBuy = run.Money;

            Assert.IsTrue(shop.TrySell(bought, run));
            Assert.AreEqual(afterBuy + shop.SellValueOf(bought), run.Money);
            Assert.IsFalse(run.Owns(bought), "a sold part leaves the run entirely — it is not benched");
            Assert.IsTrue(run.HasFreeSlot);
        }

        [Test]
        public void SellValueIsHalfPriceFlooredAtOne()
        {
            var shop = new ShopLogic(seed: 22);
            Assert.AreEqual(3, shop.SellValueOf(Part(6)));
            Assert.AreEqual(1, shop.SellValueOf(Part(1)), "nothing is ever worthless");
            Assert.AreEqual(0, shop.SellValueOf(null));
        }

        [Test]
        public void CannotBuyWhenEveryPartSlotIsFull()
        {
            var shop = new ShopLogic(seed: 23);
            var run = new RunState { Money = 100, MaxEquipSlots = 1 };
            shop.BeginVisit(Pool(6), run);

            Assert.IsTrue(shop.TryBuy(shop.Offers[0], run));
            int money = run.Money;
            Assert.IsFalse(shop.TryBuy(shop.Offers[0], run), "car is full");
            Assert.AreEqual(money, run.Money, "a refused buy charges nothing");
        }

        // ---- consecutive-reroll escalation -------------------------------------------------------

        [Test]
        public void ConsecutiveRerollsEscalate()
        {
            var shop = new ShopLogic(seed: 24);
            var run = new RunState { Money = 100 };
            shop.BeginVisit(Pool(20), run);

            Assert.AreEqual(ShopLogic.BaseRerollCost, shop.RerollCost);
            shop.TryReroll(Pool(20), run);
            Assert.AreEqual(ShopLogic.BaseRerollCost + ShopLogic.RerollCostStep, shop.RerollCost);
            shop.TryReroll(Pool(20), run);
            Assert.AreEqual(ShopLogic.BaseRerollCost + 2 * ShopLogic.RerollCostStep, shop.RerollCost);
        }

        [Test]
        public void BuyingResetsTheRerollEscalation()
        {
            // The escalation punishes FISHING, not shopping: a player who engages with the shelf gets a
            // cheap reroll again, while one who only spins pays more each time.
            var shop = new ShopLogic(seed: 25);
            var run = new RunState { Money = 200 };
            shop.BeginVisit(Pool(20), run);

            shop.TryReroll(Pool(20), run);
            shop.TryReroll(Pool(20), run);
            Assert.Greater(shop.RerollCost, ShopLogic.BaseRerollCost);

            shop.TryBuy(shop.Offers[0], run);
            Assert.AreEqual(ShopLogic.BaseRerollCost, shop.RerollCost, "a purchase breaks the streak");
        }

        [Test]
        public void SellingAlsoBreaksTheRerollStreak()
        {
            var shop = new ShopLogic(seed: 26);
            var run = new RunState { Money = 200 };
            shop.BeginVisit(Pool(20), run);

            PartDef bought = shop.Offers[0];
            shop.TryBuy(bought, run);
            shop.TryReroll(Pool(20), run);
            shop.TryReroll(Pool(20), run);
            Assert.Greater(shop.RerollCost, ShopLogic.BaseRerollCost);

            shop.TrySell(bought, run);
            Assert.AreEqual(ShopLogic.BaseRerollCost, shop.RerollCost);
        }

        // ---- packs ---------------------------------------------------------------------------------

        [Test]
        public void EveryVisitOffersTwoPacks()
        {
            var shop = new ShopLogic(seed: 27);
            var run = new RunState { Money = 100 };
            shop.BeginVisit(Pool(20), run);
            Assert.AreEqual(ShopLogic.PacksPerVisit, shop.Packs.Count);
        }

        [Test]
        public void SpectralPacks_AreStockedButScarce_SinceEditionsBecameMaterials()
        {
            // Slice 13 deliberately retired the old "never stocked" pin: spectrals ARE content now
            // (edition materials for fitted stat parts). The pack rolls, but as the scarce slot —
            // its prize outclasses one component level. The money-safety half of the old rule
            // survives at BUY time instead: TryBuyPack refuses a Spectral when nothing fitted can
            // take an edition (see SpectralPackTests).
            Assert.Greater(ShopPackCatalog.Weight(ShopPackKind.Spectral), 0,
                "spectrals have content now — a zero weight would be dead code again");
            Assert.Less(ShopPackCatalog.Weight(ShopPackKind.Spectral), ShopPackCatalog.Weight(ShopPackKind.Components),
                "the material pack stays the scarce pull");

            var shop = new ShopLogic(seed: 28);
            var run = new RunState { Money = 100 };
            bool sawSpectral = false;
            for (int visit = 0; visit < 200 && !sawSpectral; visit++)
            {
                shop.BeginVisit(Pool(20), run);
                foreach (ShopPack pack in shop.Packs)
                    if (pack.Kind == ShopPackKind.Spectral) sawSpectral = true;
            }
            Assert.IsTrue(sawSpectral, "a positive weight must actually reach the shelf");
        }

        [Test]
        public void AComponentsPackLevelsThePickedComponent()
        {
            var shop = new ShopLogic(seed: 29);
            var run = new RunState { Money = 100 };
            shop.BeginVisit(Pool(20), run);

            int index = FindPack(shop, ShopPackKind.Components);
            if (index < 0) Assert.Ignore("this seed rolled no components pack");

            int price = shop.Packs[index].Price;
            Assert.IsTrue(shop.TryBuyPack(index, Pool(20), run));
            Assert.AreEqual(100 - price, run.Money);
            Assert.IsTrue(run.ComponentPackOpen);

            var picked = (CarComponent)run.PackComponents[0];
            int before = run.LevelOf(picked);
            Assert.IsTrue(shop.TryTakeComponent(picked, run));
            Assert.AreEqual(before + 1, run.LevelOf(picked));
            Assert.IsFalse(run.ComponentPackOpen, "the pack closes on the pick");
        }

        // --- Blueprints: component levels are ROLLED, not browsed ----------------------------------

        [Test]
        public void BeginVisit_StocksDistinctBlueprints()
        {
            var shop = new ShopLogic(seed: 40);
            var run = new RunState { Money = 100 };
            shop.BeginVisit(Pool(20), run);

            Assert.AreEqual(ShopLogic.BlueprintOfferCount, shop.Blueprints.Count);
            CollectionAssert.AllItemsAreUnique(shop.Blueprints);
        }

        [Test]
        public void BuyingABlueprintChargesLevelsAndLeavesTheShelf()
        {
            var shop = new ShopLogic(seed: 41);
            var run = new RunState { Money = 100 };
            shop.BeginVisit(Pool(20), run);

            CarComponent offered = shop.Blueprints[0];
            int price = run.BlueprintPriceFor(offered);
            int level = run.LevelOf(offered);

            Assert.IsTrue(shop.TryBuyBlueprint(offered, run));
            Assert.AreEqual(100 - price, run.Money);
            Assert.AreEqual(level + 1, run.LevelOf(offered));
            CollectionAssert.DoesNotContain(shop.Blueprints, offered,
                "a bought Blueprint is consumed — the next level has to turn up again");
        }

        [Test]
        public void ABlueprintNotOnTheShelfIsRefused()
        {
            // The load-bearing rule of the rework: with the ten-row list demoted to a read-out, the shelf
            // check is the ONLY thing standing between the player and buying any component at will.
            var shop = new ShopLogic(seed: 42);
            var run = new RunState { Money = 100 };
            shop.BeginVisit(Pool(20), run);

            CarComponent unstocked = FirstUnstocked(shop);
            int level = run.LevelOf(unstocked);

            Assert.IsFalse(shop.TryBuyBlueprint(unstocked, run));
            Assert.AreEqual(100, run.Money, "a refused buy charges nothing");
            Assert.AreEqual(level, run.LevelOf(unstocked));
        }

        [Test]
        public void ARerollRestocksTheBlueprintRow()
        {
            var shop = new ShopLogic(seed: 43);
            var run = new RunState { Money = 100 };
            shop.BeginVisit(Pool(20), run);

            Assert.IsTrue(shop.TryBuyBlueprint(shop.Blueprints[0], run));
            Assert.AreEqual(ShopLogic.BlueprintOfferCount - 1, shop.Blueprints.Count);

            Assert.IsTrue(shop.TryReroll(Pool(20), run));
            Assert.AreEqual(ShopLogic.BlueprintOfferCount, shop.Blueprints.Count,
                "one reroll buys a whole new shelf, Blueprints included");
        }

        [Test]
        public void MaxedComponentsAreNeverStocked()
        {
            // Late in a run the row legitimately runs short rather than offering a level that would do
            // nothing. Everything maxed except Tyres, so there is exactly one legal draw left.
            var shop = new ShopLogic(seed: 44);
            var run = new RunState { Money = 100 };
            foreach (CarComponentInfo info in CarComponentCatalog.All)
                run.ComponentLevels[(int)info.Component] = CarComponentCatalog.MaxLevel;
            run.ComponentLevels[(int)CarComponent.Tyres] = CarComponentCatalog.MinLevel;

            shop.BeginVisit(Pool(20), run);

            Assert.AreEqual(1, shop.Blueprints.Count);
            Assert.AreEqual(CarComponent.Tyres, shop.Blueprints[0]);
        }

        [Test]
        public void BuyingABlueprintBreaksTheRerollStreak()
        {
            // Blueprints are a purchase like any other, so they end the escalating-reroll streak — a
            // player engaging with the shelf gets a cheap reroll back.
            var shop = new ShopLogic(seed: 45);
            var run = new RunState { Money = 200 };
            shop.BeginVisit(Pool(20), run);

            Assert.IsTrue(shop.TryReroll(Pool(20), run));
            Assert.AreEqual(ShopLogic.BaseRerollCost + ShopLogic.RerollCostStep, shop.RerollCost);

            Assert.IsTrue(shop.TryBuyBlueprint(shop.Blueprints[0], run));
            Assert.AreEqual(ShopLogic.BaseRerollCost, shop.RerollCost);
        }

        /// <summary>A component this visit did not stock — with 10 in the catalogue and two on the
        /// shelf there is always one.</summary>
        private static CarComponent FirstUnstocked(ShopLogic shop)
        {
            foreach (CarComponentInfo info in CarComponentCatalog.All)
                if (!ListHas(shop.Blueprints, info.Component)) return info.Component;
            Assert.Fail("every component was stocked — this test can no longer say anything");
            return default;
        }

        private static bool ListHas(IReadOnlyList<CarComponent> list, CarComponent component)
        {
            for (int i = 0; i < list.Count; i++)
                if (list[i] == component) return true;
            return false;
        }

        [Test]
        public void OnlyOnePackCanBeOpenAtATime()
        {
            var shop = new ShopLogic(seed: 30);
            var run = new RunState { Money = 100 };
            shop.BeginVisit(Pool(20), run);

            Assert.IsTrue(shop.TryBuyPack(0, Pool(20), run));
            int money = run.Money;
            Assert.IsFalse(shop.TryBuyPack(0, Pool(20), run), "one pack at a time");
            Assert.AreEqual(money, run.Money);
        }

        [Test]
        public void ABoughtPackLeavesTheShelf()
        {
            var shop = new ShopLogic(seed: 31);
            var run = new RunState { Money = 100 };
            shop.BeginVisit(Pool(20), run);

            int before = shop.Packs.Count;
            Assert.IsTrue(shop.TryBuyPack(0, Pool(20), run));
            Assert.AreEqual(before - 1, shop.Packs.Count);
        }

        [Test]
        public void BuyingAPackBreaksTheRerollStreak()
        {
            var shop = new ShopLogic(seed: 32);
            var run = new RunState { Money = 200 };
            shop.BeginVisit(Pool(20), run);

            shop.TryReroll(Pool(20), run);
            shop.TryReroll(Pool(20), run);
            Assert.Greater(shop.RerollCost, ShopLogic.BaseRerollCost);

            Assert.IsTrue(shop.TryBuyPack(0, Pool(20), run));
            Assert.AreEqual(ShopLogic.BaseRerollCost, shop.RerollCost);
        }

        [Test]
        public void AnOutOfRangePackIndexIsRejected()
        {
            var shop = new ShopLogic(seed: 33);
            var run = new RunState { Money = 100 };
            shop.BeginVisit(Pool(20), run);
            Assert.IsFalse(shop.TryBuyPack(-1, Pool(20), run));
            Assert.IsFalse(shop.TryBuyPack(99, Pool(20), run));
            Assert.AreEqual(100, run.Money);
        }

        private static int FindPack(ShopLogic shop, ShopPackKind kind)
        {
            for (int i = 0; i < shop.Packs.Count; i++)
                if (shop.Packs[i].Kind == kind) return i;
            return -1;
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
