using NUnit.Framework;
using Shitboxer.Meta;
using UnityEngine;

namespace Shitboxer.Tests
{
    /// <summary>
    /// The persistent cross-run profile (MetaProgress): its JSON round-trip of lifetime stats + unlock
    /// flags, the stake unlock/gate ladder that drives the roguelike escalation hook, and the
    /// never-throw IO contract (a missing/corrupt file yields a fresh profile, never breaks the game).
    /// </summary>
    public class MetaProgressTests : TestBase
    {
        [Test]
        public void Fresh_DefaultsAreZero_AndOnlyBaseStakeUnlocked()
        {
            var meta = new MetaProgress();
            Assert.AreEqual(0, meta.totalRuns);
            Assert.AreEqual(0, meta.bestCircuitReached);
            Assert.AreEqual(0, meta.seasonsCleared);
            Assert.AreEqual(0, meta.lifetimeMoney);
            Assert.IsNotNull(meta.unlocks);
            Assert.IsTrue(meta.IsStakeUnlocked(0));   // base license is always playable
            Assert.IsFalse(meta.IsStakeUnlocked(1));  // higher stakes are locked until earned
            Assert.AreEqual(0, meta.HighestUnlockedStake);
        }

        [Test]
        public void RegisterSeasonCleared_UnlocksTheNextStake()
        {
            var meta = new MetaProgress();
            string flag = meta.RegisterSeasonCleared(0);   // clear the base-stake season

            Assert.AreEqual(1, meta.seasonsCleared);
            Assert.AreEqual(MetaProgress.StakeFlag(1), flag); // "stake2"
            Assert.IsTrue(meta.IsStakeUnlocked(1));           // stake 1 now playable
            Assert.IsFalse(meta.IsStakeUnlocked(2));          // but not two ahead
            Assert.AreEqual(1, meta.HighestUnlockedStake);

            // Clearing stake 1's season unlocks stake 2 — the ladder climbs one rung per clear.
            string flag2 = meta.RegisterSeasonCleared(1);
            Assert.AreEqual(MetaProgress.StakeFlag(2), flag2); // "stake3"
            Assert.IsTrue(meta.IsStakeUnlocked(2));
            Assert.AreEqual(2, meta.HighestUnlockedStake);
        }

        [Test]
        public void RegisterSeasonCleared_WhenAlreadyUnlocked_ReturnsNull_ButStillCountsSeason()
        {
            var meta = new MetaProgress();
            meta.RegisterSeasonCleared(0);                 // unlocks stake 1
            Assert.AreEqual(1, meta.seasonsCleared);

            string again = meta.RegisterSeasonCleared(0);  // re-clearing base — stake 1 already unlocked
            Assert.IsNull(again);                          // nothing new to unlock
            Assert.AreEqual(2, meta.seasonsCleared);       // the season still counts
        }

        [Test]
        public void RegisterRunEnd_TracksCount_BestCircuit_AndLifetimeMoney()
        {
            var meta = new MetaProgress();
            meta.RegisterRunEnd(circuitReached: 1, endingMoney: 30);
            meta.RegisterRunEnd(circuitReached: 0, endingMoney: 12); // worse circuit, still adds money

            Assert.AreEqual(2, meta.totalRuns);
            Assert.AreEqual(1, meta.bestCircuitReached);   // best is the MAX ever reached, not the last
            Assert.AreEqual(42, meta.lifetimeMoney);       // money accumulates across runs
        }

        [Test]
        public void RegisterRunEnd_NeverSubtractsNegativeEndingMoney()
        {
            var meta = new MetaProgress();
            meta.RegisterRunEnd(0, -5);
            Assert.AreEqual(0, meta.lifetimeMoney);        // a negative wallet never drains the lifetime stat
            Assert.AreEqual(1, meta.totalRuns);
        }

        [Test]
        public void Unlock_IsIdempotent()
        {
            var meta = new MetaProgress();
            Assert.IsTrue(meta.Unlock("powerbox_start"));  // newly unlocked
            Assert.IsFalse(meta.Unlock("powerbox_start")); // already held — no duplicate
            Assert.IsTrue(meta.IsUnlocked("powerbox_start"));
            Assert.AreEqual(1, meta.unlocks.Count);
        }

        [Test]
        public void SaveDto_RoundTrips_StatsAndUnlocks_ThroughJson()
        {
            var meta = new MetaProgress
            {
                totalRuns = 7,
                bestCircuitReached = 2,
                seasonsCleared = 3,
                lifetimeMoney = 512,
            };
            meta.Unlock(MetaProgress.StakeFlag(1)); // "stake2"
            meta.Unlock(MetaProgress.StakeFlag(2)); // "stake3"
            meta.Unlock("powerbox_start");

            // Through the actual JSON text form (proves it survives JsonUtility, not just a copy).
            string json = JsonUtility.ToJson(meta);
            var restored = JsonUtility.FromJson<MetaProgress>(json);

            Assert.AreEqual(7, restored.totalRuns);
            Assert.AreEqual(2, restored.bestCircuitReached);
            Assert.AreEqual(3, restored.seasonsCleared);
            Assert.AreEqual(512, restored.lifetimeMoney);
            CollectionAssert.AreEquivalent(
                new[] { "stake2", "stake3", "powerbox_start" }, restored.unlocks);
            Assert.IsTrue(restored.IsStakeUnlocked(2));       // resolved flags still gate stakes
            Assert.IsTrue(restored.IsUnlocked("powerbox_start"));
        }

        [Test]
        public void File_RoundTrips_ThroughDisk()
        {
            var meta = new MetaProgress { totalRuns = 4, seasonsCleared = 1, lifetimeMoney = 99 };
            meta.Unlock(MetaProgress.StakeFlag(1));

            // Scratch path so tests never clobber a real player's profile at persistentDataPath.
            string path = System.IO.Path.Combine(
                Application.temporaryCachePath, "shitboxer_meta_roundtrip.json");
            try
            {
                MetaProgress.Save(meta, path);
                Assert.IsTrue(MetaProgress.Exists(path));

                MetaProgress loaded = MetaProgress.Load(path);
                Assert.AreEqual(4, loaded.totalRuns);
                Assert.AreEqual(1, loaded.seasonsCleared);
                Assert.AreEqual(99, loaded.lifetimeMoney);
                Assert.IsTrue(loaded.IsStakeUnlocked(1));
            }
            finally
            {
                MetaProgress.Delete(path);
            }
            Assert.IsFalse(MetaProgress.Exists(path));
        }

        [Test]
        public void Load_MissingFile_ReturnsFreshProfile()
        {
            string path = System.IO.Path.Combine(
                Application.temporaryCachePath, "shitboxer_meta_missing.json");
            MetaProgress.Delete(path); // ensure absent

            MetaProgress meta = MetaProgress.Load(path);
            Assert.IsNotNull(meta);           // never null — a fresh profile, so the game never breaks
            Assert.AreEqual(0, meta.totalRuns);
        }

        [Test]
        public void Load_CorruptFile_ReturnsFreshProfile()
        {
            string path = System.IO.Path.Combine(
                Application.temporaryCachePath, "shitboxer_meta_corrupt.json");
            System.IO.File.WriteAllText(path, "{ this is not valid json ]");
            try
            {
                MetaProgress meta = MetaProgress.Load(path);
                Assert.IsNotNull(meta);       // parse failure is swallowed — fresh profile, not a throw
                Assert.AreEqual(0, meta.totalRuns);
            }
            finally
            {
                MetaProgress.Delete(path);
            }
        }
    }
}
