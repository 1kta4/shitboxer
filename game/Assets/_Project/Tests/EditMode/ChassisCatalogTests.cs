using NUnit.Framework;
using Shitboxer.Meta;

namespace Shitboxer.Tests
{
    /// <summary>
    /// The chassis-select catalog and The Brute's unlock (doc 08 slice 11). Ids double as indices
    /// into RunDirector.chassisSpecs, so their shape is load-bearing, not cosmetic.
    /// </summary>
    public class ChassisCatalogTests : TestBase
    {
        [Test]
        public void Ids_AreContiguousFromZero_BecauseTheyIndexTheSpecArray()
        {
            for (int i = 0; i < ChassisCatalog.All.Count; i++)
                Assert.AreEqual(i, ChassisCatalog.All[i].Id,
                    "catalog ids map straight into RunDirector.chassisSpecs — a gap would select the wrong car");
        }

        [Test]
        public void Starters_AreAlwaysUnlocked_EvenWithNoProfile()
        {
            Assert.IsTrue(ChassisCatalog.IsUnlocked(ChassisCatalog.All[0], null), "GripBox needs no profile");
            Assert.IsTrue(ChassisCatalog.IsUnlocked(ChassisCatalog.All[1], null), "PowerBox needs no profile");
        }

        [Test]
        public void Brute_IsLockedUntilItsSeasonClearFlag()
        {
            ChassisInfo brute = ChassisCatalog.All[2];
            Assert.AreEqual(ChassisCatalog.BruteUnlockFlag, brute.UnlockFlag,
                "the catalog entry and the flag RunDirector grants on a season clear must be the same string");

            var meta = new MetaProgress();
            Assert.IsFalse(ChassisCatalog.IsUnlocked(brute, meta), "a fresh profile has not earned The Brute");
            Assert.IsFalse(ChassisCatalog.IsUnlocked(brute, null), "no profile certainly hasn't");

            meta.Unlock(ChassisCatalog.BruteUnlockFlag); // what RecordRunEndToMeta does on seasonCleared
            Assert.IsTrue(ChassisCatalog.IsUnlocked(brute, meta), "a cleared season opens The Brute");
        }
    }
}
