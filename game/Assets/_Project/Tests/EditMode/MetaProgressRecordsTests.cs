using NUnit.Framework;
using Shitboxer.Meta;
using UnityEngine;

namespace Shitboxer.Tests
{
    /// <summary>
    /// The additive meta-progression memory of performance: per-track best-lap records and the bounded
    /// rolling run-history log on <see cref="MetaProgress"/>. Covers the improvement-only lap rule and
    /// its sentinel, history append + trim-to-cap ordering, a JSON round-trip that preserves both, and
    /// back-compat — a profile saved WITHOUT the new fields loads with empty (non-null) records.
    /// </summary>
    public class MetaProgressRecordsTests : TestBase
    {
        // ---- Best-lap records ----------------------------------------------

        [Test]
        public void RecordBestLap_FirstLap_IsANewRecord_AndStored()
        {
            var meta = new MetaProgress();
            Assert.IsTrue(meta.RecordBestLap("track_a", 91.5f)); // first valid lap is always a record
            Assert.AreEqual(91.5f, meta.BestLap("track_a"), 1e-4f);
            Assert.IsTrue(meta.HasLapRecord("track_a"));
        }

        [Test]
        public void RecordBestLap_ReturnsTrueOnlyOnImprovement_AndKeepsTheMinimum()
        {
            var meta = new MetaProgress();
            Assert.IsTrue(meta.RecordBestLap("track_a", 90f));   // new
            Assert.IsFalse(meta.RecordBestLap("track_a", 95f));  // slower — rejected
            Assert.IsFalse(meta.RecordBestLap("track_a", 90f));  // equal — not an improvement
            Assert.IsTrue(meta.RecordBestLap("track_a", 88.25f)); // faster — improvement

            Assert.AreEqual(88.25f, meta.BestLap("track_a"), 1e-4f); // stores the fastest (min) seen
            Assert.AreEqual(1, meta.lapRecords.Count);              // still one entry for the track
        }

        [Test]
        public void RecordBestLap_TracksAreIndependent()
        {
            var meta = new MetaProgress();
            meta.RecordBestLap("track_a", 90f);
            meta.RecordBestLap("track_b", 120f);

            Assert.AreEqual(90f, meta.BestLap("track_a"), 1e-4f);
            Assert.AreEqual(120f, meta.BestLap("track_b"), 1e-4f);
            Assert.AreEqual(2, meta.lapRecords.Count);
        }

        [Test]
        public void RecordBestLap_IgnoresInvalidInput()
        {
            var meta = new MetaProgress();
            Assert.IsFalse(meta.RecordBestLap("track_a", 0f));    // non-positive lap ignored
            Assert.IsFalse(meta.RecordBestLap("track_a", -5f));   // negative lap ignored
            Assert.IsFalse(meta.RecordBestLap(null, 90f));        // null trackId ignored
            Assert.IsFalse(meta.RecordBestLap("", 90f));          // blank trackId ignored
            Assert.AreEqual(0, meta.lapRecords.Count);
        }

        [Test]
        public void BestLap_ReturnsSentinel_WhenAbsent()
        {
            var meta = new MetaProgress();
            Assert.AreEqual(MetaProgress.NoLapRecord, meta.BestLap("never_raced"), 1e-4f);
            Assert.AreEqual(0f, MetaProgress.NoLapRecord);        // the sentinel is 0
            Assert.IsFalse(meta.HasLapRecord("never_raced"));
            Assert.AreEqual(MetaProgress.NoLapRecord, meta.BestLap(null), 1e-4f); // null is safe
        }

        // ---- Run history ----------------------------------------------------

        [Test]
        public void RecordRun_AppendsNewestLast_InOrder()
        {
            var meta = new MetaProgress();
            meta.RecordRun(new RunHistoryEntry { circuitsCleared = 0, finalMoney = 10, stakeLevel = 0, timestamp = 100 });
            meta.RecordRun(new RunHistoryEntry { circuitsCleared = 1, finalMoney = 20, stakeLevel = 0, timestamp = 200 });
            meta.RecordRun(new RunHistoryEntry { circuitsCleared = 2, finalMoney = 30, stakeLevel = 1, timestamp = 300 });

            Assert.AreEqual(3, meta.runHistory.Count);
            Assert.AreEqual(10, meta.runHistory[0].finalMoney);   // oldest first
            Assert.AreEqual(30, meta.runHistory[2].finalMoney);   // newest last
            Assert.AreEqual(1, meta.runHistory[2].stakeLevel);
            Assert.AreEqual(300, meta.runHistory[2].timestamp);
        }

