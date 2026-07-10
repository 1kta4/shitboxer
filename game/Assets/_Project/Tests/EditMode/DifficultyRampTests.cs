using NUnit.Framework;
using Shitboxer.Meta;
using UnityEngine;

namespace Shitboxer.Tests
{
    /// <summary>
    /// Covers RunDirector's opt-in per-circuit difficulty ramp (<see cref="RunDirector.RampedDifficulty"/>),
    /// the pure helper behind ApplyDifficulty's bot-commitment request. The load-bearing contract mirrors the
    /// project's headline constraint: the DEFAULT (rampPerCircuit == 0) is a true no-op — for EVERY circuit the
    /// helper reduces to <c>Mathf.Clamp(baseScalar, min, max)</c>, byte-for-byte the value the shipped
    /// ApplyDifficulty already handed to RaceManager.SetDifficultyScalar — so a run in progress is unchanged
    /// unless a designer opts in. With the ramp on, the request rises strictly with the circuit until it
    /// saturates at the ceiling, and never escapes the [min, max] band (SetDifficultyScalar re-clamps to its
    /// own authored range on top). The ramp adds only a per-circuit term; the license stake lives in the base
    /// (RunState.DifficultyMult), so nothing here double-applies it and no economy formula is touched.
    /// </summary>
    public class DifficultyRampTests : TestBase
    {
        // The shipped clamp band ApplyDifficulty uses with the ramp OFF (mirror RunDirector's private
        // MinDifficultyScalar / MaxDifficultyScalar). RaceManager.SetDifficultyScalar re-clamps to [0.5, 1.5].
        private const float ShippedMin = 1f;
        private const float ShippedMax = 1.3f;

        // A spread of base scalars: inside the band, at both edges, and beyond it (so the clamp is exercised).
        private static readonly float[] Bases = { 0.4f, 0.9f, 1f, 1.14f, 1.28f, 1.3f, 1.32f, 1.5f, 2.0f };
        private static readonly int[] Circuits = { 0, 1, 2, 3, 4, 5, 8, 12 };

        // ---- Ramp OFF (default 0): a true no-op — pure clamp, identical for every circuit -------------------

        [Test]
        public void RampZero_IsPureClamp_ForEveryCircuit()
        {
            // With the ramp at 0, the circuit index must contribute NOTHING: the result equals the base
            // clamped into the band, for every circuit index — the byte-for-byte shipped behaviour.
            foreach (float b in Bases)
            {
                float expected = Mathf.Clamp(b, ShippedMin, ShippedMax);
                foreach (int ci in Circuits)
                {
                    float actual = RunDirector.RampedDifficulty(b, ci, 0f, ShippedMin, ShippedMax);
                    Assert.That(actual, Is.EqualTo(expected),
                        $"ramp 0 must equal Mathf.Clamp(base={b}, {ShippedMin}, {ShippedMax}) @ circuit {ci}");
                }
            }
        }

        [Test]
        public void RampZero_IsConstantAcrossCircuits()
        {
            // The whole point of "flat": the value the director requests does not move from circuit to circuit
            // when the ramp is off, for any base (whether it clamps or not).
            foreach (float b in Bases)
            {
                float first = RunDirector.RampedDifficulty(b, 0, 0f, ShippedMin, ShippedMax);
                foreach (int ci in Circuits)
                    Assert.That(RunDirector.RampedDifficulty(b, ci, 0f, ShippedMin, ShippedMax),
                        Is.EqualTo(first), $"ramp 0 stays flat across circuits (base {b}, circuit {ci})");
            }
        }

        [Test]
        public void RampZero_ReproducesShippedScalarSequence_ByteForByte()
        {
            // Reconstruct today's base scalar exactly as ApplyDifficulty does — 1 + (DifficultyMult - 1) * 0.4,
            // stake 0 — for the first several circuits and confirm the ramp-OFF helper returns precisely the
            // clamped value the shipped code produced. Pins the guarantee against real per-run numbers.
            const float gain = 0.4f; // mirrors RunDirector.DifficultyScalarGain
            for (int ci = 0; ci < 8; ci++)
            {
                float difficultyMult = 1f + 0.3f * ci + 0.05f * ci * ci; // RunState.DifficultyMult at stake 0
                float baseScalar = 1f + (difficultyMult - 1f) * gain;
                float shipped = Mathf.Clamp(baseScalar, ShippedMin, ShippedMax); // the exact old expression
                float ramped = RunDirector.RampedDifficulty(baseScalar, ci, 0f, ShippedMin, ShippedMax);
                Assert.That(ramped, Is.EqualTo(shipped), $"ramp-off scalar byte-for-byte @ circuit {ci}");
            }
        }

        // ---- Negative circuit index contributes no ramp ---------------------------------------------------

