using System.Collections.Generic;
using NUnit.Framework;
using Shitboxer.Meta;
using Shitboxer.Vehicle;
using UnityEngine;

namespace Shitboxer.Tests
{
    /// <summary>
    /// Covers the Balatro-foil-style part editions: PartEditionInfo's multiplier table, and
    /// SpecModApplier scaling a part's stat EFFECT by the edition factor. Default Edition.None must
    /// be a byte-for-byte identity so today's driving feel and economy balance never move.
    /// </summary>
    public class PartEditionTests : TestBase
    {
        private static PartDef StatPart(
            SpecModTarget target,
            float mult,
            PartEdition edition = PartEdition.None,
            SpecModOp op = SpecModOp.Multiply)
        {
            var p = ScriptableObject.CreateInstance<PartDef>();
            p.Category = PartCategory.Stat;
            p.Edition = edition;
            p.SpecMods = new List<SpecMod> { new SpecMod { Target = target, Multiplier = mult, Op = op } };
            return p;
        }

        // --- PartEditionInfo lookup table ------------------------------------------------------

        [Test]
        public void StatMult_None_IsExactIdentity()
        {
            Assert.That(PartEditionInfo.StatMult(PartEdition.None), Is.EqualTo(1f));
            Assert.That(PartEditionInfo.PriceMult(PartEdition.None), Is.EqualTo(1f));
        }

        [Test]
        public void StatMult_HigherTiers_UseMappedFactors()
        {
            Assert.That(PartEditionInfo.StatMult(PartEdition.Foil), Is.EqualTo(1.25f));
            Assert.That(PartEditionInfo.StatMult(PartEdition.Holo), Is.EqualTo(1.5f));
            Assert.That(PartEditionInfo.StatMult(PartEdition.Polychrome), Is.EqualTo(2f));
        }

        [Test]
        public void StatMult_IsMonotonicAcrossTiers()
        {
            Assert.That(PartEditionInfo.StatMult(PartEdition.Foil),
                Is.GreaterThan(PartEditionInfo.StatMult(PartEdition.None)));
            Assert.That(PartEditionInfo.StatMult(PartEdition.Holo),
                Is.GreaterThan(PartEditionInfo.StatMult(PartEdition.Foil)));
            Assert.That(PartEditionInfo.StatMult(PartEdition.Polychrome),
                Is.GreaterThan(PartEditionInfo.StatMult(PartEdition.Holo)));
        }

        [Test]
        public void PriceMult_HigherTiers_CostMore()
        {
            Assert.That(PartEditionInfo.PriceMult(PartEdition.Foil),
                Is.GreaterThan(PartEditionInfo.PriceMult(PartEdition.None)));
            Assert.That(PartEditionInfo.PriceMult(PartEdition.Polychrome),
                Is.GreaterThan(PartEditionInfo.PriceMult(PartEdition.Foil)));
        }

        // --- PartDef default -------------------------------------------------------------------

        [Test]
        public void PartDef_DefaultEdition_IsNone()
        {
            var p = ScriptableObject.CreateInstance<PartDef>();
            Assert.That(p.Edition, Is.EqualTo(PartEdition.None));
        }

        // --- SpecModApplier: None is identity --------------------------------------------------

        [Test]
        public void Apply_NoneEdition_IsByteForByteIdentity_Multiply()
        {
            var baseSpec = new VehicleSpec();
            // Baseline off the same deep-copy Apply uses, so the identity is bit-exact regardless of
            // any serialization rounding: None collapses to the exact original expression 1f * 1.15f.
            float clonedPower = SpecModApplier.Clone(baseSpec).Engine.PeakTorqueNm;
            VehicleSpec result = SpecModApplier.Apply(
                baseSpec, new[] { StatPart(SpecModTarget.Power, 1.15f, PartEdition.None) });
            Assert.That(result.Engine.PeakTorqueNm, Is.EqualTo(clonedPower * 1.15f));
        }

        [Test]
        public void Apply_NoneEdition_IsByteForByteIdentity_Add()
        {
            var baseSpec = new VehicleSpec();
            float clonedGrip = SpecModApplier.Clone(baseSpec).FrontTyre.PeakMu;
            VehicleSpec result = SpecModApplier.Apply(
                baseSpec, new[] { StatPart(SpecModTarget.GripFront, 0.2f, PartEdition.None, SpecModOp.Add) });
            Assert.That(result.FrontTyre.PeakMu, Is.EqualTo(clonedGrip * 1.2f));
        }

        // --- SpecModApplier: higher tiers scale the EFFECT by exactly the mapped factor ---------

        [Test]
        public void Apply_Foil_ScalesMultiplyEffectByFactor()
        {
            var baseSpec = new VehicleSpec();
            float baseGrip = baseSpec.FrontTyre.PeakMu;
            // A x1.2 mod is a +20% effect; Foil (1.25x) scales the effect to +25% → base * 1.25.
            VehicleSpec result = SpecModApplier.Apply(
                baseSpec, new[] { StatPart(SpecModTarget.GripFront, 1.2f, PartEdition.Foil) });
            float expected = baseGrip * (1f + (1.2f - 1f) * 1.25f);
            Assert.That(result.FrontTyre.PeakMu, Is.EqualTo(expected).Within(1e-4f));
            Assert.That(result.FrontTyre.PeakMu, Is.EqualTo(baseGrip * 1.25f).Within(1e-4f));
        }

