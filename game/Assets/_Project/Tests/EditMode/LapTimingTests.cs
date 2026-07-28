using NUnit.Framework;
using Shitboxer.Meta;
using Shitboxer.Race;

namespace Shitboxer.Tests
{
    /// <summary>
    /// Pins the wave-12 lap-timing vertical slice at its two testable seams, both pure so no scene,
    /// clock or MonoBehaviour is needed: the <see cref="LapTiming"/> math the RaceManager uses to turn a
    /// lap-start stamp and the race clock into last/best lap seconds, and the record-write decision the
    /// RunDirector delegates to <see cref="MetaProgress.RecordBestLap"/> when it persists the player's
    /// fastest lap on a finish. The load-bearing contract: the delta is a plain clamped subtraction, the
    /// best lap keeps the minimum across a race, and the persisted record only improves on a faster lap —
    /// all purely additive history with no gameplay or economy effect.
    /// </summary>
    public class LapTimingTests : TestBase
    {
        // Mirrors how the referee folds a run of validated laps into BestLapTimeS: start from the
        // "no lap yet" sentinel (-1) and Fold each lap time in order. Returns the fastest.
        private static float FoldLaps(params float[] lapTimes)
        {
            float best = -1f;
            foreach (float lap in lapTimes) best = LapTiming.Fold(best, lap);
            return best;
        }

        // ---- Lap-time delta math ----------------------------------------------------

        [Test]
        public void Elapsed_IsTheClockDeltaSinceLapStart()
        {
            // Lap 1 times from the green flag (start 0): validating at 30.0 is a 30.0 s lap.
            Assert.AreEqual(30f, LapTiming.Elapsed(30f, 0f), 1e-4f);
            // Lap 2 started when lap 1 validated (30.0): validating at 58.5 is a 28.5 s lap.
            Assert.AreEqual(28.5f, LapTiming.Elapsed(58.5f, 30f), 1e-4f);
        }

        [Test]
        public void Elapsed_ClampsNonNegative_DuringCountdownAndOnInversion()
        {
            // During the pre-green countdown the race clock is negative; a not-yet-started lap reads 0.
            Assert.AreEqual(0f, LapTiming.Elapsed(-1.2f, 0f), 1e-4f);
            // Defensive: a start stamp ahead of "now" never yields a negative lap time.
            Assert.AreEqual(0f, LapTiming.Elapsed(5f, 9f), 1e-4f);
        }

        // ---- Best lap tracks the minimum across a race ------------------------------

        [Test]
        public void Fold_FirstValidLapAlwaysBecomesTheBest()
        {
            // The sentinel is negative ("no lap yet"), so the first completed lap is always taken.
            Assert.AreEqual(42.3f, LapTiming.Fold(-1f, 42.3f), 1e-4f);
        }

        [Test]
        public void Fold_KeepsTheFastestLapAcrossARace()
        {
            // A three-lap race: improve, then a slower lap must NOT replace the best.
            float best = -1f;
            best = LapTiming.Fold(best, 30f);    // first lap sets the best
            Assert.AreEqual(30f, best, 1e-4f);
            best = LapTiming.Fold(best, 28.5f);  // faster — new best
            Assert.AreEqual(28.5f, best, 1e-4f);
            best = LapTiming.Fold(best, 31f);    // slower — best is unchanged
            Assert.AreEqual(28.5f, best, 1e-4f);
            best = LapTiming.Fold(best, 28.5f);  // equal — still the minimum, unchanged
            Assert.AreEqual(28.5f, best, 1e-4f);
        }

        [Test]
        public void FoldLaps_YieldsTheMinimumOfTheRace()
        {
            Assert.AreEqual(27.9f, FoldLaps(30f, 28.5f, 31f, 27.9f), 1e-4f);
        }

        // ---- Record-write decision on finish (RunDirector -> RecordBestLap) ---------

        [Test]
        public void RecordBestLapOnFinish_ImprovesOnlyOnAFasterLap()
        {
            // The exact chain RunDirector runs at race end: fold the race's laps into a best, then hand
            // that best to MetaProgress.RecordBestLap for the current track.
            var meta = new MetaProgress();
            const string track = "greybox";

            // Run A sets the first record (fastest of its laps = 28.5).
            float bestA = FoldLaps(30f, 28.5f, 31f);
            Assert.IsTrue(meta.RecordBestLap(track, bestA), "the first valid finish is always a new record");
            Assert.AreEqual(28.5f, meta.BestLap(track), 1e-4f);

            // Run B is slower overall (fastest = 29.0): the persisted record is NOT beaten.
            float bestB = FoldLaps(29f, 30f);
            Assert.IsFalse(meta.RecordBestLap(track, bestB), "a slower race must not overwrite the record");
            Assert.AreEqual(28.5f, meta.BestLap(track), 1e-4f);

            // Run C is faster (fastest = 27.2): the record improves.
            float bestC = FoldLaps(27.2f, 40f);
            Assert.IsTrue(meta.RecordBestLap(track, bestC), "a faster race improves the record");
            Assert.AreEqual(27.2f, meta.BestLap(track), 1e-4f);
        }

        [Test]
        public void RecordBestLapOnFinish_IgnoresARaceWithNoValidatedLap()
        {
            // A car that validated no lap carries BestLapTimeS == -1 (the sentinel); the record write is a
            // no-op and leaves the track without a record, exactly as RecordPlayerBestLap relies on.
            var meta = new MetaProgress();
            const string track = "greybox";

            float noLap = FoldLaps(); // no laps folded -> stays at the -1 sentinel
            Assert.Less(noLap, 0f, "an unraced lap must stay at the negative sentinel");
            Assert.IsFalse(meta.RecordBestLap(track, noLap), "a race with no lap records nothing");
            Assert.IsFalse(meta.HasLapRecord(track));
            Assert.AreEqual(MetaProgress.NoLapRecord, meta.BestLap(track), 1e-4f);
        }
    }
}
