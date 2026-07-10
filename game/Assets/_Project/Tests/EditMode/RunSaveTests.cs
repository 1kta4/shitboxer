using System.Collections.Generic;
using NUnit.Framework;
using Shitboxer.Meta;
using UnityEngine;

namespace Shitboxer.Tests
{
    /// <summary>
    /// Run reproducibility + persistence: the by-Id save DTO round-trips a RunState (through the
    /// real JSON text form and through disk), and a run seed makes the shop stock deterministic
    /// across restarts.
    /// </summary>
    public class RunSaveTests : TestBase
    {
        private static PartDef Part(string id, int price = 5, Rarity rarity = Rarity.Common)
        {
            var p = ScriptableObject.CreateInstance<PartDef>();
            p.Id = id;
            p.Price = price;
            p.Rarity = rarity;
            return p;
        }

        private static PartPool Pool(params PartDef[] parts)
        {
            var pool = ScriptableObject.CreateInstance<PartPool>();
            pool.Parts = new List<PartDef>(parts);
            return pool;
        }

        private static List<string> IdsOf(IEnumerable<PartDef> parts)
        {
            var ids = new List<string>();
            foreach (PartDef p in parts) ids.Add(p ? p.Id : null);
            return ids;
        }

        [Test]
        public void SaveDto_RoundTrips_ScalarsAndPartIdSets_ThroughJson()
        {
            var a = Part("grip");
            var b = Part("power");
            var c = Part("cash");
            PartPool pool = Pool(a, b, c);

            var run = new RunState
            {
                Money = 37,
                Lives = 2,
                CircuitIndex = 1,
                RaceIndex = 2,
                Seed = 123456,
            };
            run.OwnedParts.Add(a);
            run.OwnedParts.Add(b);
            run.OwnedParts.Add(c);
            Assert.IsTrue(run.Equip(a));
            Assert.IsTrue(run.Equip(c));

            // Through the actual JSON text form (proves it survives JsonUtility, not just a copy).
            RunSave dto = RunSave.From(run);
            string json = JsonUtility.ToJson(dto);
            RunSave restoredDto = JsonUtility.FromJson<RunSave>(json);
            RunState restored = restoredDto.ToRunState(pool);

            Assert.AreEqual(run.Money, restored.Money);
            Assert.AreEqual(run.Lives, restored.Lives);
            Assert.AreEqual(run.CircuitIndex, restored.CircuitIndex);
            Assert.AreEqual(run.RaceIndex, restored.RaceIndex);
            Assert.AreEqual(run.Seed, restored.Seed);

            CollectionAssert.AreEquivalent(new[] { "grip", "power", "cash" }, IdsOf(restored.OwnedParts));
            CollectionAssert.AreEquivalent(new[] { "grip", "cash" }, IdsOf(restored.EquippedParts));

            // Resolved parts are the pool's live instances, so ownership/equip predicates hold.
            Assert.IsTrue(restored.Owns(a));
            Assert.IsTrue(restored.IsEquipped(a));
            Assert.IsTrue(restored.IsEquipped(c));
            Assert.IsFalse(restored.IsEquipped(b));
        }

        [Test]
        public void EquippedId_NotAlsoOwned_IsDroppedOnLoad()
        {
            var a = Part("a");
            PartPool pool = Pool(a);
            var dto = new RunSave { seed = 1, money = 5, lives = 3 };
            dto.equippedPartIds.Add("a"); // equipped but never listed as owned

            RunState run = dto.ToRunState(pool);

            // Equip requires ownership; an equipped-but-unowned id is dropped, not smuggled in.
            Assert.IsFalse(run.IsEquipped(a));
            Assert.AreEqual(0, run.EquippedParts.Count);
        }

        [Test]
        public void UnknownPartIds_AreDropped_NotThrown()
        {
            PartPool pool = Pool(Part("known"));
            var dto = new RunSave { seed = 7 };
            dto.ownedPartIds.Add("known");
            dto.ownedPartIds.Add("vanished"); // no longer in the catalogue

            RunState run = dto.ToRunState(pool);

            Assert.AreEqual(1, run.OwnedParts.Count);
            Assert.AreEqual("known", run.OwnedParts[0].Id);
        }

