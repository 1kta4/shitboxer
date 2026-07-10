using NUnit.Framework;
using Shitboxer.Meta;

namespace Shitboxer.Tests
{
    /// <summary>
    /// Covers the inverted catch-up economy payout curve after the anti-sandbagging reshape:
    /// the back still out-earns the front, but with diminishing returns that plateau at the
    /// bottom, a podium bonus that keeps winning worthwhile, and a mid-pack-capped economy hook.
    /// </summary>
    public class PayoutTableTests : TestBase
    {
        [Test]
        public void Payout_IsInverted_LastPaysMoreThanFirst()
        {
            var t = new PayoutTable();
            Assert.Less(t.PayoutFor(1, false), t.PayoutFor(8, false));
        }

        [Test]
        public void Payout_BaseCurve_HasDiminishingReturns()
        {
            // Each drop in position adds no more than the previous drop did (concave curve).
            var t = new PayoutTable();
            int[] p = t.PayoutByPosition;
            for (int i = 2; i < p.Length; i++)
            {
                int prevGain = p[i - 1] - p[i - 2];
                int thisGain = p[i] - p[i - 1];
                Assert.LessOrEqual(thisGain, prevGain,
                    $"Marginal gain from P{i} to P{i + 1} should not exceed the earlier one.");
            }
        }

        [Test]
        public void Payout_PlateausAtBottom_NoRewardForTankingPastMidpack()
        {
            // Dropping below mid-pack earns nothing extra — kills the "cruise dead-last" incentive.
            var t = new PayoutTable();
            Assert.AreEqual(t.PayoutFor(6, false), t.PayoutFor(8, false));
            int topGain = t.PayoutFor(2, false) - t.PayoutFor(1, false);
            int bottomGain = t.PayoutFor(8, false) - t.PayoutFor(7, false);
            Assert.Greater(topGain, bottomGain);
        }

        [Test]
        public void Payout_WinBonus_PaysPodiumOnTopOfBase()
        {
            // Winning collects a podium bonus over its (lean) base cash, so a win is worthwhile...
            var t = new PayoutTable();
            Assert.Greater(t.PayoutFor(1, false), t.PayoutByPosition[0]);
            // ...but only the podium gets it — P4 is base-only.
            Assert.AreEqual(t.PayoutByPosition[3], t.PayoutFor(4, false));
        }

        [Test]
        public void Payout_FarmingPremiumIsBounded()
        {
            // The residual "cruise low" premium over winning is modest (hazard pay), not runaway.
            var t = new PayoutTable();
            int premium = t.PayoutFor(8, false) - t.PayoutFor(1, false);
            Assert.LessOrEqual(premium, 4);
        }

        [Test]
        public void EconomyBonus_CapsAtMidpack_NoLastPlaceCompounding()
        {
            var t = new PayoutTable();
            int atCap = t.EconomyBonusFor(1, t.EconomyBonusPositionCap);
            Assert.AreEqual(atCap, t.EconomyBonusFor(1, 8), "Past the cap the bonus must not grow.");
            Assert.AreEqual(atCap, t.EconomyBonusFor(1, 99), "Oversized fields still clamp to the cap.");
            Assert.Less(t.EconomyBonusFor(1, 1), atCap, "Below the cap the bonus scales with position.");
            Assert.AreEqual(2 * t.EconomyBonusPositionCap, t.EconomyBonusFor(2, 8), "Rate scales, cap holds.");
        }

        [Test]
        public void EconomyBonus_ZeroForNonPositiveRate()
        {
            var t = new PayoutTable();
            Assert.AreEqual(0, t.EconomyBonusFor(0, 8));
            Assert.AreEqual(0, t.EconomyBonusFor(-3, 8));
        }

        [Test]
        public void Payout_Eliminated_GetsConsolationRegardlessOfPosition()
        {
            var t = new PayoutTable();
            Assert.AreEqual(t.EliminationConsolation, t.PayoutFor(3, true));
            Assert.AreEqual(t.EliminationConsolation, t.PayoutFor(8, true));
        }

        [Test]
        public void Payout_ClampsBeyondTable()
        {
            var t = new PayoutTable();
            // Beyond the field, base clamps to the last (richest) entry and there is no podium.
            Assert.AreEqual(t.PayoutFor(8, false), t.PayoutFor(99, false));
        }

        [Test]
        public void Payout_ClampsBelowFirstPosition()
        {
            var t = new PayoutTable();
            Assert.AreEqual(t.PayoutFor(1, false), t.PayoutFor(0, false));
        }
    }
}
