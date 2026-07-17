using NUnit.Framework;
using Shitboxer.Race;

namespace Shitboxer.Tests
{
    /// <summary>
    /// Pins the one pure seam of the live HUD telemetry: <see cref="RaceDisplay.FormatPaceDelta"/>, the signed
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
            Assert.AreEqual(string.Empty, RaceDisplay.FormatPaceDelta(12.8f, -1f));
        }

        [Test]
        public void FormatPaceDelta_NegativeCurrent_IsEmpty()
        {
            // Defensive: a not-yet-started / invalid current lap never yields a delta even with a best set.
            Assert.AreEqual(string.Empty, RaceDisplay.FormatPaceDelta(-1f, 44.1f));
        }

        [Test]
        public void FormatPaceDelta_SlowerThanBest_IsSignedPositive()
        {
            // The current lap has already run 3.0 s longer than the best lap's total — guaranteed off pace.
            Assert.AreEqual("+3.0", RaceDisplay.FormatPaceDelta(47.1f, 44.1f));
        }

        [Test]
        public void FormatPaceDelta_UnderBest_IsSignedNegative()
        {
            // Mid-lap, still 3.0 s inside the best lap's total — a negative (on/ahead of pace) delta.
            Assert.AreEqual("-3.0", RaceDisplay.FormatPaceDelta(41.1f, 44.1f));
        }

        [Test]
        public void FormatPaceDelta_ExactlyOnBest_ReadsAsPlusZero()
        {
            // At the best-lap mark the delta is zero; the positive section carries the sign, so it reads "+0.0".
            Assert.AreEqual("+0.0", RaceDisplay.FormatPaceDelta(44.1f, 44.1f));
        }

        // --- Cutoff pace projection (wave 16) -------------------------------------------------------
        //
        // The gate the whole of Phase 2 is about used to be invisible until the winner crossed, because
        // CutoffDeadlineS is defined off the winner's finish time. These pin the projection that replaces
        // that silence. Lap = 700 m in these fixtures; the min-distance gate is one lap.

        private const float Lap = 700f;

        [Test]
        public void ProjectedPaceExcess_BeforeTheGateDistance_IsOmitted()
        {
            // Under the gate the ~27 m grid spread dominates real pace, so any number here would be noise
            // dressed as a verdict. -1 tells the caller to draw nothing.
            Assert.AreEqual(-1f, RaceDisplay.ProjectedPaceExcess01(400f, 300f, Lap));
        }

        [Test]
        public void ProjectedPaceExcess_LeaderUnderGate_IsOmitted()
        {
            // Both cars must clear the gate — a leader still inside the opening lap can't anchor a projection.
            Assert.AreEqual(-1f, RaceDisplay.ProjectedPaceExcess01(650f, 640f, Lap));
        }

        [Test]
        public void ProjectedPaceExcess_PlayerIsTheLeader_IsZero()
        {
            // Identical distances: projected to finish exactly with the winner, i.e. 0% behind — never -1,
            // which would wrongly hide the readout from whoever is winning.
            Assert.AreEqual(0f, RaceDisplay.ProjectedPaceExcess01(1400f, 1400f, Lap), 1e-4f);
        }

        [Test]
        public void ProjectedPaceExcess_IsTheDistanceRatio_NotTheDistanceGap()
        {
            // 1500 / 1000 - 1 = 0.5: holding this pace the player takes 50% longer than the leader. The
            // ratio (not the 500 m gap) is what the cutoff rule compares, since the cutoff is on TIME.
            Assert.AreEqual(0.5f, RaceDisplay.ProjectedPaceExcess01(1500f, 1000f, Lap), 1e-4f);
        }

        [Test]
        public void FormatCutoffPace_OmittedSentinel_IsEmpty()
        {
            Assert.AreEqual(string.Empty, RaceDisplay.FormatCutoffPace(-1f, 0.15f));
        }

        [Test]
        public void FormatCutoffPace_InsideTheGate_ReadsSafe()
        {
            Assert.AreEqual("PACE +8%  /  CUT +15%   SAFE", RaceDisplay.FormatCutoffPace(0.08f, 0.15f));
        }

        [Test]
        public void FormatCutoffPace_OutsideTheGate_ReadsAtRisk()
        {
            Assert.AreEqual("PACE +19%  /  CUT +15%   AT RISK", RaceDisplay.FormatCutoffPace(0.19f, 0.15f));
        }

        [Test]
        public void FormatCutoffPace_ExactlyOnTheGate_StillReadsSafe()
        {
            // The rule is "within X% of the winner's time" — inclusive. Sitting exactly on the line passes,
            // so the readout must not tell the player they're already dead.
            Assert.AreEqual("PACE +15%  /  CUT +15%   SAFE", RaceDisplay.FormatCutoffPace(0.15f, 0.15f));
        }

        // --- Payout preview (wave 16) ---------------------------------------------------------------

        [Test]
        public void FormatPayoutPreview_MidfieldShowsTheInversion()
        {
            // The whole point of the line: banking MORE at P6 than winning would pay. If this ever stops
            // reading as a trade, the run's signature tension has stopped being visible.
            Assert.AreEqual("BANKING $10 at P6   (WIN PAYS $7)", RaceDisplay.FormatPayoutPreview(6, 10, 7));
        }

        [Test]
        public void FormatPayoutPreview_LeadingDropsTheRedundantComparison()
        {
            // Leading, "what winning pays" IS what you're banking — the comparison would be noise.
            Assert.AreEqual("BANKING $7 — LEADING", RaceDisplay.FormatPayoutPreview(1, 7, 7));
        }
    }
}
