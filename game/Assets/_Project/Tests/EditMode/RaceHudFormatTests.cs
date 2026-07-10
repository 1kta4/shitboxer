using NUnit.Framework;
using Shitboxer.Race;

namespace Shitboxer.Tests
{
    /// <summary>
    /// Pins the one pure seam of the live HUD telemetry: <see cref="RaceHud.FormatPaceDelta"/>, the signed
    /// pace-delta text (this lap's elapsed vs the player's best) drawn next to the CUR/BEST readout. The rest
    /// of RaceHud is OnGUI layout and cannot be unit-tested; the durability bar, SLIPSTREAM cue and CUR/BEST
    /// line are verified manually in play. This helper is a plain string of a subtraction with no engine,
    /// scene or clock state, so a headless readout would format it identically — purely additive display.
    /// </summary>
    public class RaceHudFormatTests : TestBase
    {
        [Test]
        public void FormatPaceDelta_NoBestYet_IsEmpty()
        {
            // Best is the -1 "no lap yet" sentinel: nothing to compare against, so the delta is omitted.
            Assert.AreEqual(string.Empty, RaceHud.FormatPaceDelta(12.8f, -1f));
        }

        [Test]
        public void FormatPaceDelta_NegativeCurrent_IsEmpty()
        {
            // Defensive: a not-yet-started / invalid current lap never yields a delta even with a best set.
            Assert.AreEqual(string.Empty, RaceHud.FormatPaceDelta(-1f, 44.1f));
        }

        [Test]
        public void FormatPaceDelta_SlowerThanBest_IsSignedPositive()
        {
            // The current lap has already run 3.0 s longer than the best lap's total — guaranteed off pace.
            Assert.AreEqual("+3.0", RaceHud.FormatPaceDelta(47.1f, 44.1f));
        }

        [Test]
        public void FormatPaceDelta_UnderBest_IsSignedNegative()
        {
            // Mid-lap, still 3.0 s inside the best lap's total — a negative (on/ahead of pace) delta.
            Assert.AreEqual("-3.0", RaceHud.FormatPaceDelta(41.1f, 44.1f));
        }

        [Test]
        public void FormatPaceDelta_ExactlyOnBest_ReadsAsPlusZero()
        {
            // At the best-lap mark the delta is zero; the positive section carries the sign, so it reads "+0.0".
            Assert.AreEqual("+0.0", RaceHud.FormatPaceDelta(44.1f, 44.1f));
        }
    }
}
