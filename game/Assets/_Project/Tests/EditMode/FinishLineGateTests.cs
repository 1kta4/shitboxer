using NUnit.Framework;
using Shitboxer.Race;
using UnityEngine;

namespace Shitboxer.Tests
{
    /// <summary>
    /// Covers the finish-plane refinement on top of the distance gate. LapProgress decides the final
    /// lap is COMPLETE; FinishLineGate decides WHEN the car actually crossed the physical start/finish
    /// plane — because arc-length projection can lead a car driving off the racing line by metres, and
    /// metres at the flag reorder a podium (the observed bug: a player visibly second across the line
    /// registered P1). Deliberately NOT a gate a car must hit: the referee's window and grace timeout
    /// finish any pending car regardless, so these tests pin the math, not a strand-able rule.
    /// </summary>
    public class FinishLineGateTests : TestBase
    {
        static readonly Vector3 LinePoint = new Vector3(10f, 0f, 20f);
        static readonly Vector3 LineForward = Vector3.forward;

        [Test]
        public void PlaneDistance_NegativeBeforeTheLine_PositivePast()
        {
            Assert.Less(FinishLineGate.PlaneDistance(LinePoint - 3f * LineForward, LinePoint, LineForward), 0f);
            Assert.Greater(FinishLineGate.PlaneDistance(LinePoint + 3f * LineForward, LinePoint, LineForward), 0f);
            Assert.AreEqual(0f, FinishLineGate.PlaneDistance(LinePoint, LinePoint, LineForward), 1e-4f);
        }

        [Test]
        public void PlaneDistance_LateralOffset_DoesNotMoveTheLine()
        {
            // A car wide of the racing line is still exactly AT the flag when the flag's plane says so.
            Vector3 wide = LinePoint + 6f * Vector3.right - 2f * LineForward;
            Assert.AreEqual(-2f, FinishLineGate.PlaneDistance(wide, LinePoint, LineForward), 1e-4f);
        }

        [Test]
        public void Crossed_OnlyOnTheBeforeToPastStep()
        {
            Assert.IsTrue(FinishLineGate.Crossed(-0.5f, 0.8f));
            Assert.IsTrue(FinishLineGate.Crossed(-0.5f, 0f));   // arriving exactly on the paint counts
            Assert.IsFalse(FinishLineGate.Crossed(-3f, -0.1f)); // still short
            Assert.IsFalse(FinishLineGate.Crossed(0.1f, 2f));   // already past — never re-arms
            Assert.IsFalse(FinishLineGate.Crossed(2f, -1f));    // backwards over the line is not a finish
        }

        [Test]
        public void CrossingTime_InterpolatesWithinTheStep()
        {
            // Crossed exactly mid-step: half the tick is refunded.
            Assert.AreEqual(10f - 0.01f, FinishLineGate.CrossingTime(10f, 0.02f, -1f, 1f), 1e-5f);
            // Crossed at the very end of the step (dNow == 0): the stamp is now.
            Assert.AreEqual(10f, FinishLineGate.CrossingTime(10f, 0.02f, -2f, 0f), 1e-5f);
            // Crossed almost immediately after the previous step: nearly the whole tick refunded.
            Assert.AreEqual(10f - 0.02f, FinishLineGate.CrossingTime(10f, 0.02f, -0.001f, 10f), 1e-3f);
        }

        [Test]
        public void CrossingTime_StampsInsideTheTick()
        {
            float stamp = FinishLineGate.CrossingTime(10f, 0.02f, -0.7f, 1.3f);
            Assert.GreaterOrEqual(stamp, 10f - 0.02f);
            Assert.LessOrEqual(stamp, 10f);
        }

        [Test]
        public void CrossingTime_SameTick_OrdersTwoCarsByWhoCrossedFirst()
        {
            // Two cars at the same speed cross within one tick; A was closer to the line, so A crossed
            // earlier in the tick and must stamp earlier. This is the photo-finish the gate exists for.
            float a = FinishLineGate.CrossingTime(10f, 0.02f, -1f, 3f);
            float b = FinishLineGate.CrossingTime(10f, 0.02f, -3f, 1f);
            Assert.Less(a, b);
        }

        [Test]
        public void CrossingTime_DegenerateStep_FallsBackToNow()
        {
            Assert.AreEqual(10f, FinishLineGate.CrossingTime(10f, 0.02f, 1f, 1f), 1e-5f);  // zero span
            Assert.AreEqual(10f, FinishLineGate.CrossingTime(10f, 0f, -1f, 1f), 1e-5f);    // zero dt
        }
    }
}
