using NUnit.Framework;
using Shitboxer.Meta;
using Shitboxer.Vehicle;
using UnityEngine;

namespace Shitboxer.Tests
{
    /// <summary>
    /// The decision-15 damage model (doc 08): pace is Durability^WearExponent with a floor of ZERO,
    /// DamageResistance scales what a hit costs at intake, and a car that reaches zero is a wreck the
    /// host retires — "crippled at half, retired at zero". Pure plain-C# maths, no scene access.
    /// </summary>
    public class DamageModelTests
    {
        private static VehicleSim SimWithExponent(float wearExponent, float damageResistance = 0f)
        {
            var spec = new VehicleSpec { WearExponent = wearExponent, DamageResistance = damageResistance };
            return new VehicleSim(spec);
        }

        [Test]
        public void FullDurability_IsAnExactIdentity_AtEveryExponent()
        {
            // 1^e == 1 for any exponent, so an undamaged car drives byte-for-byte the authored chassis
            // whatever its toughness character — the same identity rule every doc-08 system obeys.
            foreach (float exp in new[] { 0.4f, 1f, 2f })
            {
                var sim = SimWithExponent(exp);
                Assert.That(sim.DurabilityMult, Is.EqualTo(1f).Within(1e-6f), $"exponent {exp} broke the fresh-car identity");
                Assert.IsFalse(sim.IsDestroyed, "a fresh car must not read as destroyed");
            }
        }

        [Test]
        public void HalfDurability_FollowsThePerChassisCurve()
        {
            // The decision-15 table: at half durability a monster truck (0.4) barely notices at 76%
            // pace, the default chassis (1.0) is crippled to half, an open-wheeler (2.0) is down to 25%.
            var truck = SimWithExponent(0.4f);
            var box = SimWithExponent(1f);
            var openWheeler = SimWithExponent(2f);
            truck.SetDurability(0.5f);
            box.SetDurability(0.5f);
            openWheeler.SetDurability(0.5f);

            Assert.That(truck.DurabilityMult, Is.EqualTo(Mathf.Pow(0.5f, 0.4f)).Within(1e-5f), "monster-truck curve");
            Assert.That(truck.DurabilityMult, Is.EqualTo(0.7579f).Within(1e-3f), "doc table says ~76% pace at half durability");
            Assert.That(box.DurabilityMult, Is.EqualTo(0.5f).Within(1e-6f), "default chassis: half durability = half pace");
            Assert.That(openWheeler.DurabilityMult, Is.EqualTo(0.25f).Within(1e-6f), "open-wheeler: half durability = quarter pace");
        }

        [Test]
        public void ZeroDurability_IsAWreck_WithZeroPace()
        {
            foreach (float exp in new[] { 0.4f, 1f, 2f })
            {
                var sim = SimWithExponent(exp);
                sim.SetDurability(0f);
                Assert.That(sim.Durability, Is.EqualTo(0f).Within(1e-6f), $"exponent {exp}: durability floor is zero");
                Assert.That(sim.DurabilityMult, Is.EqualTo(0f).Within(1e-6f), $"exponent {exp}: a wreck has zero pace");
                Assert.IsTrue(sim.IsDestroyed, $"exponent {exp}: zero durability must read as destroyed");
            }
        }

        [Test]
        public void DamageResistance_ScalesTheHitAtIntake()
        {
            var bare = SimWithExponent(1f, damageResistance: 0f);
            var tough = SimWithExponent(1f, damageResistance: 0.5f);
            bare.ApplyDamage(0.4f);
            tough.ApplyDamage(0.4f);

            Assert.That(bare.Durability, Is.EqualTo(0.6f).Within(1e-6f), "no resistance takes the full hit");
            Assert.That(tough.Durability, Is.EqualTo(0.8f).Within(1e-6f), "50% resistance halves what a hit costs");
        }

        [Test]
        public void DamageResistance_CanNeverMakeACarUnhittable()
        {
            // The ledger bake and Validate both cap resistance at 0.9: a deep-durability build takes a
            // tenth of every hit, but no build switches contact damage off entirely.
            var spec = new VehicleSpec { DamageResistance = 5f };
            var sim = new VehicleSim(spec); // ctor runs Validate, which clamps
            Assert.That(spec.DamageResistance, Is.EqualTo(0.9f).Within(1e-6f), "Validate did not cap resistance at 0.9");
            sim.ApplyDamage(1f);
            Assert.Less(sim.Durability, 1f, "a capped-resistance car must still take some damage");
        }

        [Test]
        public void WearExponent_ValidatesIntoTheAuthoredRange()
        {
            // Exponent 0 would make D^e read 1 at any damage — the whole rework switched off by one
            // hand-edited YAML field. Validate clamps into the inspector range instead.
            var zeroed = new VehicleSpec { WearExponent = 0f };
            zeroed.Validate();
            Assert.That(zeroed.WearExponent, Is.EqualTo(0.25f).Within(1e-6f), "a zero exponent must clamp up");

            var wild = new VehicleSpec { WearExponent = 50f };
            wild.Validate();
            Assert.That(wild.WearExponent, Is.EqualTo(3f).Within(1e-6f), "an absurd exponent must clamp down");
        }

        [Test]
        public void RaceStartDurability_FloorsAWreck_AndLeavesAHealthyCarAlone()
        {
            // The anti-death-spiral guard: a failed race is RETRIED, so without this floor a broke
            // player with a wrecked car would retire at every green flag until the run bled out.
            Assert.That(RunDirector.RaceStartDurability(0f), Is.EqualTo(RunDirector.MinRaceStartDurability).Within(1e-6f),
                "a wreck must be hammered straight enough to roll");
            Assert.That(RunDirector.RaceStartDurability(0.1f), Is.EqualTo(RunDirector.MinRaceStartDurability).Within(1e-6f),
                "below the floor lifts to the floor");
            Assert.That(RunDirector.RaceStartDurability(0.6f), Is.EqualTo(0.6f).Within(1e-6f),
                "carried wear above the floor rides through untouched — repair still costs money");
            Assert.That(RunDirector.RaceStartDurability(1f), Is.EqualTo(1f).Within(1e-6f), "a pristine car is untouched");
            Assert.That(RunDirector.RaceStartDurability(1.7f), Is.EqualTo(1f).Within(1e-6f), "over-full clamps to pristine");
        }
    }
}
