using System.Collections.Generic;
using NUnit.Framework;
using Shitboxer.Meta;
using Shitboxer.Vehicle;
using UnityEngine;

namespace Shitboxer.Tests
{
    /// <summary>Covers baking equipped stat parts into a VehicleSpec without mutating the base.</summary>
    public class SpecModApplierTests : TestBase
    {
        private static PartDef StatPart(SpecModTarget target, float mult)
        {
            var p = ScriptableObject.CreateInstance<PartDef>();
            p.Category = PartCategory.Stat;
            p.SpecMods = new List<SpecMod> { new SpecMod { Target = target, Multiplier = mult } };
            return p;
        }

        /// <summary>An additive stat part (Op=Add): Multiplier read as a +fraction, e.g. 0.2 = +20%.</summary>
        private static PartDef AddPart(SpecModTarget target, float amount)
        {
            var p = ScriptableObject.CreateInstance<PartDef>();
            p.Category = PartCategory.Stat;
            p.SpecMods = new List<SpecMod>
            {
                new SpecMod { Target = target, Multiplier = amount, Op = SpecModOp.Add },
            };
            return p;
        }

        [Test]
        public void Apply_MultipliesPower()
        {
            var baseSpec = new VehicleSpec();
            float basePower = baseSpec.Engine.PeakTorqueNm;
            VehicleSpec result = SpecModApplier.Apply(baseSpec, new[] { StatPart(SpecModTarget.Power, 1.15f) });
            Assert.That(result.Engine.PeakTorqueNm, Is.EqualTo(basePower * 1.15f).Within(1e-2f));
        }

        [Test]
        public void Apply_DoesNotMutateBaseSpec()
        {
            var baseSpec = new VehicleSpec();
            float basePower = baseSpec.Engine.PeakTorqueNm;
            SpecModApplier.Apply(baseSpec, new[] { StatPart(SpecModTarget.Power, 2f) });
            Assert.That(baseSpec.Engine.PeakTorqueNm, Is.EqualTo(basePower).Within(1e-4f));
        }

        [Test]
        public void Apply_StacksMultiplicatively()
        {
            var baseSpec = new VehicleSpec();
            float baseGrip = baseSpec.FrontTyre.PeakMu;
            VehicleSpec result = SpecModApplier.Apply(baseSpec, new[]
            {
                StatPart(SpecModTarget.GripFront, 1.1f),
                StatPart(SpecModTarget.GripFront, 1.2f),
            });
            Assert.That(result.FrontTyre.PeakMu, Is.EqualTo(baseGrip * 1.1f * 1.2f).Within(1e-3f));
        }

        [Test]
        public void Apply_AdditiveBeforeMultiplicative_BeatsReverse()
        {
            var baseSpec = new VehicleSpec();
            float baseGrip = baseSpec.FrontTyre.PeakMu;
            PartDef add = AddPart(SpecModTarget.GripFront, 0.2f);   // +20%, additive
            PartDef mul = StatPart(SpecModTarget.GripFront, 1.2f);  // x1.20, multiplicative

            // Equip order is the resolve order: (1 + 0.2) x 1.2 = 1.44 vs (1 x 1.2) + 0.2 = 1.40.
            VehicleSpec addFirst = SpecModApplier.Apply(baseSpec, new[] { add, mul });
            VehicleSpec mulFirst = SpecModApplier.Apply(baseSpec, new[] { mul, add });

            Assert.That(addFirst.FrontTyre.PeakMu, Is.GreaterThan(mulFirst.FrontTyre.PeakMu));
            Assert.That(addFirst.FrontTyre.PeakMu, Is.EqualTo(baseGrip * 1.44f).Within(1e-3f));
            Assert.That(mulFirst.FrontTyre.PeakMu, Is.EqualTo(baseGrip * 1.40f).Within(1e-3f));
        }

        [Test]
        public void Apply_MultiplyOnly_IsOrderIndependent()
        {
            var baseSpec = new VehicleSpec();
            float baseGrip = baseSpec.FrontTyre.PeakMu;
            PartDef a = StatPart(SpecModTarget.GripFront, 1.1f);
            PartDef b = StatPart(SpecModTarget.GripFront, 1.2f);

            // Pure-Multiply loadouts commute, so both orders bake to base x 1.1 x 1.2 (old behaviour).
            VehicleSpec ab = SpecModApplier.Apply(baseSpec, new[] { a, b });
            VehicleSpec ba = SpecModApplier.Apply(baseSpec, new[] { b, a });

            Assert.That(ab.FrontTyre.PeakMu, Is.EqualTo(baseGrip * 1.1f * 1.2f).Within(1e-3f));
            Assert.That(ba.FrontTyre.PeakMu, Is.EqualTo(ab.FrontTyre.PeakMu).Within(1e-4f));
        }

        [Test]
        public void Apply_WeightMultiplierBelowOne_ReducesMass()
        {
            var baseSpec = new VehicleSpec();
            float baseMass = baseSpec.MassKg;
            VehicleSpec result = SpecModApplier.Apply(baseSpec, new[] { StatPart(SpecModTarget.Weight, 0.9f) });
            Assert.That(result.MassKg, Is.EqualTo(baseMass * 0.9f).Within(1e-2f));
        }

        [Test]
        public void Apply_IgnoresNonStatParts()
        {
            var baseSpec = new VehicleSpec();
            float basePower = baseSpec.Engine.PeakTorqueNm;
            var attack = ScriptableObject.CreateInstance<PartDef>();
            attack.Category = PartCategory.Attack;
            attack.SpecMods = new List<SpecMod> { new SpecMod { Target = SpecModTarget.Power, Multiplier = 5f } };
            VehicleSpec result = SpecModApplier.Apply(baseSpec, new[] { attack });
            Assert.That(result.Engine.PeakTorqueNm, Is.EqualTo(basePower).Within(1e-2f));
        }

        [Test]
        public void Apply_NullParts_ReturnsClone()
        {
            var baseSpec = new VehicleSpec();
            VehicleSpec result = SpecModApplier.Apply(baseSpec, null);
            Assert.That(result.Engine.PeakTorqueNm, Is.EqualTo(baseSpec.Engine.PeakTorqueNm).Within(1e-4f));
            Assert.AreNotSame(baseSpec, result);
        }
    }
}