        [Test]
        public void RecordRun_TrimsToCap_KeepingTheMostRecent()
        {
            var meta = new MetaProgress();
            int extra = 5;
            int total = MetaProgress.MaxRunHistory + extra;
            for (int i = 0; i < total; i++)
                meta.RecordRun(new RunHistoryEntry { finalMoney = i, timestamp = i });

            Assert.AreEqual(MetaProgress.MaxRunHistory, meta.runHistory.Count); // trimmed to the cap
            // The first `extra` entries (money 0..extra-1) were dropped; the window keeps the newest run.
            Assert.AreEqual(extra, meta.runHistory[0].finalMoney);
            Assert.AreEqual(total - 1, meta.runHistory[meta.runHistory.Count - 1].finalMoney);
        }

        // ---- Persistence round-trip ----------------------------------------

        [Test]
        public void Records_RoundTrip_ThroughJson()
        {
            var meta = new MetaProgress { totalRuns = 3, lifetimeMoney = 123 };
            meta.RecordBestLap("track_a", 88.5f);
            meta.RecordBestLap("track_b", 101.25f);
            meta.RecordRun(new RunHistoryEntry { circuitsCleared = 2, finalMoney = 40, stakeLevel = 1, timestamp = 777 });

            // Through the actual JSON text (proves it survives JsonUtility, not just a reference copy).
            string json = JsonUtility.ToJson(meta);
            var restored = JsonUtility.FromJson<MetaProgress>(json);

            Assert.AreEqual(3, restored.totalRuns);               // existing fields still round-trip
            Assert.AreEqual(88.5f, restored.BestLap("track_a"), 1e-4f);
            Assert.AreEqual(101.25f, restored.BestLap("track_b"), 1e-4f);
            Assert.AreEqual(1, restored.runHistory.Count);
            Assert.AreEqual(2, restored.runHistory[0].circuitsCleared);
            Assert.AreEqual(40, restored.runHistory[0].finalMoney);
            Assert.AreEqual(1, restored.runHistory[0].stakeLevel);
            Assert.AreEqual(777, restored.runHistory[0].timestamp);
        }

        [Test]
        public void Records_RoundTrip_ThroughDisk()
        {
            var meta = new MetaProgress();
            meta.RecordBestLap("track_a", 75f);
            meta.RecordRun(new RunHistoryEntry { circuitsCleared = 1, finalMoney = 55, stakeLevel = 0, timestamp = 42 });

            string path = System.IO.Path.Combine(
                Application.temporaryCachePath, "shitboxer_meta_records_roundtrip.json");
            try
            {
                MetaProgress.Save(meta, path);
                MetaProgress loaded = MetaProgress.Load(path);

                Assert.AreEqual(75f, loaded.BestLap("track_a"), 1e-4f);
                Assert.AreEqual(1, loaded.runHistory.Count);
                Assert.AreEqual(55, loaded.runHistory[0].finalMoney);
            }
            finally
            {
                MetaProgress.Delete(path);
            }
        }

        // ---- Back-compat: a save written before these fields existed --------

        [Test]
        public void Load_OldProfileWithoutRecordFields_YieldsEmptyRecords()
        {
            // A profile JSON as written before lap records / run history existed: only the original
            // fields are present, so the new List fields are simply absent from the file.
            const string oldJson =
                "{\"totalRuns\":4,\"bestCircuitReached\":1,\"seasonsCleared\":0," +
                "\"lifetimeMoney\":80,\"unlocks\":[\"stake2\"]}";
            string path = System.IO.Path.Combine(
                Application.temporaryCachePath, "shitboxer_meta_records_backcompat.json");
            System.IO.File.WriteAllText(path, oldJson);
            try
            {
                MetaProgress loaded = MetaProgress.Load(path);

                // Original fields still load exactly.
                Assert.AreEqual(4, loaded.totalRuns);
                Assert.AreEqual(80, loaded.lifetimeMoney);
                Assert.IsTrue(loaded.IsUnlocked("stake2"));

                // New record stores load empty and non-null (never a NullReferenceException).
                Assert.IsNotNull(loaded.lapRecords);
                Assert.IsNotNull(loaded.runHistory);
                Assert.AreEqual(0, loaded.lapRecords.Count);
                Assert.AreEqual(0, loaded.runHistory.Count);
                Assert.AreEqual(MetaProgress.NoLapRecord, loaded.BestLap("track_a"), 1e-4f);

                // And the loaded profile is still writable — recording works post-load.
                Assert.IsTrue(loaded.RecordBestLap("track_a", 60f));
            }
            finally
            {
                MetaProgress.Delete(path);
            }
        }
    }
}