        [Test]
        public void File_RoundTrips_ThroughDisk()
        {
            var a = Part("front");
            PartPool pool = Pool(a);
            var run = new RunState { Money = 9, Lives = 1, Seed = 42, CircuitIndex = 2, RaceIndex = 1 };
            run.OwnedParts.Add(a);
            Assert.IsTrue(run.Equip(a));

            // Use a scratch path so tests never clobber a real player's save at persistentDataPath.
            string path = System.IO.Path.Combine(
                Application.temporaryCachePath, "shitboxer_runsave_roundtrip.json");
            try
            {
                RunSave.Save(run, path);
                Assert.IsTrue(RunSave.Exists(path));

                Assert.IsTrue(RunSave.TryLoad(pool, path, out RunState loaded));
                Assert.AreEqual(42, loaded.Seed);
                Assert.AreEqual(9, loaded.Money);
                Assert.AreEqual(1, loaded.Lives);
                Assert.AreEqual(2, loaded.CircuitIndex);
                Assert.AreEqual(1, loaded.RaceIndex);
                Assert.IsTrue(loaded.Owns(a));
                Assert.IsTrue(loaded.IsEquipped(a));
            }
            finally
            {
                RunSave.Delete(path);
            }
            Assert.IsFalse(RunSave.Exists(path));
        }

        [Test]
        public void TryLoad_MissingFile_ReturnsFalseAndNull()
        {
            PartPool pool = Pool(Part("x"));
            string path = System.IO.Path.Combine(
                Application.temporaryCachePath, "shitboxer_runsave_missing.json");
            RunSave.Delete(path); // ensure absent

            Assert.IsFalse(RunSave.TryLoad(pool, path, out RunState run));
            Assert.IsNull(run);
        }

        [Test]
        public void SameSeed_ProducesIdenticalShop_IncludingReroll()
        {
            List<PartDef> pool = SeedPool();
            var runA = new RunState { Money = 100 };
            var runB = new RunState { Money = 100 };

            var shopA = new ShopLogic();
            var shopB = new ShopLogic();
            const int visitSeed = 20260710;
            shopA.BeginVisit(pool, runA, visitSeed);
            shopB.BeginVisit(pool, runB, visitSeed);

            CollectionAssert.AreEqual(IdsOf(shopA.Offers), IdsOf(shopB.Offers));

            // The reroll chain continues off the same seeded RNG, so it matches too.
            Assert.IsTrue(shopA.TryReroll(pool, runA));
            Assert.IsTrue(shopB.TryReroll(pool, runB));
            CollectionAssert.AreEqual(IdsOf(shopA.Offers), IdsOf(shopB.Offers));
        }

        [Test]
        public void DifferentSeeds_DoNotAllProduceTheSameShop()
        {
            // Two specific seeds could coincidentally match, so assert across a spread of seeds
            // that they are not ALL identical — the seed demonstrably steers the draw.
            List<PartDef> pool = SeedPool();
            string baseline = null;
            bool sawDifference = false;
            for (int seed = 1; seed <= 8; seed++)
            {
                var shop = new ShopLogic();
                shop.BeginVisit(pool, new RunState { Money = 100 }, seed);
                Assert.AreEqual(ShopLogic.OfferCount, shop.Offers.Count);
                string key = string.Join(",", IdsOf(shop.Offers));
                if (baseline == null) baseline = key;
                else if (key != baseline) sawDifference = true;
            }
            Assert.IsTrue(sawDifference, "the run seed must steer the shop draw");
        }

        [Test]
        public void Reseed_MakesTheSeedlessBeginVisitReproducible()
        {
            List<PartDef> pool = SeedPool();
            var run = new RunState { Money = 100 };

            var shop = new ShopLogic();
            shop.BeginVisit(pool, run, 555);
            var first = new List<string>(IdsOf(shop.Offers));

            shop.Reseed(555);
            shop.BeginVisit(pool, run); // 2-arg overload rolls off the just-reseeded RNG
            CollectionAssert.AreEqual(first, IdsOf(shop.Offers));
        }

        private static List<PartDef> SeedPool() => new List<PartDef>
        {
            Part("c1"), Part("c2"), Part("c3"),
            Part("u1", 7, Rarity.Uncommon), Part("u2", 7, Rarity.Uncommon),
            Part("r1", 12, Rarity.Rare), Part("r2", 12, Rarity.Rare),
        };
    }
}
