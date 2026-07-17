using NUnit.Framework;
using Shitboxer.Meta;

namespace Shitboxer.Tests
{
    /// <summary>
    /// Covers the inverted catch-up economy payout curve: the back out-earns the front, but with
    /// diminishing returns that plateau at the very back, a podium bonus that keeps winning
    /// worthwhile, and a mid-pack-capped economy hook.
    ///
    /// Wave 21 widened the curve (P1=$5 … P8=$13, was $7 … $10) because the old one made the
    /// signature tension worth $3 — less than a reroll. Two assertions here were pinned to those
    /// numbers and changed WITH the balance, deliberately: the farming premium's bound, and where
    /// the bottom plateau starts. Every other assertion is shape-based and did not move, which is
    /// the evidence that only the magnitude changed and not the design.
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
        public void Payout_PlateausAtTheBack_NoRewardForTankingToLast()
        {
            // Tanking into the last slot earns nothing extra — kills the "cruise dead-last" incentive.
            // The plateau moved P6 -> P7 when wave 21 widened the curve: the old table was so flat
            // that three positions paid the same, which killed the signature along with the exploit.
            // The anti-tank property that actually matters is that the MARGINAL gain decays to zero
            // at the back, which this pins and Payout_BaseCurve_HasDiminishingReturns pins in general.
            var t = new PayoutTable();
            Assert.AreEqual(t.PayoutFor(7, false), t.PayoutFor(8, false));
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
        public void Payout_FarmingPremium_IsWorthAboutOnePart()
        {
            // THE test for the signature tension, and the one wave 21 exists to change.
            //
            // It used to assert premium <= 4, which passed at $3 — and $3 is not a decision when a
            // reroll is $5 and the median part is $7. A payout spread smaller than anything you can
            // buy with it means "push to win vs hang back to farm" is a slogan, not a choice.
            //
            // The floor is the real assertion: the premium must be worth roughly a part, or the
            // tension doesn't exist. The ceiling still guards the other failure mode — hazard pay,
            // not a runaway farm. If you retune the curve, these bounds are the contract.
            var t = new PayoutTable();
            int premium = t.PayoutFor(8, false) - t.PayoutFor(1, false);
            Assert.GreaterOrEqual(premium, 7, "Farming must out-pay winning by ~a median part ($7) or it isn't a decision.");
            Assert.LessOrEqual(premium, 12, "...but not so much that cruising the back is a free ride.");
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
