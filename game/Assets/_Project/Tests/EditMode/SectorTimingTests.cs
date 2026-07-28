using NUnit.Framework;
using Shitboxer.Race;

namespace Shitboxer.Tests
{
    /// <summary>
    /// Covers the F1 timing-screen colours: purple = fastest anyone has run this sector all session,
    /// green = your own personal best, yellow = neither. The ordering matters more than it looks —
    /// a purple time also beats your personal best, so testing green first would report every purple
    /// sector as merely green.
    /// </summary>
    public class SectorTimingTests : TestBase
    {
        const float Unset = -1f;

        [Test]
        public void FirstTimeEverSet_IsPurple()
        {
            // The first car through a sector genuinely does hold the fastest time in it.
            Assert.AreEqual(SectorColour.Purple, SectorTiming.Classify(22.5f, Unset, Unset));
        }

        [Test]
        public void BeatingTheSession_IsPurple()
        {
            Assert.AreEqual(SectorColour.Purple, SectorTiming.Classify(21.0f, 22.0f, 21.5f));
        }

        [Test]
        public void BeatingPersonalButNotSession_IsGreen()
        {
            Assert.AreEqual(SectorColour.Green, SectorTiming.Classify(21.8f, 22.0f, 21.5f));
        }

        [Test]
        public void BeatingNeither_IsYellow()
        {
            Assert.AreEqual(SectorColour.Yellow, SectorTiming.Classify(23.0f, 22.0f, 21.5f));
        }

        [Test]
        public void MatchingTheSessionBestExactly_IsNotPurple()
        {
            // Ties don't take the record — the incumbent keeps it. It still beats a slower personal best.
            Assert.AreEqual(SectorColour.Green, SectorTiming.Classify(21.5f, 22.0f, 21.5f));
        }

        [Test]
        public void MatchingYourOwnBestExactly_IsYellow()
        {
            Assert.AreEqual(SectorColour.Yellow, SectorTiming.Classify(22.0f, 22.0f, 21.5f));
        }

        [Test]
        public void PurpleAlwaysOutranksGreen()
        {
            // A time fast enough to be purple must never be reported as green, whatever the personal
            // best happens to be. This is the ordering guard.
            for (float personal = 21.0f; personal <= 25f; personal += 0.5f)
                Assert.AreEqual(SectorColour.Purple, SectorTiming.Classify(20.0f, personal, 21.0f));
        }

        [Test]
        public void PersonalBestUnsetButSessionSet_BeatingSession_IsPurple()
        {
            // A car's first-ever run at a sector that someone else already owns.
            Assert.AreEqual(SectorColour.Purple, SectorTiming.Classify(20.0f, Unset, 21.0f));
        }

        [Test]
        public void PersonalBestUnsetAndSlowerThanSession_IsGreen()
        {
            Assert.AreEqual(SectorColour.Green, SectorTiming.Classify(23.0f, Unset, 21.0f));
        }

        [Test]
        public void NonPositiveOrInfiniteTime_HasNoColour()
        {
            Assert.AreEqual(SectorColour.None, SectorTiming.Classify(0f, 22f, 21f));
            Assert.AreEqual(SectorColour.None, SectorTiming.Classify(-3f, 22f, 21f));
            Assert.AreEqual(SectorColour.None, SectorTiming.Classify(float.PositiveInfinity, 22f, 21f));
        }

        [Test]
        public void Fold_KeepsTheMinimum_AndFirstTimeAlwaysWins()
        {
            Assert.AreEqual(22.0f, SectorTiming.Fold(Unset, 22.0f), 1e-4f);
            Assert.AreEqual(21.0f, SectorTiming.Fold(22.0f, 21.0f), 1e-4f);
            Assert.AreEqual(21.0f, SectorTiming.Fold(21.0f, 22.0f), 1e-4f);
        }

        [Test]
        public void Fold_RejectsADegenerateZeroTime()
        {
            // Two boundaries credited inside one physics step yields a 0-second second sector. Folding
            // that in would pin the session best at ~0 and make every genuine sector afterwards yellow —
            // a corruption that would persist for the whole session.
            Assert.AreEqual(22.0f, SectorTiming.Fold(22.0f, 0f), 1e-4f);
            Assert.AreEqual(22.0f, SectorTiming.Fold(22.0f, -3f), 1e-4f);
            Assert.AreEqual(Unset, SectorTiming.Fold(Unset, 0f), 1e-4f,
                "and it must not install 0 as the first-ever best either");
        }

        [Test]
        public void Elapsed_IsClampedNonNegative()
        {
            Assert.AreEqual(5f, SectorTiming.Elapsed(20f, 15f), 1e-4f);
            Assert.AreEqual(0f, SectorTiming.Elapsed(10f, 15f), 1e-4f);
        }

        [Test]
        public void ClassifyThenFold_ReproducesATimingScreen()
        {
            // Walk a two-car session through one sector each and assert the sequence a real timing
            // screen would show: first car purple, slower rival yellow, rival improves to green, then
            // takes purple off the leader.
            float sessionBest = Unset;
            float carA = Unset, carB = Unset;

            Assert.AreEqual(SectorColour.Purple, SectorTiming.Classify(22.0f, carA, sessionBest));
            carA = SectorTiming.Fold(carA, 22.0f);
            sessionBest = SectorTiming.Fold(sessionBest, 22.0f);

            // carB's first run is slower than the session but is still its own best, so it's green.
            Assert.AreEqual(SectorColour.Green, SectorTiming.Classify(23.5f, carB, sessionBest));
            carB = SectorTiming.Fold(carB, 23.5f);

            // Improves on itself, still short of the leader.
            Assert.AreEqual(SectorColour.Green, SectorTiming.Classify(22.4f, carB, sessionBest));
            carB = SectorTiming.Fold(carB, 22.4f);

            // A lap that would NOT beat its own best now reads yellow.
            Assert.AreEqual(SectorColour.Yellow, SectorTiming.Classify(23.0f, carB, sessionBest));

            // And finally takes purple off the leader.
            Assert.AreEqual(SectorColour.Purple, SectorTiming.Classify(21.7f, carB, sessionBest));
        }
    }
}
