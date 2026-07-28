using NUnit.Framework;
using Shitboxer.Fx;

namespace Shitboxer.Tests
{
    /// <summary>
    /// The visual-juice tuning maps are pure, so pin the properties that keep the FX honest: sparks
    /// scale with how hard you actually hit, and tyre smoke never fires on a clean fast lap — only a
    /// real drift smokes. The ParticleSystem plumbing itself is play-mode glue, exercised by playing.
    /// </summary>
    public class FxTuningTests : TestBase
    {
        [Test]
        public void Sparks_ScaleWithSeverity_AndTapsStaySmall()
        {
            int tap = RaceVisualFx.SparkCountFor(0.1f);
            int slam = RaceVisualFx.SparkCountFor(1f);
            Assert.Greater(slam, tap);
            Assert.LessOrEqual(tap, 6, "a tap should be a few sparks, not a shower");
            Assert.GreaterOrEqual(slam, 30, "a slam should be a shower");
        }

        [Test]
        public void Smoke_SilentOnACleanFastLap()
        {
            // Peak cornering slip sits around 6-8 deg — below the smoke threshold by design.
            Assert.AreEqual(0f, RaceVisualFx.SmokeRateFor(7f, 120f));
        }

        [Test]
        public void Smoke_SilentWhenSlow_EvenSideways()
        {
            Assert.AreEqual(0f, RaceVisualFx.SmokeRateFor(45f, 10f), "parking-lot shuffling isn't a burnout");
        }

        [Test]
        public void Smoke_RampsWithSlip()
        {
            float drift = RaceVisualFx.SmokeRateFor(18f, 80f);
            float spin = RaceVisualFx.SmokeRateFor(35f, 80f);
            Assert.Greater(drift, 0f);
            Assert.Greater(spin, drift);
        }
    }
}