        [Test]
        public void NegativeCircuitIndex_ContributesNoRamp_EvenWhenOn()
        {
            foreach (float b in Bases)
            {
                float expected = Mathf.Clamp(b, ShippedMin, 1.5f);
                foreach (int ci in new[] { -1, -3, -100 })
                    Assert.That(RunDirector.RampedDifficulty(b, ci, 0.1f, ShippedMin, 1.5f),
                        Is.EqualTo(expected), $"negative circuit adds no ramp (base {b}, circuit {ci})");
            }
        }

        // ---- Ramp ON (> 0): strictly increasing with circuit, then saturates at the ceiling ---------------

        [Test]
        public void RampPositive_StrictlyIncreasesWithCircuit_UntilSaturation()
        {
            const float min = 1f, max = 1.5f, ramp = 0.05f;
            float baseScalar = 1f; // starts at the floor so several circuits climb before the ceiling
            float prev = RunDirector.RampedDifficulty(baseScalar, 0, ramp, min, max);
            for (int ci = 1; ci <= 20; ci++)
            {
                float cur = RunDirector.RampedDifficulty(baseScalar, ci, ramp, min, max);
                // Monotonic non-decreasing always; STRICTLY increasing until the value reaches the ceiling.
                Assert.That(cur, Is.GreaterThanOrEqualTo(prev), $"never decreases with circuit @ {ci}");
                if (prev < max)
                    Assert.That(cur, Is.GreaterThan(prev), $"strictly increases while below the ceiling @ {ci}");
                prev = cur;
            }
            // By circuit 20 the ramp has long exceeded the ceiling and must sit exactly at it.
            Assert.That(prev, Is.EqualTo(max), "the ramped scalar saturates exactly at the ceiling");
        }

        [Test]
        public void RampPositive_HigherCircuitIsAtLeastAsHard()
        {
            // Cross-check the ordering property on a coarser spread and a non-floor base.
            const float min = 1f, max = 1.5f, ramp = 0.08f;
            float baseScalar = 1.1f;
            float a = RunDirector.RampedDifficulty(baseScalar, 0, ramp, min, max);
            float b = RunDirector.RampedDifficulty(baseScalar, 1, ramp, min, max);
            float c = RunDirector.RampedDifficulty(baseScalar, 2, ramp, min, max);
            Assert.That(b, Is.GreaterThan(a), "circuit 1 harder than circuit 0");
            Assert.That(c, Is.GreaterThan(b), "circuit 2 harder than circuit 1");
        }

        // ---- Boundedness: no ramp/circuit can escape the [min, max] band -----------------------------------

        [Test]
        public void Result_NeverLeavesBand_UnderExtremeInputs()
        {
            float[] ramps = { 0f, 0.01f, 0.05f, 0.5f, 5f, 100f };
            float[] mins = { 0.5f, 1f };
            float[] maxes = { 1.3f, 1.5f };
            foreach (float b in Bases)
                foreach (float ramp in ramps)
                    foreach (float min in mins)
                        foreach (float max in maxes)
                            foreach (int ci in new[] { -50, -1, 0, 1, 3, 7, 25, 1000 })
                            {
                                float v = RunDirector.RampedDifficulty(b, ci, ramp, min, max);
                                Assert.That(v, Is.InRange(min, max),
                                    $"base{b} ramp{ramp} circuit{ci} min{min} max{max} must stay in band");
                                Assert.That(v, Is.LessThanOrEqualTo(max),
                                    $"base{b} ramp{ramp} circuit{ci} never exceeds max{max}");
                            }
        }

        [Test]
        public void RampPositive_ClampsExactlyToMax_WhenOvershooting()
        {
            // A big ramp on a high circuit overshoots the ceiling and must clamp precisely to it (not beyond,
            // and not to SetDifficultyScalar's wider 1.5 unless that IS the max passed).
            Assert.That(RunDirector.RampedDifficulty(1f, 100, 0.5f, 1f, 1.3f), Is.EqualTo(1.3f),
                "overshoot clamps to the passed max (1.3)");
            Assert.That(RunDirector.RampedDifficulty(1f, 100, 0.5f, 1f, 1.5f), Is.EqualTo(1.5f),
                "overshoot clamps to the passed max (1.5)");
        }

        [Test]
        public void BaseBelowMin_ClampsUpToMin()
        {
            // A base under the floor (only reachable via a hand-passed base) still never dips below min.
            Assert.That(RunDirector.RampedDifficulty(0.2f, 0, 0f, 1f, 1.5f), Is.EqualTo(1f),
                "a sub-floor base clamps up to min with the ramp off");
            Assert.That(RunDirector.RampedDifficulty(0.2f, 3, 0.05f, 1f, 1.5f), Is.GreaterThanOrEqualTo(1f),
                "a sub-floor base never drops below min with the ramp on");
        }
    }
}
