using System.Collections.Generic;
using NUnit.Framework;
using Shitboxer.Meta;
using Shitboxer.Vehicle;
using UnityEngine;

namespace Shitboxer.Tests
{
    /// <summary>
    /// Covers the Draft-Leech payoff: the pure <see cref="DraftReward.Accumulate"/> integrator (sums only
    /// while drafting, never otherwise, and rejects a non-positive/NaN dt), the payoff math
    /// (<see cref="RunDirector.DraftLeechPayout"/> — rate*seconds, rounded and capped), and the OWNERSHIP
    /// GATE (<see cref="RunDirector.OwnsDraftLeechPart"/>): a run owning no DraftLeech part is never paid, so
    /// its economy is byte-for-byte unchanged. The load-bearing contract: the reward pays out ONLY when the
    /// player opts in by owning a part flagged <see cref="PartDef.DraftLeech"/>.
    /// </summary>
    public class DraftRewardTests : TestBase
    {
        private const float Dt = 0.02f; // one 50 Hz FixedUpdate step

        private static PartDef NewPart() => ScriptableObject.CreateInstance<PartDef>();

        private static PartDef NewLeechPart()
        {
            PartDef p = NewPart();
            p.DraftLeech = true; // the SHARED CONTRACT field (Agent A adds it to PartDef)
            return p;
        }

        // ---------------------------------------------------------------- Accumulate (pure integrator)

        [Test]
        public void Accumulate_AddsOnlyWhileDrafting()
        {
            float seconds = 0f;
            for (int i = 0; i < 100; i++) seconds = DraftReward.Accumulate(seconds, Dt, isDrafting: true);
            Assert.That(seconds, Is.EqualTo(100 * Dt).Within(1e-4f), "drafting must integrate dt each step");
        }

        [Test]
        public void Accumulate_NeverAddsWhileNotDrafting()
        {
            float seconds = 0f;
            for (int i = 0; i < 100; i++) seconds = DraftReward.Accumulate(seconds, Dt, isDrafting: false);
            Assert.That(seconds, Is.EqualTo(0f).Within(1e-6f), "not drafting must never accumulate any time");
        }

        [Test]
        public void Accumulate_InterleavedCountsOnlyTheDraftingSteps()
        {
            float seconds = 0f;
            int draftingSteps = 0;
            for (int i = 0; i < 200; i++)
            {
                bool drafting = i % 2 == 0; // draft every other step
                if (drafting) draftingSteps++;
                seconds = DraftReward.Accumulate(seconds, Dt, drafting);
            }
            Assert.That(seconds, Is.EqualTo(draftingSteps * Dt).Within(1e-4f),
                "only the drafting steps may contribute to the total");
        }

        [Test]
        public void Accumulate_IsMonotonicNonDecreasing()
        {
            float seconds = 0f;
            float prev = seconds;
            for (int i = 0; i < 100; i++)
            {
                seconds = DraftReward.Accumulate(seconds, Dt, isDrafting: i % 3 != 0);
                Assert.GreaterOrEqual(seconds, prev, "the draft total must never go backwards");
                prev = seconds;
            }
        }

        [Test]
        public void Accumulate_NonPositiveOrNaNDt_IsANoOp()
        {
            float seconds = 5f;
            Assert.That(DraftReward.Accumulate(seconds, 0f, true), Is.EqualTo(seconds).Within(1e-6f),
                "zero dt must not integrate");
            Assert.That(DraftReward.Accumulate(seconds, -1f, true), Is.EqualTo(seconds).Within(1e-6f),
                "negative dt must not integrate");
            Assert.That(DraftReward.Accumulate(seconds, float.NaN, true), Is.EqualTo(seconds).Within(1e-6f),
                "NaN dt must not integrate");
        }

        // ---------------------------------------------------------------- DraftLeechPayout (payoff math)

        [Test]
        public void Payout_IsRateTimesSeconds_Rounded()
        {
            // 12 s at $0.5/s = $6; uncapped.
            Assert.AreEqual(6, RunDirector.DraftLeechPayout(12f, 0.5f, 0));
            // Rounds to the nearest whole cash: 5 s * 0.5 = 2.5 -> 2 (banker-free RoundToInt: .5 to even = 2).
            Assert.AreEqual(2, RunDirector.DraftLeechPayout(5f, 0.5f, 0));
            // 7 s * 0.5 = 3.5 -> 4 (rounds to even).
            Assert.AreEqual(4, RunDirector.DraftLeechPayout(7f, 0.5f, 0));
        }