        [Test]
        public void Apply_Tiers_ScaleAddEffectByFactor()
        {
            var baseSpec = new VehicleSpec();
            float baseGrip = baseSpec.FrontTyre.PeakMu;
            // An Add mod's Multiplier IS its +fraction effect, so the factor scales it directly.
            VehicleSpec none = SpecModApplier.Apply(
                baseSpec, new[] { StatPart(SpecModTarget.GripFront, 0.2f, PartEdition.None, SpecModOp.Add) });
            VehicleSpec holo = SpecModApplier.Apply(
                baseSpec, new[] { StatPart(SpecModTarget.GripFront, 0.2f, PartEdition.Holo, SpecModOp.Add) });

            Assert.That(none.FrontTyre.PeakMu, Is.EqualTo(baseGrip * 1.2f).Within(1e-4f));
            // Holo (1.5x): +20% effect → +30% → 1 + 0.2 * 1.5 = 1.30.
            Assert.That(holo.FrontTyre.PeakMu, Is.EqualTo(baseGrip * 1.3f).Within(1e-4f));
        }

        [Test]
        public void Apply_Polychrome_ScalesPowerEffect()
        {
            var baseSpec = new VehicleSpec();
            float basePower = baseSpec.Engine.PeakTorqueNm;
            // x1.15 = +15% effect; Polychrome (2x) → +30% → base * 1.30.
            VehicleSpec result = SpecModApplier.Apply(
                baseSpec, new[] { StatPart(SpecModTarget.Power, 1.15f, PartEdition.Polychrome) });
            Assert.That(result.Engine.PeakTorqueNm, Is.EqualTo(basePower * 1.3f).Within(1e-3f));
        }

        // --- SpecModApplier: edition preserves the SIGN / direction of an effect ----------------

        [Test]
        public void Apply_Edition_PreservesReductionSign_Weight()
        {
            var baseSpec = new VehicleSpec();
            float baseMass = baseSpec.MassKg;
            // A 0.9 weight mod is a -10% reduction (an upgrade). Polychrome (2x) must deepen the
            // reduction to -20%, NOT flip it into an increase: 1 + (0.9 - 1) * 2 = 0.8.
            VehicleSpec result = SpecModApplier.Apply(
                baseSpec, new[] { StatPart(SpecModTarget.Weight, 0.9f, PartEdition.Polychrome) });
            Assert.That(result.MassKg, Is.LessThan(baseMass));      // still a reduction, sign intact
            Assert.That(result.MassKg, Is.GreaterThan(0f));         // never negative / degenerate
            Assert.That(result.MassKg, Is.EqualTo(baseMass * 0.8f).Within(1e-2f));
        }

        [Test]
        public void Apply_Edition_PreservesNegativeAddSign()
        {
            var baseSpec = new VehicleSpec();
            float baseGrip = baseSpec.FrontTyre.PeakMu;
            // A -0.04 Add mod is a -4% penalty. Polychrome deepens it to -8%, still a penalty.
            VehicleSpec result = SpecModApplier.Apply(
                baseSpec, new[] { StatPart(SpecModTarget.GripFront, -0.04f, PartEdition.Polychrome, SpecModOp.Add) });
            Assert.That(result.FrontTyre.PeakMu, Is.LessThan(baseGrip));
            Assert.That(result.FrontTyre.PeakMu, Is.EqualTo(baseGrip * 0.92f).Within(1e-3f));
        }

        // --- SpecModApplier: edition never changes which parts apply (category) -----------------

        [Test]
        public void Apply_EditionOnNonStatPart_StillIgnored()
        {
            var baseSpec = new VehicleSpec();
            float basePower = baseSpec.Engine.PeakTorqueNm;
            var attack = ScriptableObject.CreateInstance<PartDef>();
            attack.Category = PartCategory.Attack;
            attack.Edition = PartEdition.Polychrome;   // a fancy edition must not promote it into the bake
            attack.SpecMods = new List<SpecMod> { new SpecMod { Target = SpecModTarget.Power, Multiplier = 5f } };
            VehicleSpec result = SpecModApplier.Apply(baseSpec, new[] { attack });
            Assert.That(result.Engine.PeakTorqueNm, Is.EqualTo(basePower).Within(1e-2f));
        }

        [Test]
        public void Apply_DoesNotMutatePartEditionOrCategory()
        {
            var baseSpec = new VehicleSpec();
            PartDef part = StatPart(SpecModTarget.Power, 1.2f, PartEdition.Foil);
            SpecModApplier.Apply(baseSpec, new[] { part });
            Assert.That(part.Category, Is.EqualTo(PartCategory.Stat));
            Assert.That(part.Edition, Is.EqualTo(PartEdition.Foil));
        }

        // --- Editioned + un-editioned parts still fold in equip order ---------------------------

        [Test]
        public void Apply_NoneAndFoil_FoldTogetherInOrder()
        {
            var baseSpec = new VehicleSpec();
            float baseGrip = baseSpec.FrontTyre.PeakMu;
            PartDef none = StatPart(SpecModTarget.GripFront, 1.1f);                    // +10% effect
            PartDef foil = StatPart(SpecModTarget.GripFront, 1.2f, PartEdition.Foil);  // +20% → +25% effect
            VehicleSpec result = SpecModApplier.Apply(baseSpec, new[] { none, foil });
            // 1.1 (None) * (1 + 0.2 * 1.25) = 1.1 * 1.25.
            Assert.That(result.FrontTyre.PeakMu, Is.EqualTo(baseGrip * 1.1f * 1.25f).Within(1e-3f));
        }
    }
}
