using NUnit.Framework;
using Shitboxer.Race;

namespace Shitboxer.Tests
{
    /// <summary>
    /// Covers the distance-based lap gate that replaced the ordered-checkpoint ring: a car's guarded net
    /// forward distance (metres from the start/finish line) completes one lap per whole loop-length. The
    /// old ring stranded a HUMAN driving off the racing line because the teleport/mis-projection guard
    /// hard-reset lap progress on every projection swing; distance-based counting cannot strand.
    /// </summary>
    public class LapProgressTests : TestBase
    {
        const float Loop = 686f; // matches the greybox test track length used in play sessions

        [Test]
        public void NoDistance_IsZeroLaps()
        {
            Assert.AreEqual(0, LapProgress.CompletedLaps(0f, Loop));
        }

        [Test]
        public void NegativeDistance_IsZeroLaps()
        {
            // Grid cars start just behind the line (slightly negative distance) — that must not be a lap.
            Assert.AreEqual(0, LapProgress.CompletedLaps(-8f, Loop));
        }

        [Test]
        public void JustUnderALoop_IsZeroLaps()
        {
            Assert.AreEqual(0, LapProgress.CompletedLaps(Loop - 1f, Loop));
        }

        [Test]
        public void ExactlyOneLoop_IsOneLap()
        {
            Assert.AreEqual(1, LapProgress.CompletedLaps(Loop, Loop));
        }

        [Test]
        public void PartwayThroughSecondLap_StillOneLap()
        {
            Assert.AreEqual(1, LapProgress.CompletedLaps(Loop + 300f, Loop));
        }

        [Test]
        public void MultipleLoops_CountFloored()
        {
            Assert.AreEqual(2, LapProgress.CompletedLaps(2f * Loop, Loop));
            Assert.AreEqual(3, LapProgress.CompletedLaps(3f * Loop + 5f, Loop));
        }

        [Test]
        public void NonPositiveLoopLength_IsZero_NoDivideByZero()
        {
            Assert.AreEqual(0, LapProgress.CompletedLaps(1000f, 0f));
            Assert.AreEqual(0, LapProgress.CompletedLaps(1000f, -50f));
        }

        [Test]
        public void MonotonicWithDistance()
        {
            // Progress never decreases as distance grows — the finish gate only ratchets up.
            int prev = 0;
            for (float d = 0f; d <= 5f * Loop; d += 37f)
            {
                int laps = LapProgress.CompletedLaps(d, Loop);
                Assert.GreaterOrEqual(laps, prev);
                prev = laps;
            }
        }
    }
}
