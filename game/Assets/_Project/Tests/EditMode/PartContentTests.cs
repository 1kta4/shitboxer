using System.Collections.Generic;
using NUnit.Framework;
using Shitboxer.Meta;
using Shitboxer.Vehicle;
using UnityEngine;

namespace Shitboxer.Tests
{
    /// <summary>
    /// Pure-logic coverage for the wave-10 content additions: the DraftLeech marker on PartDef, and
    /// the "editioned variant is a premium opt-in, never a free power spike" invariant the authored
    /// Foil/Holo parts lean on. MetaAssetsBuilder itself is UnityEditor asset-building code (it only
    /// materialises .asset files from the "Build Meta Assets" menu) and lives in the Shitboxer.Editor
    /// assembly, so it cannot be exercised from an EditMode fixture — these tests instead pin the
    /// pure mechanisms (PartEditionInfo + SpecModApplier) those defs are built on, and confirm an
    /// economy/DraftLeech part never touches the driving spec.
    /// </summary>
    public class PartContentTests : TestBase
    {
        private static PartDef StatPart(SpecModTarget target, float mult, PartEdition edition = PartEdition.None)
        {
            var p = ScriptableObject.CreateInstance<PartDef>();
            p.Category = PartCategory.Stat;
            p.Edition = edition;
            p.SpecMods = new List<SpecMod> { new SpecMod { Target = target, Multiplier = mult, Op = SpecModOp.Multiply } };
            return p;
        }

        // --- DraftLeech marker (the shared A<->B contract) -------------------------------------

        [Test]
        public void PartDef_DefaultDraftLeech_IsFalse()
        {
            var p = ScriptableObject.CreateInstance<PartDef>();
            Assert.That(p.DraftLeech, Is.False);
        }

        [Test]
        public void PartDef_DraftLeech_IsSettable()
        {
            var p = ScriptableObject.CreateInstance<PartDef>();
            p.DraftLeech = true;
            Assert.That(p.DraftLeech, Is.True);
        }

        [Test]
        public void DraftLeechEconomyPart_HasNoDrivingEffect()
        {
            // The authored Slipstream Siphon is an Economy part flagged DraftLeech with no SpecMods:
            // its value is the draft payoff, and it must never alter the driving spec (invariant a).
            var baseSpec = new VehicleSpec();
            VehicleSpec cloned = SpecModApplier.Clone(baseSpec);

            var leech = ScriptableObject.CreateInstance<PartDef>();
            leech.Category = PartCategory.Economy;
            leech.DraftLeech = true;
            leech.MoneyPerPositionHeld = 0;

            VehicleSpec result = SpecModApplier.Apply(baseSpec, new[] { leech });
            Assert.That(result.Engine.PeakTorqueNm, Is.EqualTo(cloned.Engine.PeakTorqueNm));
            Assert.That(result.FrontTyre.PeakMu, Is.EqualTo(cloned.FrontTyre.PeakMu));
        }

        // --- Editioned variants are a PREMIUM opt-in, not a free spike -------------------------

        [Test]
        public void AuthoredEditions_CostMoreThanBase()
        {
            // Prices for the Foil/Holo variants are basePrice * PartEditionInfo.PriceMult(edition),
            // so each editioned variant is strictly pricier than the plain part it derives from.
            Assert.That(Mathf.RoundToInt(6 * PartEditionInfo.PriceMult(PartEdition.Foil)),  Is.GreaterThan(6));   // Sticky Compound -> Foil
            Assert.That(Mathf.RoundToInt(8 * PartEditionInfo.PriceMult(PartEdition.Holo)),  Is.GreaterThan(8));   // Junkyard Turbo -> Holo
            Assert.That(Mathf.RoundToInt(12 * PartEditionInfo.PriceMult(PartEdition.Foil)), Is.GreaterThan(12));  // Race Slicks -> Foil
        }

        [Test]
        public void FoilStatPart_BakesStrongerThanNone_ForSameMods()
        {
            // A Foil variant carries the SAME SpecMods as its base part; SpecModApplier amplifies the
            // effect magnitude (PartEditionInfo.StatMult), so the baked grip is strictly higher than
            // the un-editioned part — the "stronger stat" half of the premium.
            var baseSpec = new VehicleSpec();
            float none = SpecModApplier.Apply(baseSpec, new[] { StatPart(SpecModTarget.GripFront, 1.10f) })
                .FrontTyre.PeakMu;
            float foil = SpecModApplier.Apply(baseSpec, new[] { StatPart(SpecModTarget.GripFront, 1.10f, PartEdition.Foil) })
                .FrontTyre.PeakMu;
            Assert.That(foil, Is.GreaterThan(none));
        }

        [Test]
        public void FoilEdition_AmplifiesDownsideToo_NotAFreeUpgrade()
        {
            // Foil Race Slicks pairs +grip with a +mass downside (Weight x1.03). The edition must
            // deepen the downside as well (heavier, ~+3.75%), never wash it out into a free upgrade.
            var baseSpec = new VehicleSpec();
            float baseMass = baseSpec.MassKg;
            float noneMass = SpecModApplier.Apply(baseSpec, new[] { StatPart(SpecModTarget.Weight, 1.03f) }).MassKg;
            float foilMass = SpecModApplier.Apply(baseSpec, new[] { StatPart(SpecModTarget.Weight, 1.03f, PartEdition.Foil) }).MassKg;
            Assert.That(noneMass, Is.GreaterThan(baseMass));   // the base variant already adds mass
            Assert.That(foilMass, Is.GreaterThan(noneMass));   // Foil deepens that downside, not washes it out
        }
    }
}