        [Test]
        public void Payout_ClampsToPerRaceCap()
        {
            // 100 s * 0.5 = 50 raw, capped at 10.
            Assert.AreEqual(10, RunDirector.DraftLeechPayout(100f, 0.5f, 10));
            // Below the cap it passes through.
            Assert.AreEqual(6, RunDirector.DraftLeechPayout(12f, 0.5f, 10));
        }

        [Test]
        public void Payout_NonPositiveCap_MeansUncapped()
        {
            Assert.AreEqual(50, RunDirector.DraftLeechPayout(100f, 0.5f, 0), "cap 0 => uncapped");
            Assert.AreEqual(50, RunDirector.DraftLeechPayout(100f, 0.5f, -1), "negative cap => uncapped");
        }

        [Test]
        public void Payout_ZeroForNonPositiveTimeOrRate()
        {
            Assert.AreEqual(0, RunDirector.DraftLeechPayout(0f, 0.5f, 10), "no draft time => no payout");
            Assert.AreEqual(0, RunDirector.DraftLeechPayout(-3f, 0.5f, 10), "negative time => no payout");
            Assert.AreEqual(0, RunDirector.DraftLeechPayout(12f, 0f, 10), "zero rate => no payout");
            Assert.AreEqual(0, RunDirector.DraftLeechPayout(12f, -1f, 10), "negative rate => no payout");
        }

        // ---------------------------------------------------------------- OwnsDraftLeechPart (ownership gate)

        [Test]
        public void OwnershipGate_TrueOnlyWhenALeechPartIsOwned()
        {
            var owned = new List<PartDef> { NewPart(), NewLeechPart(), NewPart() };
            Assert.IsTrue(RunDirector.OwnsDraftLeechPart(owned), "a DraftLeech part in the pool opens the gate");
        }

        [Test]
        public void OwnershipGate_FalseWithNoLeechPart_NullSafe()
        {
            Assert.IsFalse(RunDirector.OwnsDraftLeechPart(null), "a null list must never open the gate");
            Assert.IsFalse(RunDirector.OwnsDraftLeechPart(new List<PartDef>()), "an empty list => gate closed");
            var plainOnly = new List<PartDef> { NewPart(), NewPart(), null };
            Assert.IsFalse(RunDirector.OwnsDraftLeechPart(plainOnly),
                "ordinary (non-leech) parts and nulls must never open the gate");
        }

        [Test]
        public void NoLeechPartOwned_MeansZeroPayoutAndNoEconomyChange()
        {
            // The no-change guarantee: with no DraftLeech part owned, the gate is closed. RunDirector then
            // never reads the reward nor computes any grant (leechBonus stays 0), so the money math reduces
            // to the shipped `payout + economyBonus` — byte-for-byte. We assert the gate here (the sole
            // guard) and that even a large draft total would be gated behind it.
            var run = new RunState();
            run.OwnedParts.Add(NewPart());   // an ordinary owned part
            run.OwnedParts.Add(NewPart());
            Assert.IsFalse(RunDirector.OwnsDraftLeechPart(run.OwnedParts),
                "a run owning no DraftLeech part must never open the payoff gate");

            // Sanity: had the gate opened, a big draft total would pay — proving the gate is what suppresses it.
            Assert.Greater(RunDirector.DraftLeechPayout(30f, 0.5f, 10), 0,
                "the payoff itself is non-zero for real draft time; only the closed gate keeps it out of the economy");
        }

        // ---------------------------------------------------------------- DraftReward host (light)

        [Test]
        public void Host_FreshRewardIsZero_AndGetOrAddIsIdempotent()
        {
            var go = new GameObject(nameof(Host_FreshRewardIsZero_AndGetOrAddIsIdempotent));
            try
            {
                DraftReward reward = DraftReward.GetOrAdd(go);
                Assert.That(reward.DraftSeconds, Is.EqualTo(0f).Within(1e-6f),
                    "a car merely carrying the component has accrued nothing — byte-for-byte free");

                reward.Reset();
                Assert.That(reward.DraftSeconds, Is.EqualTo(0f).Within(1e-6f), "Reset holds the total at zero");

                Assert.AreSame(reward, DraftReward.GetOrAdd(go), "GetOrAdd must return the existing component");
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }
    }
}
