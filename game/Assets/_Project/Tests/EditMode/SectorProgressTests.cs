using NUnit.Framework;
using Shitboxer.Race;

namespace Shitboxer.Tests
{
    /// <summary>
    /// Covers the distance-based sector split — the structural twin of the lap gate with a smaller
    /// divisor. The load-bearing property is the last test here: sectors and laps are derived from the
    /// SAME guarded distance, so they can never disagree about which lap a car is on. Anything that
    /// broke that agreement would mean sectors had become their own gate, which is exactly the
    /// ordered-checkpoint failure the distance gate was introduced to kill.
    /// </summary>
    public class SectorProgressTests : TestBase
    {
        const float Loop = 686f;  // the greybox test track length used in play sessions
        const int Sectors = SectorProgress.DefaultSectorsPerLap;

        static float SectorLen => SectorProgress.SectorLength(Loop, Sectors);

        [Test]
        public void SectorLength_IsAnEqualSplitOfTheLoop()
        {
            Assert.AreEqual(Loop / 3f, SectorProgress.SectorLength(Loop, 3), 1e-3f);
            Assert.AreEqual(Loop, SectorProgress.SectorLength(Loop, 1), 1e-3f);
        }

        [Test]
        public void SectorLength_NonPositiveInputs_AreZero_NoDivideByZero()
        {
            Assert.AreEqual(0f, SectorProgress.SectorLength(0f, 3));
            Assert.AreEqual(0f, SectorProgress.SectorLength(-50f, 3));
            Assert.AreEqual(0f, SectorProgress.SectorLength(Loop, 0));
            Assert.AreEqual(0f, SectorProgress.SectorLength(Loop, -2));
        }

        [Test]
        public void NegativeDistance_IsZeroSectors()
        {
            // Grid cars start just behind the line, so their distance is briefly negative. That must
            // never read as a completed sector.
            Assert.AreEqual(0, SectorProgress.CompletedSectors(-8f, SectorLen));
        }

        [Test]
        public void JustUnderABoundary_HasNotCompleted()
        {
            Assert.AreEqual(0, SectorProgress.CompletedSectors(SectorLen - 1f, SectorLen));
        }

        [Test]
        public void ExactlyOnABoundary_Completes()
        {
            Assert.AreEqual(1, SectorProgress.CompletedSectors(SectorLen, SectorLen));
            Assert.AreEqual(2, SectorProgress.CompletedSectors(2f * SectorLen, SectorLen));
        }

        [Test]
        public void CountingIsContinuousAcrossLaps()
        {
            // A full loop is exactly one lap's worth of sectors; the count does not reset per lap.
            Assert.AreEqual(3, SectorProgress.CompletedSectors(Loop, SectorLen));
            Assert.AreEqual(6, SectorProgress.CompletedSectors(2f * Loop, SectorLen));
        }

        [Test]
        public void NonPositiveSectorLength_IsZero_NoDivideByZero()
        {
            Assert.AreEqual(0, SectorProgress.CompletedSectors(1000f, 0f));
            Assert.AreEqual(0, SectorProgress.CompletedSectors(1000f, -5f));
        }

        [Test]
        public void SectorIndex_WrapsWithinTheLap()
        {
            Assert.AreEqual(0, SectorProgress.SectorIndex(0, 3));
            Assert.AreEqual(1, SectorProgress.SectorIndex(1, 3));
            Assert.AreEqual(2, SectorProgress.SectorIndex(2, 3));
            Assert.AreEqual(0, SectorProgress.SectorIndex(3, 3));   // lap 2, sector 1
            Assert.AreEqual(1, SectorProgress.SectorIndex(4, 3));
        }

        [Test]
        public void SectorIndex_GuardsBadInputs()
        {
            Assert.AreEqual(0, SectorProgress.SectorIndex(5, 0));
            Assert.AreEqual(0, SectorProgress.SectorIndex(5, -3));
            // A negative completed count can't happen upstream (CompletedSectors floors at 0), but the
            // index must stay in range regardless of what it's handed.
            int index = SectorProgress.SectorIndex(-1, 3);
            Assert.GreaterOrEqual(index, 0);
            Assert.Less(index, 3);
        }

        [Test]
        public void TotalSectors_IsLapsTimesSectors()
        {
            Assert.AreEqual(9, SectorProgress.TotalSectors(3, 3));
            Assert.AreEqual(0, SectorProgress.TotalSectors(0, 3));
            Assert.AreEqual(0, SectorProgress.TotalSectors(3, 0));
        }

        [Test]
        public void MonotonicWithDistance()
        {
            int prev = 0;
            for (float d = 0f; d <= 5f * Loop; d += 13f)
            {
                int sectors = SectorProgress.CompletedSectors(d, SectorLen);
                Assert.GreaterOrEqual(sectors, prev);
                prev = sectors;
            }
        }

        [Test]
        public void SectorsAndLapsNeverDisagree()
        {
            // THE invariant. Both are floors of the same distance over divisors that differ by exactly
            // the sector count, so integer-dividing the sector count by that factor must reproduce the
            // lap count at every distance. If this ever fails, sectors have stopped being a readout of
            // the lap gate and become a second, competing gate.
            for (float d = -20f; d <= 6f * Loop; d += 7f)
            {
                int laps = LapProgress.CompletedLaps(d, Loop);
                int sectors = SectorProgress.CompletedSectors(d, SectorLen);
                Assert.AreEqual(laps, sectors / Sectors, $"disagreed at distance {d}");
            }
        }
    }
}
